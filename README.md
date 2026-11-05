# Movie Time 🎬

> A premium, cinematic, offline-first local media library manager and desktop player for Windows, meticulously inspired by Apple TV's visual language and fluid navigation.

[![.NET](https://img.shields.io/badge/.NET-10.0%20LTS%20(x64)-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![Windows App SDK](https://img.shields.io/badge/WinUI%203-Windows%20App%20SDK%201.6-0078D7?logo=windows)](https://learn.microsoft.com/en-us/windows/apps/winui/winui3/)
[![Tests](https://img.shields.io/badge/Unit%20Tests-146%20Passed-brightgreen)](https://github.com/alnexi/Movie-time)
[![License](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

---

## 🌟 Overview

**Movie Time** is designed for film and television enthusiasts who keep personal media collections on local hard drives or network shares and desire a first-class, distraction-free, 10-foot-capable desktop experience.

Unlike cluttered streaming apps or heavy web-wrapped wrappers, Movie Time is built natively on **.NET 10 (x64)** and **WinUI 3 (Windows App SDK)**. It runs completely offline without tracking, external telemetry, or unrequested web calls.

---

## 📺 Apple TV Visual Design System (Phase 3A)

The Phase 3A user interface reproduces the signature design principles of modern Apple TV:

1. **Ambient Billboard Hero Section**
   - High-impact cinematic banner with atmospheric dark gradient canvas.
   - Stylized typography, genre tags, and rating badges (e.g. `[R]`, `4K HDR`).
   - Signature white capsule action button (`Go to Movie` / `Play Movie`).
   - Centered horizontal pagination dots.

2. **16:9 Cinematic Media Card Rows**
   - True 16:9 widescreen thumbnails (`260x146`).
   - Smooth focus and hover state: scale lift (`1.06x`), crisp white highlight stroke, and elevation drop shadow.
   - Integrated playback progress indicators for "Continue Watching" items.
   - Typographical gradient fallbacks when poster artwork is absent.

3. **Floating Frosted Acrylic Sidebar**
   - Collapsible floating sidebar with translucent frosted glass styling (`#E8141418` acrylic base with border stroke).
   - Profile avatar header with live clock.
   - Quick navigation categories: `Home`, `Movies`, `TV Shows`, and `Library`.
   - Top-left floating trigger capsule (`( 🏠 Home )`) and dimming canvas backdrop.
   - Instant "Scan & Refresh" action button.

4. **Apple TV Reference Showcase Fallback**
   - When launching with an empty library or before configuring folders, Movie Time renders an authentic Apple TV preview showcase (featuring titles like *Killers of the Flower Moon*, *Slow Horses*, *Dark Matter*, *Dune: Part Two*, and *The Penguin*) so the user immediately experiences the intended look and feel.

---

## 🏛️ System Architecture

Movie Time follows a decoupled layered architecture:

```
MovieTime/
├── src/
│   ├── MovieTime.App/             # WinUI 3 Native Desktop Application
│   │   ├── Common/                # MVVM ObservableObject & RelayCommand
│   │   ├── Controls/              # HeroBanner, MediaCard, MediaRow, SidebarNavigation
│   │   ├── ViewModels/            # Presentation Models (HomeViewModel, MediaCardViewModel, etc.)
│   │   ├── Views/                 # Full-screen views (HomeView)
│   │   ├── MainWindow.xaml        # Edge-to-edge window host
│   │   └── App.xaml               # Composition Root & DI configuration
│   │
│   └── MovieTime.Core/            # Core Domain & Platform-Agnostic Engine
│       ├── Database/              # SQLite-net WAL database, migrations & repositories
│       ├── Entities/              # Movie, Series, Season, Episode, MediaFile, ScanRoot
│       ├── Library/               # LibraryImportService & ScanRootService
│       ├── Logging/               # Serilog rolling local file logger (%LocalAppData%\MovieTime\Logs)
│       ├── Parsing/               # FilenameParserEngine (heuristic classification & extraction)
│       ├── Scanning/              # MediaScannerService (recursive async filesystem scanner)
│       └── Services/              # StoragePathService & AppEnvironment
│
└── tests/
    └── MovieTime.Tests/           # Unit & Integration Tests (146 tests)
```

### Key Components

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

# Run all 146 automated unit tests
dotnet test MovieTime.sln -p:Platform=x64

# Launch the application
dotnet run --project src\MovieTime.App\MovieTime.App.csproj -p:Platform=x64
```

---

## 🧪 Automated Test Suite

The solution features 146 unit and integration tests covering:
- **Dependency Injection & Bootstrapping**: Service registration, lifetime scopes, and environment paths.
- **Local Logging**: Serilog file sink rolling rules, log formatting, and output directory isolation.
- **SQLite Engine & Migrations**: Schema creation, table constraints, indices, WAL mode, and migrations from v1 to v2.
- **Filesystem Scanner**: File extensions, hidden directory exclusions, symlink safety, and progress reporting.
- **Filename Parsing**: Over 50 real-world filename fixtures spanning complex scene naming conventions, foreign characters, and anime tags.
- **Library Synchronization**: In-memory import cycles, cancellation tokens, missing file detection, and entity deduplication.
- **Presentation Layer**: Showcase fallback initialization, sidebar category navigation, active category toggles, and card progress bar width math.

---

## 🗺️ Roadmap

- [x] **Phase 0**: Project Scaffolding (.NET 10, x64, WinUI 3, XAML).
- [x] **Phase 1**: Dependency Injection, Serilog Local Logging, SQLite Database Layer.
- [x] **Phase 2A**: Recursive Filesystem Media Scanner.
- [x] **Phase 2B**: Heuristic Filename & Metadata Parser Engine.
- [x] **Phase 2C**: Scanner, Parser & Database Synchronization Service.
- [x] **Phase 3A**: Apple TV-Inspired Home UI Canvas, Billboard Hero Section, and Floating Sidebar.
- [ ] **Phase 3B**: Cinematic Movie & TV Show Detail Screens.
- [ ] **Phase 4**: Native In-App Hardware-Accelerated Video Player (LibVLC / WinUI MediaPlayer).
- [ ] **Phase 5**: Embedded Artwork & Local Thumbnail Extraction.
- [ ] **Phase 6**: Library Settings & Folder Management UI.

---

## 📄 License

This project is licensed under the MIT License.
