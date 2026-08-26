using System.Runtime.InteropServices;

namespace Marco;

/// <summary>
/// Silencia el audio de un proceso vía su sesión WASAPI en el dispositivo de render por
/// defecto. COM interop a mano, sin NuGet. Limitaciones asumidas: solo el dispositivo por
/// defecto y matching por pid exacto (un launcher con proceso hijo distinto no se silencia).
/// Cualquier fallo devuelve false: el audio jamás rompe el watcher.
/// </summary>
public static class AudioService
{
    /// <returns>true si se cambió de verdad al menos una sesión de ese pid.</returns>
    public static bool Silenciar(int pid, bool silencio)
    {
        object? enumerador = null, dispositivoObj = null, activado = null, sesionesObj = null;
        try
        {
            enumerador = new MMDeviceEnumerator();
            if (((IMMDeviceEnumerator)enumerador)
                    .GetDefaultAudioEndpoint(0 /* eRender */, 1 /* eMultimedia */, out IMMDevice dispositivo) != 0)
                return false;
            dispositivoObj = dispositivo;
            Guid iid = typeof(IAudioSessionManager2).GUID;
            if (dispositivo.Activate(ref iid, CLSCTX_ALL, IntPtr.Zero, out activado) != 0)
                return false;
            if (((IAudioSessionManager2)activado!).GetSessionEnumerator(out IAudioSessionEnumerator sesiones) != 0)
                return false;
            sesionesObj = sesiones;

            bool alguna = false;
            sesiones.GetCount(out int total);
            for (int i = 0; i < total; i++)
            {
                if (sesiones.GetSession(i, out IAudioSessionControl control) != 0) continue;
                try
                {
                    // AUDCLNT_S_NO_SINGLE_PROCESS es un HRESULT de éxito (> 0) y rellena el pid
                    if (control is IAudioSessionControl2 control2 &&
                        control2.GetProcessId(out uint sesionPid) >= 0 && sesionPid == (uint)pid &&
                        control is ISimpleAudioVolume volumen)
                    {
                        // Una sesión que el usuario ya muteó en el mezclador no es nuestra:
                        // si la contáramos, al recuperar el foco le devolveríamos el audio
                        // que él mismo había quitado
                        if (silencio && volumen.GetMute(out bool yaMuda) >= 0 && yaMuda) continue;
                        Guid contexto = Guid.Empty;
                        if (volumen.SetMute(silencio, ref contexto) >= 0) alguna = true;
                    }
                }
                finally
                {
                    Marshal.ReleaseComObject(control);
                }
            }
            return alguna;
        }
        catch
        {
            return false;
        }
        finally
        {
            // Liberación determinista: sin esto cada llamada deja decenas de RCWs vivos
            // apuntando al motor de audio hasta que el GC pase por ellos
            foreach (object? com in new[] { sesionesObj, activado, dispositivoObj, enumerador })
                if (com is not null) Marshal.ReleaseComObject(com);
        }
    }

    private const int CLSCTX_ALL = 0x17;

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumerator { }

    // Interfaces truncadas: solo hasta el último método que se usa, en su orden de vtable
    // exacto (COM despacha por posición; un método de menos antes del usado lo rompería)

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice endpoint);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig]
        int Activate(ref Guid iid, int clsCtx, IntPtr activationParams,
            [MarshalAs(UnmanagedType.IUnknown)] out object instancia);
    }

    [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionManager2
    {
        // IAudioSessionManager
        [PreserveSig] int GetAudioSessionControl(ref Guid sessionGuid, int streamFlags, out IntPtr session);
        [PreserveSig] int GetSimpleAudioVolume(ref Guid sessionGuid, int streamFlags, out IntPtr volume);
        // IAudioSessionManager2
        [PreserveSig] int GetSessionEnumerator(out IAudioSessionEnumerator enumerator);
    }

    [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionEnumerator
    {
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int GetSession(int index, out IAudioSessionControl session);
    }

    [ComImport, Guid("F4B1A599-7266-4319-A8CA-E70ACB11E8CD"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionControl
    {
        [PreserveSig] int GetState(out int state);
        [PreserveSig] int GetDisplayName(out IntPtr name);
        [PreserveSig] int SetDisplayName(IntPtr name, ref Guid eventContext);
        [PreserveSig] int GetIconPath(out IntPtr path);
        [PreserveSig] int SetIconPath(IntPtr path, ref Guid eventContext);
        [PreserveSig] int GetGroupingParam(out Guid grouping);
        [PreserveSig] int SetGroupingParam(ref Guid grouping, ref Guid eventContext);
        [PreserveSig] int RegisterAudioSessionNotification(IntPtr client);
        [PreserveSig] int UnregisterAudioSessionNotification(IntPtr client);
    }

    [ComImport, Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionControl2
    {
        // IAudioSessionControl completo primero (orden de vtable)
        [PreserveSig] int GetState(out int state);
        [PreserveSig] int GetDisplayName(out IntPtr name);
        [PreserveSig] int SetDisplayName(IntPtr name, ref Guid eventContext);
        [PreserveSig] int GetIconPath(out IntPtr path);
        [PreserveSig] int SetIconPath(IntPtr path, ref Guid eventContext);
        [PreserveSig] int GetGroupingParam(out Guid grouping);
        [PreserveSig] int SetGroupingParam(ref Guid grouping, ref Guid eventContext);
        [PreserveSig] int RegisterAudioSessionNotification(IntPtr client);
        [PreserveSig] int UnregisterAudioSessionNotification(IntPtr client);
        // IAudioSessionControl2
        [PreserveSig] int GetSessionIdentifier(out IntPtr id);
        [PreserveSig] int GetSessionInstanceIdentifier(out IntPtr id);
        [PreserveSig] int GetProcessId(out uint pid);
    }

    [ComImport, Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ISimpleAudioVolume
    {
        [PreserveSig] int SetMasterVolume(float level, ref Guid eventContext);
        [PreserveSig] int GetMasterVolume(out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid eventContext);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }
}
