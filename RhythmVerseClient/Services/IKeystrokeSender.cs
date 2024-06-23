using System.Runtime.InteropServices;

namespace RhythmVerseClient.Services
{
    public interface IKeystrokeSender
    {
        void SendKeys(string keys);
        public void SendKey(byte keyCode);
        public void SendSpecialKey(string specialKey);
    }

    public class WindowsKeystrokeSender : IKeystrokeSender
    {
        [DllImport("user32.dll", SetLastError = true)]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        private const int KEYEVENTF_KEYUP = 0x0002;

        public void SendKeys(string keys)
        {
            foreach (char key in keys)
            {
                SendKey((byte)key);
            }
        }

        public void SendKey(byte keyCode)
        {
            keybd_event(keyCode, 0, 0, UIntPtr.Zero); // Key down
            keybd_event(keyCode, 0, KEYEVENTF_KEYUP, UIntPtr.Zero); // Key up
        }

        public void SendSpecialKey(string specialKey)
        {
            switch (specialKey.ToLower())
            {
                case "tab":
                    SendKey(VirtualKeyCodes.VK_TAB);
                    break;
                case "enter":
                    SendKey(VirtualKeyCodes.VK_ENTER);
                    break;
                    // Add cases for more special keys as needed
            }
        }
    }
}
