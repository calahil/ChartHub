namespace ChartHub.Server.Options;

public sealed class InputOptions
{
    public const string SectionName = "Input";

    public string GamepadDeviceName { get; set; } = "ChartHub Gamepad";

    public string MouseDeviceName { get; set; } = "ChartHub Mouse";

    public string KeyboardDeviceName { get; set; } = "ChartHub Keyboard";
}
