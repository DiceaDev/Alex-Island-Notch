namespace AlexIsland.Core.Notifications;

/// <summary>Notificación de Windows ya leída, sin depender de las API de WinRT.</summary>
/// <param name="Id">Identificador que asigna Windows; no se repite mientras la notificación exista.</param>
/// <param name="AppName">Nombre visible de la app que la envió.</param>
/// <param name="Title">Primera línea de texto (puede venir vacía).</param>
/// <param name="Body">Resto del texto, unido en una sola línea.</param>
/// <param name="Created">Momento en que Windows la creó.</param>
public sealed record IslandNotification(
    uint Id,
    string AppName,
    string Title,
    string Body,
    DateTimeOffset Created);
