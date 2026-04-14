using ChartHub.Server.Options;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ChartHub.Server.Services;

public sealed partial class UinputKeyboardService : IUinputKeyboardService, IDisposable
{
    private readonly ILogger<UinputKeyboardService> _logger;
    private readonly int _fd;
    private bool _disposed;

    public bool IsSupported => _fd >= 0;

    public UinputKeyboardService(IOptions<InputOptions> options, ILogger<UinputKeyboardService> logger)
    {
        _logger = logger;
        string deviceName = options.Value.KeyboardDeviceName;
        _fd = TryCreateDevice(deviceName);
    }

    private int TryCreateDevice(string name)
    {
        if (!OperatingSystem.IsLinux())
        {
            LogKeyboardLinuxOnly(_logger);
            return -1;
        }

        int fd = UinputNative.open("/dev/uinput", UinputNative.O_WRONLY | UinputNative.O_NONBLOCK);
        if (fd < 0)
        {
            LogKeyboardOpenFailed(_logger, fd);
            return -1;
        }

        _ = UinputNative.ioctl(fd, UinputNative.UI_SET_EVBIT, UinputNative.EV_KEY);
        _ = UinputNative.ioctl(fd, UinputNative.UI_SET_EVBIT, UinputNative.EV_SYN);

        // Register all key codes up to KEY_CAPSLOCK (58) plus a useful extended set.
        for (int i = 1; i <= 58; i++)
        {
            _ = UinputNative.ioctl(fd, UinputNative.UI_SET_KEYBIT, i);
        }

        UinputNative.UinputSetup setup = new()
        {
            Id = new UinputNative.UinputId { BusType = 0x03, Vendor = 0x046d, Product = 0xc31c, Version = 1 },
            Name = name,
            FfEffectsMax = 0,
        };

        _ = UinputNative.ioctl(fd, UinputNative.UI_DEV_SETUP, ref setup);
        _ = UinputNative.ioctl(fd, UinputNative.UI_DEV_CREATE, 0);

        LogKeyboardCreated(_logger, name);
        return fd;
    }

    public void PressKey(int linuxKeyCode, bool pressed)
    {
        if (!IsSupported || linuxKeyCode <= 0)
        {
            return;
        }

        byte[] ev = UinputNative.SerialiseInputEvent((ushort)UinputNative.EV_KEY, (ushort)linuxKeyCode, pressed ? 1 : 0);
        _ = UinputNative.write(_fd, ev, ev.Length);
        UinputNative.WriteSync(_fd);
    }

    public void TypeChar(char c)
    {
        if (!IsSupported)
        {
            return;
        }

        if (!UinputNative.TryMapChar(c, out int keyCode, out bool shift))
        {
            LogKeyboardNoMapping(_logger, (int)c);
            return;
        }

        if (shift)
        {
            PressKey(UinputNative.KEY_LEFTSHIFT, true);
        }

        PressKey(keyCode, true);
        PressKey(keyCode, false);

        if (shift)
        {
            PressKey(UinputNative.KEY_LEFTSHIFT, false);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_fd >= 0)
        {
            _ = UinputNative.ioctl(_fd, UinputNative.UI_DEV_DESTROY, 0);
            _ = UinputNative.close(_fd);
            LogKeyboardDestroyed(_logger);
        }
    }

    [LoggerMessage(EventId = 8021, Level = LogLevel.Warning, Message = "UinputKeyboardService: uinput is Linux-only; keyboard input will be unavailable.")]
    private static partial void LogKeyboardLinuxOnly(ILogger logger);

    [LoggerMessage(EventId = 8022, Level = LogLevel.Warning, Message = "UinputKeyboardService: could not open /dev/uinput (fd={Fd}). Ensure the process user is in the 'input' group.")]
    private static partial void LogKeyboardOpenFailed(ILogger logger, int fd);

    [LoggerMessage(EventId = 8023, Level = LogLevel.Information, Message = "UinputKeyboardService: virtual keyboard '{Name}' created.")]
    private static partial void LogKeyboardCreated(ILogger logger, string name);

    [LoggerMessage(EventId = 8024, Level = LogLevel.Debug, Message = "UinputKeyboardService: no mapping for character U+{Codepoint:X4}.")]
    private static partial void LogKeyboardNoMapping(ILogger logger, int codepoint);

    [LoggerMessage(EventId = 8025, Level = LogLevel.Information, Message = "UinputKeyboardService: virtual keyboard destroyed.")]
    private static partial void LogKeyboardDestroyed(ILogger logger);
}
