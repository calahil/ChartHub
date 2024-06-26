using SharpCompress.Archives;
using SharpCompress.Common;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace RhythmVerseClient.Services
{
    public partial class Nautilus(IFileSystemManager fileSystem, IKeystrokeSender keystrokeSender)
    {
        public ProcessStartInfo? CmdArgs { get; set; }
        public Process? Program { get; set; }
        public nint Hwnd { get; set; }

        //private string nautilisEXE;

        private readonly IFileSystemManager _fileSystem = fileSystem;
        private readonly IKeystrokeSender _keystrokeSender = keystrokeSender;

        public static void Initialize()
        {
            /*if (!File.Exists(Constants.ZipFilePath) && !File.Exists(nautilisEXE))
            {
                await DownloadFileAsync();
            }

            if (File.Exists(Constants.ZipFilePath) && !File.Exists(nautilisEXE))
            {
                ExtractZipFile();
            }

            if (File.Exists(Constants.ZipFilePath) && File.Exists(nautilisEXE))
            {
                File.Delete(Constants.ZipFilePath);
            }*/

            //if (Constants.NAUTILUS_ARGS != null && nautilisEXE != null)
            //{
            //    CmdArgs = new(nautilisEXE, Constants.NAUTILUS_ARGS);
            //}
        }
        
        public void Run()
        {
            if (CmdArgs == null)
                return;

            Program = Process.Start(CmdArgs);

            if (Program == null)
                return;

            Program.WaitForInputIdle();
            Hwnd = Program.MainWindowHandle;

            User32.ShowWindow(Hwnd, 2);

            Thread.Sleep(1000);

            _keystrokeSender.SendSpecialKey("tab");
            _keystrokeSender.SendSpecialKey("tab");
            _keystrokeSender.SendSpecialKey("tab");
            _keystrokeSender.SendSpecialKey("enter");

            Thread.Sleep(1000);

            int counter = 0;
            var test = true;
            while (test)
            {
                TimeSpan prevCpuTime = Program.TotalProcessorTime;
                Thread.Sleep(5000); // Wait for 1 second
                TimeSpan currCpuTime = Program.TotalProcessorTime;

                if (currCpuTime - prevCpuTime > TimeSpan.FromMilliseconds(100))
                {
                    counter = 0;
                }
                else
                {
                    if (counter < 2)
                    {
                        counter++;
                    }
                    else
                    {
                        test = false;
                    }
                }
            }

            Program.Kill();
            Program.WaitForExit();
            //_fileSystem.ResourceWatchers[0]?.RefreshItems();
        }
    }

    public static class Constants
    {
        public const string NAUTILUS_ARGS = "-clonehero";
        /*public const string ZIP_FILE_URL = "https://calahil.github.io/nautilus.zip";
        public static readonly string NautilusDirectoryPath = Path.Combine(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "rhythmverse"), "nautilus");
        public static readonly string ZipFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "nautilus.zip");*/
    }

    public static partial class User32
    {
        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool SetForegroundWindow(nint hWnd);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool ShowWindowAsync(nint hWnd, int nCmdShow);

        [LibraryImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool BringWindowToTop(nint hWnd);

        [LibraryImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool ShowWindow(nint hWnd, uint nCmdShow);
    }

    public static class VirtualKeyCodes
    {
        public const byte VK_TAB = 0x09;
        public const byte VK_ENTER = 0x0D;
        // Add more key codes as needed
    }
}
