using ChartHub.Server.Options;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ChartHub.Server.Services;

public sealed partial class UinputMouseService : IUinputMouseService, IDisposable
{
    private readonly ILogger<UinputMouseService> _logger;
    private readonly int _fd;
    private bool _disposed;

    public bool IsSupported => _fd >= 0;

    public UinputMouseService(IOptions<InputOptions> options, ILogger<UinputMouseService> logger)
    {
        _logger = logger;
        string deviceName = options.Value.MouseDeviceName;
        _fd = TryCreateDevice(deviceName);
    }

    private int TryCreateDevice(string name)
    {
        if (!OperatingSystem.IsLinux())
        {
            LogMouseLinuxOnly(_logger);
            return -1;
        }

        int fd = UinputNative.open("/dev/uinput", UinputNative.O_WRONLY | UinputNative.O_NONBLOCK);
        if (fd < 0)
        {
            LogMouseOpenFailed(_logger, fd);
            return -1;
        }

        _ = UinputNative.ioctl(fd, UinputNative.UI_SET_EVBIT, UinputNative.EV_KEY);
        _ = UinputNative.ioctl(fd, UinputNative.UI_SET_EVBIT, UinputNative.EV_REL);
        _ = UinputNative.ioctl(fd, UinputNative.UI_SET_EVBIT, UinputNative.EV_SYN);

        _ = UinputNative.ioctl(fd, UinputNative.UI_SET_KEYBIT, UinputNative.BTN_LEFT);
        _ = UinputNative.ioctl(fd, UinputNative.UI_SET_KEYBIT, UinputNative.BTN_RIGHT);

        _ = UinputNative.ioctl(fd, UinputNative.UI_SET_RELBIT, UinputNative.REL_X);
        _ = UinputNative.ioctl(fd, UinputNative.UI_SET_RELBIT, UinputNative.REL_Y);

        UinputNative.UinputSetup setup = new()
        {
            Id = new UinputNative.UinputId { BusType = 0x03, Vendor = 0x046d, Product = 0xc077, Version = 1 },
            Name = name,
            FfEffectsMax = 0,
        };

        _ = UinputNative.ioctl(fd, UinputNative.UI_DEV_SETUP, ref setup);
        _ = UinputNative.ioctl(fd, UinputNative.UI_DEV_CREATE, 0);

        LogMouseCreated(_logger, name);
        return fd;
    }

    public void MoveDelta(int dx, int dy)
    {
        if (!IsSupported)
        {
            return;
        }

        byte[] evX = UinputNative.SerialiseInputEvent((ushort)UinputNative.EV_REL, (ushort)UinputNative.REL_X, dx);
        byte[] evY = UinputNative.SerialiseInputEvent((ushort)UinputNative.EV_REL, (ushort)UinputNative.REL_Y, dy);
        _ = UinputNative.write(_fd, evX, evX.Length);
        _ = UinputNative.write(_fd, evY, evY.Length);
        UinputNative.WriteSync(_fd);
    }

    public void PressButton(string side, bool pressed)
    {
        if (!IsSupported)
        {
            return;
        }

        int code = side.Equals("right", StringComparison.OrdinalIgnoreCase)
            ? UinputNative.BTN_RIGHT
            : UinputNative.BTN_LEFT;

        byte[] ev = UinputNative.SerialiseInputEvent((ushort)UinputNative.EV_KEY, (ushort)code, pressed ? 1 : 0);
        _ = UinputNative.write(_fd, ev, ev.Length);
        UinputNative.WriteSync(_fd);
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
            LogMouseDestroyed(_logger);
        }
    }

    [LoggerMessage(EventId = 8011, Level = LogLevel.Warning, Message = "UinputMouseService: uinput is Linux-only; mouse input will be unavailable.")]
    private static partial void LogMouseLinuxOnly(ILogger logger);

    [LoggerMessage(EventId = 8012, Level = LogLevel.Warning, Message = "UinputMouseService: could not open /dev/uinput (fd={Fd}). Ensure the process user is in the 'input' group.")]
    private static partial void LogMouseOpenFailed(ILogger logger, int fd);

    [LoggerMessage(EventId = 8013, Level = LogLevel.Information, Message = "UinputMouseService: virtual mouse '{Name}' created.")]
    private static partial void LogMouseCreated(ILogger logger, string name);

    [LoggerMessage(EventId = 8014, Level = LogLevel.Information, Message = "UinputMouseService: virtual mouse destroyed.")]
    private static partial void LogMouseDestroyed(ILogger logger);
}
