using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;

namespace heirowDebug.ProtectedTestApp;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (e.Args.Length == 2 && string.Equals(e.Args[0], "--self-test", StringComparison.OrdinalIgnoreCase))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            try
            {
                var method = typeof(SelfDiagnostics)
                    .GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
                    .Single(candidate => candidate.GetCustomAttribute<ShieldSelfTestAttribute>() is not null);
                var reflectionResult = method.Invoke(null, null)?.ToString();
                var fingerprint = new SensitiveDataVault().ComputeFingerprint();
                var reportObject = new JsonObject
                {
                    ["ownAssemblyReflection"] = reflectionResult,
                    ["protectedValueFingerprint"] = fingerprint,
                    ["passed"] = !string.IsNullOrWhiteSpace(reflectionResult) && fingerprint.Length == 12
                };
                var report = reportObject.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(e.Args[1], report);
                Shutdown(0);
            }
            catch (Exception ex)
            {
                await File.WriteAllTextAsync(e.Args[1], ex.ToString());
                Shutdown(1);
            }

            return;
        }

        MainWindow = new MainWindow();
        MainWindow.Show();
    }
}
