# Changelog

All notable changes to **Movie Time** will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [Unreleased]

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
    - Displays high-fidelity Apple TV dark atmospheric radial glow and metadata typography with zero UI pop-in or crashes.
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
  - Audible and selectable from the Apple TV Audio Options menu with immediate playback switching.
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
- **Apple TV Liquid Glass Menus**:
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
- **Enhanced Apple TV Timeline Styling**:
  - Styled `TimelineSlider` with sleek Apple TV Liquid Glass theme brushes (solid white active track and thumb, 25% translucent unplayed track, 36px comfortable hit-test target).
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
- **Integrated Apple TV Cinematic Player Layout & Z-Order**:
  - Restored clean declarative `<lvs:VideoView x:Name="LibVlcVideoView">` in `PlayerView.xaml` with early `LibVLCSharp.Shared.Core.Initialize()` execution in `App.xaml.cs`.
  - Enforced strict Z-order composition inside `PlayerRootGrid`: `LibVlcVideoView` serves as the underlying background surface, beneath atmospheric cinematic vignette gradients and Apple TV Liquid Glass transport controls, timeline playhead, and back button.
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
