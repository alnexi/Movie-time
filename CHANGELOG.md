# Changelog

All notable changes to **River** will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [Unreleased]

## [1.2.2] - 2026-11-12 (River — Premium All-in-One Installer)

### Added
- **Premium All-in-One Installer (`River-Setup-x64.exe`)**:
  - Implemented custom standalone single-file installer UI in `MovieTime.Installer` using .NET 10 x64 WPF with River's signature dark Liquid Glass aesthetic.
  - Complete custom styling: translucent acrylic glass cards, rounded chromeless window, minimize/close controls, and River's animated pixel-art sailboat floating in an atmospheric night scene.
  - Multi-step guided wizard: Welcome, Installation Options (directory selection, disk space check, shortcut & file association toggles), Real-Time Progress, Completion, and Error Recovery.
- **100% Offline Dependency Bundling**:
  - Automatically embeds all prerequisites inside a single 184 MB standalone binary (`River-Setup-x64.exe`):
    - .NET 10 x64 self-contained runtime.
    - Windows App SDK / WinUI 3 self-contained runtime assemblies.
    - Native LibVLC 3.0.24 libraries (`libvlc.dll`, `libvlccore.dll`) and full codecs/plugins directory.
    - SQLite native engine (`e_sqlite3.dll`).
    - All River visual assets and icons.
  - Zero external prerequisites or internet connectivity required during installation.
- **Windows Integration & Shell Registration**:
  - Native Windows Shell COM shortcut creation for Start Menu and Desktop via `IShellLinkW` / `IPersistFile`.
  - Windows Explorer "Open With" file association registration for video formats (`.mp4`, `.mkv`, `.avi`, `.mov`, etc.).
  - Proper Windows "Installed apps" / "Add or remove programs" registration with publisher, version, display icon, and estimated size.
- **Dedicated Uninstaller with Data Safety Guarantee**:
  - Drops matching `Uninstall.exe` in the application directory.
  - Strictly preserves personal user data: **never deletes or modifies `%LocalAppData%\MovieTime` (database, settings, artwork) or user media files**.
- **Automated Build Pipeline**:
  - Created `build-installer.ps1` automating self-contained publish, optimal payload compression, single-file compilation, and automated deployment to `%USERPROFILE%\Desktop\River Installer\River-Setup-x64.exe` with SHA-256 validation.
- **Automated Verification Tests**:
  - Added `InstallerTests.cs` covering shortcut creation/deletion, desktop binary existence & PE header validation, payload archive dependency inspection, and uninstallation data safety. Total automated test suite expanded to 351 tests (100% passing).

## [1.2.1] - 2026-11-11 (River — Final Data Safety & Lifecycle Audit)

### Fixed & Improved
- **Filesystem Directory Boundary Safety**:
  - Implemented `MediaRemovalService.IsPathUnderDirectory` with strict directory separator matching to ensure scan root unregistration never matches sibling directories sharing common prefixes (e.g. `C:\Movies` vs `C:\Movies_Backup`).
- **Surviving Nested Root Media Preservation**:
  - Unregistering a child or nested scan root now checks for surviving active scan roots covering the media file; files covered by surviving roots are retained and reassigned (`ScanRootId = coveringRoot.Id`) rather than deleted.
- **Cascade PlaybackProgress Cleanup**:
  - Added `DeleteByMovieIdAsync` and `DeleteByEpisodeIdAsync` to `IPlaybackProgressRepository` and `PlaybackProgressRepository`.
  - Ensures all associated playback progress records are deleted whenever a movie or episode is removed from the library.
- **PlaybackProgress SQLite Upsert Fix**:
  - Fixed `PlaybackProgressRepository.SaveProgressAsync` to use `InsertAsync` for new records (allowing SQLite auto-increment primary key generation) and `UpdateAsync` for existing records, preventing new records from overwriting row `Id = 0`.
- **Player Shutdown & Progress Persistence**:
  - Updated `App.xaml.cs` `OnWindowClosed` to retrieve the active `MainWindow.PlayerViewModel` instance, stopping playback and flushing progress to database before database disposal.
  - Added `SemaphoreSlim _saveProgressLock` in `PlayerViewModel` to guard against race conditions between periodic 5-second background progress saves and shutdown/exit saves.
  - Disposed `_volumeFeedbackTimer`, `_speedFeedbackTimer`, `_speedHudTimer`, and `_saveProgressLock` in `PlayerViewModel.Dispose()`.
- **Theme Settings Flush on Shutdown**:
  - Ensured `themeSettings.FlushPendingSavesAsync()` is invoked during `App.xaml.cs` `OnWindowClosed` prior to database disposal.

## [1.2.0] - 2026-11-11 (River — Safe Final QA Bug Fixes & Home Navigation Repair)

### Fixed
- **Home Screen Navigation Repair**:
  - Bound the upper-left Home button in `HomeView.xaml` to `ViewModel.NavigateHomeCommand`.
  - Implemented harmless navigation when already on the Home screen: closes the sidebar overlay and smoothly scrolls to top without reloading database records, re-querying collections, or causing navigation loops.
- **QA-001 (High - Audio Leak & Unsaved Progress on Player Exit)**:
  - Added `StopPlaybackAsync()` to `PlayerViewModel` to ensure progress is saved and `_playerService.Stop()` is invoked whenever navigating away from the player view.
  - Updated `MainWindow.ExitPlayerMode()` to stop the active player and save playback position.
- **QA-002 (High - Native VLC Resource Cleanup on App Close)**:
  - Updated `App.xaml.cs` `OnWindowClosed` to stop and dispose `PlayerViewModel` and native `IVlcPlayerService` (`_libVlc`, `_mediaPlayer`), preventing audio continuation and unmanaged memory leaks.
- **QA-003 (Medium - Hero Billboard Carousel Indicator Clutter)**:
  - Implemented dynamic sliding window of at most 7 dots (`MaxVisibleDots = 7`) in `HeroBannerViewModel`.
  - Prevents horizontal overflow across wide screens for large collections while preserving all candidate movies and full rotation.
- **QA-004 (Medium - Remove Movie When Physical File Missing)**:
  - Added `RemoveMovieFromLibraryAsync(int movieId)` in `MediaRemovalService` and `IMediaRemovalService`.
  - Permits removing stale database records and recording persistent exclusions even when physical media files have been moved or deleted outside the app.
  - Updated `MovieDetailsViewModel` and `MoviesViewModel` to remove movies by ID.
- **QA-005 (Medium - Unregistering Library Root Leaves Orphaned Records)**:
  - Added `RemoveMediaForScanRootAsync` in `MediaRemovalService` called by `ScanRootService.RemoveRootAsync`.
  - Reconciles and deletes orphaned media files, episodes, seasons, series, and movies belonging exclusively to the unregistered scan root.
- **QA-006 (Medium - Concurrent Theme Settings JSON Writes)**:
  - Added a 300ms debounce timer in `ThemeSettingsService.UpdateThemeValueAsync`.
  - Updates in-memory theme values instantly for 60 FPS slider responsiveness while throttling disk I/O to avoid file lock contention.
  - Added `FlushPendingSavesAsync` to guarantee final persistence on demand and during shutdown.
- **QA-007 (Low - Series Without Media Files Displays Empty Cards)**:
  - Updated `TvShowsViewModel.LoadSeriesAsync` and `HomeViewModel.PopulateFromDatabaseAsync` to omit TV series that contain 0 media files (or only missing files).
- **QA-008 (Low - Version Discrepancy)**:
  - Synchronized `AppConstants.ApplicationVersion` to `"1.2.0"`.

## [1.1.0] - 2026-11-10 (River — UI Bugfix Phase 2 & Home, TV Show Details and Player UX Improvements)

### Added
- **Home Screen "Recently Added" Section**:
  - Dynamically populated from synchronized SQLite database containing ALL eligible video files (Movies, TV episodes, standalone video files, editions).
  - Sorted newest-first by discovery/import timestamp (`LastScannedAt` desc, `Id` desc).
  - Skips missing and excluded media files.
  - Interactive playback and navigation hooks for each card type.
- **Home Hero Billboard Candidate Expansion & Stable Fisher-Yates Shuffle**:
  - Billboard candidate collection now includes EVERY eligible movie present in the local database (removed artificial 7-item cap).
  - Shuffled via Fisher-Yates on startup and cached in `_heroMovieIdSequence` to remain consistent across navigation visits.
  - Previous/Next and two-finger swipe gestures navigate through the full candidate sequence without reshuffling.
- **TV Show Details Dynamic Episode Background & Cinematic Crossfade**:
  - Automatically builds a shuffled rotation pool of backdrops exclusively from that TV show's own episodes with valid extracted thumbnails.
  - Subtle, distraction-free 9-second crossfade animation (350ms transition) rotating through episode artwork.
  - Automatic fallback to series backdrop or ambient canvas if episode artwork is not available.
  - Clean lifecycle management: timer is stopped and disposed immediately when navigating away or uncompleted.
- **Universal Home Navigation from All Views**:
  - Added dedicated "Home" button (`\uE80F`) in top navigation bars of Player, Movie Details, and Series Details.
  - Clicking "Home" while already on Home view smoothly scrolls to the top (`MainHomeView.ScrollToTop()`) and closes sidebar.
  - Restores active navigation state to "Home" in sidebar and top triggers.
- **Decoupled Player Control Panel & Bottom-Right Feedback Capsules**:
  - Completely separated playback control panel visibility from temporary speed and volume feedback.
  - Adjusting volume or speed when controls are hidden displays a subtle, semi-transparent feedback capsule in the bottom-right corner without revealing the full control HUD or resetting the auto-hide timer.
  - Fast, responsive volume (0–200%) and speed (0.1x–2.0x) status indicators.
- **Player Multi-Touch Two-Finger Gestures**:
  - Native WinUI touch gesture recognition (`PointerDeviceType.Touch`):
    - Two-finger vertical drag smoothly adjusts volume (0–200%) with capsule feedback.
    - Two-finger horizontal swipe seeks ±10 seconds with temporary skip feedback.
  - Zero conflict with mouse clicks, single-finger tap (play/pause toggle), or keyboard shortcuts.
- **Fixed Player Skip Forward Icon**:
  - Replaced unmapped Segoe Fluent glyph `\uEE36` (tofu box) with clockwise circular jump arrow via `\uEE35` horizontally scaled (`ScaleX="-1"`, `RenderTransformOrigin="0.5,0.5"`).
- **Safe, Persistent, and Explicit Media Removal Architecture**:
  - Created `library_exclusions` SQLite database table with Schema Version 4 migration and indexed paths (`IX_library_exclusions_Path`).
  - Added `ILibraryExclusionRepository` and `IMediaRemovalService` in Core.
  - Two explicit removal options:
    - **Remove from River Library**: Persists normalized path to `library_exclusions`, removes database records, and cleans up empty parent entities (movies, series, seasons). Auto-sync and manual scans will never re-import the file. The original file on disk remains completely untouched.
    - **Delete from Computer (Recycle Bin)**: Safely sends file to Windows Recycle Bin with user undo support via native Win32 `SHFileOperation` (`FOF_ALLOWUNDO`), and removes database records.
  - Wired into context flyouts across Movie Poster cards, Episode list items, and Movie Details editions.
- **Comprehensive UI Interaction Audit & Visual Polish**:
  - Removed obsolete development text ("Phase 4" playback stub).
  - Verified 0 decorative emojis and 0 Apple TV branding remnants.
  - Preserved approved Theme Experience with retro pixel-art canvas, water shaders, and dot slider thumb across 100% Night, Twilight, and Day.
- **Automated Verification**:
  - Added 6 new unit tests in `Phase8ImprovementsTests.cs` (335/335 unit and integration tests passing).


### Added
- **Theme Experience: Authentic Hand-Drawn Sailboat Companion Artwork Integration**:
  - Seamlessly integrated the authentic user-supplied hand-drawn sailboat artwork into the real-time pixel-art ocean scene in `PixelDitherThemeControl`.
  - Positioned the sailboat in the midground open waters between the celestial slider and the foreground rowboat, creating a painterly diagonal visual composition.
  - Added continuous 0–100% atmospheric lighting transitions using $C^2$ Hermite SmootherStep interpolation:
    - **0% Day**: Sunlit crisp white sail outlines with warm highlights over emerald waters.
    - **50% Twilight**: Warm sunset golden/amber rim lighting and peach sail cloth over glowing twilight waters.
    - **100% Night**: Radiant moonlit silvery outlines with translucent lunar glow catching the moonlight path.
  - Implemented gentle ambient water pitch/heave bobbing ($\sin(1.8t) \times 1.3\text{px}$) and midground perspective parallax ($1.15\times$ water parallax).
  - Preserved the existing foreground wooden rowboat, perched seagull, physical sinking simulation, waterline depth absorption, concentric ripples, and slider controls completely intact.
  - Added new automated verification unit test `ThemeAssets_SailboatCompanionArtwork_ExistsAndMatchesDimensions` in `ThemeExperienceTests` (329/329 tests passing).

- **Global Theme Engine Refinement & Exact Original Dark Restoration (100% Night Parity)**:
  - Exact visual restoration of the original River Dark Theme at 100% Night (`t = 1.0`): main background `#0A0A0C`, card `#141418`, surface elevated `#181822`, subtle surface `#14141C`, primary text `#FFFFFF`, secondary text `#8E8E93`, tertiary text `#6E6E78`, disabled text `#4A4A52`, accent `#FFFFFF`, borders `#20FFFFFF` / `#12FFFFFF`.
  - Independently optimized Warm Pearl Light Theme at 0% Day (`t = 0.0`): background `#F4F5F7`, layered cards `#FFFFFF`, elevated surfaces `#FFFFFF`, subtle slate surfaces `#EAECEF`, deep charcoal text `#111114`, slate secondary text `#5C6068`, borders `#18000000` / `#0D000000`, active pills `#111114` with `#FFFFFF` text.
  - Continuous 5-stop Hermite smoothstep color interpolation (`ThemeColorInterpolator.Interpolate5`) with in-place mutation of existing `SolidColorBrush` and `LinearGradientBrush` instances in `Application.Current.Resources`, achieving seamless 60 FPS live slider tracking with zero UI freezing or visual tree recreation.
  - Dynamic adaptive backdrop vignettes: replaced hardcoded `#0A0A0C` gradients in `MovieDetailsView`, `SeriesDetailsView`, and `HeroBannerControl` with dynamic vignette brushes (`AppBackdropVignetteBottomBrush`, `AppBackdropVignetteLeftBrush`) whose end stops dynamically adapt to the active theme's background color, eliminating white cutoffs and dark bands in light and intermediate themes.
  - Comprehensive theme token coverage across all views and controls (`MovieDetailsView`, `SeriesDetailsView`, `HeroBannerControl`, `MediaRowControl`, `MediaCardControl`, `MoviePosterCardControl`, `SidebarNavigationControl`, `SettingsView`, `TvShowsView`, and `MoviesView`).
  - 5 new automated regression tests verifying Night endpoint exact match, Day endpoint exact match, and backdrop vignette adaptive color progression.

---

## [1.1.0] - 2026-11-10 (River — Phase 7: Reliable Library Auto-Sync, Missing Media Reconciliation & Universal Open With)

### Added
- **Universal Windows "Open With River" & External Video Playback**:
  - Full support for opening arbitrary local video files from any folder on the computer (Desktop, Downloads, Documents, USB drives, external HDDs, secondary internal drives) regardless of whether the folder is inside `MLibrary`.
  - Windows command-line parsing via native `CommandLineToArgvW` (with managed fallback) handling paths with spaces, Unicode characters (e.g. Persian, Arabic, CJK), long paths, and double quotes.
  - Multi-file playlist playback: selecting multiple video files in Windows Explorer and opening with River queues them sequentially into the embedded player (`NavigateToPlayerWithPlaylist`).
  - Single-instance activation redirection: opening a video while River is already running forwards activation arguments to the active window without spawning multiple instances or losing file paths.
  - Temporary external playback boundaries: files opened outside `MLibrary` play directly in the embedded LibVLC player with zero database import, zero library scanning, zero file modification, and zero pollution of the Movies library or Continue Watching.
  - Intelligent library file resolution: when an opened file belongs to an existing registered `MLibrary` record, River restores saved playback progress and continues updating progress.
  - Thread-safe WinUI VideoView swapchain marshalling in `VlcPlayerService`, eliminating COM marshalling exceptions during background player initialization.
- **Automatic Real-Time Library Synchronization (`LibraryAutoSyncService`)**:
  - Implemented hybrid filesystem synchronization using high-capacity (64KB buffer) `System.IO.FileSystemWatcher` instances across all registered and enabled `MLibrary` directories.
  - 2.0-second debounced event processing: coalesces burst filesystem notifications (e.g. copying 100 media files into a folder) into a single stabilized import run, preventing redundant full scans.
  - Periodic background reconciliation cycle running every 60 seconds (internally configurable) to catch any external filesystem changes or dropped watcher notifications.
  - Automatic dynamic watcher management: disposes watchers when scan roots are removed or disabled, and registers new watchers when drives reconnect.
  - UI auto-updates: active views (`HomeViewModel`, `MoviesViewModel`, `TvShowsViewModel`, `SettingsViewModel`) subscribe to sync completion events and refresh their UI collections on the UI thread without requiring manual scans or application restart.
- **Missing Media Reconciliation & Data Integrity**:
  - Relocated and renamed file detection: matches moved files within a library root by exact file size and modification timestamp, updating `MediaFile.FilePath` and preserving entity IDs, playback progress, and artwork caches.
  - Genuinely missing file reconciliation: marks unlocated files as `IsMissing = true` while keeping database integrity intact; restores `IsMissing = false` upon rediscovery.
  - Multi-edition preservation: if a movie has multiple media files and one is deleted, only the missing file association is marked missing, keeping the movie accessible via its other editions.
  - Drive disconnection safety: offline or disconnected drives are safely skipped without interpreting them as mass deletions, preserving all database records until reconnected.
  - Stale UI protection: missing media files are cleanly excluded from the active Movies Library and Continue Watching shelves; details view prevents playback attempts with a clear notification.
- **Authentic Hand-Drawn Sailboat Artwork**:
  - Generated complete multi-resolution icon set (`app.ico`, `WindowIcon.png`, `Square*Logo.png`) from the authentic user-supplied sailboat artwork.

---

---

## [1.0.0] - 2026-11-10 (River — Final Polish, Rebranding & Complete UI Interaction Audit)

### Added
- **Product Rebranding: Movie Time → River**:
  - Rebranded the application name to **River** across all user-facing surfaces:
    - Main Window Title: `River`.
    - Application display name: `River`.
    - Navigation sidebar header: `River`.
    - Settings & Storage section descriptions: `River`.
    - Windows Explorer Open With menu: `River` and `River Video File`.
    - Windows single-instance startup logging and error diagnostics.
    - Application assembly metadata: `<AssemblyTitle>River</AssemblyTitle>` and `<Product>River</Product>`.
    - Desktop shortcut: `River.lnk` with the approved sailboat icon.
  - **Preserved Technical Core**: Retained existing namespaces (`MovieTime.*`), database schema, storage paths (`%LocalAppData%\MovieTime`), and user databases without disruption.
- **Iconography & Visual Polish**:
  - Completely purged all decorative emojis and stickers from the user interface and comments.
  - Replaced theme preset emojis (`☀️`, `🌅`, `🌙`) with crisp native Fluent glyphs (`&#xE706;` Sun, `&#xE708;` Sunset/Horizon, `&#xEC46;` Moon).
  - Cleaned all legacy Apple TV brand references from the codebase, documentation, and user-facing dialogs while preserving the cinematic dark/glass design language.
- **Complete UI Interaction Audit & Hardening**:
  - Audited all 136 interactive controls across Navigation, Home, Movies Library, Movie Details, TV Shows, Series Details, Player, Settings, and Playlists.
  - Verified 100% legitimate functionality: zero dead buttons, zero fake menus, zero placeholder click handlers.
  - Bound dual-state segmented tab buttons in Settings to ensure both active and inactive tabs respond reliably.
  - Ensured consistent pointer cursors, hover lifts, focus indicators, and keyboard shortcuts across all views.

---

## [0.9.5] - 2026-11-10 (River — Movie Detection & Library Import Critical Fix)

### Added
- **Universal Movie Detection & Classification Engine**:
  - Validated rule: **valid movies do not require a release year**. Standalone video files without year metadata (e.g. `Gladiator.mkv`, `The Dark Knight.mp4`, `Inception.mkv`) are now properly classified as `MediaClassification.Movie` and imported into SQLite.
  - Full support for Persian, Arabic, and Unicode movie titles with accents and symbols (e.g. `فروشنده.mkv`, `جدایی نادر از سیمین.mkv`, `طعم گیلاس (1997).mkv`, `Silent Hill- Townfall - Part 2 - کابوسی که ازش نمیشه بیدار شد.mp4`, `رفتیم خونه جنی یه مرد غریبه برای هالووین 😂.mkv`).
  - Bracketed year notation support (`Inception [2010].mkv`, `Interstellar [2014] [1080p].mkv`).
  - High-precision title number disambiguation: numbers in titles (`1917`, `300`, `2001 A Space Odyssey`, `Se7en`, `District 9`, `Apollo 13`, `12 Angry Men`) are accurately preserved as title words rather than incorrectly swallowed as release years.
  - Future year protection: release years clamped to `1888` through `DateTime.UtcNow.Year + 2` to prevent sci-fi titles (e.g. `Blade Runner 2049`) from treating futuristic years as release dates.
  - Preserved multi-part titles and roman numerals: `Part Two`, `Part 1`, `Part 2`, `Part II`, `Rocky II`, `Volume 1`, `No Way Home`, `The Fellowship of the Ring`.
  - Folder-context title/year inheritance for generic filenames (`cd1.avi`, `cd2.avi`, `movie.mkv`, `feature.mkv`, `part1.mkv`).
- **Comprehensive Movie Import Diagnostic Reporting**:
  - `ImportDiagnosticItem` structured model recording filename, classification, and precise rejection reasons for anomalous or non-feature media.
  - `LibraryImportResult` expanded to provide structured counts: `FilesDiscovered`, `MoviesClassified`, `TvEpisodesClassified`, `UnknownFiles`, `FilesImported`, `MoviesCreated`, `MoviesUpdated`, `DuplicateFiles`, `FilesSkipped`, `FilesUnchanged`, `FilesMissing`, and `FailedImports`.
  - Formatted diagnostic summaries surfaced via `FormatDiagnosticSummary()` across logging, `MLibraryManagerService`, and Settings UI.
- **Extended Video File Extensions**:
  - Expanded `MediaScanOptions.DefaultSupportedExtensions` to support 21 video formats: `.mkv`, `.mp4`, `.avi`, `.mov`, `.wmv`, `.m4v`, `.mpg`, `.mpeg`, `.ts`, `.m2ts`, `.mts`, `.m2t`, `.webm`, `.vob`, `.flv`, `.divx`, `.3gp`, `.ogv`, `.asf`, `.rmvb`, `.rm`.

### Fixed & Stabilized
- **Unlinked Media File Re-Evaluation**:
  - Fixed a critical import pipeline bug in `LibraryImportService` where existing files with `MovieId == null && EpisodeId == null` were silently skipped on subsequent scans.
  - Automatically re-evaluates all previously unclassified media files and links them to newly created or updated `Movie` records in SQLite.
- **Deterministic Classification Priority**:
  - Strict precedence: TV episode heuristics (`S01E08`, `1x03`, `Season 1 Episode 2`) take precedence over movie heuristics, guaranteeing zero TV regressions.
  - Non-feature media filtering: explicit `sample.mkv`, `trailer.mp4`, `random_clip_001.mp4`, and `Extras/Bonus` directories are cleanly separated from the main feature movie library with human-readable diagnostic tags.
- **Test Suite**:
  - Added comprehensive test fixtures in `FilenameParserTests.cs` and `LibraryImportTests.cs` verifying movies without years, bracketed years, Persian Unicode titles, complex titles, unlinked re-evaluation, and diagnostic summaries. All 308 tests passing.

---

## [0.9.4] - 2026-11-10 (Complete Stabilization, Phase 6 TV Shows, Playlists & Windows Integration)

### Added
- **Persistent Movie Playlists (I-04)**:
  - SQLite database schema migration (v3) adding `Playlists` and `PlaylistItems` tables with indexing.
  - Full CRUD operations: Create, Rename, Delete playlists; Add/Remove movies; Reorder items (Move Up / Move Down).
  - cinematic `PlaylistsView` with split navigation, playlist statistics, and empty guidance states.
  - "Add to Playlist" quick-flyout action in `MovieDetailsView`.
  - Continuous sequential playback: automatic seamless transition to the next movie upon completion.
  - Player HUD transport controls updated with Previous / Next Playlist item buttons and position badge (`N of Total`).
- **Approved Sailboat Application Icon (I-05)**:
  - Integrated the approved white sailboat line art on black branding asset across all platform targets.
  - Multi-resolution `app.ico` (16, 24, 32, 48, 64, 128, 256 px) embedded as application icon in `MovieTime.App.exe`.
  - Window titlebar and taskbar icons dynamically set via WinUI 3 `AppWindow.SetIcon()`.
  - Updated desktop shortcut (`River.lnk`) with the new icon.
- **Windows Explorer "Open With" Integration (I-06)**:
  - Non-destructive, elevation-free `HKCU` registry association across 11 video extensions (`.mp4`, `.mkv`, `.avi`, `.mov`, `.wmv`, `.webm`, `.m4v`, `.ts`, `.flv`, `.3gp`, `.ogv`).
  - Single-instance activation coordination via Windows App SDK `AppInstance.FindOrRegisterForKey()` and `RedirectActivationToAsync()`.
  - External video files open directly in the embedded LibVLC player without polluting `MLibrary` or scan roots.
- **Home Hero Two-Finger Gesture (B-04)**:
  - Touchpad and touch two-finger horizontal swipe gesture support with debounce and velocity thresholds.
  - Shift+Wheel and horizontal wheel support without disrupting native vertical scroll.

### Fixed & Stabilized
- **Library Scalability & Unlimited Movies (B-01)**:
  - Eliminated artificial limits across scanner, SQLite repositories, and UI virtualization.
  - Replaced $O(N)$ sequential queries in `MoviesViewModel` with $O(1)$ batch query.
- **Missing Media Reconciliation & Disconnected Drive Safety (B-02)**:
  - Missing media items properly filtered out of active catalog views.
  - Added Windows `DriveInfo.IsReady` check in `LibraryImportService` to ensure unplugged external drives are never deleted or marked missing.
- **New Movie Import & Instant Refresh (B-03)**:
  - Added "Scan Now" button with immediate catalog refresh without restarting.
  - Resolved multi-root duplicate filename collisions by incorporating scan-root verification.
- **Global Dynamic Theme Integration (B-05)**:
  - Replaced hardcoded `#0A0A0C` background across views with dynamic `{ThemeResource AppBackgroundBrush}`.
  - Live palette propagation across Home, Movies, TV Shows, Movie Details, Series Details, Settings, Sidebar, and Dialogs.
  - The approved pixel-art boat, seagull, water, and circular dot thumb Theme Experience remains completely intact.
- **Player Keyboard Shortcuts & HUD (B-06, I-02, I-03)**:
  - Verified responsive keyboard shortcuts: Space/K (play/pause), J/L (seek ±10s), Left/Right (seek ±5s), Up/Down (volume ±5%), F (fullscreen), M (mute), 0-9 (percentage jump), Esc/Backspace (exit).
  - Playback speed control from 0.1x to 2.0x in 0.1x increments with 2-second fading HUD banner.
  - Minimal volume slider supporting 0–200% software audio amplification via LibVLC.

---

## [0.9.3] - 2026-11-09 (Theme Experience: Slider Dot Thumb & Transition Smoothness Refinement)

### Changed & Improved
- **Minimalist Circular Dot Slider Thumb**:
  - Replaced the liquid glass droplet thumb with a simple, minimalist circular dot correctly proportioned to the 2px slider track.
  - Sized at radius $R = 5.0$ px (diameter $\approx 10$–$11$ px), gently expanding to $5.6$ px during interaction for subtle tactile feedback.
  - Rendered with clean subpixel antialiased edges, subtle top-to-bottom luminance gradient, and soft elevation drop shadow.
  - Retained exact celestial vertical travel ($Y \in [158, 444]$), centered at $X = 36$, preserving the handcrafted crescent Moon and radiant Sun icons.
- **C2-Continuous SmootherStep Keyframe Transition**:
  - Implemented Ken Perlin's $C^2$-continuous SmootherStep polynomial ($s(t) = 6t^5 - 15t^4 + 10t^3$) for Day $\leftrightarrow$ Twilight $\leftrightarrow$ Night interpolation.
  - Guarantees zero 1st and 2nd derivatives at keyframe boundaries ($t=0$ and $t=1$), completely eliminating the derivative inflection kink at the Twilight ($v = 0.5$) midpoint.
- **Continuous Water Absorption & Submersion Depth**:
  - Replaced the discrete binary water tint step (`v > 0.5 ? ...`) with continuous $C^2$ chromatic interpolation across the entire $0\% \to 100\%$ range: emerald green (`[82, 124, 64]`) $\to$ sunset amber (`[48, 88, 92]`) $\to$ deep obsidian navy (`[35, 24, 22]`).
  - Added subpixel vertical sinking displacement and continuous floating-point waterline depth evaluation, eliminating row-by-row horizontal stepping.
- **Smooth Quadratic Moonlight Glint Fade-In**:
  - Glint sparkles now fade in smoothly using a continuous quadratic curve from $v = 0.55$ to $1.0$, allowing the moonlight column to dawn softly as dusk deepens into night with zero sudden appearance threshold.
- **Expanded Test Coverage**:
  - Added comprehensive automated regression tests in `ThemeExperienceTests` covering `SmootherStep`, `SmoothInterpolate`, keyframe exactness, step delta monotonicity, and dot thumb geometry. All 286 tests pass.

---

## [0.9.2] - 2026-11-09 (Theme Experience Visual Quality Emergency Hotfix)

### Fixed & Overhauled
- **Authentic Retro Pixel-Art Rendering Pipeline**:
  - Replaced the high-resolution 1:1 screen-pixel renderer with a dedicated internal low-resolution framebuffer ($180 \times 320$) followed by hardware-friendly $2 \times 2$ nearest-neighbor upscaling to $360 \times 640$.
  - Deliberate $2 \times 2$ pixel clusters restore the intended 8-bit / 16-bit retro aesthetic with razor-sharp edges and zero interpolation blur.
- **Rebuilt 3D Clinker Wooden Rowboat Artwork**:
  - Replaced the featureless dark polygon with a stylized 3D clinker/lapstrake wooden rowboat oriented diagonally with its prow pointing forward-left.
  - Features 4 distinct plank tiers per side with wood-grain shading, highlighted gunwale rails, transverse wooden thwarts (bench seats), interior floorboards, and a contact foam meniscus highlighting the waterline.
  - Perceptual color ramps across all 5 stops: sunlit pine wood in Day, warm teak in Twilight, and a moonlit wood silhouette with discernible plank contours at Night.
- **Handcrafted Perched Pixel-Art Seagull**:
  - Rebuilt the seagull as a recognizable retro sprite perched securely on the prow stem.
  - Features a bright yellow/orange beak, dark eye, white crown, throat, and chest, slate-grey folded wings with dark charcoal primary wingtips, and perched legs.
- **Concentric Circular Water Ripples & Shading**:
  - Replaced glossy 3D waves and exaggerated white bands with smooth, calm concentric circular harmonic ripples radiating outward from the boat and cursor.
  - Discrete palette quantization across 6 perceptual water bands combined with $8 \times 8$ Bayer ordered dithering.
  - At Night: deep obsidian navy waters with crisp, restrained moonlit glints along the central column (zero diagonal hatching, zero repetitive white stripes).
- **100% Continuous Sinking & Underwater Optical Absorption**:
  - Retained fully continuous, reversible sinking across 100 integer stages ($Y_{\text{boat}} = 4.5\text{ to } -12.5$).
  - Submerged hull sections are tinted through the water column using exponential optical depth decay.
- **Droplet Slider Visual Polish**:
  - Crisp teardrop droplet thumb with specular highlight glint and translucent refractive core belonging naturally to the water environment.

## [0.9.1] - 2026-11-09 (Theme Experience 2.0: Real-Time 3D Water, Continuous Sinking, Droplet Slider & Settings Tabs)

### Changed & Improved
- **100% Elimination of Unwanted Night Hatching**:
  - Identified root cause in earlier 2D coordinate-phase wave thresholding (`Math.Sin((x * 0.5f) + (y * 0.35f)...)`) that generated harsh diagonal striped artifacts across the midnight water column.
  - Replaced with physical 3D Fresnel specular reflection equations and standard $8 \times 8$ Bayer ordered dithering matrix.
  - Clean, organic water highlights and shadows without artificial diagonal stripes across the entire 0%–100% range.
- **Procedural Liquid Glass Droplet Slider Thumb**:
  - Designed and rendered a custom liquid glass droplet thumb replacing the blocky icon.
  - Features an modern teardrop silhouette (bulbous lower radius with smooth transition to tapered upward tip), bright specular highlight glint, refractive translucent core, and soft elevation drop shadow.
  - Interactive tactile feedback: smooth scaling and intensified glow upon pointer hover and drag.
- **Continuous 0–100% Single-Percent Intensity Control**:
  - Replaced crude discrete stepping with exact integer percentage control ($0\%$ to $100\%$).
  - Implemented 5-stop cubic Hermite continuous color spline (`ThemeColorInterpolator.Interpolate5`):
    - **0% Day**: Sunlit emerald waters, bright sky reflection, light weathered wood boat, white seagull.
    - **25% Afternoon**: Golden sunlight, warm amber water reflections, honeyed oak wood tones.
    - **50% Twilight**: Rich violet-rose sunset glow, deep teak boat, violet-tinted water surface.
    - **75% Dusk**: Deep indigo-slate water, glowing twilight horizon, shadowed gunwales.
    - **100% Night**: Deep obsidian midnight waters, clean moonlit specular reflections, dark boat silhouette.
  - Avoids muddy brown/grey desaturation through perceptually tuned color ramps.
- **Real-Time 3D Water Surface & Volumetric Boat Mesh**:
  - Replaced flat 2D sprite simulation with a genuine 3D perspective camera (`fov = 55°`) and 3D depth-buffered rasterization.
  - Interactive mouse orbit: moving the cursor across the canvas smoothly orbits and tilts the 3D camera (yaw and pitch) around the boat.
  - 3D parametric volumetric wooden boat mesh: features contoured exterior hull planks, gunwale rails, interior floorboards, transverse bench seats, angled prow stem, and 3D perched seagull.
  - Dynamic 3D water surface with multi-octave Gerstner-style wave harmonics and mouse-driven circular wave ripples.
- **100% Continuous Boat Sinking Animation Across 100 Stages**:
  - Seamless, fully reversible vertical boat displacement keyed directly to the theme percentage: $0\%$ (riding high above water) to $100\%$ (submerged beneath midnight water with only tip of prow exposed).
  - Accurate 3D waterline clipping with depth sorting: hull sections below the local dynamic water elevation are smoothly attenuated via exponential optical depth decay ($e^{-0.22 \Delta Y}$) with submerged scatter.
  - Dynamic waterline contact meniscus foam highlight tracking boat hull contours at the water surface.
- **Strict Interaction Boundary**:
  - Pointer over water generates expanding 3D ripples and specular disturbance.
  - Pointer over boat suppresses water ripple generation, preserving the physical solidity and rigidity of the wooden vessel.
- **Artwork Typography & Header Cleanup**:
  - Completely purged text annotations ("Sinking into dither", "yarik") from the rendering canvas.
  - Removed large redundant title heading and explanatory text paragraph above the artwork in Settings for a distraction-free, cinematic layout.
- **Settings Redesign — Dedicated Two-Tab Architecture**:
  - Introduced an cinematic segmented capsule tab bar at the top of Settings:
    - **Tab 1: 🗄️ Media Libraries & Storage**: Dedicated to drive enumeration, MLibrary folder discovery, library registration cards, and scan controls.
    - **Tab 2: 🎨 Theme Experience**: Dedicated full-width showcase for the real-time 3D canvas, droplet slider, percentage readout, presets, and accessibility switches.
  - Independent scrolling and clean separation of concerns without page clutter.
- **Application Theme Integration**:
  - Continuous mapping to WinUI application resources (`AppBackgroundBrush`, `AppCardBackgroundBrush`, `AppTextPrimaryBrush`, `AppAccentBrush`, etc.) across all 5 color stops.
  - Discrete `ElementTheme.Light` ($0\%$) to `ElementTheme.Dark` ($100\%$) synchronization.
  - Local persistence to `%LocalAppData%\MovieTime\Config\theme_settings.json`.
- **Automated Test Suite Expansion**:
  - Added unit tests for 5-stop continuous spline interpolation, 0–100% integer mapping, and Settings tab navigation.
  - Total automated test suite expanded to **280 passing tests** (0 failures).

---

## [0.9.0] - 2026-11-09 (Interactive Celestial Pixel Dither Theme Experience)

### Added
- **Interactive Celestial Pixel Dither Controller (`PixelDitherThemeControl`)**:
  - Top-down oblique retro pixel-art water scene with ordered Bayer dithering, circular concentric wave geometry, wooden rowboat bow, and perched seagull.
  - High-performance native WinUI 3 360×640 `WriteableBitmap` software blitter running at 60 FPS with zero external dependencies and sub-millisecond per-frame rasterization.
  - Vertical Celestial Slider track with Moon zenith icon (`🌙`), Sun nadir icon (`☼`), solid dynamic track fill, and an 8-bit pixel thumb that morphs into an 8-bit gripping fist glove (`✊`) on hover and drag.
- **Continuous 3-Palette Color Interpolation (`ThemeColorInterpolator` & `WinUiThemePaletteApplier`)**:
  - Smooth mathematical interpolation across a continuous $[0.0, 1.0]$ domain:
    - **Day ($V = 0.0$)**: Sunlit emerald green waters, bright highlights, light weathered wood boat, white seagull, crisp light theme (`ElementTheme.Light`).
    - **Twilight ($V = 0.5$)**: Warm golden-amber sunset waters, warm teak boat, atmospheric twilight ambiance, warm dark theme (`ElementTheme.Dark`).
    - **Night ($V = 1.0$)**: Deep obsidian midnight waters, shimmering vertical moonlight reflection column, dark silhouette boat, signature cinematic dark theme (`ElementTheme.Dark`).
  - Continuous real-time updates for application background (`AppBackgroundBrush`), cards (`AppCardBackgroundBrush`), primary/secondary text (`AppTextPrimaryBrush`, `AppTextSecondaryBrush`), accent tones (`AppAccentBrush`), specular borders (`AppBorderBrush`), and translucent liquid glass fills (`AppGlassFillBrush`).
- **Mouse-Reactive Physical Simulation**:
  - Expanding circular water ripples generated dynamically from cursor movement, propagating outward and naturally attenuating over time.
  - Layered spring-damped parallax shifting water and boat perspectives with pointer coordinates.
  - Cursor proximity specular lighting casting subtle radial highlights onto water pixels.
  - Idle ambient shimmer and dancing moonlight column glints.
- **Settings Integration & Preferences Persistence (`ThemeManagerService` & `ThemeSettingsService`)**:
  - Integrated into Movie Time Settings under a dedicated "Appearance & Atmosphere" section with live percentage indicator and mode descriptions.
  - Quick-preset capsule buttons for `Day`, `Twilight`, and `Night`.
  - Accessible toggles for **Reduced Motion** (suppresses ripple displacement and parallax while preserving palette transitions) and **Ambient Animation**.
  - Local persistence to `%LocalAppData%\MovieTime\Config\theme_settings.json`.
- **Comprehensive Test Suite Expansion (`ThemeExperienceTests`)**:
  - Added unit test coverage for theme mode thresholds, continuous color interpolation math, persistence serialization, out-of-bounds value clamping, event notification triggering, and ViewModel synchronization (total tests increased to 277).

---

## [0.8.0] - 2026-11-09 (Phase 6: TV Show & Season/Episode Detail Experience)

### Added
- **Deterministic TV Episode Filename Recognition (`FilenameParserEngine`)**:
  - Implemented advanced multi-pattern TV recognition: standard scene tags (`The.Wire.S01E08.Lessons.REMASTERED.720p.WEB-DL.2CH.x265.HEVC.PSA.AvaMovie.mkv`), `1x08`, `Season 1 Episode 8`, multi-episode packs (`S01E08E09`, `S01E08-E09`), and Specials (`S00E01`).
  - Added strict technical token cutoffs and boundary guards: accurately isolates authentic episode titles (`Lessons`) from release technical tags (`REMASTERED`, `720p`, `WEB-DL`, `2CH`, `x265`, `HEVC`, `PSA`, `AvaMovie`).
  - Completely rejects false episode titles: filenames without genuine episode titles produce `null` episode titles instead of release tags (e.g. `720p`).
  - Zero fabricated metadata: series names, season numbers, and episode numbers are parsed strictly from the local filename.
- **Hierarchical Relational Synchronization (`LibraryImportService`)**:
  - Scanned TV episodes automatically correlate with `Series`, `Season`, and `Episode` records in SQLite.
  - Linked to physical media files via `MediaFile.EpisodeId` foreign-key tracking.
  - Season 0 explicitly titled `"Specials"`; Season $N$ titled `"Season N"`.
  - Re-scan refinement: updates generic episode titles (`Episode 8` -> `Lessons`) if improved naming is detected, without duplicating database entities.
- **Dedicated TV Shows Library Screen (`TvShowsView` & `TvShowsViewModel`)**:
  - cinematic 2:3 vertical poster grid (`TvShowsView.xaml`) with smooth hover lift and focus border animation.
  - Displays series title, release year span (`2002–2008`), season count, and episode count.
  - Real-time instant title search filtering with empty search state feedback.
  - Multi-criteria sorting: `Title (A-Z)`, `Release Year (Newest)`, and `Recently Added`.
  - Empty library guidance with direct scan trigger.
- **Cinematic TV Show Detail Experience (`SeriesDetailsView` & `SeriesDetailsViewModel`)**:
  - Full-bleed edge-to-edge backdrop hero canvas with dark radial and linear vignette gradients.
  - Smart `▶ Resume / Play Next Up` action button: automatically determines next episode to watch (prioritizing in-progress episodes, then first uncompleted episode, then Season 1 Episode 1).
  - Fluid Season Selector capsule pill tabs with instant switching without page reloads.
  - 16:9 episode list cards with episode number, genuine episode title, duration, quality badges (`4K UHD`, `1080p`, `720p`), progress indicator bar, and candidate video frame thumbnails.
  - Seamless back navigation returning to the TV Shows catalog or Home.
- **Episode Playback & Discrete Resume Tracking (`PlayerViewModel` & `PlaybackProgressRepository`)**:
  - Extended embedded hardware-accelerated LibVLC player engine to support TV episodes.
  - Contextual header presentation: resolves `Episode` -> `Season` -> `Series`, displaying series title and episode subtitle (`Season 1, Episode 8 • Lessons • 720p`).
  - Discrete `PlaybackProgress` tracking keyed by `EpisodeId`, ensuring movie progress and episode progress never conflict.
  - Back navigation returns seamlessly to `SeriesDetails:{seriesId}`.
- **Series & Episode Local Artwork Pipeline (`ArtworkPipelineService`)**:
  - Support for TV series and episode artwork processing: extracts embedded cover art and candidate video frame thumbnails.
  - Dispatches `SeriesArtworkUpdated` events to refresh visible series cards and detail views in real time.
- **Home Screen & Navigation Integration (`HomeViewModel` & `MainWindow`)**:
  - Added dedicated "TV Shows" shelf on Home for direct navigation to series details.
  - TV episodes with active playback progress automatically populate the "Continue Watching" row with formatted subtitles (`S01E08 • Lessons (45%)`) and one-click instant resume.
  - Navigation routing in `MainWindow` for `TVShows`, `TV Shows`, and `SeriesDetails:{id}`.
- **Unit Test Suite Expansion (`TvShowExperienceTests.cs`)**:
  - Added comprehensive test suite for TV episode recognition, database hierarchy, progress persistence by episode, catalog search/sorting, series details season switching, Next Up resolution, and player episode playback.
  - Test suite expanded from 242 to **263 passing unit tests** (0 failed, 0 skipped).

---

## [0.7.1] - 2026-11-09 (Home Experience Fix & Dynamic Artwork Integration)

### Added
- **Dynamic Rotating Hero Billboard**:
  - Replaced static placeholder showcase with dynamic rotating banner populated exclusively from eligible real movies in the local SQLite library (up to 7 items).
  - Automatic 7.5-second rotation with smooth hardware-accelerated crossfade backdrop transitions.
  - Dynamic interactive carousel indicator dots reflecting active movie index with direct jump navigation.
  - Pointer interaction pause/resume: timer automatically pauses when user hovers or focuses the hero section and resumes on exit.
  - Dual action buttons: primary white pill `▶ Play Movie` and secondary `(i) Details` button.
  - Clickable title and backdrop canvas navigating directly to Movie Details.
  - Dynamic artwork updates responding in real time to `ArtworkPipelineService.ArtworkUpdated` events.
- **Genuine Continue Watching Artwork & Position Frame Extraction**:
  - Completely replaced placeholder camera icons with genuine media-derived artwork.
  - Integrated `IHoverPreviewService` / `IArtworkStorageService` position thumbnail extraction: captures authentic keyframe from exact saved playback timestamp (`%LocalAppData%\MovieTime\Artwork\Positions\`).
  - Seamless multi-tier artwork fallback: exact position thumbnail -> movie backdrop -> movie poster -> dark typographical fallback.
  - Accurate progress bars bound directly to SQLite `PlaybackProgress.PositionPercentage`.
- **Silent In-Card Hover Video Micro-Preview**:
  - 600ms intentional hover delay initiates in-card micro-preview sequence.
  - Smoothly cycles through 4 authentic keyframes sampled around the saved playback timestamp at 650ms intervals.
  - Subtle top-left "PREVIEW" badge indicator.
  - Instant cancellation and cleanup on pointer exit, ensuring zero audio interference, zero Direct3D swapchain contention, and zero UI stutter.
  - Hover/focus play button overlay allowing instant one-click resume.
- **Home Navigation & Playback Event Wiring**:
  - Fixed unassigned card commands: clicking movie cards in Continue Watching or Movies shelves now reliably navigates to Movie Details for that exact title.
  - Fixed card ID assignment in Continue Watching to properly reference `MovieId`.
  - Connected `HomeViewModel.PlaybackRequested` to `MainWindow.NavigateToPlayer(filePath, movieId)`.
- **Unit Test Suite Expansion**:
  - Added `HomeExperienceTests.cs` covering dynamic hero rotation, carousel dot synchronization, pause/resume, Continue Watching artwork resolution, command execution, and hover preview caching.
  - Test suite expanded from 235 to **242 passing tests** (0 failed, 0 skipped).

---

## [0.7.0] - 2026-11-09 (Phase 5: Embedded Artwork & Local Thumbnail Extraction)

### Added
- **Multi-Tier Local Artwork Extraction Pipeline (`IArtworkPipelineService`)**:
  - **Tier 1 (Embedded Cover Artwork)**:
    - Direct binary atom/element stream parser for MP4/M4V/MOV `moov/udta/meta/ilst/covr/data` boxes and MKV/WebM EBML `Segment/Attachments/AttachedFile` elements.
    - Zero video decoding overhead; streams embedded front cover art directly in < 5ms.
    - Automatic binary image validation (JPEG, PNG, WebP) enforcing minimum byte bounds and image dimensions, rejecting arbitrary binary payloads.
  - **Tier 2 (Video Thumbnail Candidate Extraction)**:
    - Multi-timestamp keyframe extraction across the media timeline (sampling candidate frames across video duration).
    - Uses native Windows Imaging and Media Composition APIs with fallback to Shell Thumbnail provider for 10-bit HEVC, AVC, and modern formats.
    - Multi-metric composite quality scoring (`ImageQualityEvaluator`): computes ITU-R BT.601 luminance, contrast std-dev, edge gradient sharpness, and Hasler-Süsstrunk colorfulness metrics.
    - Rejects black screen frames, blown-out white flash frames, and low-contrast flat scenes.
    - Hardware-accelerated aspect ratio cropping and scaling (2:3 theatrical poster at 600x900, 16:9 widescreen backdrop at 1920x1080) via Windows Imaging Component (`WicImageProcessingService`).
  - **Tier 3 (Safe Typographical Fallback)**:
    - Graceful fallback for unreadable or damaged media files; sets `IsPosterFallback` and `IsBackdropFallback` flags in SQLite.
    - Displays high-fidelity dark atmospheric radial glow and metadata typography with zero UI pop-in or crashes.
- **Collision-Safe Local Storage & Deterministic Cache (`ArtworkStorageService`)**:
  - Normalized path + file size + UTC modified timestamp SHA256 deterministic key generation.
  - Strictly non-destructive: all extracted posters and backdrops are cached under `%LocalAppData%\MovieTime\Artwork\Posters\` and `Backdrops\`; original user video directories are never touched.
  - Multi-drive support: differentiates files with identical names across distinct drives (e.g. `D:\Movies\Gladiator.mkv` vs `E:\Movies\Gladiator.mkv`).
  - Automatic cache invalidation: media file size or timestamp modifications generate distinct hash keys and trigger seamless re-extraction.
  - Atomic persistence with `.tmp` staging files and thread-safe per-file locking (`ConcurrentDictionary<string, SemaphoreSlim>`).
- **Real-Time UI Integration & Event Dispatching**:
  - `MoviePosterCardControl` and `MediaCardControl` updated with hardware-decoded `BitmapImage` (`DecodePixelWidth` high-DPI scaling) and smooth transition from typographical fallback to extracted artwork.
  - `HeroBannerControl` dynamic atmospheric backdrop loading.
  - `MoviesViewModel`, `HomeViewModel`, and `MovieDetailsViewModel` subscribed to `ArtworkPipelineService.ArtworkUpdated` events to dynamically refresh visible cards and hero billboards on the UI dispatcher without requiring app restart or manual library reload.
  - Non-blocking background extraction: initiates after UI initialization and runs asynchronously without blocking WinUI 3 message pumps or active video playback.
  - `LibraryImportService` automatically triggers artwork processing upon completion of filesystem scans when new or updated media files are imported.
- **Unit & Integration Test Suite Expansion**:
  - Expanded test suite from 217 to **235 passing unit tests**.
  - Added comprehensive tests for `ImageHeaderValidator`, `ImageQualityEvaluator`, `ArtworkStorageService`, `EmbeddedArtworkExtractor`, and `ArtworkPipelineService`.

---

---

## [0.6.4] - 2026-11-07 (Phase 4.4: YouTube-Style Shortcuts, External Audio, External Subtitles & Subtitle Customization)

### Added
- **YouTube-Style Keyboard Shortcuts**:
  - `K` / `Space`: Toggle Play/Pause without stealing keyboard focus or triggering unwanted button clicks.
  - `J` / `L`: Seek backward / forward by 10 seconds.
  - `Left Arrow` / `Right Arrow`: Seek backward / forward by 5 seconds.
  - `0` to `9`: Seek to 0%, 10%, 20%, 30%, 40%, 50%, 60%, 70%, 80%, 90% of actual media duration.
  - `F`: Toggle edge-to-edge Fullscreen.
  - `M`: Toggle Audio Mute.
  - `Esc` / `Backspace`: Return cleanly to Movie Details.
  - Text-input focus guard to prevent shortcut interception when entering text in search/input boxes.
  - Interactive Keyboard Shortcuts reference card embedded directly inside the InSight flyout.
- **External Audio & Dubbed Track Synchronization**:
  - Added native LibVLC audio slave support (`.mka`, `.mkv`, `.aac`, `.ac3`, `.mp3`, `.m4a`, `.wav`, `.flac`, `.ogg`, `.opus`) alongside running video.
  - Audible and selectable from the Audio Options menu with immediate playback switching.
  - Audio Delay Synchronization controls with `±100ms` nudging and `0ms` instant reset via native LibVLC microsecond delay APIs.
  - Option to remove external audio and restore embedded audio tracks.
- **External Subtitles Support**:
  - Added support for loading external subtitle files (`.srt`, `.ass`, `.ssa`, `.vtt`, `.sub`) using LibVLC's native `AddSlave(MediaSlaveType.Subtitle, ...)`.
  - Integrated into the Subtitles flyout menu with embedded tracks, external tracks, track disabling ("Off"), and detach option.
- **Subtitle Appearance Customization**:
  - Font Size: Small (22pt), Medium (30pt default), Large (40pt), Extra Large (52pt).
  - Text Color: White (`#FFFFFF`), Yellow (`#FFE500`), Cyan (`#00E5FF`), Green (`#00E676`).
  - Text Outline / Stroke: Off (0), Thin (1px), Medium (2px), Thick (3px).
  - Background Opacity: Off (transparent), Subtle (35% black tint), Dark (75% solid black).
  - Position: Default, Higher (45px margin), Lower (10px margin).
  - One-click "Reset to Default" command.
  - Persistent storage in `%LocalAppData%\MovieTime\Settings\subtitle_settings.json` across application sessions.
  - Preserves ASS/SSA embedded script typography while styling text subtitle formats (SRT/VTT).
- **Liquid Glass Menus**:
  - Implemented sleek Liquid Glass flyout panels for Audio Options, Subtitles, Subtitle Appearance, and InSight.
- **Comprehensive Unit Testing**:
  - Expanded test suite from 201 to **217 passing unit tests**, validating all keyboard shortcuts, percentage seeking algorithms, audio delay adjustment, external track attachment, and subtitle settings persistence.

---

## [0.6.3] - 2026-11-07 (Phase 4.3: Critical Player Seek Stability Hotfix)

### Fixed
- **Resolved Player Freeze / "Not Responding" and Seek Instability**:
  - **Root Cause 1 — Synchronous LibVLC Seek on UI Thread**: In `VlcPlayerService.Seek(long positionMs)`, assigning `_mediaPlayer.Time = clampedTime` was performed synchronously on the UI thread. In unmanaged LibVLC, `libvlc_media_player_set_time` flushes Direct3D11 swapchain buffers and halts thread execution while the demuxer seeks to the keyframe. When scrubbing or clicking, repeated synchronous calls starved the WinUI message pump, causing Windows to flag the process as "Not Responding" (`AppHang`).
  - **Resolution 1 — Asynchronous Non-Blocking Coalesced Seek Worker**: Refactored `VlcPlayerService.Seek` to return immediately (< 0.1ms) to the UI thread. Introduced a dedicated background seek worker (`ProcessPendingSeeksAsync`) with atomic request coalescing. Intermediate rapid seek targets generated during slider drags are automatically discarded in favor of the latest requested timestamp, preventing native decoder flooding while delivering fluid 60fps scrubbing.
  - **Root Cause 2 — XAML Event Feedback Loop**: In `PlayerView.xaml`, `Slider.ValueChanged` was triggered both by user interaction and by LibVLC's high-frequency `PositionChanged` callbacks (10-30Hz) setting `ViewModel.ProgressRatio`. This established an infinite feedback loop where each position callback triggered a new seek command back into LibVLC.
  - **Resolution 2 — Scrubbing Lifecycle & Programmatic Update Guard**: Introduced `IsDraggingSlider` and `IsUpdatingFromPlayer` guards in `PlayerViewModel`. While the user drags the slider, inbound player updates are suppressed so the UI displays the user's scrub preview without jitter. During regular playback, `IsUpdatingFromPlayer` prevents `ValueChanged` from dispatching redundant seeks.
  - **Root Cause 3 — Unresponsive Timeline Direct Clicking**: In WinUI 3, internal templated parts of `Slider` (`HorizontalTrackRect`, `Thumb`) consume pointer events and set `e.Handled = true`, preventing standard XAML click handlers from firing. Furthermore, focus checks prevented click-to-seek when the slider was not already focused.
  - **Resolution 3 — Universal Pointer Capture & Timeline Container**: Registered low-level pointer handlers (`PointerPressed`, `PointerMoved`, `PointerReleased`, `PointerCaptureLost`) using `AddHandler(..., handledEventsToo: true)` on both `TimelineSlider` and `TimelineContainer`. Clicking anywhere along the 36px hit-testing region computes the proportional ratio `X / ActualWidth` and immediately seeks to that position.
  - **Root Cause 4 — EOF Hang & Floating-Point Truncation**: Seeks beyond media duration previously risked decoding stalls at the exact EOF boundary, and `(long)(ratio * duration)` truncated fractional values like `62999.999ms`, producing off-by-one second display errors.
  - **Resolution 4 — Safe Bounds Clamping & Rounded Timestamps**: Implemented strict clamping to `[0, Math.Max(0, durationMs - 250)]` and `(long)Math.Round(ratio * durationMs)`. Seeking while paused preserves paused state; seeking while playing resumes smoothly from the new timestamp.
- **Enhanced Timeline Styling**:
  - Styled `TimelineSlider` with sleek Liquid Glass theme brushes (solid white active track and thumb, 25% translucent unplayed track, 36px comfortable hit-test target).
  - Disabled default Windows numeric tooltips (`IsThumbToolTipEnabled="False"`), keeping focus on the clean HUD playhead and remaining time displays.
- **Unit Test Coverage Expansion**:
  - Expanded unit test suite to **201 passed tests** in `tests/MovieTime.Tests/PlayerViewModelTests.cs`.
  - Added deterministic tests for scrubbing lifecycle, external position suppression during drag, bounds clamping, rapid sequential seeks, and seek-while-paused state preservation.

---

## [0.6.2] - 2026-11-07 (Phase 4.2: Single-Window Player Architecture & Integrated Playback UI)

### Fixed
- **Resolved Disconnected / External VLC Popup Window Defect**:
  - **Root Cause**: `LibVLC` on Windows requires explicit Direct3D11 swap chain options (`--winrt-d3dcontext=0x...` and `--winrt-swapchain=0x...`) passed into the `LibVLC` constructor for it to render into a WinUI 3 `SwapChainPanel`. Previously, `VlcPlayerService` allocated `LibVLC` with general options before `VideoView` had completed layout and allocated its swapchain. Lacking swapchain parameters, LibVLC's native Direct3D11 video output module automatically fell back to spawning an external top-level Win32 window (`VLC (Direct3D11 output)`), visually detaching video playback from the Movie Time window and separating it from the player controls.
  - **Resolution**:
    - Synchronized `VlcPlayerService` with `VideoView.Initialized` to receive the native Direct3D11 swap chain options (`e.SwapChainOptions`).
    - Allocated `LibVLC` with `--winrt-d3dcontext` and `--winrt-swapchain` parameters, routing all hardware decoding frames directly into the in-tree `SwapChainPanel`.
    - Added asynchronous synchronization in `OpenMediaAsync` ensuring the DirectComposition swapchain is established before media playback starts.
    - Verified with Win32 window enumeration that exactly ONE application window (`WinUIDesktopWin32WindowClass`, "Movie Time") exists throughout playback, with zero external or secondary windows.
- **Integrated Cinematic Player Layout & Z-Order**:
  - Restored clean declarative `<lvs:VideoView x:Name="LibVlcVideoView">` in `PlayerView.xaml` with early `LibVLCSharp.Shared.Core.Initialize()` execution in `App.xaml.cs`.
  - Enforced strict Z-order composition inside `PlayerRootGrid`: `LibVlcVideoView` serves as the underlying background surface, beneath atmospheric cinematic vignette gradients and Liquid Glass transport controls, timeline playhead, and back button.
  - Interactive hit testing: All control buttons, sliders, and flyout menus reliably capture input while transparent overlay regions pass pointer events through to manage auto-hiding (3.5s inactivity timer).
  - Single-window fullscreen transitions: Fullscreen toggles `AppWindow.SetPresenter(AppWindowPresenterKind.FullScreen)` on the active `MainWindow` with Esc / Back key returning seamlessly to Movie Details.

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

- **Cinematic Player Interface**:
  - Pixel-accurate reproduction of user-supplied player visual references:
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
- **Cinematic Movie Details Experience**: Dedicated `MovieDetailsView` and `MovieDetailsViewModel` inspired by design references.
  - **Full-Width Hero Section**: Dynamic background rendering genuine local backdrops when available on disk, with subtle atmospheric radial and linear dark vignette overlays. Sophisticated ambient gradient fallback when no local artwork exists.
  - **Cinematic Title & Badges**: Prominent movie title typography with genuine release year and edition badges (e.g. *Director's Cut*, *Extended Edition*).
  - **Real Local Metadata Display**: Quality badges (`4K UHD`, `1080p HD`), video codec (`HEVC`, `AVC`), audio format, container format (`MKV`, `MP4`), and human-readable file sizes (`GB`/`MB`).
  - **Zero Fabricated Information Guarantee**: Only displays metadata genuinely available in the local SQLite database or scanned files; omits unverified fields (no fake genres, ratings, synopses, directors, or fictional cast).
  - **Primary Play Action**: signature white capsule Play button (`▶ Play Movie`) connected to the active local physical media file with feedback banner and integration boundary for Phase 4 VLC player.
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
  - cinematic `SettingsView` and `SettingsViewModel` with real-time drive availability indicators (Available, Folder Missing, Drive Disconnected, Disabled).
  - Interactive drive picker modal with disk preview and explicit confirmation before folder creation.
  - Pure zero-mock empty states for Home and Movies views when no libraries or movies are cataloged.

---

## [0.3.5] - 2026-11-06 (Phase 3B)

### Added
- **Cinematic Movies Library Experience**:
  - Embedded category and genre filter sidebar (`All Movies`, `Recently Added`, `4K & High Definition`, and genres).
  - 2:3 vertical poster grid (`170x255`) with 1.06x scale lift, white focus border, and elevation drop shadow.
  - Interactive quick-action context dialog (`Play`, `Go to Movie`).
  - Real-time local search and multi-criteria sorting (Title, Release Year, Date Added).

---

## [0.3.0] - 2026-11-05 (Phase 3A)

### Added
- **Cinematic Home UI**:
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
