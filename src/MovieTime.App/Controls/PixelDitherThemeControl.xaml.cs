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
/// Real-time interactive 3D water and boat theme controller for Movie Time.
/// Features a true perspective 3D camera with mouse orbit/tilt, dynamic 3D water surface,
/// volumetric 3D wooden rowboat with a 100-stage continuous sinking animation,
/// authentic 8x8 Bayer ordered dithering without diagonal hatching, and an elegant liquid glass droplet thumb.
/// </summary>
public sealed partial class PixelDitherThemeControl : UserControl
{
    private const int CanvasWidth = 360;
    private const int CanvasHeight = 640;
    private const int PixelCount = CanvasWidth * CanvasHeight;
    private const int BufferByteLength = PixelCount * 4;

    // Celestial Slider Coordinate System (within 360x640 canvas)
    private const int SliderLeft = 18;
    private const int SliderRight = 56;
    private const int SliderTop = 158;      // 100% (Night / Moon zenith)
    private const int SliderBottom = 444;   // 0%   (Day / Sun nadir)
    private const int SliderCenterX = 37;
    private const int SliderTravel = SliderBottom - SliderTop; // 286 px

    // 8x8 Bayer ordered dithering matrix normalized to [-0.5, 0.5] * 28.0f
    private static readonly float[] Bayer8x8 = new float[64]
    {
         0f, 32f,  8f, 40f,  2f, 34f, 10f, 42f,
        48f, 16f, 56f, 24f, 50f, 18f, 58f, 26f,
        12f, 44f,  4f, 36f, 14f, 46f,  6f, 38f,
        60f, 28f, 52f, 20f, 62f, 30f, 54f, 22f,
         3f, 35f, 11f, 43f,  1f, 33f,  9f, 41f,
        51f, 19f, 59f, 27f, 49f, 17f, 57f, 25f,
        15f, 47f,  7f, 39f, 13f, 45f,  5f, 37f,
        63f, 31f, 55f, 23f, 61f, 29f, 53f, 21f
    };

    private readonly IThemeManagerService? _themeManager;
    private readonly ILogger<PixelDitherThemeControl> _logger;

    private WriteableBitmap? _writeableBitmap;
    private readonly byte[] _renderBuffer = new byte[BufferByteLength];
    private readonly float[] _depthBuffer = new float[PixelCount];
    private readonly bool[] _boatMask = new bool[PixelCount];

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
    private float _targetPitch = 46.0f;
    private float _targetYaw = -24.0f;
    private float _currentPitch = 46.0f;
    private float _currentYaw = -24.0f;

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

        // 1. Smooth 3D Camera Orbit & Tilt
        if (_isCursorInside && !reducedMotion)
        {
            float normX = (_cursorX / CanvasWidth) - 0.5f;
            float normY = (_cursorY / CanvasHeight) - 0.5f;
            _targetYaw = -24.0f + (normX * 22.0f);
            _targetPitch = 46.0f + (normY * 16.0f);
        }
        else
        {
            _targetYaw = -24.0f;
            _targetPitch = 46.0f;
        }

        float smoothFactor = (float)Math.Min(1.0, dt * 7.0f);
        _currentYaw += (_targetYaw - _currentYaw) * smoothFactor;
        _currentPitch += (_targetPitch - _currentPitch) * smoothFactor;

        // 2. Age out inactive ripples
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

    #region Real-Time 3D Rendering Engine

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

    private void RenderScene(float totalSec)
    {
        if (_writeableBitmap == null) return;

        double v = _themeValue;
        bool reducedMotion = _themeManager?.ReducedMotion ?? false;
        bool ambient = _themeManager?.AmbientAnimationEnabled ?? true;

        // 1. Initialize Depth Buffer and Clear Render Buffers
        Array.Fill(_depthBuffer, 1e8f);
        Array.Fill(_boatMask, false);

        // 2. Setup 3D Perspective Camera
        float yawRad = _currentYaw * ((float)Math.PI / 180.0f);
        float pitchRad = _currentPitch * ((float)Math.PI / 180.0f);

        Vector3 camTarget = new(32.0f, -2.0f, 36.0f);
        float camDist = 162.0f;

        float cosP = (float)Math.Cos(pitchRad);
        float sinP = (float)Math.Sin(pitchRad);
        float cosY = (float)Math.Cos(yawRad);
        float sinY = (float)Math.Sin(yawRad);

        Vector3 camPos = camTarget + new Vector3(sinY * cosP * camDist, sinP * camDist, -cosY * cosP * camDist);
        Vector3 camFwd = (camTarget - camPos).Normalized();
        Vector3 camRight = Vector3.Cross(new Vector3(0, 1, 0), camFwd).Normalized();
        Vector3 camUp = Vector3.Cross(camFwd, camRight).Normalized();

        float focal = 385.0f;
        float halfW = CanvasWidth * 0.5f;
        float halfH = CanvasHeight * 0.5f;

        // 3. Directional Sun/Moon Lighting Vector
        // Day (v=0): sun high in south-east; Night (v=1): moonlight column
        float lightYaw = ((float)(1.0 - v) * 0.8f) - 0.4f;
        Vector3 lightDir = new Vector3((float)Math.Sin(lightYaw), 0.75f, (float)Math.Cos(lightYaw)).Normalized();

        // 4. Continuous Palette Color Definitions across 5 key stops
        // 0% (Day), 25% (Afternoon), 50% (Sunset Twilight), 75% (Dusk), 100% (Obsidian Night)
        ThemeColor waterDeepCol = ThemeColorInterpolator.Interpolate5(
            c0: ThemeColor.FromArgb(255, 18, 92, 54),       // Day Emerald Green
            c25: ThemeColor.FromArgb(255, 26, 75, 48),      // Afternoon Muted Green
            c50: ThemeColor.FromArgb(255, 140, 72, 22),     // Twilight Golden Umber
            c75: ThemeColor.FromArgb(255, 18, 42, 64),      // Dusk Deep Indigo
            c100: ThemeColor.FromArgb(255, 6, 12, 20),      // Night Obsidian Navy
            t: v);

        ThemeColor waterShallowCol = ThemeColorInterpolator.Interpolate5(
            c0: ThemeColor.FromArgb(255, 48, 148, 92),     // Bright Seafoam Highlight
            c25: ThemeColor.FromArgb(255, 54, 132, 85),
            c50: ThemeColor.FromArgb(255, 215, 145, 52),    // Warm Amber Sunset
            c75: ThemeColor.FromArgb(255, 36, 78, 110),
            c100: ThemeColor.FromArgb(255, 22, 38, 58),     // Midnight Silver-Blue
            t: v);

        ThemeColor woodCol = ThemeColorInterpolator.Interpolate5(
            c0: ThemeColor.FromArgb(255, 185, 150, 110),    // Sunlit Pine Wood
            c25: ThemeColor.FromArgb(255, 165, 132, 95),
            c50: ThemeColor.FromArgb(255, 128, 76, 34),     // Warm Teak
            c75: ThemeColor.FromArgb(255, 68, 52, 44),
            c100: ThemeColor.FromArgb(255, 24, 28, 36),     // Dark Silhouette
            t: v);

        // 5. 100-Stage Continuous Sinking Elevation ($0.0 \to 1.0$)
        // s=0: boat rides high (Y_boat = +8.0), full hull exposed
        // s=0.5: boat half submerged (Y_boat = -4.0), waterline at mid-hull
        // s=1.0: boat reaches final sinking state (Y_boat = -16.0), tip of prow and perched seagull at waterline
        float sinkProgress = (float)v;
        float boatElevationY = 8.0f - (sinkProgress * 24.0f);

        // 6. Rasterize 3D Boat Hull Geometry into Depth Buffer
        RasterizeBoatMesh(camPos, camFwd, camRight, camUp, focal, halfW, halfH, boatElevationY, totalSec, reducedMotion, ambient, lightDir, woodCol);

        // 7. Raycast & Shade 3D Dynamic Water Surface with Continuous Palette & Bayer Dithering
        unsafe
        {
            fixed (byte* pRender = _renderBuffer)
            fixed (float* pDepth = _depthBuffer)
            fixed (bool* pMask = _boatMask)
            {
                for (int y = 0; y < CanvasHeight; y++)
                {
                    int rowOffset = y * CanvasWidth * 4;
                    float screenY = halfH - y;
                    int bayerRow = (y & 7) << 3;

                    for (int x = 0; x < CanvasWidth; x++)
                    {
                        int pixelIdx = (y * CanvasWidth) + x;
                        int pixelOffset = rowOffset + (x * 4);

                        // Reserved vertical channel for celestial slider track
                        bool inSliderChannel = (x >= SliderLeft && x <= SliderRight && y >= SliderTop - 25 && y <= SliderBottom + 25);

                        // If boat geometry already covered this pixel and is ABOVE the water, keep boat shading
                        if (pMask[pixelIdx])
                        {
                            continue;
                        }

                        // Generate 3D ray for this water pixel
                        float screenX = x - halfW;
                        Vector3 rayDir = ((camFwd * focal) + (camRight * screenX) + (camUp * screenY)).Normalized();

                        // Intersect ray with 3D water plane (Y = 0)
                        if (rayDir.Y >= -0.0001f)
                        {
                            // Sky / Horizon gradient
                            pRender[pixelOffset + 0] = waterDeepCol.B;
                            pRender[pixelOffset + 1] = waterDeepCol.G;
                            pRender[pixelOffset + 2] = waterDeepCol.R;
                            pRender[pixelOffset + 3] = 255;
                            continue;
                        }

                        float tWater = -camPos.Y / rayDir.Y;
                        float worldX = camPos.X + (tWater * rayDir.X);
                        float worldZ = camPos.Z + (tWater * rayDir.Z);

                        // Evaluate 3D wave heightfield and surface normal
                        EvaluateWater(worldX, worldZ, totalSec, reducedMotion, ambient, out float waveH, out Vector3 waterNormal);

                        // 3D Fresnel Reflection factor
                        float nDotV = Math.Max(0.0f, -Vector3.Dot(rayDir, waterNormal));
                        float fresnel = 0.12f + (0.88f * (float)Math.Pow(1.0f - nDotV, 5.0));

                        // 3D Specular Sun/Moon Reflection (AUTHENTIC 3D specular - eliminates unwanted diagonal hatching!)
                        Vector3 reflectDir = (waterNormal * (2.0f * Vector3.Dot(waterNormal, lightDir))) - lightDir;
                        float rDotV = Math.Max(0.0f, -Vector3.Dot(rayDir, reflectDir));
                        float specular = (float)Math.Pow(rDotV, 28.0) * 1.6f;

                        // Additional Moonlight shimmer column along world X near boat
                        if (v > 0.65 && !inSliderChannel)
                        {
                            float moonColDist = Math.Abs(worldX - 32.0f);
                            if (moonColDist < 28.0f)
                            {
                                float moonFactor = (1.0f - (moonColDist / 28.0f)) * (float)((v - 0.65) / 0.35);
                                float waveGlint = Math.Max(0.0f, waterNormal.X * 1.8f);
                                specular += waveGlint * moonFactor * 0.9f;
                            }
                        }

                        // Cursor proximity lighting highlight (only on water!)
                        float cursorLight = 0f;
                        if (_isCursorInside && !inSliderChannel && !reducedMotion)
                        {
                            float cdx = x - _cursorX;
                            float cdy = y - _cursorY;
                            float cdistSq = (cdx * cdx) + (cdy * cdy);
                            if (cdistSq < 1600.0f)
                            {
                                cursorLight = (1.0f - (cdistSq / 1600.0f)) * 32.0f;
                            }
                        }

                        // Diffuse lighting on water waves
                        float diffuse = Math.Max(0.0f, Vector3.Dot(waterNormal, lightDir)) * 0.45f;
                        float waveShade = (waveH * 0.18f) + diffuse;

                        // Interpolate base water color with wave shading
                        float finalR = waterDeepCol.R + ((waterShallowCol.R - waterDeepCol.R) * (fresnel + waveShade));
                        float finalG = waterDeepCol.G + ((waterShallowCol.G - waterDeepCol.G) * (fresnel + waveShade));
                        float finalB = waterDeepCol.B + ((waterShallowCol.B - waterDeepCol.B) * (fresnel + waveShade));

                        // Add specular reflection and cursor proximity illumination
                        float specBoost = specular * 180.0f;
                        finalR += specBoost + cursorLight;
                        finalG += specBoost + cursorLight;
                        finalB += specBoost + cursorLight;

                        // Check if submerged boat hull lies beneath this water pixel
                        float existingDepth = pDepth[pixelIdx];
                        if (existingDepth < tWater + 15.0f && existingDepth > 1.0f)
                        {
                            // Submerged boat hull tinted through water depth absorption
                            float depthUnder = Math.Clamp((tWater - existingDepth) * 0.25f, 0.0f, 1.0f);
                            finalR = (finalR * depthUnder) + (pRender[pixelOffset + 2] * (1.0f - depthUnder));
                            finalG = (finalG * depthUnder) + (pRender[pixelOffset + 1] * (1.0f - depthUnder));
                            finalB = (finalB * depthUnder) + (pRender[pixelOffset + 0] * (1.0f - depthUnder));
                        }

                        // 8x8 Bayer Ordered Dithering (adds micro-texture without diagonal banding)
                        float dither = Bayer8x8[bayerRow | (x & 7)] * 0.35f;
                        int outR = Math.Clamp((int)(finalR + dither), 0, 255);
                        int outG = Math.Clamp((int)(finalG + dither), 0, 255);
                        int outB = Math.Clamp((int)(finalB + dither), 0, 255);

                        pRender[pixelOffset + 0] = (byte)outB;
                        pRender[pixelOffset + 1] = (byte)outG;
                        pRender[pixelOffset + 2] = (byte)outR;
                        pRender[pixelOffset + 3] = 255;
                    }
                }

                // 8. Render Vertical Celestial Slider Track
                RenderSliderTrack(pRender);

                // 9. Render Liquid Glass Droplet Thumb (Task 2)
                RenderDropletThumb(pRender, (float)v);
            }
        }

        // Copy rendered frame to WriteableBitmap surface
        using (var stream = _writeableBitmap.PixelBuffer.AsStream())
        {
            stream.Position = 0;
            stream.Write(_renderBuffer, 0, BufferByteLength);
        }

        _writeableBitmap.Invalidate();
    }

    private void EvaluateWater(float x, float z, float totalSec, bool reducedMotion, bool ambient, out float waveH, out Vector3 normal)
    {
        waveH = 0f;
        float dHdX = 0f;
        float dHdZ = 0f;

        if (ambient && !reducedMotion)
        {
            // Center ambient harmonics around boat
            float dx = x - 32.0f;
            float dz = z - 36.0f;
            float dist = (float)Math.Sqrt((dx * dx) + (dz * dz));

            float w1 = (dist * 0.18f) - (totalSec * 2.2f);
            float cos1 = (float)Math.Cos(w1);
            float sin1 = (float)Math.Sin(w1);
            waveH += sin1 * 0.85f;

            float invD = dist > 0.001f ? 1.0f / dist : 0f;
            dHdX += cos1 * 0.85f * 0.18f * dx * invD;
            dHdZ += cos1 * 0.85f * 0.18f * dz * invD;

            float w2 = (x * 0.22f) - (totalSec * 1.4f);
            waveH += (float)Math.Sin(w2) * 0.45f;
            dHdX += (float)Math.Cos(w2) * 0.45f * 0.22f;
        }

        // Mouse interactive ripples
        if (!reducedMotion)
        {
            for (int r = 0; r < _ripples.Length; r++)
            {
                if (!_ripples[r].Active) continue;

                float age = totalSec - _ripples[r].StartTime;
                float waveDist = age * 165.0f;

                float rx = x - _ripples[r].WorldX;
                float rz = z - _ripples[r].WorldZ;
                float dist = (float)Math.Sqrt((rx * rx) + (rz * rz));

                if (dist > 0.5f && dist <= waveDist + 32.0f && dist >= waveDist - 32.0f)
                {
                    float decay = (float)Math.Exp(-age * 1.15f);
                    float phase = (dist - waveDist) * 0.28f;
                    float amp = _ripples[r].Amplitude * decay;
                    float sinW = (float)Math.Sin(phase);
                    float cosW = (float)Math.Cos(phase);

                    waveH += sinW * amp;
                    float invD = 1.0f / dist;
                    dHdX += cosW * amp * 0.28f * rx * invD;
                    dHdZ += cosW * amp * 0.28f * rz * invD;
                }
            }
        }

        normal = new Vector3(-dHdX, 1.0f, -dHdZ).Normalized();
    }

    #endregion

    #region 3D Boat Hull Rasterizer & Waterline Sinking Physics

    private void RasterizeBoatMesh(
        Vector3 camPos, Vector3 camFwd, Vector3 camRight, Vector3 camUp,
        float focal, float halfW, float halfH,
        float boatElevationY, float totalSec, bool reducedMotion, bool ambient,
        Vector3 lightDir, ThemeColor woodCol)
    {
        // Parameterized 3D rowboat geometry:
        // u in [0, 1] along centerline from bow to stern (16 segments)
        // v in [-1, 1] across cross-section from port to starboard (8 segments)
        const int uSteps = 16;
        const int vSteps = 8;

        for (int i = 0; i < uSteps; i++)
        {
            float u0 = (float)i / uSteps;
            float u1 = (float)(i + 1) / uSteps;

            for (int j = 0; j < vSteps; j++)
            {
                float v0 = ((float)j / vSteps * 2.0f) - 1.0f;
                float v1 = ((float)(j + 1) / vSteps * 2.0f) - 1.0f;

                Vector3 p00 = GetBoatHullPoint(u0, v0, boatElevationY);
                Vector3 p10 = GetBoatHullPoint(u1, v0, boatElevationY);
                Vector3 p01 = GetBoatHullPoint(u0, v1, boatElevationY);
                Vector3 p11 = GetBoatHullPoint(u1, v1, boatElevationY);

                // Compute hull surface normal
                Vector3 norm = Vector3.Cross(p10 - p00, p01 - p00).Normalized();

                // Rasterize 2 triangles making up quad
                Draw3DTriangle(p00, p10, p01, norm, camPos, camFwd, camRight, camUp, focal, halfW, halfH,
                               totalSec, reducedMotion, ambient, lightDir, woodCol, u0, false);
                Draw3DTriangle(p10, p11, p01, norm, camPos, camFwd, camRight, camUp, focal, halfW, halfH,
                               totalSec, reducedMotion, ambient, lightDir, woodCol, u1, false);
            }
        }

        // Render Seagull perched on top of the bow post (u = 0, Y = prow tip)
        Vector3 prowTip = GetBoatHullPoint(0.0f, 0.0f, boatElevationY) + new Vector3(0, 6.0f, 0);
        DrawSeagull(prowTip, camPos, camFwd, camRight, camUp, focal, halfW, halfH, lightDir);
    }

    private static Vector3 GetBoatHullPoint(float u, float v, float boatElevationY)
    {
        // Centerline curve
        float xc = 15.0f + (u * 55.0f) + ((float)Math.Sin(u * 1.6f) * 8.0f);
        float zc = -5.0f + (u * 98.0f);

        // Centerline tangent and normal in XZ plane
        float du = 0.01f;
        float xc2 = 15.0f + ((u + du) * 55.0f) + ((float)Math.Sin((u + du) * 1.6f) * 8.0f);
        float zc2 = -5.0f + ((u + du) * 98.0f);
        float tanX = xc2 - xc;
        float tanZ = zc2 - zc;
        float tanLen = (float)Math.Sqrt((tanX * tanX) + (tanZ * tanZ));
        tanX /= tanLen;
        tanZ /= tanLen;
        float normX = -tanZ;
        float normZ = tanX;

        // Beam half-width
        float beam = 26.0f * (float)Math.Pow(Math.Sin(u * Math.PI * 0.72), 0.75);

        // Keel and gunwale vertical profiles
        float yKeel = boatElevationY - (16.0f * (1.0f - (0.4f * (1.0f - u) * (1.0f - u))));
        float yGunwale = boatElevationY + (4.0f * (1.0f - u) * (1.0f - u));

        float absV = Math.Abs(v);
        float px = xc + (v * beam * normX);
        float pz = zc + (v * beam * normZ);
        float py = yKeel + ((yGunwale - yKeel) * (float)Math.Pow(absV, 1.6));

        return new Vector3(px, py, pz);
    }

    private void Draw3DTriangle(
        Vector3 v0, Vector3 v1, Vector3 v2, Vector3 normal,
        Vector3 camPos, Vector3 camFwd, Vector3 camRight, Vector3 camUp,
        float focal, float halfW, float halfH,
        float totalSec, bool reducedMotion, bool ambient,
        Vector3 lightDir, ThemeColor woodCol, float uCoord, bool isSeagull)
    {
        // Project 3 vertices to screen space
        if (!ProjectVertex(v0, camPos, camFwd, camRight, camUp, focal, halfW, halfH, out float sx0, out float sy0, out float sz0)) return;
        if (!ProjectVertex(v1, camPos, camFwd, camRight, camUp, focal, halfW, halfH, out float sx1, out float sy1, out float sz1)) return;
        if (!ProjectVertex(v2, camPos, camFwd, camRight, camUp, focal, halfW, halfH, out float sx2, out float sy2, out float sz2)) return;

        // 2D bounding box
        int minX = Math.Clamp((int)Math.Floor(Math.Min(sx0, Math.Min(sx1, sx2))), 0, CanvasWidth - 1);
        int maxX = Math.Clamp((int)Math.Ceiling(Math.Max(sx0, Math.Max(sx1, sx2))), 0, CanvasWidth - 1);
        int minY = Math.Clamp((int)Math.Floor(Math.Min(sy0, Math.Min(sy1, sy2))), 0, CanvasHeight - 1);
        int maxY = Math.Clamp((int)Math.Ceiling(Math.Max(sy0, Math.Max(sy1, sy2))), 0, CanvasHeight - 1);

        float area = ((sx1 - sx0) * (sy2 - sy0)) - ((sy1 - sy0) * (sx2 - sx0));
        if (Math.Abs(area) < 0.0001f) return;
        float invArea = 1.0f / area;

        // Compute diffuse lighting on wood
        float nDotL = Math.Max(0.12f, Vector3.Dot(normal, lightDir));
        // Subtle plank lines along u
        float plankPattern = 0.92f + (0.08f * (float)Math.Cos(uCoord * 64.0f));

        unsafe
        {
            fixed (byte* pRender = _renderBuffer)
            fixed (float* pDepth = _depthBuffer)
            fixed (bool* pMask = _boatMask)
            {
                for (int y = minY; y <= maxY; y++)
                {
                    int rowOffset = y * CanvasWidth * 4;

                    for (int x = minX; x <= maxX; x++)
                    {
                        // Barycentric weights
                        float w0 = (((sx1 - x) * (sy2 - y)) - ((sy1 - y) * (sx2 - x))) * invArea;
                        float w1 = (((sx2 - x) * (sy0 - y)) - ((sy2 - y) * (sx0 - x))) * invArea;
                        float w2 = 1.0f - w0 - w1;

                        if (w0 < 0f || w1 < 0f || w2 < 0f) continue;

                        float depth = (w0 * sz0) + (w1 * sz1) + (w2 * sz2);
                        int pixelIdx = (y * CanvasWidth) + x;

                        if (depth >= pDepth[pixelIdx]) continue;
                        pDepth[pixelIdx] = depth;

                        // Interpolate world position
                        Vector3 worldPos = (v0 * w0) + (v1 * w1) + (v2 * w2);

                        // Evaluate water surface elevation at this world location
                        EvaluateWater(worldPos.X, worldPos.Z, totalSec, reducedMotion, ambient, out float waveH, out _);

                        float submergeDepth = waveH - worldPos.Y;
                        int pixelOffset = rowOffset + (x * 4);

                        if (submergeDepth <= 0.0f)
                        {
                            // ABOVE WATER: Crisp illuminated wood!
                            pMask[pixelIdx] = true;

                            float r = woodCol.R * nDotL * plankPattern;
                            float g = woodCol.G * nDotL * plankPattern;
                            float b = woodCol.B * nDotL * plankPattern;

                            // Cursor proximity varnish gleam
                            if (_isCursorInside && !reducedMotion)
                            {
                                float cdx = x - _cursorX;
                                float cdy = y - _cursorY;
                                float cdistSq = (cdx * cdx) + (cdy * cdy);
                                if (cdistSq < 900.0f)
                                {
                                    float gleam = (1.0f - (cdistSq / 900.0f)) * 36.0f;
                                    r += gleam;
                                    g += gleam;
                                    b += gleam;
                                }
                            }

                            // Waterline foam meniscus highlight right at contact boundary
                            if (submergeDepth > -1.2f)
                            {
                                r = Math.Min(255f, r + 55f);
                                g = Math.Min(255f, g + 65f);
                                b = Math.Min(255f, b + 60f);
                            }

                            pRender[pixelOffset + 0] = (byte)Math.Clamp((int)b, 0, 255);
                            pRender[pixelOffset + 1] = (byte)Math.Clamp((int)g, 0, 255);
                            pRender[pixelOffset + 2] = (byte)Math.Clamp((int)r, 0, 255);
                            pRender[pixelOffset + 3] = 255;
                        }
                        else
                        {
                            // SUBMERGED UNDERWATER: Submerged portion recorded in depth buffer for water absorption tint
                            pMask[pixelIdx] = false;

                            float depthDecay = (float)Math.Exp(-submergeDepth * 0.22f);
                            pRender[pixelOffset + 0] = (byte)(woodCol.B * nDotL * depthDecay * 0.6f);
                            pRender[pixelOffset + 1] = (byte)(woodCol.G * nDotL * depthDecay * 0.7f);
                            pRender[pixelOffset + 2] = (byte)(woodCol.R * nDotL * depthDecay * 0.5f);
                            pRender[pixelOffset + 3] = 255;
                        }
                    }
                }
            }
        }
    }

    private void DrawSeagull(
        Vector3 prowTip, Vector3 camPos, Vector3 camFwd, Vector3 camRight, Vector3 camUp,
        float focal, float halfW, float halfH, Vector3 lightDir)
    {
        if (!ProjectVertex(prowTip, camPos, camFwd, camRight, camUp, focal, halfW, halfH, out float sx, out float sy, out float sz)) return;

        int cx = (int)Math.Round(sx);
        int cy = (int)Math.Round(sy);

        // Seagull pixel footprint (~7x10 px silhouette)
        unsafe
        {
            fixed (byte* pRender = _renderBuffer)
            fixed (bool* pMask = _boatMask)
            {
                for (int dy = -7; dy <= 2; dy++)
                {
                    int py = cy + dy;
                    if (py < 0 || py >= CanvasHeight) continue;
                    int rowOffset = py * CanvasWidth * 4;

                    for (int dx = -3; dx <= 3; dx++)
                    {
                        int px = cx + dx;
                        if (px < 0 || px >= CanvasWidth) continue;

                        int pixelIdx = (py * CanvasWidth) + px;
                        int offset = rowOffset + (px * 4);

                        // Seagull body shape
                        if (dy == -7 && Math.Abs(dx) <= 1)
                        {
                            // Yellow beak
                            pRender[offset + 0] = 30;
                            pRender[offset + 1] = 190;
                            pRender[offset + 2] = 240;
                            pMask[pixelIdx] = true;
                        }
                        else if (dy <= -4 && Math.Abs(dx) <= 2)
                        {
                            // White head / neck
                            pRender[offset + 0] = 245;
                            pRender[offset + 1] = 250;
                            pRender[offset + 2] = 255;
                            pMask[pixelIdx] = true;
                        }
                        else if (dy <= 0 && Math.Abs(dx) <= 3)
                        {
                            // White body with soft grey wing shading
                            byte c = (byte)(Math.Abs(dx) == 3 ? 160 : 240);
                            pRender[offset + 0] = c;
                            pRender[offset + 1] = c;
                            pRender[offset + 2] = c;
                            pMask[pixelIdx] = true;
                        }
                        else if (dy > 0 && Math.Abs(dx) <= 1)
                        {
                            // Legs / feet
                            pRender[offset + 0] = 40;
                            pRender[offset + 1] = 140;
                            pRender[offset + 2] = 180;
                            pMask[pixelIdx] = true;
                        }
                    }
                }
            }
        }
    }

    private static bool ProjectVertex(
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

    #region Liquid Glass Droplet Slider Thumb & Track (Task 2)

    private unsafe void RenderSliderTrack(byte* pRender)
    {
        int thumbY = SliderBottom - (int)(_themeValue * SliderTravel);
        thumbY = Math.Clamp(thumbY, SliderTop, SliderBottom);

        // Dynamic solid white track line from thumb position down to bottom
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

        // Subtly lit track line above the thumb
        for (int y = SliderTop; y < thumbY; y++)
        {
            int lineOffset = (y * CanvasWidth * 4) + (SliderCenterX * 4);
            pRender[lineOffset + 0] = Math.Max(pRender[lineOffset + 0], (byte)70);
            pRender[lineOffset + 1] = Math.Max(pRender[lineOffset + 1], (byte)70);
            pRender[lineOffset + 2] = Math.Max(pRender[lineOffset + 2], (byte)70);
        }

        // 8-bit Moon icon with star at top zenith (SliderTop - 18)
        DrawMoonIcon(pRender, SliderCenterX, SliderTop - 18);

        // 8-bit Sun icon with radial dots at bottom nadir (SliderBottom + 18)
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

                // Subtle drop shadow around bottom and right
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
                    // Crystal liquid glass droplet shading
                    float rim = Math.Clamp((normDist - 0.65f) / 0.35f, 0.0f, 1.0f);

                    // Specular glint on upper-left curve
                    float glintD = (float)Math.Sqrt(((dx - (-3.0f)) * (dx - (-3.0f))) + ((dy - (-3.0f)) * (dy - (-3.0f))));
                    float glint = (float)Math.Pow(Math.Clamp(1.0f - (glintD / 2.6f), 0.0f, 1.0f), 2.0);

                    // Glass refraction core tint depending on theme value
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

        bool wasOverSlider = _isPointerOverSlider;
        _isPointerOverSlider = (_cursorX >= SliderLeft && _cursorX <= SliderRight &&
                                _cursorY >= SliderTop - 25 && _cursorY <= SliderBottom + 25);

        // Check if pointer is dragging the slider
        if (_isDraggingSlider)
        {
            UpdateSliderFromPointerY(_cursorY);
            return;
        }

        // Pointer movement over water generates ripple disturbances
        // CRITICAL BOUNDARY (Task 6): If pointer is directly over the boat, DO NOT spawn water ripples!
        int px = (int)Math.Clamp(_cursorX, 0, CanvasWidth - 1);
        int py = (int)Math.Clamp(_cursorY, 0, CanvasHeight - 1);
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

        // Click on slider channel engages dragging
        if (px >= SliderLeft - 5 && px <= SliderRight + 5 && py >= SliderTop - 25 && py <= SliderBottom + 25)
        {
            _isDraggingSlider = true;
            PixelDisplayImage.CapturePointer(e.Pointer);
            UpdateSliderFromPointerY(py);
            e.Handled = true;
            return;
        }

        // Click on water spawns a strong impulse ripple (unless clicking on the boat!)
        int cx = (int)Math.Clamp(px, 0, CanvasWidth - 1);
        int cy = (int)Math.Clamp(py, 0, CanvasHeight - 1);
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
            // Scrolling up increases theme value (toward Night), scrolling down decreases (toward Day)
            double step = delta > 0 ? 0.05 : -0.05;
            ThemeValue = Math.Clamp(_themeValue + step, 0.0, 1.0);
            e.Handled = true;
        }
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        double step = 0.05; // 5% per arrow key
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
                ThemeValue = 0.0; // Day
                e.Handled = true;
                break;
            case VirtualKey.End:
                ThemeValue = 1.0; // Night
                e.Handled = true;
                break;
        }
    }

    private void UpdateSliderFromPointerY(float py)
    {
        float clampedY = Math.Clamp(py, SliderTop, SliderBottom);
        // SliderTop (158) is 1.0 (Night), SliderBottom (444) is 0.0 (Day)
        double newVal = (double)(SliderBottom - clampedY) / SliderTravel;
        ThemeValue = Math.Clamp(newVal, 0.0, 1.0);
    }

    private void SpawnRippleAtCursor(float sx, float sy, float amp)
    {
        // Unproject screen (sx, sy) to world water plane (Y = 0)
        float yawRad = _currentYaw * ((float)Math.PI / 180.0f);
        float pitchRad = _currentPitch * ((float)Math.PI / 180.0f);

        Vector3 camTarget = new(32.0f, -2.0f, 36.0f);
        float camDist = 162.0f;
        float cosP = (float)Math.Cos(pitchRad);
        float sinP = (float)Math.Sin(pitchRad);
        float cosY = (float)Math.Cos(yawRad);
        float sinY = (float)Math.Sin(yawRad);

        Vector3 camPos = camTarget + new Vector3(sinY * cosP * camDist, sinP * camDist, -cosY * cosP * camDist);
        Vector3 camFwd = (camTarget - camPos).Normalized();
        Vector3 camRight = Vector3.Cross(new Vector3(0, 1, 0), camFwd).Normalized();
        Vector3 camUp = Vector3.Cross(camFwd, camRight).Normalized();

        float focal = 385.0f;
        float halfW = CanvasWidth * 0.5f;
        float halfH = CanvasHeight * 0.5f;

        Vector3 rayDir = ((camFwd * focal) + (camRight * (sx - halfW)) + (camUp * (halfH - sy))).Normalized();
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
