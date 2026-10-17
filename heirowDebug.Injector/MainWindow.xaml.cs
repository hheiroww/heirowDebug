using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;

namespace heirowDebugInjector;

public partial class MainWindow : Window
{
    public ObservableCollection<WpfProcessInfo> Processes { get; } = [];

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
        Loaded += async (_, _) => await RefreshProcessesAsync();
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e) => await RefreshProcessesAsync();

    private async Task RefreshProcessesAsync()
    {
        SetBusy(true, "Inspecting running processes for WPF...");
        var selectedId = (ProcessGrid.SelectedItem as WpfProcessInfo)?.Id;
        var processes = await Task.Run(InjectorService.GetWpfProcesses);
        Processes.Clear();
        foreach (var process in processes)
        {
            Processes.Add(process);
        }

        ProcessGrid.SelectedItem = Processes.FirstOrDefault(process => process.Id == selectedId);
        SetBusy(false, Processes.Count == 0
            ? "No accessible WPF processes are currently running."
            : $"Found {Processes.Count} accessible WPF process{(Processes.Count == 1 ? string.Empty : "es")}. Select one to inject.");
    }

    private void ProcessGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        InjectButton.IsEnabled = ProcessGrid.SelectedItem is WpfProcessInfo;
        if (e.AddedItems.Count > 0)
        {
            RootTypeTextBox.Text = string.Empty;
            StatusText.Text = "All instantiated windows will be discovered inside the selected WPF process; static types can be added from the injected debugger.";
        }
    }

    private async void InjectButton_Click(object sender, RoutedEventArgs e)
    {
        if (ProcessGrid.SelectedItem is not WpfProcessInfo process)
        {
            return;
        }

        var confirmation = MessageBox.Show(
            $"Restart {process.Name} (PID {process.Id}) and inject heirowDebug?\n\nSave any work in that application before continuing.",
            "Restart and inject",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning);
        if (confirmation != MessageBoxResult.OK)
        {
            return;
        }

        string? completionStatus = null;
        try
        {
            SetBusy(true, $"Stopping {process.Name} (PID {process.Id})...");
            var progress = new Progress<string>(message => StatusText.Text = message);
            var result = await InjectorService.RestartAndInjectAsync(
                process,
                string.IsNullOrWhiteSpace(RootTypeTextBox.Text) ? null : RootTypeTextBox.Text.Trim(),
                RecursiveCheckBox.IsChecked == true,
                progress);
            completionStatus = result;
            StatusText.Text = completionStatus;
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Injection failed: {ex.Message}";
            MessageBox.Show(ex.Message, "heirowDebug injection failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false, StatusText.Text);
            await RefreshProcessesAsync();
            IfCompletedRestoreStatus(completionStatus);
        }
    }

    private void IfCompletedRestoreStatus(string? completionStatus)
    {
        if (!string.IsNullOrWhiteSpace(completionStatus))
        {
            StatusText.Text = completionStatus;
        }
    }

    private void SetBusy(bool busy, string status)
    {
        RefreshButton.IsEnabled = !busy;
        ProcessGrid.IsEnabled = !busy;
        InjectButton.IsEnabled = !busy && ProcessGrid.SelectedItem is WpfProcessInfo;
        StatusText.Text = status;
    }

}
