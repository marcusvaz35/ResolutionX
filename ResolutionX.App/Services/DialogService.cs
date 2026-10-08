using System.Windows;
using ResolutionX.App.Views;
using ResolutionX.Core.Models;

namespace ResolutionX.App.Services;

public sealed class DialogService : IDialogService
{
    public bool ConfirmKeepResolution(MonitorInfo monitor, DisplayMode mode, int timeoutSeconds)
    {
        var dialog = new ConfirmResolutionWindow(monitor, mode, timeoutSeconds);
        return dialog.ShowDialog() == true;
    }

    public bool Confirm(string title, string message)
        => MessageBox.Show(
            Application.Current.MainWindow!, message, title, MessageBoxButton.YesNo, MessageBoxImage.Question,
            MessageBoxResult.No) == MessageBoxResult.Yes;

    public void ShowDiagnostics(string report)
    {
        var window = new DiagnosticsWindow(report) { Owner = Application.Current.MainWindow };
        window.ShowDialog();
    }
}
