using System;

namespace MovieTime.Core.Artwork;

/// <summary>
/// Evaluates visual quality of extracted video candidate frames.
/// Detects black frames, dark fades, blurry frames, and flat credits scenes.
/// </summary>
public interface IImageQualityEvaluator
{
    ImageQualityScore Evaluate(ReadOnlySpan<byte> bgraPixels, int width, int height);
}
