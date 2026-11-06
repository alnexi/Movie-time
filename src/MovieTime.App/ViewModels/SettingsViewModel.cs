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

namespace MovieTime.App.ViewModels;

/// <summary>
/// ViewModel managing the Settings and Multi-Drive MLibrary management system.
/// Allows users to view, register, confirm, scan, and remove MLibrary folders across all local drives.
/// </summary>
public sealed class SettingsViewModel : ObservableObject
{
    private readonly IMLibraryManagerService _libraryManager;
    private readonly ILogger<SettingsViewModel> _logger;

    private bool _isLoading;
    private bool _isScanning;
    private bool _isAddDialogOpen;
    private bool _confirmCreateFolder;
    private string? _statusMessage;
    private DriveStorageInfo? _selectedDrive;
    private MLibraryRegistrationPreview? _currentPreview;

    public event Action<string>? NavigationRequested;

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
        ILogger<SettingsViewModel>? logger = null)
    {
        _libraryManager = libraryManager ?? throw new ArgumentNullException(nameof(libraryManager));
        _logger = logger ?? NullLogger<SettingsViewModel>.Instance;

        LoadLibrariesCommand = new AsyncRelayCommand(LoadLibrariesAsync);
        OpenAddDialogCommand = new AsyncRelayCommand(OpenAddDialogAsync);
        CloseAddDialogCommand = new RelayCommand(CloseAddDialog);
        ConfirmAddLibraryCommand = new AsyncRelayCommand(ConfirmAddLibraryAsync);
        RemoveLibraryCommand = new AsyncRelayCommand<int>(RemoveLibraryAsync);
        ToggleLibraryEnabledCommand = new AsyncRelayCommand<RegisteredMLibraryViewModel>(ToggleLibraryEnabledAsync);
        ScanAllLibrariesCommand = new AsyncRelayCommand(ScanAllLibrariesAsync);
        ScanSingleLibraryCommand = new AsyncRelayCommand<int>(ScanSingleLibraryAsync);
        NavigateHomeCommand = new RelayCommand(() => NavigationRequested?.Invoke("Home"));
        NavigateMoviesCommand = new RelayCommand(() => NavigationRequested?.Invoke("Movies"));
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
                StatusMessage = "Library folder unhooked from Movie Time.";
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
            StatusMessage = $"Scan completed: {result.FilesDiscovered} files examined, {result.MoviesCreated} movies cataloged.";

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
            StatusMessage = $"Library scan completed: {result.FilesDiscovered} files examined, {result.MoviesCreated} movies cataloged.";

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
