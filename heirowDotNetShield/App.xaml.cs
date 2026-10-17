using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace HeirowDotNetShield;

public partial class App : System.Windows.Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (e.Args.Length == 3 && string.Equals(e.Args[0], "--protect", StringComparison.OrdinalIgnoreCase))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            try
            {
                await ShieldService.ProtectAsync(
                    new ShieldRequest(e.Args[1], e.Args[2]),
                    progress: null,
                    CancellationToken.None);
                Shutdown(0);
            }
            catch (Exception ex)
            {
                try
                {
                    Directory.CreateDirectory(e.Args[2]);
                    await File.WriteAllTextAsync(Path.Combine(e.Args[2], "shield-error.txt"), ex.ToString());
                }
                catch
                {
                    // The original exception is the useful failure.
                }

                Shutdown(1);
            }

            return;
        }

        MainWindow = new MainWindow();
        MainWindow.Show();
    }
}
