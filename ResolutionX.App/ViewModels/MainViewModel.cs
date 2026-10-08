using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using ResolutionX.App.Services;
using ResolutionX.Core.Interfaces;
using ResolutionX.Core.Models;
using ResolutionX.Core.Services;

namespace ResolutionX.App.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private const int ConfirmTimeoutSeconds = 15;

    private readonly IDisplayService _displayService;
    private readonly IResolutionService _resolutionService;
    private readonly IPresetStore _presetStore;
    private readonly IVirtualDisplayService _virtualDisplayService;
    private readonly ICustomResolutionService _customResolutionService;
    private readonly IEdidService _edidService;
    private readonly IDialogService _dialogs;

    private IReadOnlyList<MonitorInfo> _monitors = [];
    private MonitorInfo? _selectedMonitor;
    private IReadOnlyList<string> _refreshRates = [];
    private DisplayMode? _selectedMode;
    private IReadOnlyList<ResolutionPreset> _presets = [];
    private ResolutionPreset? _selectedPreset;
    private string _widthText = "";
    private string _heightText = "";
    private string _refreshText = "";
    private bool _isPhysical = true;
    private bool _isBusy;
    private string _infoMessage = "";
    private string _errorTitle = "";
    private string _errorSuggestion = "";
    private string _errorDetails = "";
    private bool _detailsVisible;
    private bool _canCreateFromError;
    private IReadOnlyList<DisplayMode> _customModes = [];
    private DisplayMode? _selectedCustomMode;

    // Evita que preencher os campos por código dispare a sincronização inversa.
    private bool _fillingFields;
    // Ao recarregar monitores depois de um teste, preserva o que o usuário digitou.
    private bool _keepInputOnSelection;

    public MainViewModel(
        IDisplayService displayService,
        IResolutionService resolutionService,
        IPresetStore presetStore,
        IVirtualDisplayService virtualDisplayService,
        ICustomResolutionService customResolutionService,
        IEdidService edidService,
        IDialogService dialogs)
    {
        _customResolutionService = customResolutionService;
        _edidService = edidService;
        _displayService = displayService;
        _resolutionService = resolutionService;
        _presetStore = presetStore;
        _virtualDisplayService = virtualDisplayService;
        _dialogs = dialogs;

        RefreshCommand = new RelayCommand(() => LoadMonitors(keepInput: false), () => !IsBusy);
        TestCommand = new RelayCommand(async () => await ChangeResolutionAsync(persist: false), CanChangeResolution);
        ApplyCommand = new RelayCommand(async () => await ChangeResolutionAsync(persist: true), CanChangeResolution);
        SavePresetCommand = new RelayCommand(SavePreset, () => TryParseInput(out _));
        DeletePresetCommand = new RelayCommand(DeletePreset, () => SelectedPreset is { IsBuiltIn: false });
        CreateCustomCommand = new RelayCommand(async () => await CreateCustomAsync(), CanCreateCustom);
        DeleteCustomCommand = new RelayCommand(
            async () => await DeleteCustomAsync(),
            () => !IsBusy && SelectedMonitor is not null && SelectedCustomMode is not null);
        DiagnosticsCommand = new RelayCommand(ShowDiagnostics);
        ToggleDetailsCommand = new RelayCommand(() => DetailsVisible = !DetailsVisible);

        VirtualStatusMessage = _virtualDisplayService.GetStatus().Message;
        Presets = _presetStore.GetAll();
        LoadMonitors(keepInput: false);
    }

    public ICommand RefreshCommand { get; }
    public ICommand TestCommand { get; }
    public ICommand ApplyCommand { get; }
    public ICommand SavePresetCommand { get; }
    public ICommand DeletePresetCommand { get; }
    public ICommand CreateCustomCommand { get; }
    public ICommand DeleteCustomCommand { get; }
    public ICommand DiagnosticsCommand { get; }
    public ICommand ToggleDetailsCommand { get; }

    public ObservableCollection<StatusItem> StatusItems { get; } = [];

    public string VirtualStatusMessage { get; }

    public IReadOnlyList<MonitorInfo> Monitors
    {
        get => _monitors;
        private set => SetProperty(ref _monitors, value);
    }

    public MonitorInfo? SelectedMonitor
    {
        get => _selectedMonitor;
        set
        {
            if (!SetProperty(ref _selectedMonitor, value) || value is null)
                return;

            var typed = (Width: WidthText, Height: HeightText, Refresh: RefreshText);
            CustomModes = _customResolutionService.GetCustomResolutions(value);

            var rates = value.SupportedModes
                .Select(m => m.RefreshRate)
                .Distinct()
                .OrderByDescending(r => r)
                .Select(r => r.ToString())
                .ToList();
            if (!rates.SequenceEqual(_refreshRates))
                RefreshRates = rates;

            // Trocar a lista de taxas pode fazer a caixa editável limpar o texto;
            // por isso os campos são sempre reescritos depois.
            if (_keepInputOnSelection)
                FillFields(typed.Width, typed.Height, typed.Refresh);
            else
                FillFields(value.CurrentMode);

            UpdateStatus();
        }
    }

    public IReadOnlyList<string> RefreshRates
    {
        get => _refreshRates;
        private set => SetProperty(ref _refreshRates, value);
    }

    /// <summary>Modo escolhido na lista de resoluções suportadas; escolher um preenche os campos.</summary>
    public DisplayMode? SelectedMode
    {
        get => _selectedMode;
        set
        {
            if (!SetProperty(ref _selectedMode, value) || value is null || _fillingFields)
                return;

            ClearPresetSelection();
            FillFields(value);
            UpdateStatus();
        }
    }

    public IReadOnlyList<ResolutionPreset> Presets
    {
        get => _presets;
        private set => SetProperty(ref _presets, value);
    }

    public ResolutionPreset? SelectedPreset
    {
        get => _selectedPreset;
        set
        {
            if (!SetProperty(ref _selectedPreset, value) || value is null)
                return;

            FillFields(value.ToMode());
            UpdateStatus();
        }
    }

    /// <summary>Resoluções personalizadas criadas pelo ResolutionX para o monitor selecionado.</summary>
    public IReadOnlyList<DisplayMode> CustomModes
    {
        get => _customModes;
        private set
        {
            if (SetProperty(ref _customModes, value))
                OnPropertyChanged(nameof(HasCustomModes));
        }
    }

    public bool HasCustomModes => _customModes.Count > 0;

    public DisplayMode? SelectedCustomMode
    {
        get => _selectedCustomMode;
        set
        {
            if (!SetProperty(ref _selectedCustomMode, value) || value is null)
                return;

            FillFields(value);
            UpdateStatus();
        }
    }

    /// <summary>O último erro foi "o driver não oferece esta resolução": mostra o atalho para criá-la.</summary>
    public bool CanCreateFromError
    {
        get => _canCreateFromError;
        private set => SetProperty(ref _canCreateFromError, value);
    }

    public string WidthText
    {
        get => _widthText;
        set { if (SetProperty(ref _widthText, value)) OnFieldEdited(); }
    }

    public string HeightText
    {
        get => _heightText;
        set { if (SetProperty(ref _heightText, value)) OnFieldEdited(); }
    }

    public string RefreshText
    {
        get => _refreshText;
        set { if (SetProperty(ref _refreshText, value ?? "")) OnFieldEdited(); }
    }

    public bool IsPhysical
    {
        get => _isPhysical;
        set
        {
            if (!SetProperty(ref _isPhysical, value))
                return;

            OnPropertyChanged(nameof(IsVirtual));
            UpdateStatus();
        }
    }

    public bool IsVirtual
    {
        get => !_isPhysical;
        set => IsPhysical = !value;
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
                CommandManager.InvalidateRequerySuggested();
        }
    }

    public string InfoMessage
    {
        get => _infoMessage;
        private set
        {
            if (SetProperty(ref _infoMessage, value))
                OnPropertyChanged(nameof(HasInfo));
        }
    }

    public bool HasInfo => _infoMessage.Length > 0;

    public string ErrorTitle
    {
        get => _errorTitle;
        private set
        {
            if (SetProperty(ref _errorTitle, value))
                OnPropertyChanged(nameof(HasError));
        }
    }

    public bool HasError => _errorTitle.Length > 0;

    public string ErrorSuggestion
    {
        get => _errorSuggestion;
        private set => SetProperty(ref _errorSuggestion, value);
    }

    public string ErrorDetails
    {
        get => _errorDetails;
        private set
        {
            if (SetProperty(ref _errorDetails, value))
                OnPropertyChanged(nameof(HasErrorDetails));
        }
    }

    public bool HasErrorDetails => _errorDetails.Length > 0;

    public bool DetailsVisible
    {
        get => _detailsVisible;
        set => SetProperty(ref _detailsVisible, value);
    }

    private bool CanChangeResolution() => !IsBusy && IsPhysical && SelectedMonitor is not null;

    private bool CanCreateCustom()
        => !IsBusy && IsPhysical && SelectedMonitor is { } monitor &&
           TryParseInput(out var mode) && !monitor.Supports(mode);

    private void LoadMonitors(bool keepInput)
    {
        var selectedDevice = SelectedMonitor?.DeviceName;

        IReadOnlyList<MonitorInfo> monitors;
        try
        {
            monitors = _displayService.GetMonitors();
        }
        catch (Exception ex)
        {
            ShowError(OperationResult.Fail(
                "Não foi possível ler os monitores do sistema.",
                "Clique em Atualizar para tentar novamente.",
                ex.ToString()));
            return;
        }

        _keepInputOnSelection = keepInput;
        try
        {
            // Zera a seleção antes para que o mesmo monitor, agora com dados novos, seja reprocessado.
            _selectedMonitor = null;
            Monitors = monitors;
            SelectedMonitor = monitors.FirstOrDefault(m => m.DeviceName == selectedDevice)
                              ?? monitors.FirstOrDefault(m => m.IsPrimary)
                              ?? monitors.FirstOrDefault();
            OnPropertyChanged(nameof(SelectedMonitor));
        }
        finally
        {
            _keepInputOnSelection = false;
        }

        UpdateStatus();
    }

    private void FillFields(DisplayMode mode)
        => FillFields(mode.Width.ToString(), mode.Height.ToString(), mode.RefreshRate.ToString());

    private void FillFields(string width, string height, string refresh)
    {
        _fillingFields = true;
        try
        {
            WidthText = width;
            HeightText = height;
            RefreshText = refresh;
            // Garante que a tela reflita o valor mesmo que o campo interno não tenha mudado.
            OnPropertyChanged(nameof(RefreshText));
        }
        finally
        {
            _fillingFields = false;
        }

        SyncSelectedModeFromFields();
    }

    private void OnFieldEdited()
    {
        if (_fillingFields)
            return;

        // Depois de editar à mão, clicar de novo no mesmo preset deve voltar a preencher os campos.
        ClearPresetSelection();
        SyncSelectedModeFromFields();
        UpdateStatus();
    }

    private void ClearPresetSelection()
    {
        if (_selectedPreset is null)
            return;

        _selectedPreset = null;
        OnPropertyChanged(nameof(SelectedPreset));
    }

    /// <summary>Marca na lista o modo suportado que corresponde aos campos, se houver.</summary>
    private void SyncSelectedModeFromFields()
    {
        DisplayMode? match = null;
        if (SelectedMonitor is { } monitor && TryParseInput(out var mode) && monitor.Supports(mode))
            match = mode;

        _fillingFields = true;
        try
        {
            SelectedMode = match;
        }
        finally
        {
            _fillingFields = false;
        }
    }

    private bool TryParseInput(out DisplayMode mode)
    {
        mode = new DisplayMode(0, 0, 0);
        if (!int.TryParse(WidthText.Trim(), out var width) || width is < 320 or > 16384) return false;
        if (!int.TryParse(HeightText.Trim(), out var height) || height is < 200 or > 16384) return false;
        if (!int.TryParse(RefreshText.Trim(), out var refresh) || refresh is < 1 or > 1000) return false;

        mode = new DisplayMode(width, height, refresh);
        return true;
    }

    private void UpdateStatus()
    {
        StatusItems.Clear();

        var monitor = SelectedMonitor;
        if (monitor is null)
        {
            StatusItems.Add(StatusItem.Error("Nenhum monitor detectado"));
            return;
        }

        StatusItems.Add(StatusItem.Ok($"Monitor detectado: {monitor.FriendlyName}"));

        var gpu = monitor.Gpu;
        if (gpu.IsBasicDriver)
            StatusItems.Add(StatusItem.Warning(
                "Driver básico da Microsoft em uso: instale o driver do fabricante para ter mais resoluções"));
        else if (gpu.DriverVersion is null)
            StatusItems.Add(StatusItem.Warning($"Driver {gpu.VendorName}: versão não identificada"));
        else
            StatusItems.Add(StatusItem.Ok($"Driver compatível: {gpu.VendorName} {gpu.DriverVersion}"));

        if (IsVirtual)
        {
            StatusItems.Add(StatusItem.Warning("Monitor virtual indisponível: " + VirtualStatusMessage));
            return;
        }

        if (!TryParseInput(out var mode))
            StatusItems.Add(StatusItem.Error("Informe largura, altura e taxa de atualização válidas"));
        else if (mode == monitor.CurrentMode)
            StatusItems.Add(StatusItem.Ok("Esta é a resolução em uso"));
        else if (monitor.Supports(mode))
            StatusItems.Add(StatusItem.Ok("Resolução disponível"));
        else
            StatusItems.Add(StatusItem.Warning(
                "Resolução fora da lista do driver: use CRIAR RESOLUÇÃO PERSONALIZADA para adicioná-la"));
    }

    private async Task ChangeResolutionAsync(bool persist)
    {
        var monitor = SelectedMonitor;
        if (monitor is null || IsBusy)
            return;

        ClearMessages();

        if (!TryParseInput(out var mode))
        {
            ShowError(OperationResult.Fail(
                "Os valores informados não são válidos.",
                "Informe largura, altura e taxa de atualização usando apenas números."));
            return;
        }

        IsBusy = true;
        try
        {
            if (mode == monitor.CurrentMode)
            {
                if (!persist)
                {
                    InfoMessage = "Esta resolução já está em uso neste monitor.";
                    return;
                }

                var saved = await Task.Run(() => _resolutionService.ConfirmCurrent(monitor, persist: true));
                if (saved.Success)
                    InfoMessage = $"{mode} está em uso e foi salva como resolução do monitor.";
                else
                    ShowError(saved);
                return;
            }

            var applied = await Task.Run(() => _resolutionService.ApplyTemporary(monitor, mode));
            if (!applied.Success)
            {
                ShowError(applied);
                return;
            }

            // O diálogo precisa da posição e do tamanho novos para aparecer no monitor que mudou.
            LoadMonitors(keepInput: true);
            var updated = Monitors.FirstOrDefault(m => m.DeviceName == monitor.DeviceName) ?? monitor;

            if (_dialogs.ConfirmKeepResolution(updated, mode, ConfirmTimeoutSeconds))
            {
                var kept = await Task.Run(() => _resolutionService.ConfirmCurrent(updated, persist));
                if (!kept.Success)
                    ShowError(kept);
                else if (persist)
                    InfoMessage = $"{mode} aplicada e salva como resolução do monitor.";
                else
                    InfoMessage = $"{mode} em uso até o próximo reinício. Clique em APLICAR para torná-la permanente.";
            }
            else
            {
                var restored = await Task.Run(() => _resolutionService.RestorePrevious());
                if (restored.Success)
                    InfoMessage = "A resolução anterior foi restaurada.";
                else
                    ShowError(restored);
            }
        }
        catch (Exception ex)
        {
            var restored = !_resolutionService.HasPendingChange || _resolutionService.RestorePrevious().Success;
            ShowError(OperationResult.Fail(
                "Ocorreu um erro inesperado ao trocar a resolução.",
                restored
                    ? "A resolução anterior foi mantida ou restaurada."
                    : "Não foi possível restaurar automaticamente. Reinicie o computador para voltar à resolução anterior.",
                ex.ToString()));
        }
        finally
        {
            IsBusy = false;
            LoadMonitors(keepInput: true);
        }
    }

    private async Task CreateCustomAsync()
    {
        var monitor = SelectedMonitor;
        if (monitor is null || IsBusy || !TryParseInput(out var mode))
            return;

        ClearMessages();

        var check = _customResolutionService.CheckCanCreate(monitor, mode);
        if (!check.Success)
        {
            ShowError(check);
            return;
        }

        var message =
            $"Criar {mode} para o Monitor {monitor.Index} ({monitor.FriendlyName})?\n\n" +
            "• O Windows vai pedir permissão de administrador.\n" +
            "• O driver de vídeo será reiniciado: as telas ficam pretas por alguns segundos.\n" +
            "• A resolução só é adicionada à lista. Para usá-la, clique depois em TESTAR RESOLUÇÃO.";
        if (mode.Width > monitor.MaxMode.Width || mode.Height > monitor.MaxMode.Height)
        {
            message +=
                $"\n\nAtenção: ela é maior que a resolução máxima deste monitor ({monitor.MaxMode.Width} × " +
                $"{monitor.MaxMode.Height}). Muitos monitores, e quase todas as telas de notebook, não conseguem " +
                "exibir isso. Se a imagem não aparecer no teste, a resolução anterior volta sozinha em 15 segundos.";
        }

        if (!_dialogs.Confirm("Criar resolução personalizada", message))
            return;

        IsBusy = true;
        try
        {
            var created = await Task.Run(() => _customResolutionService.CreateResolution(monitor, mode));
            if (!created.Success)
            {
                ShowError(created);
                return;
            }

            var listed = await WaitForModeAsync(monitor, mode, shouldBeListed: true);
            if (listed)
                InfoMessage = $"{mode} foi criada e já aparece na lista. Clique em TESTAR RESOLUÇÃO para experimentá-la.";
            else
                InfoMessage =
                    $"{mode} foi gravada no Windows, mas o driver ainda não a oferece. " +
                    (created.Message.Length > 0 ? created.Message + " " : "Reinicie o computador. ") +
                    $"Se depois de reiniciar ela continuar sem aparecer, o driver {monitor.Gpu.VendorName} não aceita " +
                    "esta resolução para este monitor; nesse caso exclua-a em Resoluções personalizadas.";
        }
        catch (Exception ex)
        {
            ShowError(OperationResult.Fail(
                "Ocorreu um erro inesperado ao criar a resolução.",
                "Clique em Atualizar e confira a lista de Resoluções personalizadas.",
                ex.ToString()));
        }
        finally
        {
            IsBusy = false;
            LoadMonitors(keepInput: true);
        }
    }

    private async Task DeleteCustomAsync()
    {
        var monitor = SelectedMonitor;
        var mode = SelectedCustomMode;
        if (monitor is null || mode is null || IsBusy)
            return;

        ClearMessages();

        if (mode == monitor.CurrentMode)
        {
            ShowError(OperationResult.Fail(
                "Esta resolução está em uso agora.",
                "Troque o monitor para outra resolução antes de excluí-la."));
            return;
        }

        if (!_dialogs.Confirm(
                "Excluir resolução personalizada",
                $"Excluir {mode} do Monitor {monitor.Index}?\n\n" +
                "O Windows vai pedir permissão de administrador e o driver de vídeo será reiniciado " +
                "(as telas ficam pretas por alguns segundos)."))
            return;

        IsBusy = true;
        try
        {
            var deleted = await Task.Run(() => _customResolutionService.DeleteResolution(monitor, mode));
            if (!deleted.Success)
            {
                ShowError(deleted);
                return;
            }

            var gone = await WaitForModeAsync(monitor, mode, shouldBeListed: false);
            InfoMessage = gone
                ? $"{mode} foi excluída."
                : $"{mode} foi excluída do Windows. Ela sai da lista do driver depois de reiniciar o computador.";
        }
        catch (Exception ex)
        {
            ShowError(OperationResult.Fail(
                "Ocorreu um erro inesperado ao excluir a resolução.",
                "Clique em Atualizar e confira a lista de Resoluções personalizadas.",
                ex.ToString()));
        }
        finally
        {
            IsBusy = false;
            SelectedCustomMode = null;
            LoadMonitors(keepInput: true);
        }
    }

    /// <summary>
    /// Depois que o driver reinicia, o monitor some e volta em alguns segundos. Espera até a lista
    /// de modos refletir a mudança, em vez de presumir que ela aconteceu.
    /// </summary>
    private async Task<bool> WaitForModeAsync(MonitorInfo monitor, DisplayMode mode, bool shouldBeListed)
    {
        for (var attempt = 0; attempt < 12; attempt++)
        {
            await Task.Delay(1000);
            try
            {
                var monitors = await Task.Run(() => _displayService.GetMonitors());
                var current = monitors.FirstOrDefault(m => m.InstanceId == monitor.InstanceId);
                if (current is not null && current.Supports(mode) == shouldBeListed)
                    return true;
            }
            catch (Exception)
            {
                // Durante o reinício do driver as consultas podem falhar; tenta de novo.
            }
        }

        return false;
    }

    private void SavePreset()
    {
        ClearMessages();
        if (!TryParseInput(out var mode))
            return;

        var preset = new ResolutionPreset(mode.ToString(), mode.Width, mode.Height, mode.RefreshRate);
        try
        {
            if (!_presetStore.Add(preset))
            {
                InfoMessage = $"{mode} já está na lista de resoluções salvas.";
                return;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowError(OperationResult.Fail(
                "Não foi possível salvar a resolução.",
                "O ResolutionX não conseguiu gravar o arquivo de presets na sua pasta de usuário.",
                ex.ToString()));
            return;
        }

        Presets = _presetStore.GetAll();
        InfoMessage = $"{mode} adicionada às resoluções salvas.";
    }

    private void DeletePreset()
    {
        ClearMessages();
        if (SelectedPreset is not { IsBuiltIn: false } preset)
            return;

        try
        {
            _presetStore.Remove(preset);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowError(OperationResult.Fail(
                "Não foi possível excluir a resolução.",
                "O ResolutionX não conseguiu gravar o arquivo de presets na sua pasta de usuário.",
                ex.ToString()));
            return;
        }

        SelectedPreset = null;
        Presets = _presetStore.GetAll();
        InfoMessage = $"{preset.ModeText} removida das resoluções salvas.";
    }

    private void ShowDiagnostics()
    {
        var report = DiagnosticsReport.Build(
            Monitors,
            _virtualDisplayService.GetStatus(),
            _edidService.GetInfo,
            _customResolutionService.GetCustomResolutions);
        _dialogs.ShowDiagnostics(report);
    }

    private void ClearMessages()
    {
        InfoMessage = "";
        ErrorTitle = "";
        ErrorSuggestion = "";
        ErrorDetails = "";
        DetailsVisible = false;
        CanCreateFromError = false;
    }

    private void ShowError(OperationResult result)
    {
        InfoMessage = "";
        ErrorTitle = result.Message;
        ErrorSuggestion = result.Suggestion ?? "";
        ErrorDetails = result.TechnicalDetails ?? "";
        DetailsVisible = false;
        CanCreateFromError = result.Failure == OperationFailure.ModeNotSupported;
    }
}
