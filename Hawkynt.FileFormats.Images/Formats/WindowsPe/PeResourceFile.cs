using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FileFormat.Bmp;
using FileFormat.Core;
using FileFormat.Ico;

namespace FileFormat.WindowsPe;

/// <summary>In-memory representation of image resources extracted from or written to a Windows PE file.</summary>
/// <remarks>
/// Reading enumerates grouped icons/cursors, RT_BITMAP resources and image files embedded in other
/// resource types. Writing creates a native PE32 image with a conventional <c>.rsrc</c> tree: pixels
/// supplied through <see cref="FromRawImage(RawImage)"/> become one RT_BITMAP resource, while a
/// parsed <see cref="PeResourceFile"/> can be re-serialized from its complete BMP/ICO/CUR/embedded
/// payloads. EXE/SCR output receives a minimal entry point; DLL/OCX/CPL output is a no-entry resource DLL.
/// </remarks>
[FormatMagicBytes([0x4D, 0x5A])] // MZ
[FormatDetectionPriority(999)]    // Very common signature, low priority for image detection
public sealed class PeResourceFile :
  IImageFormatReader<PeResourceFile>, IImageToRawImage<PeResourceFile>,
  IImageFromRawImage<PeResourceFile>, IImageFormatWriter<PeResourceFile>,
  IMultiImageFileFormat<PeResourceFile> {

  static string IImageFormatMetadata<PeResourceFile>.PrimaryExtension => ".exe";
  static string[] IImageFormatMetadata<PeResourceFile>.FileExtensions => [".exe", ".dll", ".ocx", ".scr", ".cpl"];
  static FormatCapability IImageFormatMetadata<PeResourceFile>.Capabilities => FormatCapability.MultiImage;
  static PeResourceFile IImageFormatReader<PeResourceFile>.FromSpan(ReadOnlySpan<byte> data) => PeResourceReader.FromSpan(data);
  static PeResourceFile IImageFromRawImage<PeResourceFile>.FromRawImage(RawImage image, string extension) => FromRawImage(image, extension);
  static byte[] IImageFormatWriter<PeResourceFile>.ToBytes(PeResourceFile file) => PeResourceWriter.ToBytes(file);

  static bool? IImageFormatMetadata<PeResourceFile>.MatchesSignature(ReadOnlySpan<byte> header) {
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

  /// <summary>All icon and cursor groups found in the PE resource section (backward-compatible).</summary>
  public IReadOnlyList<PeIconGroup> IconGroups { get; init; } = [];

  /// <summary>All image resources found in the PE resource section (icons, cursors, bitmaps, embedded images).</summary>
  public IReadOnlyList<PeImageResource> ImageResources { get; init; } = [];

  internal PeResourceModuleKind ModuleKind { get; init; }

  public static PeResourceFile FromRawImage(RawImage image) => FromRawImage(image, ".exe");

  /// <summary>Creates a PE resource image for one of the supported executable or library extensions.</summary>
  public static PeResourceFile FromRawImage(RawImage image, string extension) {
    ArgumentNullException.ThrowIfNull(image);
    ArgumentException.ThrowIfNullOrWhiteSpace(extension);

    var kind = extension.ToLowerInvariant() switch {
      ".exe" or ".scr" => PeResourceModuleKind.Executable,
      ".dll" or ".ocx" or ".cpl" => PeResourceModuleKind.Dll,
      _ => throw new ArgumentException($"Unsupported PE resource extension '{extension}'.", nameof(extension)),
    };

    var bmp = BmpWriter.ToBytes(BmpFile.FromRawImage(image));
    return new PeResourceFile {
      ImageResources = [
        new PeImageResource {
          ResourceType = PeImageResourceType.Bitmap,
          ResourceId = 1,
          Data = bmp,
        }
      ],
      ModuleKind = kind,
    };
  }

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

internal enum PeResourceModuleKind {
  Executable,
  Dll,
}
