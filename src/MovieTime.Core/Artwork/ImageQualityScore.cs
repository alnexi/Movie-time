namespace MovieTime.Core.Artwork;

/// <summary>
/// Multi-metric visual quality assessment result for video candidate frames.
/// </summary>
public sealed class ImageQualityScore
{
    public double Luminance { get; init; }
    public double Contrast { get; init; }
    public double Sharpness { get; init; }
    public double Colorfulness { get; init; }
    public double OverallScore { get; init; }
    public bool IsAcceptable { get; init; }
    public bool IsUsable { get; init; }
    public string Reason { get; init; } = string.Empty;

    public override string ToString() =>
        $"Score={OverallScore:F1}/100 [L={Luminance:F1}, C={Contrast:F1}, S={Sharpness:F1}, Clr={Colorfulness:F1}] - {Reason}";
}
