namespace ChartHub.Server.Services;

public interface IUinputKeyboardService
{
    bool IsSupported { get; }

    void PressKey(int linuxKeyCode, bool pressed);

    void TypeChar(char c);
}
