using ChartHub.Server.Options;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ChartHub.Server.Services;

public sealed partial class UinputGamepadService : IUinputGamepadService, IDisposable
{
    private readonly ILogger<UinputGamepadService> _logger;
    private readonly int _fd;
    private bool _disposed;

    public bool IsSupported => _fd >= 0;

    public UinputGamepadService(IOptions<InputOptions> options, ILogger<UinputGamepadService> logger)
    {
        _logger = logger;
        string deviceName = options.Value.GamepadDeviceName;

        _fd = TryCreateDevice(deviceName);
    }

    private int TryCreateDevice(string name)
    {
        if (!OperatingSystem.IsLinux())
        {
            LogGamepadLinuxOnly(_logger);
            return -1;
        }

        int fd = UinputNative.open("/dev/uinput", UinputNative.O_WRONLY | UinputNative.O_NONBLOCK);
        if (fd < 0)
        {
            LogGamepadOpenFailed(_logger, fd);
            return -1;
        }

        // Register event types and supported codes.
        _ = UinputNative.ioctl(fd, UinputNative.UI_SET_EVBIT, UinputNative.EV_KEY);
        _ = UinputNative.ioctl(fd, UinputNative.UI_SET_EVBIT, UinputNative.EV_ABS);
        _ = UinputNative.ioctl(fd, UinputNative.UI_SET_EVBIT, UinputNative.EV_SYN);

        // Register the complete Xbox 360 button set so SDL2 assigns correct button
        // indices: A=b0, B=b1, X=b2, Y=b3, LB=b4, RB=b5, Back=b6, Start=b7,
        // Guide=b8, LS=b9, RS=b10. Missing entries would shift SELECT/START indices.
        foreach (int code in UinputNative.GamepadButtonCodes.Values)
        {
            _ = UinputNative.ioctl(fd, UinputNative.UI_SET_KEYBIT, code);
        }

        _ = UinputNative.ioctl(fd, UinputNative.UI_SET_KEYBIT, UinputNative.BTN_TL);
        _ = UinputNative.ioctl(fd, UinputNative.UI_SET_KEYBIT, UinputNative.BTN_TR);
        _ = UinputNative.ioctl(fd, UinputNative.UI_SET_KEYBIT, UinputNative.BTN_MODE);
        _ = UinputNative.ioctl(fd, UinputNative.UI_SET_KEYBIT, UinputNative.BTN_THUMBL);
        _ = UinputNative.ioctl(fd, UinputNative.UI_SET_KEYBIT, UinputNative.BTN_THUMBR);

        // Analog sticks and triggers — registered with proper ranges so SDL2 includes
        // them in axis enumeration (a0–a5 matching the Xbox 360 gamecontrollerdb entry).
        SetupAbsAxis(fd, UinputNative.ABS_X, -32767, 32767, 16, 128);
        SetupAbsAxis(fd, UinputNative.ABS_Y, -32767, 32767, 16, 128);
        SetupAbsAxis(fd, UinputNative.ABS_Z, 0, 255, 0, 0);
        SetupAbsAxis(fd, UinputNative.ABS_RX, -32767, 32767, 16, 128);
        SetupAbsAxis(fd, UinputNative.ABS_RY, -32767, 32767, 16, 128);
        SetupAbsAxis(fd, UinputNative.ABS_RZ, 0, 255, 0, 0);

        // D-pad.
        SetupAbsAxis(fd, UinputNative.ABS_HAT0X, -1, 1, 0, 0);
        SetupAbsAxis(fd, UinputNative.ABS_HAT0Y, -1, 1, 0, 0);

        UinputNative.UinputSetup setup = new()
        {
            Id = new UinputNative.UinputId { BusType = 0x03, Vendor = 0x045e, Product = 0x028e, Version = 0x0110 },
            Name = name,
            FfEffectsMax = 0,
        };

        _ = UinputNative.ioctl(fd, UinputNative.UI_DEV_SETUP, ref setup);
        _ = UinputNative.ioctl(fd, UinputNative.UI_DEV_CREATE, 0);

        LogGamepadCreated(_logger, name);
        return fd;
    }

    public void PressButton(string buttonId, bool pressed)
    {
        if (!IsSupported)
        {
            return;
        }

        if (!UinputNative.GamepadButtonCodes.TryGetValue(buttonId, out int code))
        {
            LogGamepadUnknownButton(_logger, buttonId);
            return;
        }

        byte[] ev = UinputNative.SerialiseInputEvent((ushort)UinputNative.EV_KEY, (ushort)code, pressed ? 1 : 0);
        _ = UinputNative.write(_fd, ev, ev.Length);
        UinputNative.WriteSync(_fd);
    }

    public void SetDPad(int x, int y)
    {
        if (!IsSupported)
        {
            return;
        }

        // Clamp to valid D-pad range: -1, 0, 1
        int cx = Math.Clamp(x, -1, 1);
        int cy = Math.Clamp(y, -1, 1);

        byte[] evX = UinputNative.SerialiseInputEvent((ushort)UinputNative.EV_ABS, (ushort)UinputNative.ABS_HAT0X, cx);
        byte[] evY = UinputNative.SerialiseInputEvent((ushort)UinputNative.EV_ABS, (ushort)UinputNative.ABS_HAT0Y, cy);
        _ = UinputNative.write(_fd, evX, evX.Length);
        _ = UinputNative.write(_fd, evY, evY.Length);
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
            LogGamepadDestroyed(_logger);
        }
    }

    private static void SetupAbsAxis(int fd, int code, int min, int max, int fuzz, int flat)
    {
        _ = UinputNative.ioctl(fd, UinputNative.UI_SET_ABSBIT, code);
        UinputNative.UinputAbsSetup absSetup = new()
        {
            Code = (uint)code,
            Absinfo = new UinputNative.InputAbsinfo
            {
                Minimum = min,
                Maximum = max,
                Fuzz = fuzz,
                Flat = flat,
            },
        };
        _ = UinputNative.ioctl(fd, UinputNative.UI_ABS_SETUP, ref absSetup);
    }

    [LoggerMessage(EventId = 8001, Level = LogLevel.Warning, Message = "UinputGamepadService: uinput is Linux-only; gamepad input will be unavailable.")]
    private static partial void LogGamepadLinuxOnly(ILogger logger);

    [LoggerMessage(EventId = 8002, Level = LogLevel.Warning, Message = "UinputGamepadService: could not open /dev/uinput (fd={Fd}). Ensure the process user is in the 'input' group.")]
    private static partial void LogGamepadOpenFailed(ILogger logger, int fd);

    [LoggerMessage(EventId = 8003, Level = LogLevel.Information, Message = "UinputGamepadService: virtual gamepad '{Name}' created.")]
    private static partial void LogGamepadCreated(ILogger logger, string name);

    [LoggerMessage(EventId = 8004, Level = LogLevel.Warning, Message = "UinputGamepadService: unknown button id '{ButtonId}'.")]
    private static partial void LogGamepadUnknownButton(ILogger logger, string buttonId);

    [LoggerMessage(EventId = 8005, Level = LogLevel.Information, Message = "UinputGamepadService: virtual gamepad destroyed.")]
    private static partial void LogGamepadDestroyed(ILogger logger);
}
