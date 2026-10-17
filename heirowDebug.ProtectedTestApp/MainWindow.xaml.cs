using System.Reflection;
using System.Windows;

namespace heirowDebug.ProtectedTestApp;

public partial class MainWindow : Window
{
    private readonly SensitiveDataVault _vault = new();

    public MainWindow()
    {
        InitializeComponent();
        VaultStatusText.Text = "Plaintext is not stored in a field or property.";
        ReflectionStatusText.Text = "Available to code already inside this assembly.";
        ActivityText.Text = "The application remains running when external instrumentation is unavailable.";
        UseSecretButton.Click += (_, _) => UseProtectedValue();
        ReflectionButton.Click += (_, _) => RunOwnAssemblyReflection();
    }

    private void UseProtectedValue()
    {
        ActivityText.Text = $"Protected value used; verification fingerprint: {_vault.ComputeFingerprint()}";
    }

    private void RunOwnAssemblyReflection()
    {
        var method = typeof(SelfDiagnostics)
            .GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
            .Single(candidate => candidate.GetCustomAttribute<ShieldSelfTestAttribute>() is not null);
        var result = method.Invoke(null, null)?.ToString() ?? "no result";
        ReflectionStatusText.Text = $"Passed: {result}";
    }
}
