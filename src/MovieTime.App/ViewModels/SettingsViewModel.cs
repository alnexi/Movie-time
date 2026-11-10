using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MovieTime.App.Common;
using MovieTime.Core.Library;
using MovieTime.Core.Library.Models;
using MovieTime.Core.Theme;

namespace MovieTime.App.ViewModels;

/// <summary>
/// ViewModel managing the Settings and Multi-Drive MLibrary management system,
/// as well as the Celestial Pixel Dither theme experience.
/// </summary>
public sealed class SettingsViewModel : ObservableObject
{
    private readonly IMLibraryManagerService _libraryManager;
    private readonly IThemeManagerService? _themeManager;
    private readonly MovieTime.Core.Library.ILibraryAutoSyncService? _autoSyncService;
    private readonly ILogger<SettingsViewModel> _logger;

    private int _selectedTabIndex = 0; // 0 = General / Storage, 1 = Theme Experience
    private bool _isLoading;
    private bool _isScanning;
    private bool _isAddDialogOpen;
    private bool _confirmCreateFolder;
    private string? _statusMessage;
    private DriveStorageInfo? _selectedDrive;
    private MLibraryRegistrationPreview? _currentPreview;

    public event Action<string>? NavigationRequested;

    public IThemeManagerService? ThemeManager => _themeManager;

    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set
        {
            if (SetProperty(ref _selectedTabIndex, value))
            {
                OnPropertyChanged(nameof(IsGeneralTabSelected));
                OnPropertyChanged(nameof(IsThemeTabSelected));
            }
        }
    }

    public bool IsGeneralTabSelected => _selectedTabIndex == 0;
    public bool IsThemeTabSelected => _selectedTabIndex == 1;

    public ICommand SelectGeneralTabCommand { get; }
    public ICommand SelectThemeTabCommand { get; }

    public double CurrentThemeValue => _themeManager?.CurrentThemeValue ?? 1.0;
    public AppThemeMode CurrentThemeMode => _themeManager?.CurrentMode ?? AppThemeMode.Night;
    public int ThemePercentageInt => (int)Math.Round(CurrentThemeValue * 100.0);
    public string ThemePercentageText => $"{ThemePercentageInt}%";

    public string ThemeModeTitle => CurrentThemeMode switch
    {
        AppThemeMode.Day => "Day \u2022 Sunlit Light",
        AppThemeMode.Twilight => "Twilight \u2022 Warm Sunset Glow",
        _ => "Night \u2022 Obsidian Midnight"
    };

    public string ThemeDescription => CurrentThemeValue switch
    {
        <= 0.10 => "0–10% (Day): Boat rides high above the water. Vibrant emerald green waters, bright sunlight highlights, light weathered wood boat, white seagull, crisp light theme.",
        <= 0.35 => "11–35%: Boat begins lowering into the water. Lower hull plunges into green waters, warm afternoon sun, subtle wake displacement ripples.",
        <= 0.65 => "36–65% (Twilight): Boat is half submerged. Waterline crosses mid-hull planks, golden amber sunset atmosphere, warm teak boat, atmospheric twilight ambiance.",
        <= 0.85 => "66–85%: Boat is deeply submerged. Water reacts around the hull, gunwales near water surface, deep twilight-to-navy gradient.",
        _ => "86–100% (Night): Boat reaches final sinking state. Deep obsidian navy waters, shimmering moonlight reflection column, dark silhouette boat, signature cinematic dark theme."
    };

    public void SetThemePercentage(int percentage)
    {
        var clamped = Math.Clamp(percentage / 100.0, 0.0, 1.0);
        _themeManager?.SetThemeValue(clamped);
    }

    public bool ReducedMotion
    {
        get => _themeManager?.ReducedMotion ?? false;
        set
        {
            if (_themeManager != null && _themeManager.ReducedMotion != value)
            {
                _ = _themeManager.SetReducedMotionAsync(value);
                OnPropertyChanged(nameof(ReducedMotion));
            }
        }
    }

    public bool AmbientAnimationEnabled
    {
        get => _themeManager?.AmbientAnimationEnabled ?? true;
        set
        {
            if (_themeManager != null && _themeManager.AmbientAnimationEnabled != value)
            {
                _ = _themeManager.SetAmbientAnimationAsync(value);
                OnPropertyChanged(nameof(AmbientAnimationEnabled));
            }
        }
    }

    public ICommand SetDayThemeCommand { get; }
    public ICommand SetTwilightThemeCommand { get; }
    public ICommand SetNightThemeCommand { get; }

    public ObservableCollection<RegisteredMLibraryViewModel> RegisteredLibraries { get; } = new();
    public ObservableCollection<DriveStorageInfo> AvailableDrives { get; } = new();

    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }

    public bool IsScanning
    {
        get => _isScanning;
        set => SetProperty(ref _isScanning, value);
    }

    public bool IsAddDialogOpen
    {
        get => _isAddDialogOpen;
        set => SetProperty(ref _isAddDialogOpen, value);
    }

    public bool ConfirmCreateFolder
    {
        get => _confirmCreateFolder;
        set
        {
            if (SetProperty(ref _confirmCreateFolder, value))
            {
                OnPropertyChanged(nameof(CanConfirmAdd));
            }
        }
    }

    public string? StatusMessage
    {
        get => _statusMessage;
        set
        {
            if (SetProperty(ref _statusMessage, value))
            {
                OnPropertyChanged(nameof(HasStatusMessage));
            }
        }
    }

    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);

    public bool IsLibraryEmpty => RegisteredLibraries.Count == 0;
    public bool HasRegisteredLibraries => RegisteredLibraries.Count > 0;

    public DriveStorageInfo? SelectedDrive
    {
        get => _selectedDrive;
        set
        {
            if (SetProperty(ref _selectedDrive, value))
            {
                _ = UpdateRegistrationPreviewAsync();
            }
        }
    }

    public MLibraryRegistrationPreview? CurrentPreview
    {
        get => _currentPreview;
        private set
        {
            if (SetProperty(ref _currentPreview, value))
            {
                OnPropertyChanged(nameof(CanConfirmAdd));
                OnPropertyChanged(nameof(PreviewTargetPath));
                OnPropertyChanged(nameof(PreviewFolderExists));
                OnPropertyChanged(nameof(PreviewNeedsCreation));
                OnPropertyChanged(nameof(PreviewErrorMessage));
                OnPropertyChanged(nameof(HasPreviewError));
            }
        }
    }

    public string PreviewTargetPath => CurrentPreview?.TargetPath ?? string.Empty;
    public bool PreviewFolderExists => CurrentPreview?.FolderAlreadyExists ?? false;
    public bool PreviewNeedsCreation => CurrentPreview != null && !CurrentPreview.FolderAlreadyExists && CurrentPreview.CanRegister;
    public string? PreviewErrorMessage => CurrentPreview?.ErrorMessage;
    public bool HasPreviewError => !string.IsNullOrWhiteSpace(CurrentPreview?.ErrorMessage);

    public bool CanConfirmAdd
    {
        get
        {
            if (CurrentPreview == null || !CurrentPreview.CanRegister) return false;
            if (CurrentPreview.FolderAlreadyExists) return true;
            return ConfirmCreateFolder;
        }
    }

    public ICommand LoadLibrariesCommand { get; }
    public ICommand OpenAddDialogCommand { get; }
    public ICommand CloseAddDialogCommand { get; }
    public ICommand ConfirmAddLibraryCommand { get; }
    public ICommand RemoveLibraryCommand { get; }
    public ICommand ToggleLibraryEnabledCommand { get; }
    public ICommand ScanAllLibrariesCommand { get; }
    public ICommand ScanSingleLibraryCommand { get; }
    public ICommand NavigateHomeCommand { get; }
    public ICommand NavigateMoviesCommand { get; }

    public SettingsViewModel(
        IMLibraryManagerService libraryManager,
        ILogger<SettingsViewModel>? logger)
        : this(libraryManager, null, null, logger)
    {
    }

    public SettingsViewModel(
        IMLibraryManagerService libraryManager,
        IThemeManagerService? themeManager,
        ILogger<SettingsViewModel>? logger)
        : this(libraryManager, themeManager, null, logger)
    {
    }

    public SettingsViewModel(
        IMLibraryManagerService libraryManager,
        IThemeManagerService? themeManager = null,
        MovieTime.Core.Library.ILibraryAutoSyncService? autoSyncService = null,
        ILogger<SettingsViewModel>? logger = null)
    {
        _libraryManager = libraryManager ?? throw new ArgumentNullException(nameof(libraryManager));
        _themeManager = themeManager;
        _autoSyncService = autoSyncService;
        _logger = logger ?? NullLogger<SettingsViewModel>.Instance;

        if (_autoSyncService != null)
        {
            _autoSyncService.SyncCompleted += (s, e) =>
            {
                _ = LoadLibrariesAsync();
            };
        }

        LoadLibrariesCommand = new AsyncRelayCommand(LoadLibrariesAsync);
        OpenAddDialogCommand = new AsyncRelayCommand(OpenAddDialogAsync);
        CloseAddDialogCommand = new RelayCommand(CloseAddDialog);
        ConfirmAddLibraryCommand = new AsyncRelayCommand(ConfirmAddLibraryAsync);
        RemoveLibraryCommand = new AsyncRelayCommand<int>(RemoveLibraryAsync);
        ToggleLibraryEnabledCommand = new AsyncRelayCommand<RegisteredMLibraryViewModel>(ToggleLibraryEnabledAsync);
        ScanAllLibrariesCommand = new AsyncRelayCommand(ScanAllLibrariesAsync);
        ScanSingleLibraryCommand = new AsyncRelayCommand<int>(ScanSingleLibraryAsync);
        SelectGeneralTabCommand = new RelayCommand(() => SelectedTabIndex = 0);
        SelectThemeTabCommand = new RelayCommand(() => SelectedTabIndex = 1);
        NavigateHomeCommand = new RelayCommand(() => NavigationRequested?.Invoke("Home"));
        NavigateMoviesCommand = new RelayCommand(() => NavigationRequested?.Invoke("Movies"));

        SetDayThemeCommand = new RelayCommand(() => _themeManager?.SetThemeValue(0.0));
        SetTwilightThemeCommand = new RelayCommand(() => _themeManager?.SetThemeValue(0.5));
        SetNightThemeCommand = new RelayCommand(() => _themeManager?.SetThemeValue(1.0));

        if (_themeManager != null)
        {
            _themeManager.ThemeChanged += (val, mode) =>
            {
                OnPropertyChanged(nameof(CurrentThemeValue));
                OnPropertyChanged(nameof(CurrentThemeMode));
                OnPropertyChanged(nameof(ThemeModeTitle));
                OnPropertyChanged(nameof(ThemeDescription));
                OnPropertyChanged(nameof(ThemePercentageInt));
                OnPropertyChanged(nameof(ThemePercentageText));
            };
        }
    }

    public async Task InitializeAsync()
    {
        await LoadLibrariesAsync();
    }

    public async Task LoadLibrariesAsync()
    {
        try
        {
            IsLoading = true;
            var libraries = await _libraryManager.GetRegisteredLibrariesAsync();

            RegisteredLibraries.Clear();
            foreach (var lib in libraries)
            {
                var vm = RegisteredMLibraryViewModel.FromModel(lib);
                vm.ScanCommand = new AsyncRelayCommand(async () => await ScanSingleLibraryAsync(vm.Id));
                vm.RemoveCommand = new AsyncRelayCommand(async () => await RemoveLibraryAsync(vm.Id));
                vm.ToggleEnabledCommand = new AsyncRelayCommand(async () => await ToggleLibraryEnabledAsync(vm));
                RegisteredLibraries.Add(vm);
            }

            OnPropertyChanged(nameof(IsLibraryEmpty));
            OnPropertyChanged(nameof(HasRegisteredLibraries));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load registered libraries.");
            StatusMessage = $"Error loading libraries: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    public async Task OpenAddDialogAsync()
    {
        try
        {
            ConfirmCreateFolder = false;
            AvailableDrives.Clear();

            var drives = _libraryManager.GetAvailableDrives();
            foreach (var drive in drives)
            {
                AvailableDrives.Add(drive);
            }

            SelectedDrive = AvailableDrives.FirstOrDefault(d => d.IsReady);
            await UpdateRegistrationPreviewAsync();

            IsAddDialogOpen = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize add drive dialog.");
            StatusMessage = $"Error listing drives: {ex.Message}";
        }
    }

    public void CloseAddDialog()
    {
        IsAddDialogOpen = false;
        ConfirmCreateFolder = false;
        CurrentPreview = null;
    }

    private async Task UpdateRegistrationPreviewAsync()
    {
        if (SelectedDrive == null)
        {
            CurrentPreview = null;
            return;
        }

        try
        {
            CurrentPreview = await _libraryManager.PreviewDriveRegistrationAsync(SelectedDrive.DriveLetter);
            ConfirmCreateFolder = false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to preview registration for drive '{Drive}'.", SelectedDrive.DriveLetter);
            CurrentPreview = new MLibraryRegistrationPreview
            {
                DriveLetter = SelectedDrive.DriveLetter,
                ErrorMessage = ex.Message
            };
        }
    }

    public async Task ConfirmAddLibraryAsync()
    {
        if (SelectedDrive == null || CurrentPreview == null || !CanConfirmAdd)
        {
            return;
        }

        try
        {
            IsLoading = true;
            bool createFolder = ConfirmCreateFolder || !CurrentPreview.FolderAlreadyExists;
            var registered = await _libraryManager.RegisterDriveLibraryAsync(SelectedDrive.DriveLetter, createFolder);

            StatusMessage = $"Registered MLibrary on drive {registered.DriveLetter} ({registered.Path}).";
            CloseAddDialog();
            if (_autoSyncService != null)
            {
                await _autoSyncService.RefreshWatchersAsync();
            }
            await LoadLibrariesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to register MLibrary on drive '{Drive}'.", SelectedDrive.DriveLetter);
            StatusMessage = $"Registration failed: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    public async Task RemoveLibraryAsync(int libraryId)
    {
        try
        {
            IsLoading = true;
            bool removed = await _libraryManager.RemoveLibraryAsync(libraryId);
            if (removed)
            {
                StatusMessage = "Library folder unhooked from River.";
                if (_autoSyncService != null)
                {
                    await _autoSyncService.RefreshWatchersAsync();
                }
                await LoadLibrariesAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove library {Id}.", libraryId);
            StatusMessage = $"Failed to remove library: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    public async Task ToggleLibraryEnabledAsync(RegisteredMLibraryViewModel? library)
    {
        if (library == null) return;

        try
        {
            bool newEnabled = !library.IsEnabled;
            bool updated = await _libraryManager.SetLibraryEnabledAsync(library.Id, newEnabled);
            if (updated)
            {
                library.IsEnabled = newEnabled;
                if (_autoSyncService != null)
                {
                    await _autoSyncService.RefreshWatchersAsync();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to toggle enabled state for library {Id}.", library.Id);
            StatusMessage = $"Error updating library: {ex.Message}";
        }
    }

    public async Task ScanAllLibrariesAsync()
    {
        try
        {
            IsScanning = true;
            StatusMessage = "Scanning registered MLibrary folders...";

            var result = await _libraryManager.ScanLibrariesAsync();
            StatusMessage = $"Scan completed: {result.FilesDiscovered} files discovered ({result.MoviesClassified} movies, {result.TvEpisodesClassified} episodes), {result.FilesImported} imported, {result.FilesUpdated} updated.";

            await LoadLibrariesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed during full library scan.");
            StatusMessage = $"Scan failed: {ex.Message}";
        }
        finally
        {
            IsScanning = false;
        }
    }

    public async Task ScanSingleLibraryAsync(int libraryId)
    {
        try
        {
            IsScanning = true;
            StatusMessage = "Scanning selected library folder...";

            var result = await _libraryManager.ScanLibrariesAsync(new[] { libraryId });
            StatusMessage = $"Library scan completed: {result.FilesDiscovered} files discovered ({result.MoviesClassified} movies, {result.TvEpisodesClassified} episodes), {result.FilesImported} imported, {result.FilesUpdated} updated.";

            await LoadLibrariesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed scanning library {Id}.", libraryId);
            StatusMessage = $"Scan failed: {ex.Message}";
        }
        finally
        {
            IsScanning = false;
        }
    }
}
