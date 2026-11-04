using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using AlexIsland.Core.Calendar;

namespace AlexIsland.App.Services;

/// <summary>
/// Lee el calendario predeterminado del Outlook clásico de escritorio mediante COM
/// (modelo de objetos de Outlook). Solo lectura: nunca crea, cambia ni borra citas.
///
/// El Outlook nuevo (olk.exe) no tiene API local, pero si el clásico está instalado
/// y configurado con la cuenta, su caché sigue disponible por COM. Si Outlook no
/// estaba abierto, se abre sin ventana, se deja sincronizar un momento con el
/// servidor y se cierra al terminar, para no dejar un proceso de ~200 MB.
/// </summary>
internal static class OutlookCalendarReader
{
    private const int OlFolderCalendar = 9;
    private const int MaxItems = 1000;

    /// <summary>Hay un Outlook clásico registrado para COM en este equipo.</summary>
    public static bool IsAvailable => Type.GetTypeFromProgID("Outlook.Application") is not null;

    /// <summary>
    /// Lee las citas de [<paramref name="fromLocal"/>, <paramref name="toLocal"/>) y llama a
    /// <paramref name="onRead"/> con cada lectura (una si Outlook ya estaba abierto; dos si
    /// hubo que abrirlo: la de la caché local y otra tras dejarlo sincronizar).
    /// </summary>
    public static Task ReadAsync(
        DateTime fromLocal,
        DateTime toLocal,
        TimeSpan syncWait,
        Action<IReadOnlyList<OutlookAppointment>> onRead,
        CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // Hilo propio STA: las llamadas COM pueden tardar segundos y nunca deben
        // bloquear la interfaz de la isla.
        var thread = new Thread(() =>
        {
            try
            {
                ReadOnThread(fromLocal, toLocal, syncWait, onRead, cancellationToken);
                completion.TrySetResult();
            }
            catch (OperationCanceledException)
            {
                completion.TrySetCanceled(cancellationToken);
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        })
        {
            IsBackground = true,
            Name = "Alex Island · Outlook",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        return completion.Task;
    }

    private static void ReadOnThread(
        DateTime fromLocal,
        DateTime toLocal,
        TimeSpan syncWait,
        Action<IReadOnlyList<OutlookAppointment>> onRead,
        CancellationToken cancellationToken)
    {
        var type = Type.GetTypeFromProgID("Outlook.Application")
                   ?? throw new InvalidOperationException("No hay un Outlook clásico instalado.");

        var wasRunning = Process.GetProcessesByName("OUTLOOK").Length > 0;
        dynamic? app = null;
        try
        {
            // Outlook es de instancia única: si ya está abierto, devuelve esa instancia.
            app = Activator.CreateInstance(type)
                  ?? throw new InvalidOperationException("No se pudo iniciar Outlook.");

            onRead(ReadAppointments(app, fromLocal, toLocal));

            if (!wasRunning && syncWait > TimeSpan.Zero)
            {
                // Recién abierto, Outlook muestra lo que tenía en caché; tras unos
                // segundos ya bajó del servidor lo nuevo, así que se lee otra vez.
                cancellationToken.WaitHandle.WaitOne(syncWait);
                cancellationToken.ThrowIfCancellationRequested();
                onRead(ReadAppointments(app, fromLocal, toLocal));
            }
        }
        finally
        {
            if (app is not null)
            {
                if (!wasRunning)
                {
                    try
                    {
                        app.Quit();
                    }
                    catch
                    {
                        // Si el usuario abrió Outlook mientras tanto, Quit puede fallar; se deja como está.
                    }
                }
                Release(app);
            }
        }
    }

    private static List<OutlookAppointment> ReadAppointments(dynamic app, DateTime fromLocal, DateTime toLocal)
    {
        dynamic? ns = null, folder = null, items = null, restricted = null;
        var result = new List<OutlookAppointment>();
        try
        {
            ns = app.GetNamespace("MAPI");
            folder = ns.GetDefaultFolder(OlFolderCalendar);
            items = folder.Items;

            // Para expandir las reuniones recurrentes hay que ordenar por inicio y
            // activar IncludeRecurrences antes de filtrar.
            items.Sort("[Start]");
            items.IncludeRecurrences = true;

            // El filtro de Outlook usa fechas locales en el formato corto del sistema.
            var culture = CultureInfo.CurrentCulture;
            var filter = $"[Start] < '{toLocal.ToString("g", culture)}' AND [End] > '{fromLocal.ToString("g", culture)}'";
            restricted = items.Restrict(filter);

            // GetFirst/GetNext es la forma recomendada con IncludeRecurrences (Count no es fiable).
            var item = restricted.GetFirst();
            while (item is not null && result.Count < MaxItems)
            {
                try
                {
                    var appointment = Convert(item);
                    if (appointment is not null) result.Add(appointment);
                }
                catch
                {
                    // Una cita con datos raros no impide leer las demás.
                }
                finally
                {
                    Release(item);
                }
                item = restricted.GetNext();
            }
        }
        finally
        {
            Release(restricted);
            Release(items);
            Release(folder);
            Release(ns);
        }

        return result;
    }

    private static OutlookAppointment? Convert(dynamic item)
    {
        DateTime start = item.StartUTC;
        DateTime end = item.EndUTC;
        bool allDay = item.AllDayEvent;
        int status = item.MeetingStatus;
        bool reminderSet = item.ReminderSet;
        int reminderMinutes = item.ReminderMinutesBeforeStart;

        string? globalId = null;
        try
        {
            globalId = item.GlobalAppointmentID;
        }
        catch
        {
            // Citas antiguas sin GlobalAppointmentID: se usa EntryID.
        }
        if (string.IsNullOrWhiteSpace(globalId))
        {
            globalId = item.EntryID;
        }

        string? body = null;
        try
        {
            // El cuerpo solo se usa para encontrar el enlace de Teams/Zoom/Meet.
            body = item.Body;
        }
        catch
        {
            // Si Outlook bloquea el cuerpo (protección del modelo de objetos), se sigue sin enlace.
        }

        return new OutlookAppointment(
            globalId ?? string.Empty,
            (string?)item.Subject ?? string.Empty,
            DateTime.SpecifyKind(start, DateTimeKind.Utc),
            DateTime.SpecifyKind(end, DateTimeKind.Utc),
            allDay,
            (string?)item.Location,
            body,
            status,
            reminderSet ? reminderMinutes : null);
    }

    private static void Release(object? comObject)
    {
        if (comObject is not null && Marshal.IsComObject(comObject))
        {
            try
            {
                Marshal.ReleaseComObject(comObject);
            }
            catch
            {
            }
        }
    }
}
