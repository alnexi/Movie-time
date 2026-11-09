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
/// High-performance interactive retro pixel-art theme controller.
/// Renders authentic ordered dithering, celestial Day-to-Night palette interpolation,
/// fluid mouse ripples, and layered parallax at 60 FPS onto a native WinUI 3 WriteableBitmap.
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
    private const int SliderTop = 158;      // Moon position (ThemeValue = 1.0, Night)
    private const int SliderBottom = 444;   // Sun position (ThemeValue = 0.0, Day)
    private const int SliderCenterX = 37;
    private const int SliderTravel = SliderBottom - SliderTop; // 286 px

    private readonly IThemeManagerService? _themeManager;
    private readonly ILogger<PixelDitherThemeControl> _logger;

    private WriteableBitmap? _writeableBitmap;
    private byte[] _dayBuffer = new byte[BufferByteLength];
    private byte[] _twilightBuffer = new byte[BufferByteLength];
    private byte[] _nightBuffer = new byte[BufferByteLength];
    private byte[] _renderBuffer = new byte[BufferByteLength];

    private byte[]? _thumbBlockBuffer;
    private int _thumbBlockWidth, _thumbBlockHeight;
    private byte[]? _thumbFistBuffer;
    private int _thumbFistWidth, _thumbFistHeight;

    private readonly Stopwatch _stopwatch = new();
    private double _lastElapsedSeconds;
    private bool _isLoaded;
    private bool _isRendering;

    // Slider State
    private bool _isDraggingSlider;
    private bool _isPointerOverSlider;
    private double _themeValue = 1.0;

    // Mouse Interaction & Ripple State
    private float _cursorX = -100;
    private float _cursorY = -100;
    private float _lastRippleX = -100;
    private float _lastRippleY = -100;
    private double _lastRippleTime;
    private bool _isCursorInside;

    // Parallax Physics
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
        StopRendering();
        if (_themeManager != null)
        {
            _themeManager.ThemeChanged -= OnThemeManagerChanged;
        }
        _isLoaded = false;
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
                // Fallback check in project output
                assetsDir = Path.Combine(Directory.GetCurrentDirectory(), "Assets", "Theme");
            }

            void LoadOrFill(string fileName, ref byte[] targetBuffer, byte r, byte g, byte b)
            {
                var filePath = Path.Combine(assetsDir, fileName);
                if (File.Exists(filePath))
                {
                    var data = File.ReadAllBytes(filePath);
                    if (data.Length == BufferByteLength)
                    {
                        targetBuffer = data;
                        return;
                    }
                }

                // Generative fallback if asset missing
                targetBuffer = new byte[BufferByteLength];
                for (int i = 0; i < PixelCount; i++)
                {
                    int offset = i * 4;
                    targetBuffer[offset + 0] = b;
                    targetBuffer[offset + 1] = g;
                    targetBuffer[offset + 2] = r;
                    targetBuffer[offset + 3] = 255;
                }
            }

            LoadOrFill("day.bgra", ref _dayBuffer, 46, 139, 87);
            LoadOrFill("twilight.bgra", ref _twilightBuffer, 180, 110, 40);
            LoadOrFill("night.bgra", ref _nightBuffer, 12, 18, 38);

            // Load thumb sprites
            var blockPath = Path.Combine(assetsDir, "thumb_block.bgra");
            if (File.Exists(blockPath))
            {
                _thumbBlockBuffer = File.ReadAllBytes(blockPath);
                _thumbBlockWidth = 19;
                _thumbBlockHeight = 15;
            }

            var fistPath = Path.Combine(assetsDir, "thumb_fist.bgra");
            if (File.Exists(fistPath))
            {
                _thumbFistBuffer = File.ReadAllBytes(fistPath);
                _thumbFistWidth = 33;
                _thumbFistHeight = 31;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load pre-rendered pixel dither assets. Using procedural fallbacks.");
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
        if (_writeableBitmap == null || !_isLoaded) return;

        double currentTotalSec = _stopwatch.Elapsed.TotalSeconds;
        double dt = currentTotalSec - _lastElapsedSeconds;
        _lastElapsedSeconds = currentTotalSec;
        if (dt > 0.1) dt = 0.016; // Clamp large delta during window resize

        UpdateSimulation((float)dt, (float)currentTotalSec);
        RenderScene((float)currentTotalSec);
    }

    private void UpdateSimulation(float dt, float totalSec)
    {
        // 1. Update Parallax Physics with smooth damping
        bool reducedMotion = _themeManager?.ReducedMotion ?? false;
        if (reducedMotion)
        {
            _currentParallaxX = 0;
            _currentParallaxY = 0;
        }
        else
        {
            float lerpSpeed = 7.0f * dt;
            _currentParallaxX += (_targetParallaxX - _currentParallaxX) * lerpSpeed;
            _currentParallaxY += (_targetParallaxY - _currentParallaxY) * lerpSpeed;
        }

        // 2. Age out active ripples
        for (int i = 0; i < _ripples.Length; i++)
        {
            if (_ripples[i].Active)
            {
                float age = totalSec - _ripples[i].StartTime;
                if (age > 2.2f)
                {
                    _ripples[i].Active = false;
                }
            }
        }
    }

    private void RenderScene(float totalSec)
    {
        if (_writeableBitmap == null) return;

        double v = _themeValue;
        bool reducedMotion = _themeManager?.ReducedMotion ?? false;
        bool ambient = _themeManager?.AmbientAnimationEnabled ?? true;

        // Determine palette interpolation factors
        byte[] src1, src2;
        float factor;

        if (v <= 0.5)
        {
            factor = (float)(v / 0.5);
            src1 = _dayBuffer;
            src2 = _twilightBuffer;
        }
        else
        {
            factor = (float)((v - 0.5) / 0.5);
            src1 = _twilightBuffer;
            src2 = _nightBuffer;
        }

        int invFactor = (int)((1.0f - factor) * 256.0f);
        int factor256 = (int)(factor * 256.0f);

        int thumbY = SliderBottom - (int)(v * SliderTravel);
        thumbY = Math.Clamp(thumbY, SliderTop, SliderBottom);

        // Render pixel grid with wave distortion
        unsafe
        {
            fixed (byte* pRender = _renderBuffer, pSrc1 = src1, pSrc2 = src2)
            {
                for (int y = 0; y < CanvasHeight; y++)
                {
                    int rowOffset = y * CanvasWidth * 4;

                    // Parallax vertical offset
                    int sampleY = y + (int)_currentParallaxY;
                    sampleY = Math.Clamp(sampleY, 0, CanvasHeight - 1);

                    for (int x = 0; x < CanvasWidth; x++)
                    {
                        int pixelOffset = rowOffset + (x * 4);

                        // Slider channel reservation
                        bool inSliderChannel = (x >= SliderLeft && x <= SliderRight && y >= SliderTop - 25 && y <= SliderBottom + 25);

                        int finalSrcX = x + (int)_currentParallaxX;
                        int finalSrcY = sampleY;

                        // Apply water ripple distortion outside the slider if motion allowed
                        if (!inSliderChannel && !reducedMotion)
                        {
                            float dispX = 0f;
                            float dispY = 0f;

                            // 1. Ambient gentle water shimmer
                            if (ambient)
                            {
                                float ambientPhase = (y * 0.045f) + (totalSec * 1.8f);
                                dispX += (float)Math.Sin(ambientPhase) * 0.75f;
                            }

                            // 2. Mouse-induced expanding ripples
                            for (int ripIdx = 0; ripIdx < _ripples.Length; ripIdx++)
                            {
                                if (!_ripples[ripIdx].Active) continue;

                                float age = totalSec - _ripples[ripIdx].StartTime;
                                float waveDist = age * 165.0f; // Propagation speed px/sec

                                float dx = x - _ripples[ripIdx].X;
                                float dy = y - _ripples[ripIdx].Y;
                                float dist = (float)Math.Sqrt((dx * dx) + (dy * dy));

                                if (dist > 1.0f && dist <= waveDist + 35.0f && dist >= waveDist - 35.0f)
                                {
                                    float decay = (float)Math.Exp(-age * 1.1f);
                                    float wave = (float)Math.Sin((dist - waveDist) * 0.22f) * _ripples[ripIdx].Amplitude * decay;
                                    float invDist = 1.0f / dist;

                                    dispX += dx * invDist * wave;
                                    dispY += dy * invDist * wave;
                                }
                            }

                            finalSrcX = Math.Clamp(x + (int)dispX, 0, CanvasWidth - 1);
                            finalSrcY = Math.Clamp(sampleY + (int)dispY, 0, CanvasHeight - 1);
                        }

                        finalSrcX = Math.Clamp(finalSrcX, 0, CanvasWidth - 1);
                        finalSrcY = Math.Clamp(finalSrcY, 0, CanvasHeight - 1);

                        int srcOffset = (finalSrcY * CanvasWidth * 4) + (finalSrcX * 4);

                        // Palette interpolation in BGRA
                        int b = ((pSrc1[srcOffset + 0] * invFactor) + (pSrc2[srcOffset + 0] * factor256)) >> 8;
                        int g = ((pSrc1[srcOffset + 1] * invFactor) + (pSrc2[srcOffset + 1] * factor256)) >> 8;
                        int r = ((pSrc1[srcOffset + 2] * invFactor) + (pSrc2[srcOffset + 2] * factor256)) >> 8;

                        // Moonlight reflection sparkle for Night mode (x in [135, 210])
                        if (v > 0.65 && !inSliderChannel && x >= 135 && x <= 210 && y >= 100 && y <= 380)
                        {
                            float moonCenter = 172.0f;
                            float moonDist = Math.Abs(x - moonCenter);
                            if (moonDist < 30.0f)
                            {
                                float sparkle = (float)Math.Sin((x * 0.5f) + (y * 0.35f) + (totalSec * 4.0f));
                                if (sparkle > 0.85f)
                                {
                                    int glint = (int)((sparkle - 0.85f) * 6.6f * 180.0f);
                                    b = Math.Min(255, b + glint);
                                    g = Math.Min(255, g + glint);
                                    r = Math.Min(255, r + glint);
                                }
                            }
                        }

                        // Cursor proximity lighting highlight
                        if (_isCursorInside && !inSliderChannel && !reducedMotion)
                        {
                            float cdx = x - _cursorX;
                            float cdy = y - _cursorY;
                            float cdistSq = (cdx * cdx) + (cdy * cdy);
                            if (cdistSq < 1800.0f) // ~42px radius
                            {
                                float lightBoost = (1.0f - (cdistSq / 1800.0f)) * 28.0f;
                                b = Math.Min(255, b + (int)lightBoost);
                                g = Math.Min(255, g + (int)lightBoost);
                                r = Math.Min(255, r + (int)lightBoost);
                            }
                        }

                        pRender[pixelOffset + 0] = (byte)b;
                        pRender[pixelOffset + 1] = (byte)g;
                        pRender[pixelOffset + 2] = (byte)r;
                        pRender[pixelOffset + 3] = 255;
                    }
                }

                // Render dynamic slider track fill line (solid white from thumb down to bottom)
                for (int y = thumbY; y <= SliderBottom; y++)
                {
                    int lineOffset = (y * CanvasWidth * 4) + (SliderCenterX * 4);
                    pRender[lineOffset + 0] = 255; // White
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

                // Blit slider thumb (fist cursor when dragging/hovering, or clean pill block)
                bool showFist = _isDraggingSlider || _isPointerOverSlider;
                byte[]? thumbSprite = showFist ? _thumbFistBuffer : _thumbBlockBuffer;
                int tw = showFist ? _thumbFistWidth : _thumbBlockWidth;
                int th = showFist ? _thumbFistHeight : _thumbBlockHeight;

                if (thumbSprite != null && tw > 0 && th > 0)
                {
                    int startX = SliderCenterX - (tw / 2);
                    int startY = thumbY - (th / 2);

                    fixed (byte* pThumb = thumbSprite)
                    {
                        for (int ty = 0; ty < th; ty++)
                        {
                            int destY = startY + ty;
                            if (destY < 0 || destY >= CanvasHeight) continue;

                            for (int tx = 0; tx < tw; tx++)
                            {
                                int destX = startX + tx;
                                if (destX < 0 || destX >= CanvasWidth) continue;

                                int thumbOffset = (ty * tw * 4) + (tx * 4);
                                byte tb = pThumb[thumbOffset + 0];
                                byte tg = pThumb[thumbOffset + 1];
                                byte tr = pThumb[thumbOffset + 2];
                                byte ta = pThumb[thumbOffset + 3];

                                if (ta > 40)
                                {
                                    int destOffset = (destY * CanvasWidth * 4) + (destX * 4);
                                    pRender[destOffset + 0] = tb;
                                    pRender[destOffset + 1] = tg;
                                    pRender[destOffset + 2] = tr;
                                    pRender[destOffset + 3] = 255;
                                }
                            }
                        }
                    }
                }
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

    #region Pointer & Keyboard Interaction

    private (int x, int y) GetCanvasCoordinates(PointerRoutedEventArgs e)
    {
        var pt = e.GetCurrentPoint(PixelDisplayImage).Position;
        double renderWidth = PixelDisplayImage.ActualWidth;
        double renderHeight = PixelDisplayImage.ActualHeight;

        if (renderWidth <= 0 || renderHeight <= 0)
        {
            return ((int)pt.X, (int)pt.Y);
        }

        int x = (int)((pt.X / renderWidth) * CanvasWidth);
        int y = (int)((pt.Y / renderHeight) * CanvasHeight);

        return (Math.Clamp(x, 0, CanvasWidth - 1), Math.Clamp(y, 0, CanvasHeight - 1));
    }

    private bool IsInSliderHitBox(int x, int y)
    {
        return x >= SliderLeft - 10 && x <= SliderRight + 10 && y >= SliderTop - 35 && y <= SliderBottom + 35;
    }

    private void OnPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _isCursorInside = true;
    }

    private void OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        _isCursorInside = false;
        _isPointerOverSlider = false;
        _targetParallaxX = 0;
        _targetParallaxY = 0;
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        var (x, y) = GetCanvasCoordinates(e);
        _cursorX = x;
        _cursorY = y;
        _isCursorInside = true;

        bool overSlider = IsInSliderHitBox(x, y);
        _isPointerOverSlider = overSlider;

        if (_isDraggingSlider)
        {
            UpdateSliderFromPointer(y);
            e.Handled = true;
            return;
        }

        // Update mouse parallax (-1 to 1)
        _targetParallaxX = ((x / (float)CanvasWidth) - 0.5f) * 16.0f;
        _targetParallaxY = ((y / (float)CanvasHeight) - 0.5f) * 10.0f;

        // Generate fluid mouse ripples over water
        if (!overSlider)
        {
            float dx = x - _lastRippleX;
            float dy = y - _lastRippleY;
            double dist = Math.Sqrt((dx * dx) + (dy * dy));
            double now = _stopwatch.Elapsed.TotalSeconds;

            if (dist > 14.0 || (now - _lastRippleTime) > 0.16)
            {
                SpawnRipple(x, y, 3.2f);
                _lastRippleX = x;
                _lastRippleY = y;
                _lastRippleTime = now;
            }
        }
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var (x, y) = GetCanvasCoordinates(e);

        if (IsInSliderHitBox(x, y))
        {
            _isDraggingSlider = true;
            PixelDisplayImage.CapturePointer(e.Pointer);
            UpdateSliderFromPointer(y);
            e.Handled = true;
        }
        else
        {
            // Direct click on water spawns a prominent splash ripple
            SpawnRipple(x, y, 5.5f);
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
        var delta = e.GetCurrentPoint(PixelDisplayImage).Properties.MouseWheelDelta;
        if (delta != 0)
        {
            double step = delta > 0 ? 0.05 : -0.05;
            ThemeValue = Math.Clamp(_themeValue + step, 0.0, 1.0);
            e.Handled = true;
        }
    }

    private void UpdateSliderFromPointer(int y)
    {
        // 0.0 = SliderBottom, 1.0 = SliderTop
        double norm = (double)(SliderBottom - y) / SliderTravel;
        ThemeValue = Math.Clamp(norm, 0.0, 1.0);
    }

    private void SpawnRipple(float x, float y, float amplitude)
    {
        _ripples[_rippleHeadIndex] = new RippleWave
        {
            X = x,
            Y = y,
            StartTime = (float)_stopwatch.Elapsed.TotalSeconds,
            Amplitude = amplitude,
            Active = true
        };

        _rippleHeadIndex = (_rippleHeadIndex + 1) % _ripples.Length;
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case VirtualKey.Up:
            case VirtualKey.Right:
                ThemeValue = Math.Clamp(_themeValue + 0.05, 0.0, 1.0);
                e.Handled = true;
                break;
            case VirtualKey.Down:
            case VirtualKey.Left:
                ThemeValue = Math.Clamp(_themeValue - 0.05, 0.0, 1.0);
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

    #endregion
}
