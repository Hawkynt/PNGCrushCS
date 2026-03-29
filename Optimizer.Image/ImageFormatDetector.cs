using System;
using System.IO;

namespace Optimizer.Image;

/// <summary>Detects image format from file magic bytes or extension.</summary>
public static class ImageFormatDetector {

  /// <summary>Detects image format using magic bytes first, extension fallback.</summary>
  public static ImageFormat Detect(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("File not found.", file.FullName);

    var header = new byte[132];
    using (var stream = file.OpenRead()) {
      var bytesRead = stream.Read(header, 0, header.Length);
      if (bytesRead < 2)
        return ImageFormat.Unknown;
    }

    var result = DetectFromSignature(header);
    return result != ImageFormat.Unknown ? result : DetectFromExtension(file);
  }

  /// <summary>Detects image format from pre-read file bytes + extension fallback.</summary>
  public static ImageFormat Detect(byte[] fileBytes, FileInfo file) {
    if (fileBytes.Length < 2)
      return ImageFormat.Unknown;

    var result = DetectFromSignature(fileBytes.AsSpan(0, Math.Min(fileBytes.Length, 132)));
    return result != ImageFormat.Unknown ? result : DetectFromExtension(file);
  }

  /// <summary>Detects image format from magic bytes via the format registry.</summary>
  public static ImageFormat DetectFromSignature(ReadOnlySpan<byte> header)
    => FormatRegistry.DetectFormatFromSignature(header);

  /// <summary>Detects image format from file extension via the format registry, with overrides for TIFF-based raw formats.</summary>
  public static ImageFormat DetectFromExtension(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    var ext = file.Extension.ToLowerInvariant();

    // DNG and CameraRaw share TIFF magic bytes — must be resolved by extension
    return ext switch {
      ".dng" => ImageFormat.Dng,
      ".cr2" or ".nef" or ".arw" or ".orf" or ".rw2" or ".pef" or ".raf" or ".srw" or ".dcs" => ImageFormat.CameraRaw,
      _ => FormatRegistry.DetectFromExtension(ext),
    };
  }
}
