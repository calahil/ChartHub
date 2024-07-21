using RhythmVerseClient.Utilities;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace RhythmVerseClient.Platforms.Windows
{
    public interface IWindowSizeService
    {
        double Height { get; }
        double Width { get; }

        event PropertyChangedEventHandler? PropertyChanged;

        void Refresh();
        (double Width, double Height) GetWindowSize();
    }

    public class WindowSizeService : IWindowSizeService, INotifyPropertyChanged
    {
        private double _height;
        public double Height
        {
            get => _height;
            set
            {
                if (_height != value)
                {
                    _height = value;
                    OnPropertyChanged();
                }
            }
        }

        private double _width;
        public double Width
        {
            get => _width;
            set
            {
                _width = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public WindowSizeService()
        {
            _height = 0;
            _width = 0;
        }

        public void Refresh()
        {
            try
            {
                var window = App.Current.Windows[0].Handler.PlatformView as Microsoft.UI.Xaml.Window;
                var windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(window);
                var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(windowHandle);
                var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);


                Width = appWindow.Size.Width;
                Height = appWindow.Size.Height;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex);
                Width = 0;
                Height = 0;
            }
        }

        public (double Width, double Height) GetWindowSize()
        {
            try
            {
                var window = App.Current.Windows[0].Handler.PlatformView as Microsoft.UI.Xaml.Window;
                var windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(window);
                var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(windowHandle);
                var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);

                return (appWindow.Size.Width, appWindow.Size.Height);
            }
            catch(Exception ex)
            {
                Logger.LogError(ex);
                return (0, 0);
            }
        }

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

    }
}
