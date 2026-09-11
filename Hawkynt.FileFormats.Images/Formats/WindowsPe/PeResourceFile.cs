using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FileFormat.Bmp;
using FileFormat.Core;
using FileFormat.Cur;
using FileFormat.Ico;
using FileFormat.Png;

namespace FileFormat.WindowsPe;

/// <summary>In-memory representation of image resources extracted from or written to a Windows PE file.</summary>
/// <remarks>
/// Reading enumerates grouped icons/cursors, RT_BITMAP resources and image files embedded in other
/// resource types. A parsed PE retains its original bytes so resource replacement edits that executable
/// rather than manufacturing a new one: code, imports, relocations, manifests, version resources,
/// named resources, overlays and unrelated language variants are preserved. Replacement can address a
/// raw resource by type/name ID/language, an image by <see cref="ImageResources"/> index, or one image
/// inside an icon/cursor group by its group resource ID and group-image index.
/// <para/>
/// Writing a file created from arbitrary pixels still creates a native PE32 image with a conventional
/// <c>.rsrc</c> tree. EXE/SCR output receives a minimal entry point; DLL/OCX/CPL output is a no-entry
/// resource DLL.
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
  static PeResourceFile IImageFormatReader<PeResourceFile>.FromSpan(ReadOnlySpan<byte> data) => ReadEditable(data);
  static PeResourceFile IImageFromRawImage<PeResourceFile>.FromRawImage(RawImage image, string extension) => FromRawImage(image, extension);
  static byte[] IImageFormatWriter<PeResourceFile>.ToBytes(PeResourceFile file)
    => file.SourceData is { } source ? source[..] : PeResourceWriter.ToBytes(file);

  static bool? IImageFormatMetadata<PeResourceFile>.MatchesSignature(ReadOnlySpan<byte> header) {
    if (header.Length < 64)
      return null;

    if (header[0] != 0x4D || header[1] != 0x5A)
      return null;

    var peOffset = header[60] | (header[61] << 8) | (header[62] << 16) | (header[63] << 24);
    if (peOffset < 64 || peOffset > 0x10000000)
      return null;

    return true;
  }

  /// <summary>All icon and cursor groups found in the PE resource section (backward-compatible).</summary>
  public IReadOnlyList<PeIconGroup> IconGroups { get; init; } = [];

  /// <summary>All image resources found in the PE resource section (icons, cursors, bitmaps, embedded images).</summary>
  public IReadOnlyList<PeImageResource> ImageResources { get; init; } = [];

  /// <summary>
  /// Every resource leaf in the parsed PE, including non-image and named resources. Empty for a file
  /// constructed from a <see cref="RawImage"/> until it has been serialized and read back.
  /// </summary>
  public IReadOnlyList<PeResourceInfo> Resources
    => SourceData is { } source ? PeResourceEditor.GetResources(source) : [];

  internal PeResourceModuleKind ModuleKind { get; init; }
  internal byte[]? SourceData { get; init; }

  public static PeResourceFile FromRawImage(RawImage image) => FromRawImage(image, ".exe");

  /// <summary>Reads a PE while retaining the complete source image for later in-place resource replacement.</summary>
  public static PeResourceFile ReadEditable(ReadOnlySpan<byte> data) {
    var source = data.ToArray();
    return _AttachSource(PeResourceReader.FromBytes(source), source);
  }

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
          ResourceTypeId = 2,
          ResourceId = 1,
          Data = bmp,
        }
      ],
      ModuleKind = kind,
    };
  }

  /// <summary>Replaces the unique numeric PE resource identified by type and resource ID.</summary>
  /// <remarks>If more than one language variant exists, use the language-specific overload.</remarks>
  public PeResourceFile ReplaceResource(int typeId, int resourceId, ReadOnlySpan<byte> data)
    => _ReplaceResource(typeId, resourceId, null, data);

  /// <summary>Replaces a numeric PE resource identified by type, resource ID and language ID.</summary>
  public PeResourceFile ReplaceResource(int typeId, int resourceId, int languageId, ReadOnlySpan<byte> data)
    => _ReplaceResource(typeId, resourceId, languageId, data);

  /// <summary>Replaces an image selected by its index in <see cref="ImageResources"/>.</summary>
  /// <remarks>
  /// A bitmap or single-entry icon/cursor can be replaced directly. For a multi-entry icon/cursor
  /// group, use the overload that also supplies <paramref name="groupImageIndex"/>.
  /// </remarks>
  public PeResourceFile ReplaceImage(int index, RawImage image, int? languageId = null) {
    var resource = _ImageAt(index);
    return _ReplaceImage(resource, null, image, languageId);
  }

  /// <summary>Replaces one image inside an icon/cursor group selected by PE image-resource index.</summary>
  public PeResourceFile ReplaceImage(int index, int groupImageIndex, RawImage image, int? languageId = null) {
    var resource = _ImageAt(index);
    return _ReplaceImage(resource, groupImageIndex, image, languageId);
  }

  /// <summary>Replaces a bitmap or single-entry icon/cursor selected by resource type and resource ID.</summary>
  public PeResourceFile ReplaceImage(
    PeImageResourceType resourceType,
    int resourceId,
    RawImage image,
    int? languageId = null
  ) => _ReplaceImage(_FindImage(resourceType, resourceId), null, image, languageId);

  /// <summary>Replaces one image inside an icon/cursor group selected by group resource ID and index.</summary>
  public PeResourceFile ReplaceImage(
    PeImageResourceType resourceType,
    int resourceId,
    int groupImageIndex,
    RawImage image,
    int? languageId = null
  ) => _ReplaceImage(_FindImage(resourceType, resourceId), groupImageIndex, image, languageId);

  public static RawImage ToRawImage(PeResourceFile file) {
    ArgumentNullException.ThrowIfNull(file);
    if (file.ImageResources.Count == 0)
      throw new InvalidOperationException("PE file contains no image resources.");

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

  private PeResourceFile _ReplaceResource(int typeId, int resourceId, int? languageId, ReadOnlySpan<byte> data) {
    if (SourceData is not { } source)
      throw new InvalidOperationException("Raw PE resources can only be replaced on a file parsed from an existing executable or library.");

    var updated = PeResourceEditor.ReplaceResource(source, typeId, resourceId, languageId, data);
    return ReadEditable(updated);
  }

  private PeResourceFile _ReplaceImage(
    PeImageResource resource,
    int? groupImageIndex,
    RawImage image,
    int? languageId
  ) {
    ArgumentNullException.ThrowIfNull(image);
    if (SourceData is not { } source)
      throw new InvalidOperationException("Images can only be replaced in-place on a PE file parsed from an existing executable or library.");

    byte[] updated = resource.ResourceType switch {
      PeImageResourceType.Bitmap when groupImageIndex is null
        => PeResourceEditor.ReplaceResource(
          source,
          2,
          resource.ResourceId,
          languageId,
          BmpWriter.ToBytes(BmpFile.FromRawImage(image)).AsSpan(14)
        ),

      PeImageResourceType.Icon
        => PeResourceEditor.ReplaceGroupImage(source, false, resource.ResourceId, languageId, groupImageIndex, image),

      PeImageResourceType.Cursor
        => PeResourceEditor.ReplaceGroupImage(source, true, resource.ResourceId, languageId, groupImageIndex, image),

      PeImageResourceType.EmbeddedImage when groupImageIndex is null
        => _ReplaceEmbeddedBytes(resource, image, languageId),

      PeImageResourceType.Bitmap or PeImageResourceType.EmbeddedImage
        => throw new ArgumentException("A group-image index is only valid for icon and cursor groups.", nameof(groupImageIndex)),

      _ => throw new NotSupportedException($"Unknown PE image resource type {resource.ResourceType}."),
    };

    return ReadEditable(updated);
  }

  private byte[] _ReplaceEmbeddedBytes(PeImageResource resource, RawImage image, int? languageId) {
    if (resource.ResourceTypeId <= 0)
      throw new InvalidOperationException(
        "The embedded image does not expose its owning PE resource type; use ReplaceResource(typeId, resourceId, ...) with encoded bytes instead."
      );

    byte[] encoded = resource.FormatHint switch {
      "bmp" => BmpWriter.ToBytes(BmpFile.FromRawImage(image)),
      "ico" => IcoWriter.ToBytes(IcoFile.FromRawImage(image)),
      "cur" => CurWriter.ToBytes(CurFile.FromRawImage(image)),
      "png" => PngWriter.ToBytes(PngFile.FromRawImage(image)),
      _ => throw new NotSupportedException(
        $"Embedded image format '{resource.FormatHint ?? "unknown"}' has no in-place RawImage encoder here; use ReplaceResource with encoded bytes instead."
      ),
    };

    return PeResourceEditor.ReplaceResource(SourceData!, resource.ResourceTypeId, resource.ResourceId, languageId, encoded);
  }

  private PeImageResource _ImageAt(int index) {
    if ((uint)index >= (uint)ImageResources.Count)
      throw new ArgumentOutOfRangeException(nameof(index));
    return ImageResources[index];
  }

  private PeImageResource _FindImage(PeImageResourceType type, int resourceId) {
    var matches = ImageResources.Where(resource => resource.ResourceType == type && resource.ResourceId == resourceId).ToArray();
    return matches.Length switch {
      0 => throw new KeyNotFoundException($"PE image resource {type}/{resourceId} was not found."),
      1 => matches[0],
      _ => throw new InvalidOperationException(
        $"PE image resource {type}/{resourceId} is ambiguous; select it by ImageResources index instead."
      ),
    };
  }

  private static PeResourceFile _AttachSource(PeResourceFile parsed, byte[] source) {
    var images = new PeImageResource[parsed.ImageResources.Count];
    for (var i = 0; i < images.Length; ++i) {
      var image = parsed.ImageResources[i];
      images[i] = new PeImageResource {
        ResourceType = image.ResourceType,
        ResourceTypeId = image.ResourceTypeId != 0 ? image.ResourceTypeId : image.ResourceType switch {
          PeImageResourceType.Bitmap => 2,
          PeImageResourceType.Cursor => 12,
          PeImageResourceType.Icon => 14,
          _ => 0,
        },
        ResourceId = image.ResourceId,
        Data = image.Data,
        FormatHint = image.FormatHint,
      };
    }

    return new PeResourceFile {
      IconGroups = parsed.IconGroups,
      ImageResources = images,
      ModuleKind = parsed.ModuleKind,
      SourceData = source,
    };
  }

  private static RawImage _ToRawImage(PeImageResource resource) {
    if (resource.Data.Length == 0)
      throw new InvalidOperationException("Resource contains no data.");

    return resource.ResourceType switch {
      PeImageResourceType.Icon => IcoFile.ToRawImage(IcoReader.FromBytes(resource.Data)),
      PeImageResourceType.Cursor => CurFile.ToRawImage(CurReader.FromBytes(resource.Data)),
      PeImageResourceType.Bitmap => BmpFile.ToRawImage(BmpReader.FromBytes(resource.Data)),
      PeImageResourceType.EmbeddedImage => _DecodeEmbeddedImage(resource),
      _ => throw new NotSupportedException($"Unknown resource type: {resource.ResourceType}."),
    };
  }

  private static RawImage _DecodeEmbeddedImage(PeImageResource resource)
    => resource.FormatHint switch {
      "bmp" => BmpFile.ToRawImage(BmpReader.FromBytes(resource.Data)),
      "ico" => IcoFile.ToRawImage(IcoReader.FromBytes(resource.Data)),
      "cur" => CurFile.ToRawImage(CurReader.FromBytes(resource.Data)),
      "png" => PngFile.ToRawImage(PngReader.FromBytes(resource.Data)),
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
