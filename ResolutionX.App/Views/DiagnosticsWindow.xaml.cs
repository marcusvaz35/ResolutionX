using System.Runtime.InteropServices;
using System.Windows;

namespace ResolutionX.App.Views;

public partial class DiagnosticsWindow : Window
{
    public DiagnosticsWindow(string report)
    {
        InitializeComponent();
        ReportText.Text = report;
    }

    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(ReportText.Text);
            CopiedText.Text = "Copiado para a área de transferência.";
        }
        catch (COMException)
        {
            // Outro programa está segurando a área de transferência neste instante.
            CopiedText.Text = "Não foi possível copiar agora. Tente novamente.";
        }
    }
}
