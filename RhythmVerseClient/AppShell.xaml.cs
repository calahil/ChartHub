using RhythmVerseClient.Pages;

namespace RhythmVerseClient
{
    public partial class AppShell : Shell
    {
        public AppShell()
        {
            InitializeComponent();

            Routing.RegisterRoute("download", typeof(DownloadPage));
            Routing.RegisterRoute("clonehero", typeof(CloneHeroPage));
            Routing.RegisterRoute("installsong", typeof(InstallSongPage));
        }
    }
}
