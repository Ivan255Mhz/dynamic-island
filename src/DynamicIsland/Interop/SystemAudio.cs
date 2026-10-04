using System.Runtime.InteropServices;

namespace DynamicIsland.Interop;

/// <summary>
/// Minimal Core Audio interop used to read and control the system volume and
/// mute state for the default playback device. The endpoint is cached so the
/// volume slider can update it continuously without re-activating COM.
/// </summary>
internal static class SystemAudio
{
    private static IAudioEndpointVolume? _volume;
    private static IMMDevice? _device;
    private static object? _endpointObject;

    public static bool IsMuted()
        => TryGetVolumeEndpoint(out var volume) && volume.GetMuteState();

    public static bool TryToggleMute(out bool muted)
    {
        muted = false;

        if (!TryGetVolumeEndpoint(out var volume))
        {
            return false;
        }

        try
        {
            muted = !volume.GetMuteState();
            volume.SetMuteState(muted);
            return true;
        }
        catch
        {
            ResetCache();
            return false;
        }
    }

    public static bool TryGetVolume(out float level)
    {
        level = 0;

        if (!TryGetVolumeEndpoint(out var volume))
        {
            return false;
        }

        var context = Guid.Empty;
        try
        {
            if (volume.GetMasterVolumeLevelScalar(out level) != 0)
            {
                ResetCache();
                return false;
            }

            return true;
        }
        catch
        {
            ResetCache();
            return false;
        }
    }

    public static bool TrySetVolume(float level)
    {
        level = Math.Clamp(level, 0f, 1f);

        if (!TryGetVolumeEndpoint(out var volume))
        {
            return false;
        }

        var context = Guid.Empty;
        try
        {
            if (volume.SetMasterVolumeLevelScalar(level, ref context) != 0)
            {
                ResetCache();
                return false;
            }

            // Moving the slider while muted behaves like the volume keys.
            if (level > 0f && volume.GetMute(out var muted) == 0 && muted)
            {
                volume.SetMute(false, ref context);
            }

            return true;
        }
        catch
        {
            ResetCache();
            return false;
        }
    }

    private static bool TryGetVolumeEndpoint(out IAudioEndpointVolume volume)
    {
        if (_volume is not null)
        {
            volume = _volume;
            return true;
        }

        if (!TryActivateEndpoint(out volume, out var device, out var endpointObject))
        {
            Release(endpointObject);
            Release(device);
            return false;
        }

        _volume = volume;
        _device = device;
        _endpointObject = endpointObject;
        return true;
    }

    private static void ResetCache()
    {
        Release(_endpointObject);
        Release(_device);
        _endpointObject = null;
        _device = null;
        _volume = null;
    }

    private static bool TryActivateEndpoint(
        out IAudioEndpointVolume volume,
        out IMMDevice device,
        out object comObject)
    {
        volume = null!;
        device = null!;
        comObject = null!;

        try
        {
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            try
            {
                if (enumerator.GetDefaultAudioEndpoint(EDataFlow.Render, ERole.Multimedia, out var endpoint) != 0 ||
                    endpoint is null)
                {
                    return false;
                }

                device = endpoint;
                var iid = typeof(IAudioEndpointVolume).GUID;
                if (endpoint.Activate(ref iid, CLSCTX.All, IntPtr.Zero, out var activated) != 0 ||
                    activated is not IAudioEndpointVolume endpointVolume)
                {
                    return false;
                }

                comObject = activated;
                volume = endpointVolume;
                return true;
            }
            finally
            {
                Release(enumerator);
            }
        }
        catch
        {
            return false;
        }
    }

    private static void Release(object? comObject)
    {
        if (comObject is not null && Marshal.IsComObject(comObject))
        {
            Marshal.ReleaseComObject(comObject);
        }
    }

    private static bool GetMuteState(this IAudioEndpointVolume volume)
    {
        var context = Guid.Empty;
        return volume.GetMute(out var muted) == 0 && muted;
    }

    private static void SetMuteState(this IAudioEndpointVolume volume, bool muted)
    {
        var context = Guid.Empty;
        volume.SetMute(muted, ref context);
    }

    private enum EDataFlow
    {
        Render = 0,
        Capture = 1,
        All = 2,
    }

    private enum ERole
    {
        Console = 0,
        Multimedia = 1,
        Communications = 2,
    }

    [Flags]
    private enum CLSCTX : uint
    {
        InprocServer = 0x1,
        InprocHandler = 0x2,
        LocalServer = 0x4,
        RemoteServer = 0x10,
        All = InprocServer | InprocHandler | LocalServer | RemoteServer,
    }

    [ComImport]
    [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumerator
    {
    }

    [ComImport]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig]
        int EnumAudioEndpoints(EDataFlow dataFlow, int stateMask, out IntPtr devices);

        [PreserveSig]
        int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice endpoint);

        [PreserveSig]
        int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);

        [PreserveSig]
        int RegisterEndpointNotificationCallback(IntPtr client);

        [PreserveSig]
        int UnregisterEndpointNotificationCallback(IntPtr client);
    }

    [ComImport]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig]
        int Activate(
            ref Guid iid,
            CLSCTX clsCtx,
            IntPtr activationParams,
            [MarshalAs(UnmanagedType.IUnknown)] out object interfacePointer);

        [PreserveSig]
        int OpenPropertyStore(int access, out IntPtr properties);

        [PreserveSig]
        int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);

        [PreserveSig]
        int GetState(out int state);
    }

    [ComImport]
    [Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        [PreserveSig]
        int RegisterControlChangeNotify(IntPtr notify);

        [PreserveSig]
        int UnregisterControlChangeNotify(IntPtr notify);

        [PreserveSig]
        int GetChannelCount(out uint count);

        [PreserveSig]
        int SetMasterVolumeLevel(float level, ref Guid context);

        [PreserveSig]
        int SetMasterVolumeLevelScalar(float level, ref Guid context);

        [PreserveSig]
        int GetMasterVolumeLevel(out float level);

        [PreserveSig]
        int GetMasterVolumeLevelScalar(out float level);

        [PreserveSig]
        int SetChannelVolumeLevel(uint channel, float level, ref Guid context);

        [PreserveSig]
        int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid context);

        [PreserveSig]
        int GetChannelVolumeLevel(uint channel, out float level);

        [PreserveSig]
        int GetChannelVolumeLevelScalar(uint channel, out float level);

        [PreserveSig]
        int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context);

        [PreserveSig]
        int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);

        [PreserveSig]
        int GetVolumeStepInfo(out uint step, out uint stepCount);

        [PreserveSig]
        int VolumeStepUp(ref Guid context);

        [PreserveSig]
        int VolumeStepDown(ref Guid context);

        [PreserveSig]
        int QueryHardwareSupport(out uint mask);

        [PreserveSig]
        int GetVolumeRange(out float min, out float max, out float increment);
    }
}
