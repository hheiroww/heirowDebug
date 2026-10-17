using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Forms = System.Windows.Forms;

namespace HeirowDotNetShield;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void BrowseInput_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = ".NET assemblies (*.dll)|*.dll|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false,
            Title = "Select the application's main DLL"
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        InputPathTextBox.Text = dialog.FileName;
        var directory = Path.GetDirectoryName(dialog.FileName)!;
        var appName = Path.GetFileNameWithoutExtension(dialog.FileName);
        OutputPathTextBox.Text = Path.GetFullPath(Path.Combine(directory, "..", $"{appName}.shielded"));
    }

    private void BrowseOutput_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new Forms.FolderBrowserDialog
        {
            Description = "Choose an empty directory for the protected build",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true,
            SelectedPath = OutputPathTextBox.Text
        };

        if (dialog.ShowDialog() == Forms.DialogResult.OK)
        {
            OutputPathTextBox.Text = dialog.SelectedPath;
        }
    }

    private async void Protect_Click(object sender, RoutedEventArgs e)
    {
        ProtectButton.IsEnabled = false;
        LogText.Text = string.Empty;
        StatusText.Text = "Protecting copied build...";

        try
        {
            var progress = new Progress<string>(AppendLog);
            var result = await ShieldService.ProtectAsync(
                new ShieldRequest(InputPathTextBox.Text, OutputPathTextBox.Text),
                progress,
                CancellationToken.None);
            StatusText.Text = "Protected build created and verified.";
            AppendLog($"DONE  {result.OutputAssembly}");

            var outputDirectory = Path.GetDirectoryName(result.OutputAssembly)!;
            Process.Start(new ProcessStartInfo("explorer.exe", outputDirectory) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            StatusText.Text = "Protection failed; the original build was not changed.";
            AppendLog($"ERROR {ex.Message}");
            System.Windows.MessageBox.Show(this, ex.Message, "heirowDotNetShield", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            ProtectButton.IsEnabled = true;
        }
    }

    private void AppendLog(string message)
    {
        LogText.Text += (LogText.Text.Length == 0 ? string.Empty : Environment.NewLine) + message;
        LogText.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Background);
    }
}
