# Movie Time 🎬

> A premium, cinematic, offline-first local media library manager and desktop player for Windows, meticulously inspired by Apple TV's visual language and fluid navigation.

[![.NET](https://img.shields.io/badge/.NET-10.0%20LTS%20(x64)-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![Windows App SDK](https://img.shields.io/badge/WinUI%203-Windows%20App%20SDK%201.7-0078D7?logo=windows)](https://learn.microsoft.com/en-us/windows/apps/winui/winui3/)
[![Tests](https://img.shields.io/badge/Unit%20Tests-263%20Passed-brightgreen)](https://github.com/alnexi/Movie-time)
[![License](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

---

## 🌟 Overview

**Movie Time** is designed for film and television enthusiasts who keep personal media collections on local hard drives or network shares and desire a first-class, distraction-free, 10-foot-capable desktop experience.

Unlike cluttered streaming apps or heavy web-wrapped wrappers, Movie Time is built natively on **.NET 10 (x64)** and **WinUI 3 (Windows App SDK)**. It runs completely offline without tracking, external telemetry, or unrequested web calls.

---

## 📺 Apple TV Visual Design System

### 1. Home Screen (Phase 3A)
* **Ambient Billboard Hero Section**: Edge-to-edge cinematic banner with dark radial and linear vignette gradients that blend into content rows below.
* **Apple TV Typography & Badges**: Stylized bold titles, genre indicators, and rating badges (e.g. `[R]`, `4K HDR`).
* **White Capsule Action Pill**: Apple TV-signature white pill button (`Go to Movie` / `Play Movie`) with dark iconography.
* **Centered Carousel Dots**: Elongated active white pill dot with subtle inactive dots.
* **Top-Left Floating Trigger**: Floating translucent capsule button (`( 🏠 Home )`) positioned in the upper-left margin to open the sidebar.
* **"Continue Watching" Row**: Horizontal scrollable container hosting 16:9 aspect ratio media cards with playback progress indicators.
* **Showcase Fallback**: Built-in Apple TV preview showcase displayed when launching with an empty library.

### 2. Movies Library (Phase 3B)
* **Top-Left Navigation Capsule**: Distinctive capsule pill button `( 🗂️ Library )` / `( 🏠 Home )` providing fluid one-click transitions between Home and the Movies Library.
* **Embedded Category & Genre Filter List**:
  - Filter categories: `All Movies`, `Recently Added`, `4K & High Definition`.
  - Apple TV Section Header: `GENRES` (`Action & Adventure`, `Comedy`, `Drama`, `Kids & Family`, `Sci-Fi & Fantasy`).
  - Active category highlighted as a solid white capsule with dark iconography; inactive categories styled with clean typography.
* **Apple TV 2:3 Vertical Poster Grid**:
  - Standard 2:3 theatrical movie poster dimensions (`170x255`) with rounded corners (`14px`).
  - Smooth focus and hover animation: **1.06x scale lift**, bright white selection border (**3px**), and elevation drop shadow.
  - Sophisticated dark gradient fallback artwork for local movies without embedded or cached posters.
  - Quality badges (`4K HDR`, `1080p HD`) positioned on each card based on probed media file properties.
* **Apple TV Quick Action Context Dialog**:
  - Selecting a movie poster opens an Apple TV-inspired quick-action capsule dialog right over the card.
  - Actions: `Play` (with play triangle glyph) and `Go to Movie` (active white pill with `(i)` info glyph).
  - Background cards smoothly dim to focus user attention on the selected title.
* **Instant Local Title Search & Sorting**:
  - Built-in pill search box with real-time title filtering.
  - Multi-criteria sorting: `Title (A-Z)`, `Release Year (Newest)`, and `Recently Added`.
  - Empty search state feedback with one-click "Clear Search" recovery.
* **Polished Empty State**:
  - When the SQLite database contains 0 movies, renders an elegant empty state explaining that the local library is empty, with a white pill "Scan Library Now" button.
  - Never fabricates fake movies into the user's database.

### 3. Multi-Drive MLibrary Management & Settings (Phase 3C)
* **Complete Removal of Demo Content**:
  - Zero hardcoded mock movies, fake titles, or simulated video assets.
  - Completely clean, honest empty states that guide the user to configure real local drives.
* **Steam-Inspired Multi-Drive Storage (`MLibrary`)**:
  - Automatically enumerates available Windows drives (`C:`, `D:`, `E:`, etc.) with volume labels and storage capacities.
  - Standardizes on a dedicated, isolated `MLibrary` folder per drive (e.g. `D:\MLibrary`).
  - Strict scanning boundary: Movie Time scans **ONLY** inside registered `MLibrary` directories; never touches root disks or lateral personal folders.
* **Apple TV-Inspired Settings UI**:
  - Top-left `( ⚙️ Settings )` pill button matching the signature navigation aesthetic.
  - Drive status badges with real-time health: `Available` (Green), `Folder Missing` (Orange), `Drive Disconnected` (Red), `Disabled` (Gray).
  - Add Drive Dialog: Select drive, preview `MLibrary` target path, and require explicit confirmation checkbox before creating folders on disk.
  - Scan controls per library and global "Scan All Libraries" orchestration.
  - Non-destructive library removal: unregisters the library folder without modifying or deleting media files on disk.

### 4. Cinematic Movie Details Experience (Phase 3D)
* **Design References Inspired Layout**:
  - Full-width hero canvas rendering genuine local backdrops when available on disk, combined with left and bottom dark vignette overlays for crisp typography.
  - Atmospheric ambient lighting fallback when no backdrop file is present on disk (zero web-scraping or fabricated artwork).
  - Prominent Apple TV-signature title typography with release year and edition badges (e.g. *Director's Cut*, *Extended Edition*).
* **Genuinely Available Metadata Only**:
  - Strictly presents verified technical properties: resolution (`4K UHD`, `1080p HD`), video codec (`HEVC`, `AVC`), audio format, container format (`MKV`, `MP4`), and actual file size.
  - Zero fabricated information: omitted fields disappear naturally rather than displaying synthetic ratings, false genres, or fabricated cast.
* **Apple TV White Capsule Play Button**:
  - Prominent `▶ Play Movie` white pill button directly connected to the active local physical video file path, with feedback banner and integration boundary for Phase 4 VLC playback.
* **Multiple Local Media Editions / Files**:
  - Displays all local video files cataloged for a movie and allows the user to switch between editions with instant metadata updates.
* **Offline Library Recommendations ("More from your library")**:
  - Horizontal recommendation row populated strictly with other real movies from the user's local SQLite database. Omitted cleanly if no other movies exist.
* **Bi-Directional Navigation**:
  - Smooth navigation from Movies Library → Movie Details → Movies Library, preserving search filters and category selection state.
* **Phase 3D Hotfix (v0.5.1)**:
  - Resolved WinUI 3 XAML binding engine crash (`ArgumentException: The parameter is incorrect.`) during navigation when local backdrops are missing or null.
  - Implemented safe code-behind bitmap management and lifecycle subscriptions.
  - Generated native desktop shortcut (`Movie Time.lnk`) pointing to the x64 binary for quick launching.

### 5. Native Cinematic Player & Liquid Glass (Phase 4)
* **Embedded Hardware-Accelerated Playback (`LibVLCSharp.WinUI` + `VideoLAN.LibVLC.Windows`)**:
  - Embedded playback pipeline rendered directly inside `LibVLCSharp.Platforms.Windows.VideoView`.
  - Zero external popup windows, zero detached `vlc.exe` subprocesses.
  - Hardware decoding acceleration (`--avcodec-hw=any`, `--directx-hw-yuv=1`) providing smooth 4K/UHD and HEVC/H.264 playback.
  - Dynamic audio track and subtitle track selection and disabling.
  - Automatic progress checkpointing to SQLite (`PlaybackProgress`) for resume across sessions.
* **Apple TV Visual Player Layout**:
  - **Top Context Line**: Episode/Edition info and technical format badges (`2023 • 4K UHD • HEVC`).
  - **Cinematic Title Typography**: Prominent semi-bold movie title treatment.
  - **Right-Aligned Header Controls**: Circular Liquid Glass buttons for Subtitles (`💬`), Audio Options (`(((|||)))`), and Fullscreen/PiP (`⤢`).
  - **Full-Width Scrubber Bar**: Sleek timeline bar with played white highlight progress, draggable scrubber, elapsed time badge (`04:33 ⏸`), and negative remaining time (`-54:08`).
  - **Bottom-Left Action Pills**: Liquid Glass pill buttons for `Info`, `InSight`, and `Continue Watching`.
  - **InSight Media Card & Track Flyouts**: Liquid Glass flyouts for real-time track selection and media file technical details.
  - **3.5s Inactivity Auto-Hide**: Controls smoothly fade out during active playback after 3.5 seconds of inactivity, waking instantaneously upon mouse movement or keyboard input.
* **Liquid Glass Visual System**:
  - Native WinUI 3 multi-layered acrylic and specular composition:
    - Multi-layer translucent acrylic materials (`#30FFFFFF`, `#18FFFFFF`).
    - Incident-angle specular rim gradient borders (`#B8FFFFFF` to `#15FFFFFF`).
    - Subtle chromatic dispersion fringing (`#1870D0FF` cyan to `#18FFA850` amber).
    - Atmospheric dark linear vignette gradient overlays ensuring ultra-sharp high-contrast typography while leaving the underlying video 100% unblurred and untouched.
* **Complete Desktop Control & Shortcuts**:
  - `Space` / `K`: Play / Pause toggle without focus interference.
  - `J` / `L`: Seek backward / forward 10 seconds.
  - `Left` / `Right` Arrow: Seek backward / forward 5 seconds.
  - `0` to `9`: Seek to 0%, 10%, 20%, 30%, 40%, 50%, 60%, 70%, 80%, 90% of duration.
  - `Up` / `Down` Arrow: Volume up / down (5% increments).
  - `M`: Mute / Unmute.
  - `F` / `Alt+Enter`: Toggle edge-to-edge Fullscreen (`AppWindowPresenterKind.FullScreen`).
  - `Esc` / `Backspace`: Exit player and return seamlessly to Movie Details / Library.
* **Phase 4.1 Hotfix (v0.6.1)**:
  - Resolved fatal `0xc000027b` FailFast crash during startup by replacing static XAML `<lvs:VideoView>` markup with lazy programmatic surface attachment upon player navigation.
  - Aligned `Microsoft.WindowsAppSDK` to `1.7.250909003` to resolve DirectComposition swapchain ABI mismatches.
  - Implemented `IDispatcherService` and `WinUiDispatcherService` to marshal all unmanaged LibVLC background events to the UI thread, resolving `0x8001010E (RPC_E_WRONG_THREAD)` exceptions.
  - Verified clean launch via Desktop shortcut and direct binary execution.
* **Phase 4.2 Single-Window Player Architecture (v0.6.2)**:
  - Eliminated external/disconnected VLC popup windows by passing native Direct3D11 swapchain options (`--winrt-d3dcontext`, `--winrt-swapchain`) from `VideoView.Initialized` to `LibVLC`.
  - Enforced single-window architecture: video renders directly inside `PlayerView`'s in-tree `SwapChainPanel` beneath interactive Liquid Glass transport controls, timeline playhead, and back navigation.
  - Ensured single-window fullscreen transitions (`AppWindowPresenterKind.FullScreen`) and Esc/Back navigation to Movie Details without creating secondary windows.
* **Phase 4.3 Player Seek Stability Hotfix (v0.6.3)**:
  - **Non-Blocking Asynchronous Seeking**: Offloaded native `_mediaPlayer.Time = clampedTime` from the WinUI UI thread to a dedicated background coalescing worker, completely eliminating UI thread starvation, freezes, and "Not Responding" hangs.
  - **Coalesced Scrubbing**: Intermediate seek requests during rapid dragging are automatically coalesced, delivering fluid 60fps playhead scrubbing and live video preview without decoder backlog.
  - **Direct Timeline Tap-to-Seek**: Wired universal pointer capture handlers (`handledEventsToo: true`) on `TimelineSlider` and `TimelineContainer`, allowing immediate jump-to-position upon clicking anywhere along the timeline bar.
  - **Feedback Loop Elimination**: Introduced scrubbing lifecycle guards (`IsDraggingSlider`, `IsUpdatingFromPlayer`) preventing automatic player position ticks from triggering redundant seeks.
  - **Safe Clamping & State Preservation**: Applied strict bounds clamping `[0, duration - 250ms]` and timestamp rounding, preserving paused/playing states across seek operations.
* **Phase 4.4 YouTube Shortcuts, External Audio/Subtitles & Subtitle Customization (v0.6.4)**:
  - **YouTube-Style Keyboard Shortcuts**: Full suite (`Space`, `K`, `J`, `L`, `←`, `→`, `0`-`9`, `F`, `M`, `Esc`) with text-input focus guards and in-app quick reference.
  - **External Audio & Audio Delay Sync**: Load external audio files (`.mka`, `.mkv`, `.aac`, `.ac3`, `.mp3`, etc.) with `±100ms` step adjustments and `0ms` reset to fix out-of-sync dubbed tracks.
  - **External Subtitles Support**: Attach and manage external subtitle files (`.srt`, `.ass`, `.ssa`, `.vtt`, `.sub`) with instantaneous track switching.
  - **Subtitle Customization & Persistence**: User-configurable font sizes (22pt to 52pt), high-visibility colors (White, Yellow, Cyan, Green), outline thickness, background opacities, and vertical position offsets persisted across sessions.
  - **Apple TV Liquid Glass Flyouts**: Refined overlay menus with frosted acrylic surfaces, high-contrast typography, and zero playback interruptions.
  - **Expanded Test Suite**: 217 automated unit tests validating playback logic, seeking math, external slave loading, and preference serialization.
* **Phase 5 Embedded Artwork & Local Thumbnail Extraction (v0.7.0)**:
  - **Tier 1 Embedded Cover Art Extraction**: Fast stream parser extracting embedded artwork directly from MP4/M4V/MOV atoms (`moov/udta/meta/ilst/covr/data`) and MKV/WebM EBML elements (`Segment/Attachments/AttachedFile`) without video decoding overhead.
  - **Tier 2 Local Video Thumbnail Frame Sampling**: Multi-timestamp keyframe extraction across the video timeline using native Windows Media Composition and Shell APIs.
  - **Multi-Metric Composite Quality Scoring**: Evaluates candidate frames for luminance, contrast std-dev, edge gradient sharpness, and Hasler-Süsstrunk colorfulness, automatically discarding black frames, blown-out frames, and flat scenes.
  - **Hardware-Accelerated Aspect Ratio Cropping**: Generates 2:3 theatrical posters (`600x900`) and 16:9 widescreen backdrops (`1920x1080`) via Windows Imaging Component (`WIC`).
  - **Deterministic Collision-Safe Caching**: Normalized path + file size + UTC modified timestamp SHA256 deterministic key generation. Caches all extracted assets under `%LocalAppData%\MovieTime\Artwork\Posters\` and `Backdrops\`; original user video directories are never modified.
  - **Multi-Drive Support & Real-Time Event Dispatching**: Differentiates identical filenames on separate drives; dispatches `ArtworkUpdated` events to refresh `HeroBannerControl`, `MoviePosterCardControl`, and `MediaCardControl` in real time.
  - **Expanded Test Suite**: 235 automated unit tests validating all format parsers, quality scoring algorithms, cache invalidation rules, and pipeline coordinators.
* **Phase 6 TV Show & Season/Episode Detail Experience (v0.8.0)**:
  - **Deterministic TV Episode Recognition**: Advanced filename pattern parsing covering standard scene releases (`The.Wire.S01E08.Lessons.REMASTERED.720p.WEB-DL.2CH.x265.HEVC.PSA.AvaMovie.mkv`), `1x08`, `Season 1 Episode 8`, multi-episode packs (`S01E08E09`), and Specials (`S00E01`). Strict token cutoff bounds reliably extract authentic episode titles (`Lessons`) while discarding technical noise without fabricating metadata.
  - **Hierarchical SQLite Relational Synchronization**: Scanned episodes automatically associate with `Series`, `Season`, and `Episode` records in SQLite, tracking actual video files via `MediaFile` foreign keys without duplicate entities on rescans.
  - **Apple TV-Inspired TV Shows Library (`TvShowsView`)**: 2:3 vertical poster grid displaying series title, release years, season counts, and episode counts with real-time search filtering, multi-criteria sorting (`Title`, `Year`, `Recently Added`), and empty state handling.
  - **Cinematic Series Details Experience (`SeriesDetailsView`)**: Atmospheric full-bleed hero banner with genuine extracted backdrops and posters, smart `▶ Resume / Play Next Up` action button, interactive season pill selector tabs, and 16:9 episode list cards with duration, quality badges (`4K UHD`, `1080p`, `720p`), progress indicator bars, and candidate video frame thumbnails.
  - **Episode Playback & Discrete Resume Progress**: Full LibVLC embedded player integration displaying series title and episode context (`Season 1, Episode 8 • Lessons • 720p`), persisting resume checkpoints to SQLite keyed by `EpisodeId`, and returning smoothly to Series Details upon exit.
  - **Home & Continue Watching Integration**: Dedicated "TV Shows" shelf on Home navigating directly to series details, plus automatic population of in-progress episodes in "Continue Watching" with instant one-click resume.
  - **Expanded Test Suite**: 263 automated unit tests validating episode filename parsing, database hierarchies, view models, and playback resume tracking.

---

## 🏛️ System Architecture

Movie Time follows a decoupled layered architecture:

```
MovieTime/
├── src/
│   ├── MovieTime.App/             # WinUI 3 Native Desktop Application
│   │   ├── Common/                # MVVM ObservableObject & RelayCommand
│   │   ├── Controls/              # HeroBanner, MediaCard, MoviePosterCard, MediaRow, SidebarNavigation
│   │   ├── Services/              # VlcPlayerService, WicImageProcessingService, WindowsVideoFrameExtractor
│   │   ├── ViewModels/            # Presentation Models (HomeViewModel, MoviesViewModel, TvShowsViewModel, SeriesDetailsViewModel, MovieDetailsViewModel, SettingsViewModel, PlayerViewModel)
│   │   ├── Views/                 # Full-screen views (HomeView, MoviesView, TvShowsView, SeriesDetailsView, MovieDetailsView, SettingsView, PlayerView)
│   │   ├── MainWindow.xaml        # Edge-to-edge window host & navigation coordinator
│   │   └── App.xaml               # Composition Root & DI configuration
│   │
│   └── MovieTime.Core/            # Core Domain & Platform-Agnostic Engine
│       ├── Artwork/               # Embedded artwork extraction, quality evaluation, storage, and pipeline coordination
│       ├── Database/              # SQLite-net WAL database, migrations & repositories
│       ├── Entities/              # Movie, Series, Season, Episode, MediaFile, ScanRoot, PlaybackProgress
│       ├── Library/               # MLibraryManagerService, LibraryImportService & ScanRootService
│       ├── Logging/               # Serilog rolling local file logger (%LocalAppData%\MovieTime\Logs)
│       ├── Parsing/               # FilenameParserEngine (heuristic classification & extraction)
│       ├── Playback/              # IVlcPlayerService, PlayerState, PlayerTrackInfo, SubtitleSettings
│       ├── Scanning/              # MediaScannerService (recursive async filesystem scanner)
│       └── Services/              # StoragePathService & AppEnvironment
│
└── tests/
    └── MovieTime.Tests/           # Unit & Integration Tests (263 tests)
```

### Key Components

- **Native Video Player Engine (`IVlcPlayerService` & `VlcPlayerService`)**:
  - Embedded hardware-accelerated playback adapter wrapping `LibVLCSharp.WinUI` and `VideoLAN.LibVLC.Windows`.
  - Truly in-process DirectComposition video rendering; zero external popup windows or child processes.
  - Live discovery and switching of embedded audio and subtitle tracks.
  - Automatic progress tracking and resume in SQLite via `IPlaybackProgressRepository`.
- **Multi-Drive MLibrary Manager (`MLibraryManagerService`)**:
  - Steam-inspired multi-drive storage management with automated Windows drive enumeration (`DriveInfo.GetDrives()`).
  - Strict scanning boundary: confines media discovery to `<Drive>:\MLibrary` folders, guaranteeing system roots and personal files are never touched.
  - Interactive drive registration with disk preview, folder existence verification, and mandatory confirmation before creating directories.
- **Database Layer (`sqlite-net-pcl` & `SQLitePCLRaw.bundle_e_sqlite3`)**:
  - Local SQLite database stored at `%LocalAppData%\MovieTime\Database\movietime.db`.
  - Configured with Write-Ahead Logging (`WAL`), `NORMAL` synchronous mode, and automatic schema migrations (Version 1 & 2).
- **Scanner Engine (`MediaScannerService`)**:
  - High-performance recursive enumeration supporting `.mp4`, `.mkv`, `.avi`, `.mov`, `.wmv`, `.m4v`, `.webm`, `.flv`, `.ts`, and `.m2ts`.
  - Non-blocking asynchronous enumeration with cancellation tokens and robust permissions error handling.
- **Filename Parser Engine (`FilenameParserEngine`)**:
  - Offline heuristic regex parsing for movies, multi-part movies, and episodic TV series (e.g. `S01E02`, `1x02`, multi-episode tokens).
  - Codec, resolution, audio tag extraction (`2160p`, `1080p`, `HDR10`, `Atmos`, `x265`, etc.).
- **Synchronization Engine (`LibraryImportService`)**:
  - Reconciles scanned files with database entities, handles multi-part files, detects removed or missing files without deleting metadata, and manages user scan roots.

---

## 🛠️ Prerequisites & Building

- **Windows 10 Version 1809 (Build 17763)** or higher / **Windows 11** (x64)
- **.NET 10 SDK** (x64)
- **Visual Studio 2026 / 2022** or **VS Code** with C# Dev Kit and Windows App SDK build tools

### Build from Command Line

```powershell
# Restore dependencies and build the entire solution (x64 Debug)
dotnet build MovieTime.sln -p:Platform=x64

# Run all 194 automated unit tests
dotnet test MovieTime.sln -p:Platform=x64

# Launch the application
dotnet run --project src\MovieTime.App\MovieTime.App.csproj -p:Platform=x64
```

---

## 🧪 Automated Test Suite

The solution features **194 unit and integration tests** covering:
- **Dependency Injection & Bootstrapping**: Service registration, lifetime scopes, and environment paths.
- **Local Logging**: Serilog file sink rolling rules, log formatting, and output directory isolation.
- **SQLite Engine & Migrations**: Schema creation, table constraints, indices, WAL mode, and migrations from v1 to v2.
- **Filesystem Scanner**: File extensions, hidden directory exclusions, symlink safety, and progress reporting.
- **Filename Parsing**: Over 50 real-world filename fixtures spanning complex scene naming conventions, foreign characters, and anime tags.
- **Library Synchronization**: In-memory import cycles, cancellation tokens, missing file detection, and entity deduplication.
- **Multi-Drive MLibrary Management**: Drive enumeration, path formatting, duplicate registration prevention, explicit directory creation confirmation, library removal, and strict scanning scope isolation.
- **Home Presentation Layer**: Pure zero-sample empty library state, neutral hero banner, sidebar category navigation, active category toggles, and card progress bar width math.
- **Movies Presentation Layer**: Database loading, empty library state, live search query filtering, sorting by Title/Year/DateAdded, category filtering, and movie selection state.
- **Movie Details Presentation Layer**: Real movie identification, year handling, missing artwork fallback flags, missing metadata omission, multiple editions/files selection, Play command target file binding, and local recommendations.
- **Settings Presentation Layer**: Drive preview, registration confirmation dialog, status notification binding, and scanning triggers.
- **Cinematic Player Presentation Layer**: Time formatting, seek offset math, volume clamping, mute toggling, edge-to-edge fullscreen presenter transitions, track enumeration/selection, InSight metadata binding, and auto-hide timer triggers.
- **Playback Progress Persistence**: SQLite CRUD operations, position tick checkpoints, percentage calculations, completion flag thresholding, and play count increments.

---

## 🗺️ Roadmap

- [x] **Phase 0**: Project Scaffolding (.NET 10, x64, WinUI 3, XAML).
- [x] **Phase 1**: Dependency Injection, Serilog Local Logging, SQLite Database Layer.
- [x] **Phase 2A**: Recursive Filesystem Media Scanner.
- [x] **Phase 2B**: Heuristic Filename & Metadata Parser Engine.
- [x] **Phase 2C**: Scanner, Parser & Database Synchronization Service.
- [x] **Phase 3A**: Apple TV-Inspired Home UI Canvas, Billboard Hero Section, and Floating Sidebar.
- [x] **Phase 3B**: Apple TV-Inspired Movies Library Screen (Poster Grid, Category Filtering, Search, Quick Actions).
- [x] **Phase 3C**: Real Library System & Multi-Drive MLibrary Manager (Fake Data Removal, Drive Registration, Settings UI).
- [x] **Phase 3D**: Cinematic Movie Details Experience (Full-Width Hero, True Local Metadata, Editions Selector, Offline Recommendations).
- [x] **Phase 4**: Native In-App Hardware-Accelerated Video Player (LibVLCSharp / WinUI 3 VideoView) + Liquid Glass.
- [ ] **Phase 5**: Embedded Artwork & Local Thumbnail Extraction.
- [ ] **Phase 6**: TV Show & Season/Episode Detail Experience.

---

## 📄 License

This project is licensed under the MIT License.
