# Movie Time 🎬

> A premium, cinematic, offline-first local media library manager and desktop player for Windows, meticulously inspired by Apple TV's visual language and fluid navigation.

[![.NET](https://img.shields.io/badge/.NET-10.0%20LTS%20(x64)-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![Windows App SDK](https://img.shields.io/badge/WinUI%203-Windows%20App%20SDK%201.6-0078D7?logo=windows)](https://learn.microsoft.com/en-us/windows/apps/winui/winui3/)
[![Tests](https://img.shields.io/badge/Unit%20Tests-160%20Passed-brightgreen)](https://github.com/alnexi/Movie-time)
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

---

## 🏛️ System Architecture

Movie Time follows a decoupled layered architecture:

```
MovieTime/
├── src/
│   ├── MovieTime.App/             # WinUI 3 Native Desktop Application
│   │   ├── Common/                # MVVM ObservableObject & RelayCommand
│   │   ├── Controls/              # HeroBanner, MediaCard, MoviePosterCard, MediaRow, SidebarNavigation
│   │   ├── ViewModels/            # Presentation Models (HomeViewModel, MoviesViewModel, SettingsViewModel, etc.)
│   │   ├── Views/                 # Full-screen views (HomeView, MoviesView, SettingsView)
│   │   ├── MainWindow.xaml        # Edge-to-edge window host & navigation coordinator
│   │   └── App.xaml               # Composition Root & DI configuration
│   │
│   └── MovieTime.Core/            # Core Domain & Platform-Agnostic Engine
│       ├── Database/              # SQLite-net WAL database, migrations & repositories
│       ├── Entities/              # Movie, Series, Season, Episode, MediaFile, ScanRoot
│       ├── Library/               # MLibraryManagerService, LibraryImportService & ScanRootService
│       ├── Logging/               # Serilog rolling local file logger (%LocalAppData%\MovieTime\Logs)
│       ├── Parsing/               # FilenameParserEngine (heuristic classification & extraction)
│       ├── Scanning/              # MediaScannerService (recursive async filesystem scanner)
│       └── Services/              # StoragePathService & AppEnvironment
│
└── tests/
    └── MovieTime.Tests/           # Unit & Integration Tests (160 tests)
```

### Key Components

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

# Run all 160 automated unit tests
dotnet test MovieTime.sln -p:Platform=x64

# Launch the application
dotnet run --project src\MovieTime.App\MovieTime.App.csproj -p:Platform=x64
```

---

## 🧪 Automated Test Suite

The solution features **160 unit and integration tests** covering:
- **Dependency Injection & Bootstrapping**: Service registration, lifetime scopes, and environment paths.
- **Local Logging**: Serilog file sink rolling rules, log formatting, and output directory isolation.
- **SQLite Engine & Migrations**: Schema creation, table constraints, indices, WAL mode, and migrations from v1 to v2.
- **Filesystem Scanner**: File extensions, hidden directory exclusions, symlink safety, and progress reporting.
- **Filename Parsing**: Over 50 real-world filename fixtures spanning complex scene naming conventions, foreign characters, and anime tags.
- **Library Synchronization**: In-memory import cycles, cancellation tokens, missing file detection, and entity deduplication.
- **Multi-Drive MLibrary Management**: Drive enumeration, path formatting, duplicate registration prevention, explicit directory creation confirmation, library removal, and strict scanning scope isolation.
- **Home Presentation Layer**: Pure zero-sample empty library state, neutral hero banner, sidebar category navigation, active category toggles, and card progress bar width math.
- **Movies Presentation Layer**: Database loading, empty library state, live search query filtering, sorting by Title/Year/DateAdded, category filtering, and movie selection state.
- **Settings Presentation Layer**: Drive preview, registration confirmation dialog, status notification binding, and scanning triggers.

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
- [ ] **Phase 4**: Native In-App Hardware-Accelerated Video Player (LibVLC / WinUI MediaPlayer).
- [ ] **Phase 5**: Embedded Artwork & Local Thumbnail Extraction.
- [ ] **Phase 6**: Cinematic Movie & TV Show Detail Screens.

---

## 📄 License

This project is licensed under the MIT License.
