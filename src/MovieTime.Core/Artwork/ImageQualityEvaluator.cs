using System;

namespace MovieTime.Core.Artwork;

/// <summary>
/// Production image quality evaluator implementing luminance, contrast, edge gradient,
/// and colorfulness metrics for automatic video thumbnail candidate selection.
/// </summary>
public sealed class ImageQualityEvaluator : IImageQualityEvaluator
{
    private const double TargetLuminanceMin = 25.0;
    private const double TargetLuminanceMax = 230.0;
    private const double MinimumUsableScore = 25.0;
    private const double AcceptableScoreThreshold = 60.0;

    public ImageQualityScore Evaluate(ReadOnlySpan<byte> bgraPixels, int width, int height)
    {
        if (width <= 0 || height <= 0 || bgraPixels.Length < width * height * 4)
        {
            return new ImageQualityScore
            {
                OverallScore = 0,
                IsAcceptable = false,
                IsUsable = false,
                Reason = "Invalid or missing pixel buffer"
            };
        }

        // Subsample step for ultra-fast processing (< 2ms per 1080p frame)
        var stepX = Math.Max(1, width / 320);
        var stepY = Math.Max(1, height / 180);

        long totalLuminance = 0;
        long totalLuminanceSq = 0;
        long totalDiff = 0;
        long sampleCount = 0;

        // Colorfulness accumulators (Hasler-Süsstrunk metric)
        long sumRg = 0;
        long sumRgSq = 0;
        long sumYb = 0;
        long sumYbSq = 0;

        var bytesPerPixel = 4;
        var rowStride = width * bytesPerPixel;

        for (int y = 0; y < height - stepY; y += stepY)
        {
            var rowOffset = y * rowStride;
            var nextRowOffset = (y + stepY) * rowStride;

            for (int x = 0; x < width - stepX; x += stepX)
            {
                var idx = rowOffset + (x * bytesPerPixel);
                var b = bgraPixels[idx];
                var g = bgraPixels[idx + 1];
                var r = bgraPixels[idx + 2];

                // Luminance (ITU-R BT.601)
                var yVal = (int)Math.Round(0.299 * r + 0.587 * g + 0.114 * b);
                totalLuminance += yVal;
                totalLuminanceSq += (long)yVal * yVal;

                // Color difference channels
                var rg = r - g;
                var yb = (int)Math.Round(0.5 * (r + g) - b);
                sumRg += rg;
                sumRgSq += (long)rg * rg;
                sumYb += yb;
                sumYbSq += (long)yb * yb;

                // Edge gradient (horizontal & vertical delta)
                var nextXIdx = rowOffset + ((x + stepX) * bytesPerPixel);
                var nextYIdx = nextRowOffset + (x * bytesPerPixel);

                var yNextX = (int)Math.Round(0.299 * bgraPixels[nextXIdx + 2] + 0.587 * bgraPixels[nextXIdx + 1] + 0.114 * bgraPixels[nextXIdx]);
                var yNextY = (int)Math.Round(0.299 * bgraPixels[nextYIdx + 2] + 0.587 * bgraPixels[nextYIdx + 1] + 0.114 * bgraPixels[nextYIdx]);

                var diff = Math.Abs(yNextX - yVal) + Math.Abs(yNextY - yVal);
                totalDiff += diff;

                sampleCount++;
            }
        }

        if (sampleCount == 0)
        {
            return new ImageQualityScore
            {
                OverallScore = 0,
                IsAcceptable = false,
                IsUsable = false,
                Reason = "No samples gathered"
            };
        }

        // 1. Mean Luminance (0 - 255)
        var meanLuminance = (double)totalLuminance / sampleCount;

        // 2. Contrast: Standard deviation of luminance
        var varianceLum = Math.Max(0.0, ((double)totalLuminanceSq / sampleCount) - (meanLuminance * meanLuminance));
        var contrast = Math.Sqrt(varianceLum);

        // 3. Sharpness: Average edge gradient
        var sharpness = (double)totalDiff / sampleCount;

        // 4. Colorfulness metric
        var meanRg = (double)sumRg / sampleCount;
        var varRg = Math.Max(0.0, ((double)sumRgSq / sampleCount) - (meanRg * meanRg));
        var stdRg = Math.Sqrt(varRg);

        var meanYb = (double)sumYb / sampleCount;
        var varYb = Math.Max(0.0, ((double)sumYbSq / sampleCount) - (meanYb * meanYb));
        var stdYb = Math.Sqrt(varYb);

        var stdRgyb = Math.Sqrt(stdRg * stdRg + stdYb * stdYb);
        var meanRgyb = Math.Sqrt(meanRg * meanRg + meanYb * meanYb);
        var colorfulness = stdRgyb + (0.3 * meanRgyb);

        // Detect black, blown-out, or flat frames
        if (meanLuminance < 12.0)
        {
            return new ImageQualityScore
            {
                Luminance = meanLuminance,
                Contrast = contrast,
                Sharpness = sharpness,
                Colorfulness = colorfulness,
                OverallScore = Math.Min(10.0, meanLuminance * 0.8),
                IsAcceptable = false,
                IsUsable = false,
                Reason = $"Frame is too dark / black screen (L={meanLuminance:F1})"
            };
        }

        if (meanLuminance > 245.0)
        {
            return new ImageQualityScore
            {
                Luminance = meanLuminance,
                Contrast = contrast,
                Sharpness = sharpness,
                Colorfulness = colorfulness,
                OverallScore = 12.0,
                IsAcceptable = false,
                IsUsable = false,
                Reason = $"Frame is overexposed / white flash (L={meanLuminance:F1})"
            };
        }

        if (contrast < 12.0)
        {
            return new ImageQualityScore
            {
                Luminance = meanLuminance,
                Contrast = contrast,
                Sharpness = sharpness,
                Colorfulness = colorfulness,
                OverallScore = 18.0,
                IsAcceptable = false,
                IsUsable = false,
                Reason = $"Low contrast / flat scene (Contrast={contrast:F1})"
            };
        }

        // Multi-metric composite scoring (0 - 100)
        // Contrast score: 0 to 30 (max at 50)
        var contrastScore = Math.Clamp(contrast / 50.0 * 30.0, 0, 30.0);

        // Sharpness score: 0 to 35 (max at 25)
        var sharpnessScore = Math.Clamp(sharpness / 25.0 * 35.0, 0, 35.0);

        // Colorfulness score: 0 to 20 (max at 35)
        var colorfulnessScore = Math.Clamp(colorfulness / 35.0 * 20.0, 0, 20.0);

        // Luminance balance score: 0 to 15 (peak in middle 60..180)
        double lumScore;
        if (meanLuminance >= TargetLuminanceMin && meanLuminance <= TargetLuminanceMax)
        {
            var distFromCenter = Math.Abs(meanLuminance - 120.0);
            lumScore = Math.Max(0.0, 15.0 - (distFromCenter / 100.0 * 10.0));
        }
        else
        {
            lumScore = 3.0;
        }

        var overallScore = Math.Clamp(contrastScore + sharpnessScore + colorfulnessScore + lumScore, 0.0, 100.0);
        var isAcceptable = overallScore >= AcceptableScoreThreshold;
        var isUsable = overallScore >= MinimumUsableScore;

        var reason = isAcceptable
            ? "High visual quality scene"
            : isUsable
                ? "Acceptable scene"
                : "Subpar frame quality";

        return new ImageQualityScore
        {
            Luminance = meanLuminance,
            Contrast = contrast,
            Sharpness = sharpness,
            Colorfulness = colorfulness,
            OverallScore = overallScore,
            IsAcceptable = isAcceptable,
            IsUsable = isUsable,
            Reason = reason
        };
    }
}
