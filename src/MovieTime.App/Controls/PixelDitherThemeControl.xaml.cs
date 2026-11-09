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
/// Real-time interactive 3D retro pixel-art theme controller for Movie Time.
/// Combines a 3D perspective camera, dynamic concentric water ripples, a volumetric wooden rowboat,
/// a handcrafted perched pixel-art seagull, a 100-stage continuous sinking animation,
/// an internal 180x320 low-resolution target with 2x nearest-neighbor upscale to 360x640,
/// authentic 8x8 Bayer ordered dithering across discrete 5-stop color palettes,
/// and an elegant liquid glass droplet slider thumb.
/// </summary>
public sealed partial class PixelDitherThemeControl : UserControl
{
    // Screen / Display Canvas Size
    private const int CanvasWidth = 360;
    private const int CanvasHeight = 640;
    private const int OutputPixelCount = CanvasWidth * CanvasHeight;
    private const int BufferByteLength = OutputPixelCount * 4;

    // Internal Low-Resolution Retro Framebuffer (Pixel Scale = 2)
    private const int PixelScale = 2;
    private const int InternalWidth = CanvasWidth / PixelScale;   // 180 px
    private const int InternalHeight = CanvasHeight / PixelScale; // 320 px
    private const int InternalPixelCount = InternalWidth * InternalHeight; // 57,600 px

    // Celestial Slider Coordinate System (within 360x640 canvas)
    private const int SliderLeft = 16;
    private const int SliderRight = 56;
    private const int SliderTop = 158;      // 100% (Night / Moon zenith)
    private const int SliderBottom = 444;   // 0%   (Day / Sun nadir)
    private const int SliderCenterX = 36;
    private const int SliderTravel = SliderBottom - SliderTop; // 286 px

    // 8x8 Bayer ordered dithering matrix normalized to [-0.5, 0.5]
    private static readonly float[] Bayer8x8 = new float[64]
    {
         0f/64f - 0.5f, 32f/64f - 0.5f,  8f/64f - 0.5f, 40f/64f - 0.5f,  2f/64f - 0.5f, 34f/64f - 0.5f, 10f/64f - 0.5f, 42f/64f - 0.5f,
        48f/64f - 0.5f, 16f/64f - 0.5f, 56f/64f - 0.5f, 24f/64f - 0.5f, 50f/64f - 0.5f, 18f/64f - 0.5f, 58f/64f - 0.5f, 26f/64f - 0.5f,
        12f/64f - 0.5f, 44f/64f - 0.5f,  4f/64f - 0.5f, 36f/64f - 0.5f, 14f/64f - 0.5f, 46f/64f - 0.5f,  6f/64f - 0.5f, 38f/64f - 0.5f,
        60f/64f - 0.5f, 28f/64f - 0.5f, 52f/64f - 0.5f, 20f/64f - 0.5f, 62f/64f - 0.5f, 30f/64f - 0.5f, 54f/64f - 0.5f, 22f/64f - 0.5f,
         3f/64f - 0.5f, 35f/64f - 0.5f, 11f/64f - 0.5f, 43f/64f - 0.5f,  1f/64f - 0.5f, 33f/64f - 0.5f,  9f/64f - 0.5f, 41f/64f - 0.5f,
        51f/64f - 0.5f, 19f/64f - 0.5f, 59f/64f - 0.5f, 27f/64f - 0.5f, 49f/64f - 0.5f, 17f/64f - 0.5f, 57f/64f - 0.5f, 25f/64f - 0.5f,
        15f/64f - 0.5f, 47f/64f - 0.5f,  7f/64f - 0.5f, 39f/64f - 0.5f, 13f/64f - 0.5f, 45f/64f - 0.5f,  5f/64f - 0.5f, 37f/64f - 0.5f,
        63f/64f - 0.5f, 31f/64f - 0.5f, 55f/64f - 0.5f, 23f/64f - 0.5f, 61f/64f - 0.5f, 29f/64f - 0.5f, 53f/64f - 0.5f, 21f/64f - 0.5f
    };

    private readonly IThemeManagerService? _themeManager;
    private readonly ILogger<PixelDitherThemeControl> _logger;

    private WriteableBitmap? _writeableBitmap;
    private readonly byte[] _renderBuffer = new byte[BufferByteLength];

    // Internal Low-Resolution Framebuffer & Depth
    private readonly uint[] _internalBuffer = new uint[InternalPixelCount];
    private readonly float[] _internalDepthBuffer = new float[InternalPixelCount];
    private readonly bool[] _internalBoatMask = new bool[InternalPixelCount];

    private readonly Stopwatch _stopwatch = new();
    private double _lastElapsedSeconds;
    private bool _isLoaded;
    private bool _isRendering;

    // Slider State
    private bool _isDraggingSlider;
    private bool _isPointerOverSlider;
    private double _themeValue = 1.0; // 0.0 (Day) -> 1.0 (Night)

    // Mouse Interaction & 3D Camera State
    private float _cursorX = -100;
    private float _cursorY = -100;
    private float _lastRippleX = -100;
    private float _lastRippleY = -100;
    private double _lastRippleTime;
    private bool _isCursorInside;

    // 3D Camera Orbit & Tilt
    private float _targetPitch = 40.0f;
    private float _targetYaw = -20.0f;
    private float _currentPitch = 40.0f;
    private float _currentYaw = -20.0f;

    // Bounded Active Ripples Ring Buffer (16 ripples max, zero heap allocation)
    private struct RippleWave
    {
        public float WorldX;
        public float WorldZ;
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

        // Smooth 3D Camera Orbit & Tilt
        if (_isCursorInside && !reducedMotion)
        {
            float normX = (_cursorX / CanvasWidth) - 0.5f;
            float normY = (_cursorY / CanvasHeight) - 0.5f;
            _targetYaw = -20.0f + (normX * 18.0f);
            _targetPitch = 40.0f + (normY * 12.0f);
        }
        else
        {
            _targetYaw = -20.0f;
            _targetPitch = 40.0f;
        }

        float smoothFactor = (float)Math.Min(1.0, dt * 7.0f);
        _currentYaw += (_targetYaw - _currentYaw) * smoothFactor;
        _currentPitch += (_targetPitch - _currentPitch) * smoothFactor;

        // Age out inactive ripples
        for (int i = 0; i < _ripples.Length; i++)
        {
            if (_ripples[i].Active)
            {
                float age = totalSec - _ripples[i].StartTime;
                if (age > 4.2f)
                {
                    _ripples[i].Active = false;
                }
            }
        }
    }

    #region Real-Time 3D Retro Pixel-Art Rendering Engine

    private readonly struct Vector3
    {
        public readonly float X, Y, Z;
        public Vector3(float x, float y, float z) { X = x; Y = y; Z = z; }

        public static Vector3 operator +(Vector3 a, Vector3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static Vector3 operator *(Vector3 a, float s) => new(a.X * s, a.Y * s, a.Z * s);
        public static float Dot(Vector3 a, Vector3 b) => (a.X * b.X) + (a.Y * b.Y) + (a.Z * b.Z);

        public static Vector3 Cross(Vector3 a, Vector3 b) =>
            new((a.Y * b.Z) - (a.Z * b.Y), (a.Z * b.X) - (a.X * b.Z), (a.X * b.Y) - (a.Y * b.X));

        public float Length() => (float)Math.Sqrt((X * X) + (Y * Y) + (Z * Z));

        public Vector3 Normalized()
        {
            float len = Length();
            return len > 0.00001f ? new Vector3(X / len, Y / len, Z / len) : new Vector3(0, 1, 0);
        }
    }

    private static uint PackBgra(int r, int g, int b, int a = 255)
    {
        uint cr = (uint)Math.Clamp(r, 0, 255);
        uint cg = (uint)Math.Clamp(g, 0, 255);
        uint cb = (uint)Math.Clamp(b, 0, 255);
        uint ca = (uint)Math.Clamp(a, 0, 255);
        return (ca << 24) | (cr << 16) | (cg << 8) | cb;
    }

    private static uint LerpBgra(uint c1, uint c2, float t)
    {
        t = Math.Clamp(t, 0.0f, 1.0f);
        int b1 = (int)(c1 & 0xFF), g1 = (int)((c1 >> 8) & 0xFF), r1 = (int)((c1 >> 16) & 0xFF), a1 = (int)((c1 >> 24) & 0xFF);
        int b2 = (int)(c2 & 0xFF), g2 = (int)((c2 >> 8) & 0xFF), r2 = (int)((c2 >> 16) & 0xFF), a2 = (int)((c2 >> 24) & 0xFF);
        int r = (int)(r1 + ((r2 - r1) * t));
        int g = (int)(g1 + ((g2 - g1) * t));
        int b = (int)(b1 + ((b2 - b1) * t));
        int a = (int)(a1 + ((a2 - a1) * t));
        return PackBgra(r, g, b, a);
    }

    private void RenderScene(float totalSec)
    {
        if (_writeableBitmap == null) return;

        double v = _themeValue;
        bool reducedMotion = _themeManager?.ReducedMotion ?? false;
        bool ambient = _themeManager?.AmbientAnimationEnabled ?? true;

        // 1. Clear Internal Buffers (180x320)
        Array.Fill(_internalDepthBuffer, 1e8f);
        Array.Fill(_internalBoatMask, false);

        // 2. Setup 3D Perspective Camera (calibrated for 180x320 low-res target)
        float yawRad = _currentYaw * ((float)Math.PI / 180.0f);
        float pitchRad = _currentPitch * ((float)Math.PI / 180.0f);

        Vector3 camTarget = new(20.0f, -1.0f, 24.0f);
        float camDist = 118.0f;

        float cosP = (float)Math.Cos(pitchRad);
        float sinP = (float)Math.Sin(pitchRad);
        float cosY = (float)Math.Cos(yawRad);
        float sinY = (float)Math.Sin(yawRad);

        Vector3 camPos = camTarget + new Vector3(sinY * cosP * camDist, sinP * camDist, -cosY * cosP * camDist);
        Vector3 camFwd = (camTarget - camPos).Normalized();
        Vector3 camRight = Vector3.Cross(new Vector3(0, 1, 0), camFwd).Normalized();
        Vector3 camUp = Vector3.Cross(camFwd, camRight).Normalized();

        float focal = 192.0f;
        float halfW = InternalWidth * 0.5f;
        float halfH = InternalHeight * 0.5f;

        // 3. Five-Stop Perceptual Palette Ramps
        // [0: Deep, 1: Dark, 2: Mid, 3: Shallow, 4: Crest, 5: Specular Glint]
        uint[] waterPal = GetInterpolatedWaterPalette(v);
        // [0: Shadow, 1: Dark Plank, 2: Mid Plank, 3: Light Plank, 4: Gunwale Rim]
        uint[] woodPal = GetInterpolatedWoodPalette(v);
        // [0: Body, 1: Wing, 2: Wingtip, 3: Beak, 4: Legs]
        uint[] gullPal = GetInterpolatedGullPalette(v);

        // 4. Continuous Boat Sinking Elevation ($0.0 \to 1.0$)
        // s=0: boat rides high (Y = +4.5), full hull exposed above water
        // s=0.5: boat mid-submerged (Y = -4.0), waterline at mid-hull
        // s=1.0: boat submerged (Y = -12.5), tip of prow and perched seagull at waterline
        float sinkProgress = (float)v;
        float boatElevationY = 4.5f - (sinkProgress * 17.0f);

        // 5. Rasterize 3D Parametric Wooden Boat Geometry into Internal Target
        RasterizeBoatMesh(camPos, camFwd, camRight, camUp, focal, halfW, halfH, boatElevationY, totalSec, reducedMotion, ambient, woodPal, waterPal, gullPal);

        // 6. Raycast & Shade 3D Concentric Water Surface with Bayer Ordered Dithering
        ShadeWaterSurface(camPos, camFwd, camRight, camUp, focal, halfW, halfH, totalSec, reducedMotion, ambient, v, waterPal);

        // 7. Upscale 180x320 Low-Res Target to 360x640 Output via 2x Nearest-Neighbor
        unsafe
        {
            fixed (byte* pOutputBytes = _renderBuffer)
            fixed (uint* pInternal = _internalBuffer)
            {
                uint* pOutput = (uint*)pOutputBytes;

                for (int y = 0; y < InternalHeight; y++)
                {
                    int outY0 = y * PixelScale;
                    int outY1 = outY0 + 1;
                    int inRow = y * InternalWidth;
                    int outRow0 = outY0 * CanvasWidth;
                    int outRow1 = outY1 * CanvasWidth;

                    for (int x = 0; x < InternalWidth; x++)
                    {
                        uint col = pInternal[inRow + x];
                        int outX0 = x * PixelScale;
                        int outX1 = outX0 + 1;

                        pOutput[outRow0 + outX0] = col;
                        pOutput[outRow0 + outX1] = col;
                        pOutput[outRow1 + outX0] = col;
                        pOutput[outRow1 + outX1] = col;
                    }
                }

                // 8. Render Liquid Glass Celestial Slider (drawn over the 360x640 frame)
                RenderSliderTrack(pOutputBytes);
                RenderDropletThumb(pOutputBytes, (float)v);
            }
        }

        // 9. Write Frame to WinUI 3 WriteableBitmap
        using (var stream = _writeableBitmap.PixelBuffer.AsStream())
        {
            stream.Position = 0;
            stream.Write(_renderBuffer, 0, BufferByteLength);
        }

        _writeableBitmap.Invalidate();
    }

    #endregion

    #region Palette Definition & Color Interpolation

    private static uint[] GetInterpolatedWaterPalette(double v)
    {
        // 5 Stops: 0% Day, 25% Afternoon, 50% Twilight, 75% Dusk, 100% Night
        uint[][] stops = new uint[][]
        {
            // Day: Emerald sunlit waters
            new uint[] { PackBgra(16, 54, 44), PackBgra(24, 76, 60), PackBgra(36, 110, 86), PackBgra(54, 150, 116), PackBgra(90, 198, 156), PackBgra(160, 245, 210) },
            // Afternoon: Warm golden water
            new uint[] { PackBgra(20, 52, 40), PackBgra(32, 78, 56), PackBgra(52, 116, 78), PackBgra(80, 155, 98), PackBgra(130, 195, 120), PackBgra(210, 240, 160) },
            // Twilight: Sunset rose-violet waters
            new uint[] { PackBgra(28, 22, 44), PackBgra(48, 32, 62), PackBgra(82, 48, 76), PackBgra(128, 70, 84), PackBgra(185, 108, 92), PackBgra(245, 175, 125) },
            // Dusk: Indigo coral waters
            new uint[] { PackBgra(14, 18, 34), PackBgra(22, 30, 52), PackBgra(38, 50, 80), PackBgra(62, 78, 112), PackBgra(105, 118, 150), PackBgra(175, 185, 210) },
            // Night: Obsidian midnight waters
            new uint[] { PackBgra(6, 10, 18), PackBgra(10, 18, 30), PackBgra(18, 30, 48), PackBgra(30, 48, 72), PackBgra(54, 82, 116), PackBgra(130, 175, 225) }
        };

        return InterpolatePaletteArray(stops, v, 6);
    }

    private static uint[] GetInterpolatedWoodPalette(double v)
    {
        uint[][] stops = new uint[][]
        {
            // Day: Sunlit pine wood
            new uint[] { PackBgra(62, 38, 22), PackBgra(108, 68, 38), PackBgra(152, 102, 60), PackBgra(195, 145, 95), PackBgra(230, 188, 138) },
            // Afternoon: Warm oak
            new uint[] { PackBgra(58, 34, 18), PackBgra(102, 62, 32), PackBgra(146, 95, 52), PackBgra(188, 136, 82), PackBgra(225, 178, 122) },
            // Twilight: Warm teak
            new uint[] { PackBgra(48, 24, 16), PackBgra(85, 42, 25), PackBgra(125, 65, 38), PackBgra(165, 95, 55), PackBgra(205, 130, 80) },
            // Dusk: Dark cedar
            new uint[] { PackBgra(32, 20, 22), PackBgra(56, 34, 34), PackBgra(82, 52, 50), PackBgra(115, 76, 70), PackBgra(150, 108, 98) },
            // Night: Moonlit wood silhouette
            new uint[] { PackBgra(12, 16, 24), PackBgra(24, 32, 44), PackBgra(38, 50, 66), PackBgra(58, 74, 95), PackBgra(88, 110, 138) }
        };

        return InterpolatePaletteArray(stops, v, 5);
    }

    private static uint[] GetInterpolatedGullPalette(double v)
    {
        uint[][] stops = new uint[][]
        {
            // Day: Bright white gull with golden beak
            new uint[] { PackBgra(248, 250, 252), PackBgra(165, 175, 188), PackBgra(35, 40, 48), PackBgra(255, 185, 15), PackBgra(240, 130, 10) },
            // Afternoon: Warm sunlight on feathers
            new uint[] { PackBgra(245, 242, 235), PackBgra(160, 165, 170), PackBgra(35, 38, 42), PackBgra(250, 175, 15), PackBgra(235, 120, 10) },
            // Twilight: Sunset glow
            new uint[] { PackBgra(235, 218, 210), PackBgra(145, 118, 128), PackBgra(38, 28, 35), PackBgra(245, 140, 15), PackBgra(215, 90, 15) },
            // Dusk: Cool twilight feathers
            new uint[] { PackBgra(195, 190, 205), PackBgra(115, 105, 125), PackBgra(30, 25, 35), PackBgra(210, 125, 20), PackBgra(180, 80, 20) },
            // Night: Moonlit silver crest & cool grey body
            new uint[] { PackBgra(160, 180, 205), PackBgra(78, 94, 116), PackBgra(18, 24, 34), PackBgra(180, 120, 25), PackBgra(140, 75, 20) }
        };

        return InterpolatePaletteArray(stops, v, 5);
    }

    private static uint[] InterpolatePaletteArray(uint[][] stops, double v, int count)
    {
        v = Math.Clamp(v, 0.0, 1.0);
        int seg = Math.Min(3, (int)(v * 4.0));
        float tSeg = (float)((v - (seg * 0.25)) / 0.25);

        uint[] result = new uint[count];
        for (int i = 0; i < count; i++)
        {
            result[i] = LerpBgra(stops[seg][i], stops[seg + 1][i], tSeg);
        }
        return result;
    }

    #endregion

    #region 3D Boat Hull Rasterizer & Handcrafted Seagull

    private void RasterizeBoatMesh(
        Vector3 camPos, Vector3 camFwd, Vector3 camRight, Vector3 camUp,
        float focal, float halfW, float halfH,
        float boatElevationY, float totalSec, bool reducedMotion, bool ambient,
        uint[] woodPal, uint[] waterPal, uint[] gullPal)
    {
        // Parameterized 3D rowboat geometry:
        // u in [0, 1]: 0 = bow (prow at top-left), 1 = stern (at bottom-right)
        // side in [-1, 1]: -1 = port, +1 = starboard
        // tier in [0, 4]: 0 = keel, 4 = gunwale rail
        const int uSteps = 16;
        const int plankTiers = 4;

        Vector3 GetHullPt(float u, float side, int tier)
        {
            float xc = 4.0f + (u * 30.0f) + ((float)Math.Sin(u * 1.4f) * 6.0f);
            float zc = 52.0f - (u * 58.0f);

            float du = 0.01f;
            float xc2 = 4.0f + ((u + du) * 30.0f) + ((float)Math.Sin((u + du) * 1.4f) * 6.0f);
            float zc2 = 52.0f - ((u + du) * 58.0f);
            float tx = xc2 - xc;
            float tz = zc2 - zc;
            float tl = (float)Math.Sqrt((tx * tx) + (tz * tz));
            tx /= tl;
            tz /= tl;
            float nx = -tz;
            float nz = tx;

            float beam = 18.0f * (float)Math.Pow(Math.Sin(u * Math.PI * 0.74), 0.72);
            float yKeel = boatElevationY - (11.5f * (1.0f - (0.28f * (1.0f - u) * (1.0f - u))));
            float yGunwale = boatElevationY + (3.2f * (1.0f - u));

            float tFrac = tier / 4.0f;
            float y = yKeel + ((yGunwale - yKeel) * (float)Math.Pow(tFrac, 1.45));
            float x = xc + (side * beam * tFrac * nx);
            float z = zc + (side * beam * tFrac * nz);
            return new Vector3(x, y, z);
        }

        // Draw Wooden Hull Planks
        for (int i = 0; i < uSteps; i++)
        {
            float u0 = (float)i / uSteps;
            float u1 = (float)(i + 1) / uSteps;

            for (int s = 0; s < 2; s++)
            {
                float side = s == 0 ? -1.0f : 1.0f;

                for (int tier = 0; tier < plankTiers; tier++)
                {
                    Vector3 p00 = GetHullPt(u0, side, tier);
                    Vector3 p10 = GetHullPt(u1, side, tier);
                    Vector3 p01 = GetHullPt(u0, side, tier + 1);
                    Vector3 p11 = GetHullPt(u1, side, tier + 1);

                    uint col;
                    if (tier == plankTiers - 1)
                    {
                        col = side > 0 ? woodPal[4] : woodPal[3]; // Gunwale rim
                    }
                    else if (tier == 2)
                    {
                        col = side > 0 ? woodPal[3] : woodPal[2];
                    }
                    else if (tier == 1)
                    {
                        col = side > 0 ? woodPal[2] : woodPal[1];
                    }
                    else
                    {
                        col = side > 0 ? woodPal[1] : woodPal[0];
                    }

                    RasterizeTriangle(p00, p10, p01, col, waterPal[4], camPos, camFwd, camRight, camUp, focal, halfW, halfH);
                    RasterizeTriangle(p10, p11, p01, col, waterPal[4], camPos, camFwd, camRight, camUp, focal, halfW, halfH);
                }
            }
        }

        // Draw Cross Thwarts (Wooden Benches) & Interior Ribs
        float[] seatPositions = new float[] { 0.32f, 0.62f };
        for (int sIdx = 0; sIdx < seatPositions.Length; sIdx++)
        {
            float seatU = seatPositions[sIdx];
            Vector3 pL1 = GetHullPt(seatU, -0.86f, 3);
            Vector3 pR1 = GetHullPt(seatU, 0.86f, 3);
            Vector3 pL2 = GetHullPt(seatU + 0.07f, -0.86f, 3);
            Vector3 pR2 = GetHullPt(seatU + 0.07f, 0.86f, 3);
            uint seatCol = woodPal[3];

            RasterizeTriangle(pL1, pR1, pL2, seatCol, waterPal[4], camPos, camFwd, camRight, camUp, focal, halfW, halfH);
            RasterizeTriangle(pR1, pR2, pL2, seatCol, waterPal[4], camPos, camFwd, camRight, camUp, focal, halfW, halfH);
        }

        // Prow Stem Post (crisp wooden bow stem)
        Vector3 prowBase = GetHullPt(0.0f, 0.0f, 0);
        Vector3 prowTop = GetHullPt(0.0f, 0.0f, 4) + new Vector3(0, 1.8f, 0);
        Vector3 prowStemR = GetHullPt(0.02f, 0.2f, 4);
        Vector3 prowStemL = GetHullPt(0.02f, -0.2f, 4);
        RasterizeTriangle(prowBase, prowTop, prowStemR, woodPal[4], waterPal[4], camPos, camFwd, camRight, camUp, focal, halfW, halfH);
        RasterizeTriangle(prowBase, prowStemL, prowTop, woodPal[3], waterPal[4], camPos, camFwd, camRight, camUp, focal, halfW, halfH);

        // Handcrafted Retro Pixel-Art Seagull perched on top of the prow stem
        DrawPixelArtSeagull(prowTop, camPos, camFwd, camRight, camUp, focal, halfW, halfH, gullPal);
    }

    private void RasterizeTriangle(
        Vector3 p0, Vector3 p1, Vector3 p2, uint col, uint foamCol,
        Vector3 camPos, Vector3 camFwd, Vector3 camRight, Vector3 camUp,
        float focal, float halfW, float halfH)
    {
        if (!ProjectVertexInternal(p0, camPos, camFwd, camRight, camUp, focal, halfW, halfH, out float sx0, out float sy0, out float sz0)) return;
        if (!ProjectVertexInternal(p1, camPos, camFwd, camRight, camUp, focal, halfW, halfH, out float sx1, out float sy1, out float sz1)) return;
        if (!ProjectVertexInternal(p2, camPos, camFwd, camRight, camUp, focal, halfW, halfH, out float sx2, out float sy2, out float sz2)) return;

        int minX = Math.Clamp((int)Math.Floor(Math.Min(sx0, Math.Min(sx1, sx2))), 0, InternalWidth - 1);
        int maxX = Math.Clamp((int)Math.Ceiling(Math.Max(sx0, Math.Max(sx1, sx2))), 0, InternalWidth - 1);
        int minY = Math.Clamp((int)Math.Floor(Math.Min(sy0, Math.Min(sy1, sy2))), 0, InternalHeight - 1);
        int maxY = Math.Clamp((int)Math.Ceiling(Math.Max(sy0, Math.Max(sy1, sy2))), 0, InternalHeight - 1);

        float area = ((sx1 - sx0) * (sy2 - sy0)) - ((sy1 - sy0) * (sx2 - sx0));
        if (Math.Abs(area) < 0.0001f) return;
        float invArea = 1.0f / area;

        for (int y = minY; y <= maxY; y++)
        {
            int rowOffset = y * InternalWidth;

            for (int x = minX; x <= maxX; x++)
            {
                float w0 = (((sx1 - x) * (sy2 - y)) - ((sy1 - y) * (sx2 - x))) * invArea;
                float w1 = (((sx2 - x) * (sy0 - y)) - ((sy2 - y) * (sx0 - x))) * invArea;
                float w2 = 1.0f - w0 - w1;

                if (w0 >= 0f && w1 >= 0f && w2 >= 0f)
                {
                    float z = (w0 * sz0) + (w1 * sz1) + (w2 * sz2);
                    int pixelIdx = rowOffset + x;

                    if (z < _internalDepthBuffer[pixelIdx])
                    {
                        float worldY = (w0 * p0.Y) + (w1 * p1.Y) + (w2 * p2.Y);

                        if (worldY >= -0.2f)
                        {
                            // Above water surface: crisp retro wooden hull
                            _internalDepthBuffer[pixelIdx] = z;
                            _internalBoatMask[pixelIdx] = true;

                            // Contact foam highlight right at the waterline
                            if (worldY < 0.6f)
                            {
                                _internalBuffer[pixelIdx] = foamCol;
                            }
                            else
                            {
                                _internalBuffer[pixelIdx] = col;
                            }
                        }
                        else
                        {
                            // Submerged in water: record depth for water absorption tinting
                            _internalDepthBuffer[pixelIdx] = z;
                            float decay = (float)Math.Exp(worldY * 0.22f);
                            int b = (int)((col & 0xFF) * decay * 0.55f);
                            int g = (int)(((col >> 8) & 0xFF) * decay * 0.55f);
                            int r = (int)(((col >> 16) & 0xFF) * decay * 0.55f);
                            _internalBuffer[pixelIdx] = PackBgra(r, g, b);
                        }
                    }
                }
            }
        }
    }

    private void DrawPixelArtSeagull(
        Vector3 prowTop, Vector3 camPos, Vector3 camFwd, Vector3 camRight, Vector3 camUp,
        float focal, float halfW, float halfH, uint[] gullPal)
    {
        if (!ProjectVertexInternal(prowTop, camPos, camFwd, camRight, camUp, focal, halfW, halfH, out float sx, out float sy, out float sz)) return;

        int gx = (int)Math.Round(sx);
        int gy = (int)Math.Round(sy);

        // 10x10 Handcrafted Retro Pixel-Art Seagull Sprite
        // Unmistakably recognizable: sharp yellow beak, dark eye, white crown & chest, slate wings, dark wingtips, legs
        string[] gullArt = new string[]
        {
            "....YY....",  // 0: yellow beak
            "..BKKYY...",  // 1: head with dark eye and beak base
            ".BBBB.....",  // 2: white crown & throat
            "BBBB......",  // 3: white chest
            "BBWWWD....",  // 4: chest & upper wing
            "BBWWDD....",  // 5: body & mid wing
            ".BWWDD....",  // 6: lower body
            "..BWD.....",  // 7: white tail & dark tip
            "...B......",  // 8: rump
            "..LL......"   // 9: orange legs perched securely on prow
        };

        int gh = gullArt.Length;
        int gw = gullArt[0].Length;
        uint eyeCol = PackBgra(16, 20, 26);

        for (int dy = 0; dy < gh; dy++)
        {
            string rowStr = gullArt[dy];
            int py = gy - gh + dy;
            if (py < 0 || py >= InternalHeight) continue;
            int rowOffset = py * InternalWidth;

            for (int dx = 0; dx < gw; dx++)
            {
                char ch = rowStr[dx];
                if (ch == '.') continue;

                int px = gx - 4 + dx;
                if (px < 0 || px >= InternalWidth) continue;

                int pixelIdx = rowOffset + px;
                if (sz < _internalDepthBuffer[pixelIdx])
                {
                    _internalDepthBuffer[pixelIdx] = sz;
                    _internalBoatMask[pixelIdx] = true;

                    if (ch == 'B') _internalBuffer[pixelIdx] = gullPal[0];       // White body/head
                    else if (ch == 'W') _internalBuffer[pixelIdx] = gullPal[1];  // Slate wing
                    else if (ch == 'D') _internalBuffer[pixelIdx] = gullPal[2];  // Dark wingtip
                    else if (ch == 'Y') _internalBuffer[pixelIdx] = gullPal[3];  // Bright yellow beak
                    else if (ch == 'K') _internalBuffer[pixelIdx] = eyeCol;      // Dark eye
                    else if (ch == 'L') _internalBuffer[pixelIdx] = gullPal[4];  // Legs
                }
            }
        }
    }

    private static bool ProjectVertexInternal(
        Vector3 pt, Vector3 camPos, Vector3 camFwd, Vector3 camRight, Vector3 camUp,
        float focal, float halfW, float halfH, out float sx, out float sy, out float sz)
    {
        Vector3 rel = pt - camPos;
        float cz = Vector3.Dot(rel, camFwd);
        if (cz <= 1.0f)
        {
            sx = sy = sz = 0;
            return false;
        }

        float cx = Vector3.Dot(rel, camRight);
        float cy = Vector3.Dot(rel, camUp);

        sx = ((cx / cz) * focal) + halfW;
        sy = ((-cy / cz) * focal) + halfH;
        sz = cz;
        return true;
    }

    #endregion

    #region Pixel-Art Water Shading & Concentric Ripple Physics

    private void ShadeWaterSurface(
        Vector3 camPos, Vector3 camFwd, Vector3 camRight, Vector3 camUp,
        float focal, float halfW, float halfH,
        float totalSec, bool reducedMotion, bool ambient, double themeVal, uint[] waterPal)
    {
        int numWater = waterPal.Length;
        const float boatCenterX = 20.0f;
        const float boatCenterZ = 24.0f;

        for (int y = 0; y < InternalHeight; y++)
        {
            float screenY = halfH - y;
            int bayerRow = (y & 7) << 3;
            int rowOffset = y * InternalWidth;

            for (int x = 0; x < InternalWidth; x++)
            {
                int pixelIdx = rowOffset + x;
                if (_internalBoatMask[pixelIdx])
                {
                    continue;
                }

                float screenX = x - halfW;
                Vector3 rayDir = ((camFwd * focal) + (camRight * screenX) + (camUp * screenY)).Normalized();

                if (rayDir.Y >= -0.0001f)
                {
                    _internalBuffer[pixelIdx] = waterPal[0];
                    continue;
                }

                float tWater = -camPos.Y / rayDir.Y;
                float wx = camPos.X + (tWater * rayDir.X);
                float wz = camPos.Z + (tWater * rayDir.Z);

                // Concentric circular harmonic ripples from boat
                float dx = wx - boatCenterX;
                float dz = wz - boatCenterZ;
                float dist = (float)Math.Sqrt((dx * dx) + (dz * dz));

                float w1 = 0f;
                float w2 = 0f;
                if (ambient && !reducedMotion)
                {
                    w1 = (float)Math.Sin((dist * 0.40f) - (totalSec * 2.5f));
                    w2 = (float)Math.Cos((dist * 0.20f) - (totalSec * 1.5f)) * 0.4f;
                }

                // Add active mouse interactive ripples
                float mouseRipple = 0f;
                if (!reducedMotion)
                {
                    for (int r = 0; r < _ripples.Length; r++)
                    {
                        if (!_ripples[r].Active) continue;

                        float age = totalSec - _ripples[r].StartTime;
                        float waveDist = age * 65.0f;
                        float rdx = wx - _ripples[r].WorldX;
                        float rdz = wz - _ripples[r].WorldZ;
                        float rdist = (float)Math.Sqrt((rdx * rdx) + (rdz * rdz));

                        if (rdist > 0.5f && rdist <= waveDist + 22.0f && rdist >= waveDist - 22.0f)
                        {
                            float decay = (float)Math.Exp(-age * 1.15f);
                            float phase = (rdist - waveDist) * 0.32f;
                            mouseRipple += (float)Math.Sin(phase) * _ripples[r].Amplitude * decay;
                        }
                    }
                }

                float wave = (w1 + w2 + mouseRipple + 1.4f) / 2.8f;

                // Moonlight shimmer column at Night (crisp crest highlights, zero diagonal stripes)
                if (themeVal > 0.55)
                {
                    float colDist = Math.Abs(wx - boatCenterX);
                    if (colDist < 16.0f)
                    {
                        float moonFactor = (1.0f - (colDist / 16.0f)) * (float)((themeVal - 0.55) / 0.45);
                        if (w1 > 0.4f)
                        {
                            float crestGlint = (float)Math.Pow((w1 - 0.4f) / 0.6f, 2.0);
                            wave += crestGlint * moonFactor * 0.75f;
                        }
                    }
                }

                // Bayer 8x8 Ordered Dithering
                float dither = Bayer8x8[bayerRow | (x & 7)] * 0.35f;
                float shadeVal = wave + dither;

                // Discrete Palette Quantization (Authentic retro pixel art!)
                int band = (int)Math.Floor((shadeVal * (numWater - 1)) + 0.5f);
                band = Math.Clamp(band, 0, numWater - 1);

                // Submerged hull optical depth absorption
                float existingD = _internalDepthBuffer[pixelIdx];
                if (existingD < tWater + 12.0f && existingD > 1.0f)
                {
                    float depthFrac = Math.Clamp((tWater - existingD) * 0.22f, 0.0f, 1.0f);
                    uint baseC = waterPal[band];
                    uint hullC = _internalBuffer[pixelIdx];
                    _internalBuffer[pixelIdx] = LerpBgra(hullC, baseC, depthFrac);
                }
                else
                {
                    _internalBuffer[pixelIdx] = waterPal[band];
                }
            }
        }
    }

    #endregion

    #region Liquid Glass Celestial Droplet Slider

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

    private unsafe void RenderDropletThumb(byte* pRender, float v)
    {
        int thumbY = SliderBottom - (int)(v * SliderTravel);
        thumbY = Math.Clamp(thumbY, SliderTop, SliderBottom);

        bool isInteracting = _isDraggingSlider || _isPointerOverSlider;
        float bulbR = isInteracting ? 9.5f : 8.5f;
        float hTop = isInteracting ? 18.0f : 16.0f;
        float hBot = isInteracting ? 11.5f : 10.5f;

        int minX = SliderCenterX - 14;
        int maxX = SliderCenterX + 14;
        int minY = thumbY - (int)hTop - 2;
        int maxY = thumbY + (int)hBot + 4;

        for (int y = minY; y <= maxY; y++)
        {
            if (y < 0 || y >= CanvasHeight) continue;
            int rowOffset = y * CanvasWidth * 4;

            for (int x = minX; x <= maxX; x++)
            {
                if (x < 0 || x >= CanvasWidth) continue;

                float dx = x - SliderCenterX;
                float dy = y - thumbY;

                bool inside = false;
                float normDist = 0f;

                if (dy >= 0f)
                {
                    // Bottom rounded bulb
                    float dSq = (dx * dx) + (dy * dy);
                    if (dSq <= bulbR * bulbR)
                    {
                        inside = true;
                        normDist = (float)Math.Sqrt(dSq) / bulbR;
                    }
                }
                else
                {
                    // Tapered droplet apex
                    float t = -dy / hTop;
                    if (t <= 1.0f)
                    {
                        float maxDx = bulbR * (1.0f - (float)Math.Pow(t, 1.3));
                        if (Math.Abs(dx) <= maxDx)
                        {
                            inside = true;
                            normDist = Math.Abs(dx) / (maxDx + 0.001f);
                        }
                    }
                }

                int offset = rowOffset + (x * 4);

                // Elevation drop shadow beneath and to right
                if (!inside && dy > 1f)
                {
                    float shadowDx = dx - 1.5f;
                    float shadowDy = dy - 1.5f;
                    float sDist = (float)Math.Sqrt((shadowDx * shadowDx) + (shadowDy * shadowDy));
                    if (sDist <= bulbR + 3.0f)
                    {
                        float sFalloff = (1.0f - (sDist / (bulbR + 3.0f))) * 0.45f;
                        pRender[offset + 0] = (byte)(pRender[offset + 0] * (1.0f - sFalloff));
                        pRender[offset + 1] = (byte)(pRender[offset + 1] * (1.0f - sFalloff));
                        pRender[offset + 2] = (byte)(pRender[offset + 2] * (1.0f - sFalloff));
                    }
                    continue;
                }

                if (inside)
                {
                    // Refractive liquid glass droplet shading
                    float rim = Math.Clamp((normDist - 0.65f) / 0.35f, 0.0f, 1.0f);

                    // Specular glint on upper-left curve
                    float glintD = (float)Math.Sqrt(((dx - (-3.0f)) * (dx - (-3.0f))) + ((dy - (-3.0f)) * (dy - (-3.0f))));
                    float glint = (float)Math.Pow(Math.Clamp(1.0f - (glintD / 2.6f), 0.0f, 1.0f), 2.0);

                    // Crystal core with ambient refraction
                    int dropB = Math.Clamp((int)(240 + (rim * 15) + (glint * 15)), 0, 255);
                    int dropG = Math.Clamp((int)(245 + (rim * 10) + (glint * 10)), 0, 255);
                    int dropR = 255;

                    pRender[offset + 0] = (byte)dropB;
                    pRender[offset + 1] = (byte)dropG;
                    pRender[offset + 2] = (byte)dropR;
                    pRender[offset + 3] = 255;
                }
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
        int internalX = Math.Clamp((int)(_cursorX / PixelScale), 0, InternalWidth - 1);
        int internalY = Math.Clamp((int)(_cursorY / PixelScale), 0, InternalHeight - 1);
        int idx = (internalY * InternalWidth) + internalX;

        bool isOverBoat = _internalBoatMask[idx];
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

        int internalX = Math.Clamp((int)(px / PixelScale), 0, InternalWidth - 1);
        int internalY = Math.Clamp((int)(py / PixelScale), 0, InternalHeight - 1);
        if (!_internalBoatMask[(internalY * InternalWidth) + internalX])
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
            case VirtualKey.PageUp:
                ThemeValue = Math.Clamp(_themeValue + 0.20, 0.0, 1.0);
                e.Handled = true;
                break;
            case VirtualKey.PageDown:
                ThemeValue = Math.Clamp(_themeValue - 0.20, 0.0, 1.0);
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

    private void UpdateSliderFromPointerY(float py)
    {
        float clampedY = Math.Clamp(py, SliderTop, SliderBottom);
        double newVal = (double)(SliderBottom - clampedY) / SliderTravel;
        ThemeValue = Math.Clamp(newVal, 0.0, 1.0);
    }

    private void SpawnRippleAtCursor(float sx, float sy, float amp)
    {
        float inX = sx / PixelScale;
        float inY = sy / PixelScale;

        float yawRad = _currentYaw * ((float)Math.PI / 180.0f);
        float pitchRad = _currentPitch * ((float)Math.PI / 180.0f);

        Vector3 camTarget = new(20.0f, -1.0f, 24.0f);
        float camDist = 118.0f;
        float cosP = (float)Math.Cos(pitchRad);
        float sinP = (float)Math.Sin(pitchRad);
        float cosY = (float)Math.Cos(yawRad);
        float sinY = (float)Math.Sin(yawRad);

        Vector3 camPos = camTarget + new Vector3(sinY * cosP * camDist, sinP * camDist, -cosY * cosP * camDist);
        Vector3 camFwd = (camTarget - camPos).Normalized();
        Vector3 camRight = Vector3.Cross(new Vector3(0, 1, 0), camFwd).Normalized();
        Vector3 camUp = Vector3.Cross(camFwd, camRight).Normalized();

        float focal = 192.0f;
        float halfW = InternalWidth * 0.5f;
        float halfH = InternalHeight * 0.5f;

        Vector3 rayDir = ((camFwd * focal) + (camRight * (inX - halfW)) + (camUp * (halfH - inY))).Normalized();
        if (rayDir.Y >= -0.001f) return;

        float t = -camPos.Y / rayDir.Y;
        float worldX = camPos.X + (t * rayDir.X);
        float worldZ = camPos.Z + (t * rayDir.Z);

        _ripples[_rippleHeadIndex] = new RippleWave
        {
            WorldX = worldX,
            WorldZ = worldZ,
            StartTime = (float)_stopwatch.Elapsed.TotalSeconds,
            Amplitude = amp,
            Active = true
        };
        _rippleHeadIndex = (_rippleHeadIndex + 1) % _ripples.Length;
    }

    #endregion
}
