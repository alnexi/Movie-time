# Changelog

All notable changes to **Movie Time** will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [Unreleased]

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
