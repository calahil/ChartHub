namespace ChartHub.Server.Services;

public interface IUinputMouseService
{
    bool IsSupported { get; }

    void MoveDelta(int dx, int dy);

    void PressButton(string side, bool pressed);
}
