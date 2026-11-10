using System;
using System.IO;
using System.Windows.Input;
using MovieTime.App.Common;
using MovieTime.Core.Entities;

namespace MovieTime.App.ViewModels;

/// <summary>
/// Presentation model representing a specific local physical media file / edition for a movie.
/// </summary>
public sealed class LocalMediaFileItemViewModel : ObservableObject
{
    private bool _isSelected;

    public int Id { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string? QualityTag { get; set; }
    public string? ResolutionDisplay { get; set; }
    public string? VideoCodec { get; set; }
    public string? AudioCodec { get; set; }
    public string? ContainerFormat { get; set; }
    public bool IsMissing { get; set; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public string FileSizeDisplay
    {
        get
        {
            if (SizeBytes <= 0) return string.Empty;
            double gb = SizeBytes / (1024.0 * 1024.0 * 1024.0);
            return gb >= 1.0 ? $"{gb:F1} GB" : $"{SizeBytes / (1024.0 * 1024.0):F0} MB";
        }
    }

    public string FormatSummary
    {
        get
        {
            var parts = new System.Collections.Generic.List<string>();
            if (!string.IsNullOrWhiteSpace(QualityTag)) parts.Add(QualityTag);
            if (!string.IsNullOrWhiteSpace(VideoCodec)) parts.Add(VideoCodec);
            if (!string.IsNullOrWhiteSpace(AudioCodec)) parts.Add(AudioCodec);
            if (!string.IsNullOrWhiteSpace(FileSizeDisplay)) parts.Add(FileSizeDisplay);
            if (!string.IsNullOrWhiteSpace(ContainerFormat)) parts.Add(ContainerFormat.ToUpperInvariant());

            return parts.Count > 0 ? string.Join(" \u2022 ", parts) : FileName;
        }
    }

    public ICommand? SelectCommand { get; set; }
    public ICommand? RemoveFromLibraryCommand { get; set; }
    public ICommand? DeleteFromDiskCommand { get; set; }

    public static LocalMediaFileItemViewModel FromEntity(MediaFile file, bool isSelected = false)
    {
        string? qualityTag = null;
        string? resolutionDisplay = null;

        if (file.VideoHeight >= 2160 || file.VideoWidth >= 3840)
        {
            qualityTag = "4K UHD";
            resolutionDisplay = "3840 x 2160 (4K UHD)";
        }
        else if (file.VideoHeight >= 1080 || file.VideoWidth >= 1920)
        {
            qualityTag = "1080p HD";
            resolutionDisplay = "1920 x 1080 (Full HD)";
        }
        else if (file.VideoHeight >= 720 || file.VideoWidth >= 1280)
        {
            qualityTag = "720p HD";
            resolutionDisplay = "1280 x 720 (HD)";
        }
        else if (file.VideoHeight.HasValue && file.VideoWidth.HasValue)
        {
            resolutionDisplay = $"{file.VideoWidth} x {file.VideoHeight}";
        }

        string? container = !string.IsNullOrWhiteSpace(file.ContainerFormat)
            ? file.ContainerFormat
            : Path.GetExtension(file.FilePath).TrimStart('.');

        return new LocalMediaFileItemViewModel
        {
            Id = file.Id,
            FileName = file.FileName,
            FilePath = file.FilePath,
            SizeBytes = file.SizeBytes,
            QualityTag = qualityTag,
            ResolutionDisplay = resolutionDisplay,
            VideoCodec = file.VideoCodec,
            AudioCodec = file.AudioCodec,
            ContainerFormat = container,
            IsMissing = file.IsMissing,
            IsSelected = isSelected
        };
    }
}
