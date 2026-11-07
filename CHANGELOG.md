# Changelog

All notable changes to **Movie Time** will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [Unreleased]

---

## [0.6.1] - 2026-11-07 (Phase 4.1 Critical Hotfix: Application Startup Failure)

### Fixed
- **Resolved Fatal Application Startup Crash (`0xc000027b` STATUS_STOWED_EXCEPTION in `Microsoft.UI.Xaml.dll`)**:
  - **Root Cause 1 — Declarative SwapChainPanel Instantiation**: In `PlayerView.xaml`, `<lvs:VideoView>` (which subclasses WinUI's native `SwapChainPanel`) was declared directly in static XAML markup. During initial `MainWindow.InitializeComponent()`, WinUI 3 attempted to initialize DirectX swapchain surfaces before LibVLC's native unmanaged core was initialized and before an HWND swapchain was created, triggering an uncatchable `RoFailFastWithErrorContext` exception (`E_FAIL 0x80004005`).
  - **Resolution 1 — Lazy VideoView Lifecycle**: Replaced declarative XAML `<lvs:VideoView>` with a lightweight container `<Grid x:Name="VideoViewContainer">`. Native `VideoView` is now instantiated programmatically on demand in `PlayerView.EnsureVideoSurfaceAttached()`, strictly after `LibVLCSharp.Shared.Core.Initialize()` executes and only when navigating to the player.
  - **Root Cause 2 — Windows App SDK Version & ABI Downgrade Mismatch**: `MovieTime.App.csproj` had pinned `Microsoft.WindowsAppSDK` to `1.6.250108002`, while `LibVLCSharp.WinUI` 3.10.2 was built against `1.7.250909003` (triggering NuGet warning `NU1605`). Loading `VideoView` compiled for 1.7 into 1.6 binaries caused DirectComposition swapchain ABI incompatibilities.
  - **Resolution 2 — Package Alignment**: Upgraded `Microsoft.WindowsAppSDK` to `1.7.250909003`, eliminating downgrade warnings and aligning all native WinRT/XAML composition interfaces.
  - **Root Cause 3 — Cross-Thread UI Invocation (`0x8001010E RPC_E_WRONG_THREAD`)**: LibVLC native event callbacks (`OnLengthChanged`, `OnTimeChanged`, `OnPositionChanged`, etc.) are delivered on unmanaged background threads. Updating UI-bound properties in `PlayerViewModel` fired `PropertyChanged` notifications on worker threads, causing WinUI `{x:Bind}` setters on `TextBlock` to throw `RPC_E_WRONG_THREAD` and terminate CoreCLR with code `0xe0434352`.
  - **Resolution 3 — Thread-Safe UI Dispatcher**: Introduced `IDispatcherService` and `WinUiDispatcherService` (utilizing `Microsoft.UI.Dispatching.DispatcherQueue`). All LibVLC background event callbacks now safely marshal property changes (`PositionMs`, `DurationMs`, `RemainingTimeDisplay`, `AudioTracks`, `SubtitleTracks`, `IsPlaying`, `HasError`) directly onto the UI thread.
- **Enhanced Application Startup & Fault Tolerance**:
  - Added global `AppDomain.CurrentDomain.UnhandledException` handler with automatic Serilog flush.
  - Added guarded try/catch around `MainWindow` DI resolution and activation with explicit diagnostic logging.
  - Replaced nested `{Binding ElementName=...}` expressions inside `PlayerView.xaml` audio/subtitle DataTemplates with reliable code-behind click handlers.
  - Verified sustained process stability for direct execution of `MovieTime.App.exe` and execution through the Desktop shortcut (`C:\Users\Ali\Desktop\Movie Time.lnk`).

---

## [0.6.0] - 2026-11-06 (Phase 4: Native Cinematic Player + Liquid Glass)

### Added
- **Native In-App Cinematic Player Engine**:
  - Embedded hardware-accelerated playback utilizing `LibVLCSharp.WinUI` and `VideoLAN.LibVLC.Windows` (v3.0.24).
  - True in-process playback hosted directly inside `LibVLCSharp.Platforms.Windows.VideoView`: zero external popup windows, zero detached `vlc.exe` subprocesses.
  - Hardware acceleration options (`--avcodec-hw=any`, `--directx-hw-yuv=1`) for smooth 4K/UHD and HEVC/H.264 playback.
  - Full playback lifecycle: Play, Pause, TogglePlayPause, Stop, Seek (+/- 10s skip and fine scrubbing), Volume (0-100%), and Mute.
  - Fullscreen transitions utilizing native WinUI 3 `AppWindow.SetPresenter(AppWindowPresenterKind.FullScreen)` with ESC keyboard exit.
  - Dynamic audio track and subtitle track discovery, switching, and disabling (`Track -1`).
  - Resilient playback progress persistence: saves playback position ticks, duration, percentage, and completion status to local SQLite database via `IPlaybackProgressRepository`.
  - Auto-resume prompt / automatic position restoration from previous playback sessions.

- **Apple TV-Faithful Player Interface**:
  - Pixel-accurate reproduction of user-supplied Apple TV player visual references:
    - **Top Context Line**: Season/Episode or Edition context, technical tags (`4K UHD`, `HEVC`, `MKV`).
    - **Large Title Typography**: Semi-bold cinematic movie title treatment.
    - **Right-Aligned Header Controls**: Circular Liquid Glass buttons for Subtitles (`💬`), Audio Options (`(((|||)))`), and Fullscreen/PiP (`⤢`).
    - **Full-Width Scrubber Bar**: Sleek timeline bar with played white highlight progress, draggable scrubber, elapsed time badge (`04:33 ⏸`), and negative remaining time (`-54:08`).
    - **Bottom-Left Action Pills**: Liquid Glass pill buttons for `Info`, `InSight`, and `Continue Watching`.
    - **InSight Media Card & Track Flyouts**: Liquid Glass flyouts for real-time track selection and media file technical details.
    - **3.5s Inactivity Auto-Hide**: Controls smoothly fade out during active playback after 3.5 seconds of inactivity, waking instantaneously upon mouse movement or keyboard input.

- **Liquid Glass Visual System**:
  - Native WinUI 3 multi-layered acrylic and specular composition:
    - Multi-layer translucent acrylic materials (`#30FFFFFF`, `#18FFFFFF`).
    - Incident-angle specular rim gradient borders (`#B8FFFFFF` to `#15FFFFFF`).
    - Subtle chromatic dispersion fringing (`#1870D0FF` cyan to `#18FFA850` amber).
    - Atmospheric dark linear vignette gradient overlays ensuring ultra-sharp high-contrast typography while leaving the underlying video 100% unblurred and untouched.

- **Automated Unit Tests**:
  - Added 13 new unit tests in `PlaybackProgressRepositoryTests` and `PlayerViewModelTests` with mock/fake player service.
  - Synchronous progress reporter refactoring in `MediaScannerTests` ensuring zero test flakiness under parallel runners.
  - Test suite expanded to **194 passing unit tests** (0 failed, 0 skipped).

---

## [0.5.1] - 2026-11-06 (Phase 3D Hotfix)

### Fixed
- **Movie Details Navigation Crash**: Resolved fatal `System.ArgumentException: The parameter is incorrect.` (`IXamlBindingHelperStaticsMethods.ConvertValue`) thrown during WinUI 3 XAML binding evaluation when navigating to `MovieDetailsView`.
  - Replaced direct string `{x:Bind}` on `<Image Source="...">` in `MovieDetailsView.xaml` with safe code-behind bitmap management in `MovieDetailsView.xaml.cs`.
  - Added robust null/whitespace/existence validation and safe URI decoding with fallback handling when local artwork is missing or invalid.
  - Zero crashes when viewing movies without local backdrops or posters.

### Added
- **Windows Desktop Shortcut**: Created a native `.lnk` shortcut named `Movie Time` on the user's Windows Desktop (`C:\Users\Ali\Desktop\Movie Time.lnk`) pointing directly to the compiled x64 application executable (`MovieTime.App.exe`).
- **Regression Unit Tests**: Added tests in `MovieDetailsViewModelTests` covering `null`, empty string, whitespace, and non-existent file paths to guarantee robustness and zero regressions (test suite now at 174 passing unit tests).

---

## [0.5.0] - 2026-11-06 (Phase 3D)

### Added
- **Cinematic Movie Details Experience**: Dedicated `MovieDetailsView` and `MovieDetailsViewModel` inspired by Apple TV design references.
  - **Full-Width Hero Section**: Dynamic background rendering genuine local backdrops when available on disk, with subtle atmospheric radial and linear dark vignette overlays. Sophisticated ambient gradient fallback when no local artwork exists.
  - **Cinematic Title & Badges**: Prominent movie title typography with genuine release year and edition badges (e.g. *Director's Cut*, *Extended Edition*).
  - **Real Local Metadata Display**: Quality badges (`4K UHD`, `1080p HD`), video codec (`HEVC`, `AVC`), audio format, container format (`MKV`, `MP4`), and human-readable file sizes (`GB`/`MB`).
  - **Zero Fabricated Information Guarantee**: Only displays metadata genuinely available in the local SQLite database or scanned files; omits unverified fields (no fake genres, ratings, synopses, directors, or fictional cast).
  - **Primary Play Action**: Apple TV-signature white capsule Play button (`▶ Play Movie`) connected to the active local physical media file with feedback banner and integration boundary for Phase 4 VLC player.
  - **Multiple Local Media Files / Editions**: Support for selecting between multiple physical files/editions of the same movie, with automatic metadata updating.
  - **Offline Recommendations ("More from your library")**: Horizontal carousel presenting other real movies cataloged in the user's local database.
  - **Fluid Navigation**: Bi-directional navigation between Movies Library and Movie Details, preserving search and category filter state.
- **Automated Tests**: Added 10 comprehensive unit tests in `MovieDetailsViewModelTests.cs` (bringing test suite total to 170 passing tests).

---

## [0.4.0] - 2026-11-06 (Phase 3C)

### Added
- **Real Library System & Multi-Drive MLibrary Manager**:
  - Steam-inspired multi-drive storage management with automated Windows drive enumeration (`DriveInfo.GetDrives()`).
  - Standardized `<Drive>:\MLibrary` isolated folder hierarchy.
  - Strict scanning boundaries: only scans inside designated `MLibrary` folders; never touches system directories or root disks.
  - Apple TV-inspired `SettingsView` and `SettingsViewModel` with real-time drive availability indicators (Available, Folder Missing, Drive Disconnected, Disabled).
  - Interactive drive picker modal with disk preview and explicit confirmation before folder creation.
  - Pure zero-mock empty states for Home and Movies views when no libraries or movies are cataloged.

---

## [0.3.5] - 2026-11-06 (Phase 3B)

### Added
- **Apple TV-Inspired Movies Library Experience**:
  - Embedded category and genre filter sidebar (`All Movies`, `Recently Added`, `4K & High Definition`, and genres).
  - Apple TV 2:3 vertical poster grid (`170x255`) with 1.06x scale lift, white focus border, and elevation drop shadow.
  - Interactive quick-action context dialog (`Play`, `Go to Movie`).
  - Real-time local search and multi-criteria sorting (Title, Release Year, Date Added).

---

## [0.3.0] - 2026-11-05 (Phase 3A)

### Added
- **Apple TV-Inspired Home UI**:
  - Full-width billboard hero section with dark cinematic vignette overlays.
  - Floating translucent sidebar navigation (`( 🏠 Home )`).
  - Horizontal scrolling content rows with 16:9 aspect ratio media cards.

---

## [0.2.3] - 2026-11-05 (Phase 2C)

### Added
- **Scanner, Parser & SQLite Integration**:
  - Automated BFS filesystem scanner and filename parser engine synchronization.
  - Database persistence across `Movies`, `Series`, `Seasons`, `Episodes`, and `MediaFiles`.
  - Missing file detection and non-destructive removal tracking.

---

## [0.2.0] - 2026-11-04 (Phase 2A & 2B)

### Added
- High-performance recursive media scanner (`MediaScannerService`).
- Offline heuristic filename and metadata parser engine (`FilenameParserEngine`).

---

## [0.1.0] - 2026-11-04 (Phase 0 & 1)

### Added
- Project scaffolding (.NET 10 LTS, WinUI 3, x64).
- Dependency Injection and Serilog local rolling file logging.
- Foundational SQLite database layer with WAL mode and schema migrations.
