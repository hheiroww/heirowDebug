using System.Windows;

namespace heirowDebugInjector;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        global::SocketJack.HeirowUpdaterBootstrap.StartAfterExit();
        base.OnStartup(e);
    }

    public static bool CheckForUpdates() => global::SocketJack.HeirowUpdaterBootstrap.CheckForUpdates();
}
