using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using ResolutionX.Core.Models;

namespace ResolutionX.App.Views;

/// <summary>
/// Confirmação do teste de segurança. Fecha com resultado "false" (restaurar) se o usuário
/// recusar, fechar a janela, apertar Esc ou simplesmente não responder dentro do prazo.
/// </summary>
public partial class ConfirmResolutionWindow : Window
{
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOZORDER = 0x0004;

    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly MonitorInfo _monitor;
    private int _remainingSeconds;

    public ConfirmResolutionWindow(MonitorInfo monitor, DisplayMode mode, int timeoutSeconds)
    {
        InitializeComponent();

        _monitor = monitor;
        _remainingSeconds = timeoutSeconds;

        ModeText.Text = $"{mode}  —  Monitor {monitor.Index}";
        CountdownBar.Maximum = timeoutSeconds;
        UpdateCountdown();

        _timer.Tick += OnTick;
        Loaded += OnLoaded;
        Closed += (_, _) => _timer.Stop();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        CenterOnTargetMonitor();
        Activate();
        _timer.Start();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        _remainingSeconds--;
        if (_remainingSeconds <= 0)
        {
            _timer.Stop();
            DialogResult = false;
            return;
        }

        UpdateCountdown();
    }

    private void UpdateCountdown()
    {
        CountdownBar.Value = _remainingSeconds;
        CountdownText.Text = _remainingSeconds == 1
            ? "A resolução anterior será restaurada em 1 segundo."
            : $"A resolução anterior será restaurada em {_remainingSeconds} segundos.";
    }

    private void OnKeepClick(object sender, RoutedEventArgs e)
    {
        _timer.Stop();
        DialogResult = true;
    }

    private void OnRestoreClick(object sender, RoutedEventArgs e)
    {
        _timer.Stop();
        DialogResult = false;
    }

    /// <summary>
    /// A pergunta só faz sentido se aparecer no monitor que mudou. A posição é definida em
    /// pixels físicos via Win32, pois Left/Top do WPF dependem da escala de cada monitor.
    /// </summary>
    private void CenterOnTargetMonitor()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero || !GetWindowRect(handle, out var rect))
            return;

        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;
        var x = _monitor.PositionX + Math.Max(0, (_monitor.CurrentMode.Width - width) / 2);
        var y = _monitor.PositionY + Math.Max(0, (_monitor.CurrentMode.Height - height) / 2);

        SetWindowPos(handle, IntPtr.Zero, x, y, 0, 0, SWP_NOSIZE | SWP_NOZORDER);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);
}
