using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FileFormat.Bmp;
using FileFormat.Core;
using FileFormat.Ico;

namespace FileFormat.WindowsPe;

/// <summary>In-memory representation of image resources extracted from a Windows PE file.</summary>
[FormatMagicBytes([0x4D, 0x5A])] // MZ
[FormatDetectionPriority(999)]    // Very common signature, low priority for image detection
public sealed class PeResourceFile : IImageFileFormat<PeResourceFile>, IMultiImageFileFormat<PeResourceFile> {

  static string IImageFileFormat<PeResourceFile>.PrimaryExtension => ".exe";
  static string[] IImageFileFormat<PeResourceFile>.FileExtensions => [".exe", ".dll", ".ocx", ".scr", ".cpl"];
  static FormatCapability IImageFileFormat<PeResourceFile>.Capabilities => FormatCapability.MultiImage;

  /// <summary>All icon and cursor groups found in the PE resource section (backward-compatible).</summary>
  public IReadOnlyList<PeIconGroup> IconGroups { get; init; } = [];

  /// <summary>All image resources found in the PE resource section (icons, cursors, bitmaps, embedded images).</summary>
  public IReadOnlyList<PeImageResource> ImageResources { get; init; } = [];

  static bool? IImageFileFormat<PeResourceFile>.MatchesSignature(ReadOnlySpan<byte> header) {
    if (header.Length < 64)
      return null;

    // Check MZ signature
    if (header[0] != 0x4D || header[1] != 0x5A)
      return null;

    // Check PE offset is reasonable
    var peOffset = header[60] | (header[61] << 8) | (header[62] << 16) | (header[63] << 24);
    if (peOffset < 64 || peOffset > 0x10000000)
      return null;

    return true;
  }

  static PeResourceFile IImageFileFormat<PeResourceFile>.FromFile(FileInfo file) => PeResourceReader.FromFile(file);
  static PeResourceFile IImageFileFormat<PeResourceFile>.FromBytes(byte[] data) => PeResourceReader.FromBytes(data);
  static PeResourceFile IImageFileFormat<PeResourceFile>.FromStream(Stream stream) => PeResourceReader.FromStream(stream);

  static byte[] IImageFileFormat<PeResourceFile>.ToBytes(PeResourceFile file)
    => throw new NotSupportedException("Writing PE files is not supported.");

  public static RawImage ToRawImage(PeResourceFile file) {
    ArgumentNullException.ThrowIfNull(file);
    if (file.ImageResources.Count == 0)
      throw new InvalidOperationException("PE file contains no image resources.");

    // Prefer icons, then cursors, then bitmaps, then embedded images
    var resource = file.ImageResources.FirstOrDefault(r => r.ResourceType == PeImageResourceType.Icon)
                   ?? file.ImageResources.FirstOrDefault(r => r.ResourceType == PeImageResourceType.Cursor)
                   ?? file.ImageResources.FirstOrDefault(r => r.ResourceType == PeImageResourceType.Bitmap)
                   ?? file.ImageResources[0];

    return _ToRawImage(resource);
  }

  static PeResourceFile IImageFileFormat<PeResourceFile>.FromRawImage(RawImage image)
    => throw new NotSupportedException("Creating PE files from images is not supported.");

  public static int ImageCount(PeResourceFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return file.ImageResources.Count;
  }

  public static RawImage ToRawImage(PeResourceFile file, int index) {
    ArgumentNullException.ThrowIfNull(file);
    if ((uint)index >= (uint)file.ImageResources.Count)
      throw new ArgumentOutOfRangeException(nameof(index));

    return _ToRawImage(file.ImageResources[index]);
  }

  private static RawImage _ToRawImage(PeImageResource resource) {
    if (resource.Data.Length == 0)
      throw new InvalidOperationException("Resource contains no data.");

    return resource.ResourceType switch {
      PeImageResourceType.Icon => IcoFile.ToRawImage(IcoReader.FromBytes(resource.Data)),
      PeImageResourceType.Cursor => IcoFile.ToRawImage(IcoReader.FromBytes(resource.Data)),
      PeImageResourceType.Bitmap => BmpFile.ToRawImage(BmpReader.FromBytes(resource.Data)),
      PeImageResourceType.EmbeddedImage => _DecodeEmbeddedImage(resource),
      _ => throw new NotSupportedException($"Unknown resource type: {resource.ResourceType}."),
    };
  }

  private static RawImage _DecodeEmbeddedImage(PeImageResource resource)
    => resource.FormatHint switch {
      "bmp" => BmpFile.ToRawImage(BmpReader.FromBytes(resource.Data)),
      "ico" => IcoFile.ToRawImage(IcoReader.FromBytes(resource.Data)),
      _ => throw new NotSupportedException(
        $"Embedded image format '{resource.FormatHint ?? "unknown"}' cannot be decoded directly. "
        + "Use the Data property to access the raw bytes and an appropriate reader."
      ),
    };
}
