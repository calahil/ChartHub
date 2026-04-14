namespace ChartHub.Server.Contracts;

/// <summary>
/// Discriminated input message sent from the Android client over WebSocket.
/// All messages carry a <c>type</c> field for dispatch.
/// </summary>

/// <summary>Controller face/shoulder button event.</summary>
public sealed class ControllerButtonMessage
{
    /// <summary>Message type discriminator. Value: "btn".</summary>
    public string Type { get; init; } = "btn";

    /// <summary>Button identifier: "a", "b", "x", "y", "select", "start".</summary>
    public required string ButtonId { get; init; }

    /// <summary>True = pressed; false = released.</summary>
    public bool Pressed { get; init; }
}

/// <summary>D-pad directional event.</summary>
public sealed class ControllerDPadMessage
{
    /// <summary>Message type discriminator. Value: "dpad".</summary>
    public string Type { get; init; } = "dpad";

    /// <summary>Horizontal axis: -1 (left), 0 (centre), 1 (right).</summary>
    public int X { get; init; }

    /// <summary>Vertical axis: -1 (up), 0 (centre), 1 (down).</summary>
    public int Y { get; init; }
}

/// <summary>Relative mouse movement event.</summary>
public sealed class TouchpadMoveMessage
{
    /// <summary>Message type discriminator. Value: "move".</summary>
    public string Type { get; init; } = "move";

    public int Dx { get; init; }

    public int Dy { get; init; }
}

/// <summary>Mouse button press/release event.</summary>
public sealed class TouchpadButtonMessage
{
    /// <summary>Message type discriminator. Value: "mousebtn".</summary>
    public string Type { get; init; } = "mousebtn";

    /// <summary>"left" or "right".</summary>
    public required string Side { get; init; }

    public bool Pressed { get; init; }
}

/// <summary>Raw key code press/release event (for special keys).</summary>
public sealed class KeyboardKeyMessage
{
    /// <summary>Message type discriminator. Value: "key".</summary>
    public string Type { get; init; } = "key";

    /// <summary>Linux input subsystem key code (e.g. KEY_ENTER = 28).</summary>
    public int LinuxKeyCode { get; init; }

    public bool Pressed { get; init; }
}

/// <summary>Single printable character typed via the Android IME.</summary>
public sealed class KeyboardCharMessage
{
    /// <summary>Message type discriminator. Value: "char".</summary>
    public string Type { get; init; } = "char";

    /// <summary>A single printable character.</summary>
    public required string Char { get; init; }
}
