namespace MovieTime.Core.Artwork;

/// <summary>
/// Dimensions and format metadata verified directly from binary image headers.
/// </summary>
public readonly record struct ImageHeaderInfo(
    ImageFormat Format,
    int Width,
    int Height,
    long Length)
{
    public bool IsValid => Format != ImageFormat.Unknown && Width > 0 && Height > 0 && Length > 0;
    public double AspectRatio => Height > 0 ? (double)Width / Height : 0.0;
    public bool IsVerticalPoster => AspectRatio > 0.0 && AspectRatio < 0.9;
    public bool IsWidescreenBackdrop => AspectRatio >= 1.3;
}
