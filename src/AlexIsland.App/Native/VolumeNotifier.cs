using System.Runtime.InteropServices;

namespace AlexIsland.App.Native;

/// <summary>
/// Avisa cuando cambia el volumen o el silencio del dispositivo de salida
/// predeterminado, usando Windows Core Audio. Solo lee; nunca modifica el volumen.
/// </summary>
internal sealed class VolumeNotifier : IDisposable
{
    private IAudioEndpointVolume? _endpoint;
    private Callback? _callback;
    private bool _disposed;

    /// <summary>Volumen (0–1) y silencio. Llega en un hilo de audio, no en el de la interfaz.</summary>
    public event Action<float, bool>? VolumeChanged;

    public VolumeNotifier()
    {
        Attach();
    }

    /// <summary>Vuelve a enlazarse al dispositivo predeterminado (por ejemplo, al conectar auriculares).</summary>
    public void Reattach()
    {
        if (_disposed) return;
        Detach();
        Attach();
    }

    public (float Level, bool Muted)? Read()
    {
        try
        {
            if (_endpoint is null) return null;
            Marshal.ThrowExceptionForHR(_endpoint.GetMasterVolumeLevelScalar(out var level));
            Marshal.ThrowExceptionForHR(_endpoint.GetMute(out var muted));
            return (level, muted);
        }
        catch
        {
            return null;
        }
    }

    private void Attach()
    {
        try
        {
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
            try
            {
                Marshal.ThrowExceptionForHR(enumerator.GetDefaultAudioEndpoint(DataFlowRender, RoleMultimedia, out var device));
                var iid = typeof(IAudioEndpointVolume).GUID;
                Marshal.ThrowExceptionForHR(device.Activate(ref iid, ClsCtxAll, 0, out var endpointObject));
                _endpoint = (IAudioEndpointVolume)endpointObject;
                Marshal.ReleaseComObject(device);
            }
            finally
            {
                Marshal.ReleaseComObject(enumerator);
            }

            _callback = new Callback(this);
            Marshal.ThrowExceptionForHR(_endpoint.RegisterControlChangeNotify(_callback));
        }
        catch
        {
            // Equipo sin salida de audio o servicio de audio detenido: no hay avisos de volumen.
            Detach();
        }
    }

    private void Detach()
    {
        try
        {
            if (_endpoint is not null && _callback is not null)
            {
                _endpoint.UnregisterControlChangeNotify(_callback);
            }
        }
        catch
        {
        }

        if (_endpoint is not null)
        {
            try { Marshal.ReleaseComObject(_endpoint); } catch { }
        }

        _endpoint = null;
        _callback = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Detach();
    }

    private void Raise(nint notifyData)
    {
        if (_disposed || notifyData == 0) return;
        var data = Marshal.PtrToStructure<AudioVolumeNotificationData>(notifyData);
        VolumeChanged?.Invoke(data.MasterVolume, data.Muted != 0);
    }

    // ---------------------------------------------------------------- COM

    private const int DataFlowRender = 0;
    private const int RoleMultimedia = 1;
    private const int ClsCtxAll = 0x17;

    [StructLayout(LayoutKind.Sequential)]
    private struct AudioVolumeNotificationData
    {
        public Guid EventContext;
        public int Muted;
        public float MasterVolume;
        public uint Channels;
        // Le sigue un arreglo variable de volúmenes por canal que no se usa.
    }

    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.None)]
    private sealed class Callback(VolumeNotifier owner) : IAudioEndpointVolumeCallback
    {
        public int OnNotify(nint notifyData)
        {
            try { owner.Raise(notifyData); } catch { }
            return 0;
        }
    }

    [ComImport]
    [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumeratorComObject
    {
    }

    [ComImport]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out nint devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
    }

    [ComImport]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig]
        int Activate(ref Guid iid, int clsCtx, nint activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
    }

    [ComImport]
    [Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IAudioEndpointVolumeCallback notify);
        [PreserveSig] int UnregisterControlChangeNotify(IAudioEndpointVolumeCallback notify);
        [PreserveSig] int GetChannelCount(out uint count);
        [PreserveSig] int SetMasterVolumeLevel(float levelDb, ref Guid context);
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid context);
        [PreserveSig] int GetMasterVolumeLevel(out float levelDb);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
        [PreserveSig] int SetChannelVolumeLevel(uint channel, float levelDb, ref Guid context);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid context);
        [PreserveSig] int GetChannelVolumeLevel(uint channel, out float levelDb);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }

    [ComImport]
    [Guid("657804FA-D6AD-4496-8A60-352752AF4F89")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolumeCallback
    {
        [PreserveSig] int OnNotify(nint notifyData);
    }
}
