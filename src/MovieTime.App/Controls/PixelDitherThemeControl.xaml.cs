using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using MovieTime.Core.Theme;

namespace MovieTime.App.Controls;

/// <summary>
/// Real-time interactive retro pixel-art theme controller for Movie Time.
/// Faithfully reconstructed from the authoritative visual references:
/// authentic handcrafted Day, Twilight, and Night keyframes, continuous 0–100% theme interpolation
/// with C2 SmootherStep transition easing, dynamic subpixel boat sinking with continuous optical depth absorption
/// and foam meniscus, 3D perspective orbit/parallax driven by cursor movement with differential depth layers,
/// real-time concentric water ripple simulation with strict boat hull boundaries (boat remains rigid and undeformed),
/// clean obsidian night water with smooth fading specular moonlight column glints,
/// and a minimalist, perfectly proportioned circular dot slider thumb.
/// </summary>
public sealed partial class PixelDitherThemeControl : UserControl
{
    // Screen / Display Canvas Size
    private const int CanvasWidth = 360;
    private const int CanvasHeight = 640;
    private const int PixelCount = CanvasWidth * CanvasHeight;
    private const int BufferByteLength = PixelCount * 4;

    // Celestial Slider Coordinate System (within 360x640 canvas)
    private const int SliderLeft = 16;
    private const int SliderRight = 56;
    private const int SliderTop = 158;      // 100% (Night / Moon zenith)
    private const int SliderBottom = 444;   // 0%   (Day / Sun nadir)
    private const int SliderCenterX = 36;
    private const int SliderTravel = SliderBottom - SliderTop; // 286 px

    private readonly IThemeManagerService? _themeManager;
    private readonly ILogger<PixelDitherThemeControl> _logger;

    private WriteableBitmap? _writeableBitmap;
    private readonly byte[] _dayBuffer = new byte[BufferByteLength];
    private readonly byte[] _twilightBuffer = new byte[BufferByteLength];
    private readonly byte[] _nightBuffer = new byte[BufferByteLength];
    private readonly byte[] _renderBuffer = new byte[BufferByteLength];
    private readonly bool[] _boatMask = new bool[PixelCount];

    // Sailboat Companion Artwork (Authentic Hand-Drawn Sailboat Sprite)
    private const int SailboatWidth = 68;
    private const int SailboatHeight = 74;
    private const int SailboatByteLength = SailboatWidth * SailboatHeight * 4;
    private readonly byte[] _sailboatDayBuffer = new byte[SailboatByteLength];
    private readonly byte[] _sailboatTwilightBuffer = new byte[SailboatByteLength];
    private readonly byte[] _sailboatNightBuffer = new byte[SailboatByteLength];
    private bool _hasSailboatAssets;

    private readonly Stopwatch _stopwatch = new();
    private double _lastElapsedSeconds;
    private bool _isLoaded;
    private bool _isRendering;

    // Slider State
    private bool _isDraggingSlider;
    private bool _isPointerOverSlider;
    private double _themeValue = 1.0; // 0.0 (Day) -> 1.0 (Night)

    // Mouse Interaction & Parallax Physics
    private float _cursorX = -100;
    private float _cursorY = -100;
    private float _lastRippleX = -100;
    private float _lastRippleY = -100;
    private double _lastRippleTime;
    private bool _isCursorInside;

    private float _targetParallaxX;
    private float _targetParallaxY;
    private float _currentParallaxX;
    private float _currentParallaxY;

    // Bounded Active Ripples Ring Buffer (16 ripples max, zero heap allocation)
    private struct RippleWave
    {
        public float X;
        public float Y;
        public float StartTime;
        public float Amplitude;
        public bool Active;
    }

    private readonly RippleWave[] _ripples = new RippleWave[16];
    private int _rippleHeadIndex;

    public double ThemeValue
    {
        get => _themeValue;
        set
        {
            var clamped = Math.Clamp(value, 0.0, 1.0);
            if (Math.Abs(_themeValue - clamped) > 0.0001)
            {
                _themeValue = clamped;
                _themeManager?.SetThemeValue(_themeValue);
            }
        }
    }

    public int ThemePercentage => (int)Math.Round(_themeValue * 100.0);

    public PixelDitherThemeControl()
    {
        InitializeComponent();

        var services = App.Current?.Services;
        if (services != null)
        {
            _themeManager = services.GetService<IThemeManagerService>();
            _logger = services.GetService<ILogger<PixelDitherThemeControl>>() ?? NullLogger<PixelDitherThemeControl>.Instance;
        }
        else
        {
            _logger = NullLogger<PixelDitherThemeControl>.Instance;
        }

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_isLoaded) return;
        _isLoaded = true;

        if (_themeManager != null)
        {
            _themeValue = _themeManager.CurrentThemeValue;
            _themeManager.ThemeChanged += OnThemeManagerChanged;
        }

        InitializeBitmaps();
        LoadAssetBuffers();

        _stopwatch.Start();
        _lastElapsedSeconds = _stopwatch.Elapsed.TotalSeconds;

        StartRendering();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _isLoaded = false;
        StopRendering();

        if (_themeManager != null)
        {
            _themeManager.ThemeChanged -= OnThemeManagerChanged;
        }
    }

    private void OnThemeManagerChanged(double val, AppThemeMode mode)
    {
        _themeValue = val;
    }

    private void InitializeBitmaps()
    {
        _writeableBitmap = new WriteableBitmap(CanvasWidth, CanvasHeight);
        PixelDisplayImage.Source = _writeableBitmap;
    }

    private void LoadAssetBuffers()
    {
        try
        {
            var baseDir = AppContext.BaseDirectory;
            var assetsDir = Path.Combine(baseDir, "Assets", "Theme");

            if (!Directory.Exists(assetsDir))
            {
                assetsDir = Path.Combine(Directory.GetCurrentDirectory(), "Assets", "Theme");
            }

            void LoadOrFill(string fileName, byte[] targetBuffer, byte r, byte g, byte b)
            {
                var filePath = Path.Combine(assetsDir, fileName);
                if (File.Exists(filePath))
                {
                    using var stream = File.OpenRead(filePath);
                    int read = stream.Read(targetBuffer, 0, BufferByteLength);
                    if (read == BufferByteLength) return;
                }

                // Generative fallback if asset is missing
                for (int i = 0; i < PixelCount; i++)
                {
                    int offset = i * 4;
                    targetBuffer[offset + 0] = b;
                    targetBuffer[offset + 1] = g;
                    targetBuffer[offset + 2] = r;
                    targetBuffer[offset + 3] = 255;
                }
            }

            LoadOrFill("day.bgra", _dayBuffer, 88, 128, 91);
            LoadOrFill("twilight.bgra", _twilightBuffer, 92, 84, 48);
            LoadOrFill("night.bgra", _nightBuffer, 22, 24, 34);

            // Load Sailboat Companion Artwork buffers
            void LoadSailboat(string fileName, byte[] target)
            {
                var filePath = Path.Combine(assetsDir, fileName);
                if (File.Exists(filePath))
                {
                    using var stream = File.OpenRead(filePath);
                    int read = stream.Read(target, 0, SailboatByteLength);
                    if (read == SailboatByteLength) return;
                }
            }

            LoadSailboat("sailboat_day.bgra", _sailboatDayBuffer);
            LoadSailboat("sailboat_twilight.bgra", _sailboatTwilightBuffer);
            LoadSailboat("sailboat_night.bgra", _sailboatNightBuffer);
            _hasSailboatAssets = true;

            // Load bit-packed boat mask (28,800 bytes = 230,400 bits)
            var maskPath = Path.Combine(assetsDir, "boat_mask.bin");
            if (File.Exists(maskPath))
            {
                var packed = File.ReadAllBytes(maskPath);
                if (packed.Length >= PixelCount / 8)
                {
                    for (int i = 0; i < PixelCount; i++)
                    {
                        _boatMask[i] = (packed[i / 8] & (1 << (7 - (i % 8)))) != 0;
                    }
                    return;
                }
            }

            // Procedural fallback segmentation: boat bounding box Y in [410, 547], X in [160, 359]
            for (int y = 410; y <= 547; y++)
            {
                int rowOffset = y * CanvasWidth * 4;
                int maskOffset = y * CanvasWidth;
                for (int x = 160; x < CanvasWidth; x++)
                {
                    int off = rowOffset + (x * 4);
                    byte b = _dayBuffer[off + 0];
                    byte g = _dayBuffer[off + 1];
                    byte r = _dayBuffer[off + 2];

                    // Non-green-water pixels inside the bounding box belong to the boat & seagull
                    bool isWater = (g >= r + 28) && (g >= b + 15) && (r < 110);
                    _boatMask[maskOffset + x] = !isWater;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load pixel theme assets. Using procedural fallbacks.");
        }
    }

    private void StartRendering()
    {
        if (_isRendering) return;
        _isRendering = true;
        CompositionTarget.Rendering += OnRenderingFrame;
    }

    private void StopRendering()
    {
        if (!_isRendering) return;
        _isRendering = false;
        CompositionTarget.Rendering -= OnRenderingFrame;
    }

    private void OnRenderingFrame(object? sender, object e)
    {
        if (!_isLoaded || _writeableBitmap == null) return;

        double totalSec = _stopwatch.Elapsed.TotalSeconds;
        float dt = (float)(totalSec - _lastElapsedSeconds);
        _lastElapsedSeconds = totalSec;
        dt = Math.Clamp(dt, 0.001f, 0.050f);

        UpdatePhysics(dt, (float)totalSec);
        RenderScene((float)totalSec);
    }

    private void UpdatePhysics(float dt, float totalSec)
    {
        bool reducedMotion = _themeManager?.ReducedMotion ?? false;

        // Smooth 3D Camera Orbit & Tilt driven by cursor position
        if (_isCursorInside && !reducedMotion)
        {
            float normX = (_cursorX / CanvasWidth) - 0.5f;
            float normY = (_cursorY / CanvasHeight) - 0.5f;
            _targetParallaxX = normX * 8.0f;
            _targetParallaxY = normY * 6.0f;
        }
        else
        {
            _targetParallaxX = 0f;
            _targetParallaxY = 0f;
        }

        float smoothFactor = Math.Min(1.0f, dt * 7.0f);
        _currentParallaxX += (_targetParallaxX - _currentParallaxX) * smoothFactor;
        _currentParallaxY += (_targetParallaxY - _currentParallaxY) * smoothFactor;

        // Age out inactive ripples
        for (int i = 0; i < _ripples.Length; i++)
        {
            if (_ripples[i].Active)
            {
                float age = totalSec - _ripples[i].StartTime;
                if (age > 3.5f)
                {
                    _ripples[i].Active = false;
                }
            }
        }
    }

    #region Real-Time Reconstructed Pixel-Art Rendering Engine

    /// <summary>
    /// Ken Perlin's C2-continuous SmootherStep curve: 6t^5 - 15t^4 + 10t^3.
    /// Guarantees zero 1st and 2nd derivatives at keyframe boundaries (t=0 and t=1),
    /// delivering a silky smooth, fluid transition between Day, Twilight, and Night
    /// with zero abrupt derivative kinks or visual corner artifacts.
    /// </summary>
    private static float SmootherStep(float t)
    {
        t = Math.Clamp(t, 0.0f, 1.0f);
        return t * t * t * (t * (t * 6.0f - 15.0f) + 10.0f);
    }

    private unsafe void RenderScene(float totalSec)
    {
        if (_writeableBitmap == null) return;

        double v = _themeValue;
        bool reducedMotion = _themeManager?.ReducedMotion ?? false;
        bool ambient = _themeManager?.AmbientAnimationEnabled ?? true;

        // Determine palette interpolation keyframes and factor with C2 SmootherStep
        byte[] src1, src2;
        float factor;

        if (v <= 0.5)
        {
            float t = (float)(v / 0.5);
            factor = SmootherStep(t);
            src1 = _dayBuffer;
            src2 = _twilightBuffer;
        }
        else
        {
            float t = (float)((v - 0.5) / 0.5);
            factor = SmootherStep(t);
            src1 = _twilightBuffer;
            src2 = _nightBuffer;
        }

        int invFactor = (int)((1.0f - factor) * 256.0f);
        int factor256 = (int)(factor * 256.0f);

        // Continuous Water Absorption Tint (SmootherStep interpolation across full 0..1 range)
        float wB, wG, wR;
        if (v <= 0.5)
        {
            float wt = SmootherStep((float)(v / 0.5));
            wB = (82.0f * (1.0f - wt)) + (48.0f * wt);
            wG = (124.0f * (1.0f - wt)) + (88.0f * wt);
            wR = (64.0f * (1.0f - wt)) + (92.0f * wt);
        }
        else
        {
            float wt = SmootherStep((float)((v - 0.5) / 0.5));
            wB = (48.0f * (1.0f - wt)) + (35.0f * wt);
            wG = (88.0f * (1.0f - wt)) + (24.0f * wt);
            wR = (92.0f * (1.0f - wt)) + (22.0f * wt);
        }

        // Subpixel Continuous Sinking & Waterline Elevation
        float floatSinkDy = (float)(v * 6.0);
        float floatWaterlineY = 547.0f - (float)(v * 75.0);

        // Moonlight Glint Smooth Fade-In (quadratic ramp from v=0.55 to v=1.0)
        float moonGlintFactor = 0f;
        if (v > 0.55f)
        {
            float tm = (float)((v - 0.55f) / 0.45f);
            moonGlintFactor = tm * tm;
        }

        // Differential 3D Depth Parallax
        int boatParallaxX = (int)(_currentParallaxX * 1.4f);
        int boatParallaxY = (int)(_currentParallaxY * 1.4f);
        int waterParallaxX = (int)(_currentParallaxX * 0.6f);
        int waterParallaxY = (int)(_currentParallaxY * 0.6f);

        fixed (byte* pRender = _renderBuffer, pSrc1 = src1, pSrc2 = src2)
        {
            for (int y = 0; y < CanvasHeight; y++)
            {
                int rowOffset = y * CanvasWidth * 4;
                int maskRowOffset = y * CanvasWidth;

                for (int x = 0; x < CanvasWidth; x++)
                {
                    int pixelOffset = rowOffset + (x * 4);
                    bool inSliderChannel = (x >= SliderLeft && x <= SliderRight && y >= SliderTop - 25 && y <= SliderBottom + 25);

                    bool isBoat = _boatMask[maskRowOffset + x];

                    if (isBoat && !inSliderChannel)
                    {
                        // 1. Boat Pixel: Rigid wooden hull and perched seagull
                        // Shifts with boat parallax and subpixel-smooth vertical sink offset
                        float byFloat = y + boatParallaxY - floatSinkDy;
                        int by0 = Math.Clamp((int)Math.Floor(byFloat), 0, CanvasHeight - 1);
                        int by1 = Math.Clamp(by0 + 1, 0, CanvasHeight - 1);
                        int fy = (int)((byFloat - by0) * 256.0f);
                        int invFy = 256 - fy;

                        int bx = Math.Clamp(x + boatParallaxX, 0, CanvasWidth - 1);
                        int srcOffset0 = (by0 * CanvasWidth * 4) + (bx * 4);
                        int srcOffset1 = (by1 * CanvasWidth * 4) + (bx * 4);

                        int b0 = ((pSrc1[srcOffset0 + 0] * invFactor) + (pSrc2[srcOffset0 + 0] * factor256)) >> 8;
                        int g0 = ((pSrc1[srcOffset0 + 1] * invFactor) + (pSrc2[srcOffset0 + 1] * factor256)) >> 8;
                        int r0 = ((pSrc1[srcOffset0 + 2] * invFactor) + (pSrc2[srcOffset0 + 2] * factor256)) >> 8;

                        int b1 = ((pSrc1[srcOffset1 + 0] * invFactor) + (pSrc2[srcOffset1 + 0] * factor256)) >> 8;
                        int g1 = ((pSrc1[srcOffset1 + 1] * invFactor) + (pSrc2[srcOffset1 + 1] * factor256)) >> 8;
                        int r1 = ((pSrc1[srcOffset1 + 2] * invFactor) + (pSrc2[srcOffset1 + 2] * factor256)) >> 8;

                        int b = ((b0 * invFy) + (b1 * fy)) >> 8;
                        int g = ((g0 * invFy) + (g1 * fy)) >> 8;
                        int r = ((r0 * invFy) + (r1 * fy)) >> 8;

                        // Continuous Subpixel Waterline Submersion Absorption
                        float dyWater = y - floatWaterlineY;
                        if (dyWater > 0f)
                        {
                            float depth = dyWater * 0.16f;
                            float atten = (float)Math.Exp(-depth);
                            b = (int)((b * atten) + (wB * (1.0f - atten)));
                            g = (int)((g * atten) + (wG * (1.0f - atten)));
                            r = (int)((r * atten) + (wR * (1.0f - atten)));
                        }

                        // Antialiased foam meniscus line tracking dynamic waterline
                        float foamDist = Math.Abs(dyWater);
                        if (foamDist < 1.6f)
                        {
                            int foam = (int)(((1.6f - foamDist) / 1.6f) * 50.0f);
                            b = Math.Min(255, b + foam);
                            g = Math.Min(255, g + foam);
                            r = Math.Min(255, r + foam);
                        }

                        pRender[pixelOffset + 0] = (byte)b;
                        pRender[pixelOffset + 1] = (byte)g;
                        pRender[pixelOffset + 2] = (byte)r;
                        pRender[pixelOffset + 3] = 255;
                    }
                    else
                    {
                        // 2. Water Pixel: Wave ripple displacement & background sampling
                        float dispX = 0f;
                        float dispY = 0f;

                        if (!inSliderChannel && !reducedMotion)
                        {
                            // Ambient concentric harmonic ripples radiating outward from boat vessel
                            if (ambient)
                            {
                                float dx0 = x - 260;
                                float dy0 = y - 480;
                                float dist0 = (float)Math.Sqrt((dx0 * dx0) + (dy0 * dy0));
                                if (dist0 > 10.0f)
                                {
                                    float phase = (dist0 * 0.075f) - (totalSec * 2.4f);
                                    float wave = (float)Math.Sin(phase) * 1.15f;
                                    float invDist = 1.0f / dist0;
                                    dispX += dx0 * invDist * wave;
                                    dispY += dy0 * invDist * wave;
                                }
                            }

                            // Mouse-induced expanding circular ripples
                            for (int ripIdx = 0; ripIdx < _ripples.Length; ripIdx++)
                            {
                                if (!_ripples[ripIdx].Active) continue;

                                float age = totalSec - _ripples[ripIdx].StartTime;
                                float waveDist = age * 165.0f;

                                float dx = x - _ripples[ripIdx].X;
                                float dy = y - _ripples[ripIdx].Y;
                                float dist = (float)Math.Sqrt((dx * dx) + (dy * dy));

                                if (dist > 1.0f && dist <= waveDist + 35.0f && dist >= waveDist - 35.0f)
                                {
                                    float decay = (float)Math.Exp(-age * 1.15f);
                                    float wave = (float)Math.Sin((dist - waveDist) * 0.22f) * _ripples[ripIdx].Amplitude * decay;
                                    float invDist = 1.0f / dist;

                                    dispX += dx * invDist * wave;
                                    dispY += dy * invDist * wave;
                                }
                            }
                        }

                        int sampleX = Math.Clamp(x + waterParallaxX + (int)dispX, 0, CanvasWidth - 1);
                        int sampleY = Math.Clamp(y + waterParallaxY + (int)dispY, 0, CanvasHeight - 1);
                        int srcOffset = (sampleY * CanvasWidth * 4) + (sampleX * 4);

                        int b = ((pSrc1[srcOffset + 0] * invFactor) + (pSrc2[srcOffset + 0] * factor256)) >> 8;
                        int g = ((pSrc1[srcOffset + 1] * invFactor) + (pSrc2[srcOffset + 1] * factor256)) >> 8;
                        int r = ((pSrc1[srcOffset + 2] * invFactor) + (pSrc2[srcOffset + 2] * factor256)) >> 8;

                        // Moonlight specular glints at Night (clean, isolated sparkles; smoothly faded in with moonGlintFactor)
                        if (moonGlintFactor > 0.001f && !inSliderChannel && x >= 140 && x <= 200 && y >= 110 && y <= 360)
                        {
                            float colDist = Math.Abs(x - 170);
                            if (colDist < 25.0f)
                            {
                                float glintPhase = (x * 12.9898f) + (y * 78.233f) + (totalSec * 3.5f);
                                float noise = (float)(Math.Sin(glintPhase) * 43758.5453) % 1.0f;
                                if (noise > 0.965f)
                                {
                                    int glint = (int)((noise - 0.965f) * 28.5f * 200.0f * moonGlintFactor);
                                    b = Math.Min(255, b + glint);
                                    g = Math.Min(255, g + (glint * 8 / 10));
                                    r = Math.Min(255, r + (glint * 6 / 10));
                                }
                            }
                        }

                        pRender[pixelOffset + 0] = (byte)b;
                        pRender[pixelOffset + 1] = (byte)g;
                        pRender[pixelOffset + 2] = (byte)r;
                        pRender[pixelOffset + 3] = 255;
                    }
                }
            }

            // Render integrated hand-drawn sailboat companion vessel in open waters
            RenderSailboatArtwork(pRender, (float)v, totalSec, reducedMotion, waterParallaxX, waterParallaxY);

            // Render celestial slider track, moon/sun icons, and minimalist circular dot thumb
            RenderSliderTrack(pRender);
            RenderDotThumb(pRender, (float)v);
        }

        // Copy directly into WriteableBitmap buffer
        using var stream = _writeableBitmap.PixelBuffer.AsStream();
        stream.Write(_renderBuffer, 0, BufferByteLength);
        _writeableBitmap.Invalidate();
    }

    #endregion

    #region Sailboat Companion Artwork

    /// <summary>
    /// Renders the authentic hand-drawn sailboat companion vessel in the midground open waters.
    /// Features ambient pitch/heave bobbing, midground perspective parallax, and continuous
    /// C2-continuous Hermite SmootherStep atmosphere lighting transitions across Day, Twilight, and Night.
    /// </summary>
    private unsafe void RenderSailboatArtwork(byte* pRender, float v, float totalSec, bool reducedMotion, int waterParallaxX, int waterParallaxY)
    {
        if (!_hasSailboatAssets) return;

        // Base anchor in open ocean water (horizontally between celestial slider and foreground boat)
        const int BaseX = 115;
        const int BaseY = 135;

        // Gentle ambient water heave and sway
        float bobY = 0f;
        float bobX = 0f;
        if (!reducedMotion)
        {
            bobY = (float)Math.Sin(totalSec * 1.8f) * 1.3f;
            bobX = (float)Math.Cos(totalSec * 1.1f) * 0.5f;
        }

        // Midground perspective parallax (1.15x deep water parallax)
        int startX = BaseX + (int)(waterParallaxX * 1.15f + bobX);
        int startY = BaseY + (int)(waterParallaxY * 1.15f + bobY);

        // Continuous atmospheric palette blending using C2 SmootherStep
        byte[] src1, src2;
        int factor256;

        if (v <= 0.5f)
        {
            src1 = _sailboatDayBuffer;
            src2 = _sailboatTwilightBuffer;
            float t = SmootherStep(v / 0.5f);
            factor256 = (int)(t * 256.0f);
        }
        else
        {
            src1 = _sailboatTwilightBuffer;
            src2 = _sailboatNightBuffer;
            float t = SmootherStep((v - 0.5f) / 0.5f);
            factor256 = (int)(t * 256.0f);
        }

        int invFactor = 256 - factor256;

        fixed (byte* pSrc1 = src1, pSrc2 = src2)
        {
            for (int sy = 0; sy < SailboatHeight; sy++)
            {
                int py = startY + sy;
                if (py < 0 || py >= CanvasHeight) continue;
                int destRowOffset = py * CanvasWidth * 4;
                int srcRowOffset = sy * SailboatWidth * 4;

                for (int sx = 0; sx < SailboatWidth; sx++)
                {
                    int px = startX + sx;
                    if (px < 0 || px >= CanvasWidth) continue;

                    int srcOffset = srcRowOffset + (sx * 4);
                    int destOffset = destRowOffset + (px * 4);

                    // Interpolate alpha
                    int a1 = pSrc1[srcOffset + 3];
                    int a2 = pSrc2[srcOffset + 3];
                    int a = ((a1 * invFactor) + (a2 * factor256)) >> 8;
                    if (a <= 2) continue;

                    // Interpolate color
                    int b = ((pSrc1[srcOffset + 0] * invFactor) + (pSrc2[srcOffset + 0] * factor256)) >> 8;
                    int g = ((pSrc1[srcOffset + 1] * invFactor) + (pSrc2[srcOffset + 1] * factor256)) >> 8;
                    int r = ((pSrc1[srcOffset + 2] * invFactor) + (pSrc2[srcOffset + 2] * factor256)) >> 8;

                    // Alpha blend over background water
                    int invA = 255 - a;
                    pRender[destOffset + 0] = (byte)(((pRender[destOffset + 0] * invA) + (b * a)) / 255);
                    pRender[destOffset + 1] = (byte)(((pRender[destOffset + 1] * invA) + (g * a)) / 255);
                    pRender[destOffset + 2] = (byte)(((pRender[destOffset + 2] * invA) + (r * a)) / 255);
                }
            }
        }
    }

    #endregion

    #region Minimalist Circular Dot Slider

    private unsafe void RenderSliderTrack(byte* pRender)
    {
        int thumbY = SliderBottom - (int)(_themeValue * SliderTravel);
        thumbY = Math.Clamp(thumbY, SliderTop, SliderBottom);

        // Solid luminous white track line below the thumb (Day direction)
        for (int y = thumbY; y <= SliderBottom; y++)
        {
            int lineOffset = (y * CanvasWidth * 4) + (SliderCenterX * 4);
            pRender[lineOffset + 0] = 255;
            pRender[lineOffset + 1] = 255;
            pRender[lineOffset + 2] = 255;

            // 1px subtle glow on sides
            pRender[lineOffset - 4 + 0] = Math.Max(pRender[lineOffset - 4 + 0], (byte)160);
            pRender[lineOffset - 4 + 1] = Math.Max(pRender[lineOffset - 4 + 1], (byte)160);
            pRender[lineOffset - 4 + 2] = Math.Max(pRender[lineOffset - 4 + 2], (byte)160);

            pRender[lineOffset + 4 + 0] = Math.Max(pRender[lineOffset + 4 + 0], (byte)160);
            pRender[lineOffset + 4 + 1] = Math.Max(pRender[lineOffset + 4 + 1], (byte)160);
            pRender[lineOffset + 4 + 2] = Math.Max(pRender[lineOffset + 4 + 2], (byte)160);
        }

        // Subtly lit track line above the thumb (Night direction)
        for (int y = SliderTop; y < thumbY; y++)
        {
            int lineOffset = (y * CanvasWidth * 4) + (SliderCenterX * 4);
            pRender[lineOffset + 0] = Math.Max(pRender[lineOffset + 0], (byte)70);
            pRender[lineOffset + 1] = Math.Max(pRender[lineOffset + 1], (byte)70);
            pRender[lineOffset + 2] = Math.Max(pRender[lineOffset + 2], (byte)70);
        }

        // Moon icon at top zenith (SliderTop - 18)
        DrawMoonIcon(pRender, SliderCenterX, SliderTop - 18);

        // Sun icon at bottom nadir (SliderBottom + 18)
        DrawSunIcon(pRender, SliderCenterX, SliderBottom + 18);
    }

    private static unsafe void DrawMoonIcon(byte* pRender, int cx, int cy)
    {
        for (int dy = -6; dy <= 6; dy++)
        {
            int py = cy + dy;
            if (py < 0 || py >= CanvasHeight) continue;
            int rowOffset = py * CanvasWidth * 4;

            for (int dx = -6; dx <= 6; dx++)
            {
                int px = cx + dx;
                if (px < 0 || px >= CanvasWidth) continue;

                float dOuter = (float)Math.Sqrt((dx * dx) + (dy * dy));
                float dInner = (float)Math.Sqrt(((dx - 3) * (dx - 3)) + ((dy + 1) * (dy + 1)));

                if (dOuter <= 5.5f && dInner >= 4.2f)
                {
                    int offset = rowOffset + (px * 4);
                    pRender[offset + 0] = 255;
                    pRender[offset + 1] = 255;
                    pRender[offset + 2] = 255;
                }
            }
        }
    }

    private static unsafe void DrawSunIcon(byte* pRender, int cx, int cy)
    {
        for (int dy = -6; dy <= 6; dy++)
        {
            int py = cy + dy;
            if (py < 0 || py >= CanvasHeight) continue;
            int rowOffset = py * CanvasWidth * 4;

            for (int dx = -6; dx <= 6; dx++)
            {
                int px = cx + dx;
                if (px < 0 || px >= CanvasWidth) continue;

                float dist = (float)Math.Sqrt((dx * dx) + (dy * dy));
                bool isCore = dist <= 3.2f;
                bool isRay = (dist >= 4.8f && dist <= 6.2f) && ((dx == 0) || (dy == 0) || (Math.Abs(dx) == Math.Abs(dy)));

                if (isCore || isRay)
                {
                    int offset = rowOffset + (px * 4);
                    pRender[offset + 0] = 255;
                    pRender[offset + 1] = 255;
                    pRender[offset + 2] = 255;
                }
            }
        }
    }

    /// <summary>
    /// Renders a simple, clean minimalist circular dot slider thumb.
    /// Proportioned to diameter ~10.5px (radius 5.0f, expanding to 5.6f when active),
    /// perfectly centered on the slider track groove with smooth subpixel antialiasing
    /// and a soft elevation drop shadow.
    /// </summary>
    private unsafe void RenderDotThumb(byte* pRender, float v)
    {
        int thumbY = SliderBottom - (int)(v * SliderTravel);
        thumbY = Math.Clamp(thumbY, SliderTop, SliderBottom);

        bool isInteracting = _isDraggingSlider || _isPointerOverSlider;
        float radius = isInteracting ? 5.6f : 5.0f;

        int minX = SliderCenterX - 8;
        int maxX = SliderCenterX + 8;
        int minY = thumbY - 8;
        int maxY = thumbY + 8;

        for (int y = minY; y <= maxY; y++)
        {
            if (y < 0 || y >= CanvasHeight) continue;
            int rowOffset = y * CanvasWidth * 4;

            for (int x = minX; x <= maxX; x++)
            {
                if (x < 0 || x >= CanvasWidth) continue;

                float dx = x - SliderCenterX;
                float dy = y - thumbY;
                float dist = (float)Math.Sqrt((dx * dx) + (dy * dy));

                int offset = rowOffset + (x * 4);

                // Subtle soft drop shadow beneath and to bottom-right
                if (dist > radius)
                {
                    float sDist = (float)Math.Sqrt(((dx - 0.5f) * (dx - 0.5f)) + ((dy - 1.2f) * (dy - 1.2f)));
                    if (sDist <= radius + 2.0f)
                    {
                        float shadowAlpha = (1.0f - ((sDist - radius) / 2.0f)) * 0.35f;
                        pRender[offset + 0] = (byte)(pRender[offset + 0] * (1.0f - shadowAlpha));
                        pRender[offset + 1] = (byte)(pRender[offset + 1] * (1.0f - shadowAlpha));
                        pRender[offset + 2] = (byte)(pRender[offset + 2] * (1.0f - shadowAlpha));
                    }
                    continue;
                }

                // Clean minimalist circular dot with smooth subpixel antialiasing
                float alpha = Math.Clamp(radius + 0.5f - dist, 0.0f, 1.0f);

                // Subtle clean luminance gradient: top 255 pure white, bottom 242 light silver
                float tGrad = Math.Clamp((dy / radius) * 0.5f + 0.5f, 0.0f, 1.0f);
                int dotB = (int)(245.0f - (tGrad * 10.0f));
                int dotG = (int)(248.0f - (tGrad * 8.0f));
                int dotR = 255;

                int baseB = pRender[offset + 0];
                int baseG = pRender[offset + 1];
                int baseR = pRender[offset + 2];

                pRender[offset + 0] = (byte)((baseB * (1.0f - alpha)) + (dotB * alpha));
                pRender[offset + 1] = (byte)((baseG * (1.0f - alpha)) + (dotG * alpha));
                pRender[offset + 2] = (byte)((baseR * (1.0f - alpha)) + (dotR * alpha));
                pRender[offset + 3] = 255;
            }
        }
    }

    #endregion

    #region Pointer & Keyboard Interaction

    private void OnPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _isCursorInside = true;
    }

    private void OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        _isCursorInside = false;
        _isPointerOverSlider = false;
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        var pt = e.GetCurrentPoint(PixelDisplayImage);
        _cursorX = (float)pt.Position.X;
        _cursorY = (float)pt.Position.Y;
        _isCursorInside = true;

        _isPointerOverSlider = (_cursorX >= SliderLeft && _cursorX <= SliderRight &&
                                _cursorY >= SliderTop - 25 && _cursorY <= SliderBottom + 25);

        if (_isDraggingSlider)
        {
            UpdateSliderFromPointerY(_cursorY);
            return;
        }

        // Boundary check: if pointer is over the boat, do NOT spawn water ripples!
        int px = Math.Clamp((int)_cursorX, 0, CanvasWidth - 1);
        int py = Math.Clamp((int)_cursorY, 0, CanvasHeight - 1);
        int idx = (py * CanvasWidth) + px;

        bool isOverBoat = _boatMask[idx];
        if (!isOverBoat && !_isPointerOverSlider)
        {
            double now = _stopwatch.Elapsed.TotalSeconds;
            float distFromLast = (float)Math.Sqrt(((_cursorX - _lastRippleX) * (_cursorX - _lastRippleX)) +
                                                  ((_cursorY - _lastRippleY) * (_cursorY - _lastRippleY)));

            if (distFromLast > 18.0f && (now - _lastRippleTime) > 0.08)
            {
                SpawnRippleAtCursor(_cursorX, _cursorY, 1.4f);
                _lastRippleX = _cursorX;
                _lastRippleY = _cursorY;
                _lastRippleTime = now;
            }
        }
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var pt = e.GetCurrentPoint(PixelDisplayImage);
        float px = (float)pt.Position.X;
        float py = (float)pt.Position.Y;

        if (px >= SliderLeft - 5 && px <= SliderRight + 5 && py >= SliderTop - 25 && py <= SliderBottom + 25)
        {
            _isDraggingSlider = true;
            PixelDisplayImage.CapturePointer(e.Pointer);
            UpdateSliderFromPointerY(py);
            e.Handled = true;
            return;
        }

        int cx = Math.Clamp((int)px, 0, CanvasWidth - 1);
        int cy = Math.Clamp((int)py, 0, CanvasHeight - 1);
        if (!_boatMask[(cy * CanvasWidth) + cx])
        {
            SpawnRippleAtCursor(px, py, 2.6f);
        }
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_isDraggingSlider)
        {
            _isDraggingSlider = false;
            PixelDisplayImage.ReleasePointerCapture(e.Pointer);
            e.Handled = true;
        }
    }

    private void OnPointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        _isDraggingSlider = false;
    }

    private void OnPointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        var props = e.GetCurrentPoint(PixelDisplayImage).Properties;
        int delta = props.MouseWheelDelta;
        if (delta != 0)
        {
            double step = delta > 0 ? 0.05 : -0.05;
            ThemeValue = Math.Clamp(_themeValue + step, 0.0, 1.0);
            e.Handled = true;
        }
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        double step = 0.05;
        switch (e.Key)
        {
            case VirtualKey.Up:
                ThemeValue = Math.Clamp(_themeValue + step, 0.0, 1.0);
                e.Handled = true;
                break;
            case VirtualKey.Down:
                ThemeValue = Math.Clamp(_themeValue - step, 0.0, 1.0);
                e.Handled = true;
                break;
            case VirtualKey.Home:
                ThemeValue = 0.0;
                e.Handled = true;
                break;
            case VirtualKey.End:
                ThemeValue = 1.0;
                e.Handled = true;
                break;
        }
    }

    private void UpdateSliderFromPointerY(float pointerY)
    {
        float clampedY = Math.Clamp(pointerY, SliderTop, SliderBottom);
        // Nadir (SliderBottom = 444) -> 0.0 (Day)
        // Zenith (SliderTop = 158)    -> 1.0 (Night)
        double newVal = (SliderBottom - clampedY) / (double)SliderTravel;
        ThemeValue = newVal;
    }

    private void SpawnRippleAtCursor(float cx, float cy, float amplitude)
    {
        _ripples[_rippleHeadIndex] = new RippleWave
        {
            X = cx,
            Y = cy,
            StartTime = (float)_stopwatch.Elapsed.TotalSeconds,
            Amplitude = amplitude,
            Active = true
        };
        _rippleHeadIndex = (_rippleHeadIndex + 1) % _ripples.Length;
    }

    #endregion
}
