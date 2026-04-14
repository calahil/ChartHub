namespace ChartHub.Server.Services;

public interface IUinputGamepadService
{
    bool IsSupported { get; }

    void PressButton(string buttonId, bool pressed);

    void SetDPad(int x, int y);
}
