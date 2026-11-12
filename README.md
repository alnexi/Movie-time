# River

> A premium, cinematic, offline-first local media library manager and desktop player for Windows.

[![.NET](https://img.shields.io/badge/.NET-10.0%20LTS%20(x64)-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![Windows App SDK](https://img.shields.io/badge/WinUI%203-Windows%20App%20SDK%201.7-0078D7?logo=windows)](https://learn.microsoft.com/en-us/windows/apps/winui/winui3/)
[![Tests](https://img.shields.io/badge/Unit%20Tests-351%20Passed-brightgreen)](https://github.com/alnexi/Movie-time)
[![Version](https://img.shields.io/badge/Version-1.2.1-blue)](https://github.com/alnexi/Movie-time)
[![License](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

---

## Overview

**River** is designed for film and television enthusiasts who keep personal media collections on local hard drives or network shares and desire a first-class, distraction-free, 10-foot-capable desktop experience.

Unlike cluttered streaming apps or heavy web-wrapped wrappers, River is built natively on **.NET 10 (x64)** and **WinUI 3 (Windows App SDK)**. It runs completely offline without tracking, external telemetry, or unrequested web calls.

---

## Cinematic Visual Design System

### 1. Home Screen
* **Ambient Billboard Hero Section**: Edge-to-edge cinematic banner with dark radial and linear vignette gradients that blend seamlessly into content rows below.
* **Refined Typography & Badges**: Stylized bold titles, genre indicators, and rating/format badges (e.g. `[R]`, `4K HDR`).
* **White Capsule Action Pill**: Signature white pill button (`Go to Movie` / `Play Movie`) with dark typography and icons.
* **Centered Carousel Dots**: Elongated active white pill indicator with subtle inactive dots for carousel navigation.
* **Top-Left Floating Trigger**: Floating translucent capsule button (`Home`) positioned in the upper-left margin to toggle the navigation sidebar.
* **"Continue Watching" Row**: Horizontal scrollable container hosting 16:9 aspect ratio media cards with playback progress bars and instant resume.
* **Clean Empty State**: Built-in guidance showcase displayed when launching with an empty library.

### 2. Movies Library
* **Top-Left Navigation Capsule**: Distinctive capsule pill button providing fluid one-click transitions between Home and the Movies Library.
* **Embedded Category & Genre Filter List**:
  - Filter categories: `All Movies`, `Recently Added`, `4K & High Definition`.
  - Section Header: `GENRES` (`Action & Adventure`, `Comedy`, `Drama`, `Kids & Family`, `Sci-Fi & Fantasy`).
  - Active category highlighted as a solid white capsule with dark iconography; inactive categories styled with clean typography.
* **Theatrical 2:3 Vertical Poster Grid**:
  - Standard 2:3 theatrical movie poster dimensions (`170x255`) with smooth rounded corners (`14px`).
  - Smooth focus and hover animation: **1.06x scale lift**, bright white selection border (**3px**), and elevation drop shadow.
  - Sophisticated dark gradient fallback artwork for local movies without embedded or cached posters.
  - Quality badges (`4K HDR`, `1080p HD`) positioned on each card based on probed media file properties.
* **Quick Action Context Dialog**:
  - Selecting a movie poster opens an interactive quick-action capsule dialog right over the card.
  - Actions: `Play` (with play triangle glyph) and `Go to Movie` (active white pill with info glyph).
  - Background cards smoothly dim to focus attention on the selected title.
* **Instant Local Title Search & Sorting**:
  - Built-in pill search box with real-time title filtering.
  - Multi-criteria sorting: `Title (A-Z)`, `Release Year (Newest)`, and `Recently Added`.
  - Empty search state feedback with one-click "Clear Search" recovery.
* **Polished Empty State**:
  - When the SQLite database contains 0 movies, renders an elegant empty state explaining that the local library is empty, with a white pill "Scan Library Now" button.
  - Never fabricates fake movies into the user's database.

### 3. Multi-Drive MLibrary Management & Settings
* **Complete Removal of Demo Content**:
  - Zero hardcoded mock movies, fake titles, or simulated video assets.
  - Completely clean, honest empty states that guide the user to configure real local drives.
* **Multi-Drive Storage Architecture (`MLibrary`)**:
  - Automatically enumerates available Windows drives (`C:`, `D:`, `E:`, etc.) with volume labels and storage capacities.
  - Standardizes on a dedicated, isolated `MLibrary` folder per drive (e.g. `D:\MLibrary`).
  - Strict scanning boundary: River scans **ONLY** inside registered `MLibrary` directories; never touches root disks or personal folders.
* **Settings Interface**:
  - Top-left `Settings` pill button matching the signature navigation aesthetic.
  - Drive status badges with real-time health: `Available` (Green), `Folder Missing` (Orange), `Drive Disconnected` (Red), `Disabled` (Gray).
  - Add Drive Dialog: Select drive, preview `MLibrary` target path, and require explicit confirmation checkbox before creating folders on disk.
  - Scan controls per library and global "Scan All Libraries" orchestration.
  - Non-destructive library removal: unregisters the library folder without modifying or deleting media files on disk.

### 4. Cinematic Movie Details Experience
* **Hero Artwork Presentation**:
  - Full-width hero canvas rendering genuine local backdrops when available on disk, combined with left and bottom dark vignette overlays for crisp typography.
  - Atmospheric ambient lighting fallback when no backdrop file is present on disk (zero web-scraping or fabricated artwork).
  - Prominent title typography with release year and edition badges (e.g. *Director's Cut*, *Extended Edition*).
* **Genuinely Available Metadata Only**:
  - Strictly presents verified technical properties: resolution (`4K UHD`, `1080p HD`), video codec (`HEVC`, `AVC`), audio format, container format (`MKV`, `MP4`), and actual file size.
  - Zero fabricated information: omitted fields disappear naturally rather than displaying synthetic ratings, false genres, or fabricated cast.
* **White Capsule Play Button**:
  - Prominent `Play Movie` white pill button directly connected to the active local physical video file path, with resume tracking.
* **Multiple Local Media Editions & Files**:
  - Displays all local video files cataloged for a movie and allows the user to switch between editions with instant metadata updates.
* **Offline Library Recommendations ("More from your library")**:
  - Horizontal recommendation row populated strictly with other real movies from the user's local SQLite database. Omitted cleanly if no other movies exist.
* **Bi-Directional Navigation**:
  - Smooth navigation from Movies Library to Movie Details and back, preserving search filters and category selection state.

### 5. Native Cinematic Player & Liquid Glass
* **Embedded Hardware-Accelerated Playback (`LibVLCSharp.WinUI` + `VideoLAN.LibVLC.Windows`)**:
  - Embedded playback pipeline rendered directly inside `LibVLCSharp.Platforms.Windows.VideoView`.
  - Zero external popup windows, zero detached `vlc.exe` subprocesses.
  - Hardware decoding acceleration (`--avcodec-hw=any`, `--directx-hw-yuv=1`) providing smooth 4K/UHD and HEVC/H.264 playback.
  - Dynamic audio track and subtitle track selection and disabling.
  - Automatic progress checkpointing to SQLite (`PlaybackProgress`) for resume across sessions.
* **Visual Player Layout**:
  - **Top Context Line**: Episode/Edition info and technical format badges (`2023 • 4K UHD • HEVC`).
  - **Cinematic Title Typography**: Prominent semi-bold title treatment.
  - **Right-Aligned Header Controls**: Circular Liquid Glass buttons for Subtitles, Audio Options, and Fullscreen.
  - **Full-Width Scrubber Bar**: Sleek timeline bar with played white highlight progress, draggable scrubber, elapsed time badge, and remaining time.
  - **Bottom Action Controls**: Liquid Glass pill buttons and flyouts for track selection and media file technical details.
  - **3.5s Inactivity Auto-Hide**: Controls smoothly fade out during active playback after 3.5 seconds of inactivity, waking instantaneously upon mouse movement or keyboard input.
* **Liquid Glass Visual System**:
  - Native WinUI 3 multi-layered acrylic and specular composition:
    - Multi-layer translucent acrylic materials (`#30FFFFFF`, `#18FFFFFF`).
    - Incident-angle specular rim gradient borders (`#B8FFFFFF` to `#15FFFFFF`).
    - Subtle chromatic dispersion fringing (`#1870D0FF` cyan to `#18FFA850` amber).
    - Atmospheric dark linear vignette gradient overlays ensuring ultra-sharp high-contrast typography while leaving the underlying video untouched.
* **Volume Slider (0–200%) & Playback Speed Control (0.1x–2.0x)**:
  - Minimalist volume slider supporting amplification up to 200% with keyboard Up/Down shortcuts.
  - Dedicated speed adjustment pill buttons (`-` / `+`) in 0.1x increments with an animated speed HUD overlay.
* **Complete Desktop Control & Shortcuts**:
  - `Space` / `K`: Play / Pause toggle without focus interference.
  - `J` / `L`: Seek backward / forward 10 seconds.
  - `Left` / `Right` Arrow: Seek backward / forward 5 seconds.
  - `0` to `9`: Seek to 0%, 10%, 20%, 30%, 40%, 50%, 60%, 70%, 80%, 90% of duration.
  - `Up` / `Down` Arrow: Volume up / down (5% increments).
  - `M`: Mute / Unmute.
  - `[` / `]`: Decrease / increase playback speed by 0.1x.
  - `F` / `Alt+Enter`: Toggle edge-to-edge Fullscreen (`AppWindowPresenterKind.FullScreen`).
  - `Esc` / `Backspace`: Exit player and return seamlessly to Movie Details / Library.
* **External Audio & Subtitles Support**:
  - Attach and manage external subtitle files (`.srt`, `.ass`, `.ssa`, `.vtt`, `.sub`) with instantaneous track switching.
  - Load external audio files (`.mka`, `.mkv`, `.aac`, `.ac3`, `.mp3`) with `±100ms` step adjustments and `0ms` reset to fix out-of-sync audio.
  - Subtitle customization: user-configurable font sizes (22pt to 52pt), high-visibility colors (White, Yellow, Cyan, Green), outline thickness, background opacities, and vertical position offsets persisted across sessions.

### 6. TV Shows & Episodic Experience
* **Deterministic TV Episode Recognition**:
  - Advanced filename pattern parsing covering standard scene releases (`The.Wire.S01E08.Lessons.REMASTERED.720p.WEB-DL.2CH.x265.HEVC.PSA.AvaMovie.mkv`), `1x08`, `Season 1 Episode 8`, multi-episode packs (`S01E08E09`), and Specials (`S00E01`).
  - Strict token cutoff bounds reliably extract authentic episode titles while discarding technical noise without fabricating metadata.
* **Hierarchical SQLite Relational Synchronization**:
  - Scanned episodes automatically associate with `Series`, `Season`, and `Episode` records in SQLite, tracking actual video files via `MediaFile` foreign keys without duplicate entities on rescans.
* **TV Shows Library (`TvShowsView`)**:
  - 2:3 vertical poster grid displaying series title, release years, season counts, and episode counts with real-time search filtering, multi-criteria sorting (`Title`, `Year`, `Recently Added`), and empty state handling.
* **Series Details Experience (`SeriesDetailsView`)**:
  - Atmospheric full-bleed hero banner with genuine extracted backdrops and posters, smart `Resume / Play Next Up` action button, interactive season pill selector tabs, and 16:9 episode list cards with duration, quality badges, progress indicator bars, and candidate video frame thumbnails.
* **Episode Playback & Discrete Resume Progress**:
  - Full LibVLC embedded player integration displaying series title and episode context, persisting resume checkpoints to SQLite keyed by `EpisodeId`, and returning smoothly to Series Details upon exit.
* **Home & Continue Watching Integration**:
  - Dedicated "TV Shows" shelf on Home navigating directly to series details, plus automatic population of in-progress episodes in "Continue Watching" with instant one-click resume.

### 7. Persistent Local Playlists
* **Full CRUD Playlist Management**:
  - Create, rename, and delete custom playlists.
  - Add and remove movies directly from Movie Details flyouts or within the playlist view.
  - Reorder items up and down with instant UI and database synchronization.
* **Sequential Playback**:
  - `Play All` button sequentially streams each video through the embedded player with seamless transition to the next title.
  - Previous and Next transport buttons in the player HUD for manual playlist stepping.

### 8. Interactive Pixel Dither Theme Experience
* **Continuous 0–100% Celestial Progression**:
  - Real-time interactive retro pixel-art scene featuring a wooden sailboat on water with a perched seagull.
  - Continuous vertical sinking simulation responding dynamically to the theme intensity slider (0% Day to 100% Night).
  - 3D perspective mouse-reactive parallax tilt and water ripple physics with Bayer 8x8 ordered dithering.
  - Global theme propagation updating colors, cards, borders, and contrast across all views in real time.

### 9. Windows 11 Integration & Universal Open With
* **Universal Windows Explorer "Open With River"**:
  - Registered in Windows Explorer Open With menu under **River** for 20 video formats (`.mp4`, `.mkv`, `.avi`, `.mov`, `.wmv`, `.webm`, `.m4v`, `.mpg`, `.mpeg`, `.ts`, `.m2ts`, `.mts`, `.m2t`, `.vob`, `.flv`, `.divx`, `.3gp`, `.ogv`, `.asf`, `.rmvb`, `.rm`).
  - Opens arbitrary local video files from anywhere on the computer (Desktop, Downloads, external USB/hard drives) without requiring library registration, database insertion, or folder scanning.
  - Multi-file playlist playback: selecting multiple videos in Windows Explorer and launching with River queues all titles sequentially in the embedded player (`NavigateToPlayerWithPlaylist`).
  - Single-instance activation routing: external files open directly inside the active River window, bringing it to foreground without spawning duplicate processes.
  - Non-library playback boundaries: external playback never pollutes the Movies library or Continue Watching shelves, and original files are never copied, moved, or altered.
  - Library-aware progress restoration: if an opened file belongs to an existing `MLibrary` record, saved resume progress is restored and updated seamlessly.
* **Authentic Sailboat Artwork & Desktop Shortcut**:
  - High-resolution multi-resolution `app.ico` (16, 24, 32, 48, 64, 128, 256 px) and window logos generated directly from the authentic user-supplied sailboat artwork.
  - Native desktop shortcut (`River.lnk`) on the user's Desktop.

### 10. Automatic Real-Time Library Auto-Sync & Media Reconciliation
* **Hybrid Filesystem Synchronization (`LibraryAutoSyncService`)**:
  - High-capacity 64KB-buffer `System.IO.FileSystemWatcher` actively monitors all enabled and accessible `MLibrary` directories.
  - 2.0-second debounced event processing: coalesces rapid batch operations (e.g. copying 100 episodes) into a single stabilized import run to eliminate unnecessary disk strain.
  - Periodic 60-second background reconciliation cycle catches any dropped filesystem events or external drive modifications.
  - UI auto-updates: active views (`HomeView`, `MoviesView`, `TvShowsView`, `SettingsView`) automatically update their collections on the UI thread when filesystem changes occur without requiring manual refresh or application restart.
* **Missing Media Reconciliation & Data Integrity**:
  - Moved and renamed file detection: recognizes relocated files within the same library root by matching file size and timestamp, preserving entity IDs, playback progress, and artwork caches.
  - Genuinely missing file reconciliation: marks unlocated files as `IsMissing = true`, cleanly excluding them from the active Movies library and Continue Watching; restores `IsMissing = false` upon rediscovery.
  - Multi-edition integrity: deleting one edition of a multi-cut film removes only that file association while preserving the movie and remaining editions.
  - Drive disconnection safety: offline or disconnected drives are skipped safely without interpreting them as mass deletions, keeping all database records intact.

---

## System Architecture

River follows a decoupled layered architecture:

```
River (MovieTime)/
├── src/
│   ├── MovieTime.App/             # WinUI 3 Native Desktop Application
│   │   ├── Common/                # MVVM ObservableObject & RelayCommand
│   │   ├── Controls/              # HeroBanner, MediaCard, MoviePosterCard, MediaRow, SidebarNavigation, PixelDitherThemeControl
│   │   ├── Converters/            # XAML Value Converters
│   │   ├── Services/              # VlcPlayerService, WicImageProcessingService, WindowsVideoFrameExtractor, WindowsOpenWithRegistrar, CommandLineActivationHelper
│   │   ├── ViewModels/            # Presentation Models (Home, Movies, TvShows, SeriesDetails, MovieDetails, Settings, Playlists, Player)
│   │   ├── Views/                 # Full-screen views (Home, Movies, TvShows, SeriesDetails, MovieDetails, Settings, Playlists, Player)
│   │   ├── MainWindow.xaml        # Edge-to-edge window host & navigation coordinator
│   │   └── App.xaml               # Composition Root, Single-Instance Routing & DI configuration
│   │
│   └── MovieTime.Core/            # Core Domain & Platform-Agnostic Engine
│       ├── Artwork/               # Embedded artwork extraction, quality evaluation, storage, and pipeline coordination
│       ├── Database/              # SQLite-net WAL database, migrations & repositories
│       ├── Entities/              # Movie, Series, Season, Episode, MediaFile, ScanRoot, PlaybackProgress, Playlist, PlaylistItem
│       ├── Library/               # MLibraryManagerService, LibraryImportService, LibraryAutoSyncService & ScanRootService
│       ├── Logging/               # Serilog rolling local file logger (%LocalAppData%\MovieTime\Logs)
│       ├── Parsing/               # FilenameParserEngine (heuristic classification & extraction)
│       ├── Playback/              # IVlcPlayerService, PlayerState, PlayerTrackInfo, SubtitleSettings
│       ├── Scanning/              # MediaScannerService (recursive async filesystem scanner)
│       ├── Shell/                 # CommandLineHelper (Windows CommandLineToArgvW & managed fallback parser)
│       ├── Theme/                 # ThemeManager, PaletteInterpolator, Color models
│       └── Services/              # StoragePathService & AppEnvironment
│
└── tests/
    └── MovieTime.Tests/           # Unit & Integration Tests (323 tests)
```

---

## Prerequisites & Building

- **Windows 10 Version 1809 (Build 17763)** or higher / **Windows 11** (x64)
- **.NET 10 SDK** (x64)
- **Visual Studio 2022 / 2026** or **VS Code** with C# Dev Kit and Windows App SDK build tools

### Build from Command Line

```powershell
# Restore dependencies and build the entire solution (x64 Debug)
dotnet build MovieTime.sln -p:Platform=x64

# Run all 308 automated unit tests
dotnet test MovieTime.sln -p:Platform=x64

# Launch the application
dotnet run --project src\MovieTime.App\MovieTime.App.csproj -p:Platform=x64
```

---

## Automated Test Suite

The solution features **308 unit and integration tests** covering:
- **Dependency Injection & Bootstrapping**: Service registration, lifetime scopes, and environment paths.
- **Local Logging**: Serilog file sink rolling rules, log formatting, and output directory isolation.
- **SQLite Engine & Migrations**: Schema creation, table constraints, indices, WAL mode, and migrations.
- **Filesystem Scanner**: File extensions, hidden directory exclusions, symlink safety, and progress reporting.
- **Filename Parsing & Classification**: Scene naming conventions, bracketed years, numbers in titles, Persian and international titles, anime tags, and TV series heuristics.
- **Library Synchronization**: Unlimited movies handling, unlinked media file re-evaluation, missing media reconciliation, and entity deduplication.
- **Multi-Drive MLibrary Management**: Drive enumeration, path formatting, duplicate registration prevention, explicit directory creation confirmation, library removal, and strict scanning scope isolation.
- **Home Presentation Layer**: Empty library state, hero banner, sidebar navigation, and continue watching shelves.
- **Movies Presentation Layer**: Database loading, empty library state, live search query filtering, sorting, and category filtering.
- **Movie Details Presentation Layer**: Real movie identification, year handling, missing artwork fallback flags, editions/files selection, and playback dispatching.
- **TV Shows & Episodes Architecture**: Filename recognition, series/season/episode entity relation linking, series details hero & season selectors, episode resume logic, and continuous playback.
- **Persistent Playlists**: Full CRUD lifecycle, item reordering, and sequential playback coordination.
- **Cinematic Player Presentation Layer**: Time formatting, non-blocking asynchronous seeking, volume amplification (0-200%), playback speed controls (0.1x-2.0x), track flyouts, and auto-hide timer triggers.
- **Windows Integration**: Registry Open With configuration, single-instance activation redirection, and application metadata validation.
- **Theme Experience**: Continuous 0-100% theme progression, color interpolation, Bayer dithering, and 3D sinking physics.

---

## Premium All-in-One Installer (`River-Setup-x64.exe`)

River includes a production-grade, standalone single-file installer featuring its own custom-branded **Liquid Glass UI**, completely separate from generic Windows wizard or default Inno Setup styling.

### 1. Architecture & Design System
- **Framework**: Standalone .NET 10 x64 WPF application (`MovieTime.Installer`), published as a single-file self-contained executable with embedded native libraries and compression.
- **UI Aesthetic**: Deep cinematic dark glass backdrop (`#080A0E` / `#0D1117`), translucent acrylic cards, subtle cyan borders (`#30363D`), Segoe UI Variable typography, and custom rounded window chrome with minimize/close controls.
- **Iconic Branding**: Embedded pixel-art sailboat from River's Theme Experience with a gentle floating bobbing animation and atmospheric night reflections.
- **Workflow**:
  1. **Welcome Screen**: Clean branding overview, feature badges, "Install River" (default express install), and "Customize..." button.
  2. **Installation Options**: Target directory selection (default: `%ProgramFiles%\River`), native folder picker, free disk space calculator, and shortcut / file association toggles.
  3. **Live Progress Screen**: Real-time extraction progress bar with cyan gradient glow, percentage indicator, animated status, and active filename logging.
  4. **Completion Screen**: Emerald checkmark badge, target path summary, "Launch River now" checkbox, and finish button.
  5. **Error Recovery Screen**: Graceful error reporting with technical details and retry capabilities.

### 2. Bundled Offline Dependencies
River's installer is **100% offline-capable**; no internet access or external runtime installations are required on the target computer:
- **.NET 10 (x64) Runtime**: Self-contained runtime assemblies bundled inside the single `.exe`.
- **Windows App SDK / WinUI 3**: Self-contained native runtime (`Microsoft.ui.xaml.dll`, `resources.pri`, etc.).
- **LibVLC 3.0.24 & Codecs**: Full native LibVLC runtime (`libvlc.dll`, `libvlccore.dll`, and complete `plugins` folder for all audio/video/subtitle codecs).
- **SQLite Native Engine**: `e_sqlite3.dll` WAL-mode database provider.
- **Application Assets**: High-resolution icons, pixel-art branding, and theme assets.

### 3. Installation & Integration Behavior
- **Target Folder**: `C:\Program Files\River` (configurable by user).
- **Shortcuts**: Creates Start Menu and Desktop shortcuts with `app.ico` using native Windows Shell COM (`IShellLinkW` / `IPersistFile`).
- **File Associations**: Registers River in Windows Explorer "Open With" menu for all supported video formats (`.mp4`, `.mkv`, `.avi`, `.mov`, `.wmv`, etc.).
- **Windows Uninstaller**: Registers cleanly in Windows "Installed apps" / "Add or remove programs" with display icon, publisher, version, estimated size, and uninstall string (`"C:\Program Files\River\Uninstall.exe"`).
- **Dedicated Uninstaller**: Drops `Uninstall.exe` in the application directory.
- **Data Safety Guarantee**: Uninstallation or upgrading **never deletes or modifies user media or personal database/settings** under `%LocalAppData%\MovieTime`.

### 4. Building the Installer
Run the automated build script from an elevated or standard PowerShell console:
```powershell
powershell.exe -ExecutionPolicy Bypass -File .\build-installer.ps1
```
The script will:
1. Publish `MovieTime.App` in self-contained Release x64 mode.
2. Compress the application payload into `RiverPayload.zip`.
3. Publish `MovieTime.Installer` as a single-file executable (`River-Setup-x64.exe`).
4. Copy the final verified executable to `%USERPROFILE%\Desktop\River Installer\River-Setup-x64.exe`.
5. Verify file size and validate matching SHA-256 checksums.

### 5. Supported Operating Systems & Limitations
- **Supported**: Windows 10 Version 1809 (Build 17763) or higher (x64), Windows 11 (x64).
- **Permissions**: Requires Administrator permissions (UAC prompt) to write to `%ProgramFiles%` and register in `HKLM`.
- **Known Limitations**: ARM64 and 32-bit (x86) Windows editions are not supported (River is built strictly for 64-bit x64 systems).

---

## License

This project is licensed under the MIT License.
