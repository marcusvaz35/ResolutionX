using System.Windows;
using System.Windows.Threading;
using ResolutionX.App.Services;
using ResolutionX.App.ViewModels;
using ResolutionX.App.Views;
using ResolutionX.Core.Interfaces;
using ResolutionX.Core.Services;
using ResolutionX.VirtualDisplay;
using ResolutionX.Windows.Elevation;
using ResolutionX.Windows.Services;

namespace ResolutionX.App;

public partial class App : Application
{
    private IResolutionService? _resolutionService;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Cópia elevada do próprio app: executa só a operação de administrador pedida e sai, sem janela.
        if (e.Args.Length == 2 && e.Args[0] == ElevatedOperations.Switch)
        {
            Shutdown(ElevatedOperations.RunFromFile(e.Args[1]));
            return;
        }

        _resolutionService = new ResolutionService();
        DispatcherUnhandledException += OnUnhandledException;

        var edidService = new EdidService();
        var viewModel = new MainViewModel(
            new DisplayService(),
            _resolutionService,
            new PresetStore(),
            new VirtualDisplayService(),
            new CustomResolutionService(edidService),
            edidService,
            new DialogService());

        var version = typeof(App).Assembly.GetName().Version?.ToString(3);
        var window = new MainWindow { DataContext = viewModel, Title = $"ResolutionX {version}" };
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Se o app fechar no meio de um teste, o usuário não pode ficar na resolução não confirmada.
        RestoreIfPending();
        base.OnExit(e);
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        var restored = RestoreIfPending();
        e.Handled = true;

        MessageBox.Show(
            "Ocorreu um erro inesperado no ResolutionX." +
            (restored ? "\nA resolução anterior foi restaurada." : "") +
            "\n\nDetalhes técnicos:\n" + e.Exception.Message,
            "ResolutionX",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private bool RestoreIfPending()
    {
        if (_resolutionService is not { HasPendingChange: true })
            return false;

        return _resolutionService.RestorePrevious().Success;
    }
}
