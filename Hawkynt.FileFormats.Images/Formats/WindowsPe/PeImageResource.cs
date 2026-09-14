namespace FileFormat.WindowsPe;

/// <summary>The type of image resource found in a PE file.</summary>
public enum PeImageResourceType {
  /// <summary>Icon group assembled from RT_ICON + RT_GROUP_ICON resources.</summary>
  Icon,
  /// <summary>Cursor group assembled from RT_CURSOR + RT_GROUP_CURSOR resources.</summary>
  Cursor,
  /// <summary>RT_BITMAP resource (DIB without BITMAPFILEHEADER).</summary>
  Bitmap,
  /// <summary>A resource whose data starts with a recognized image signature (PNG, JPEG, GIF, etc.).</summary>
  EmbeddedImage,
}

/// <summary>A single image resource extracted from a PE file.</summary>
public sealed class PeImageResource {

  /// <summary>The image-level interpretation of the resource.</summary>
  public PeImageResourceType ResourceType { get; init; }

  /// <summary>
  /// Numeric PE resource type that owns this image. Icons and cursors name their group resource
  /// types (RT_GROUP_ICON=14, RT_GROUP_CURSOR=12); bitmaps use RT_BITMAP=2. For a named PE resource
  /// type this remains 0 and <see cref="ResourceTypeName"/> carries the exact selector instead.
  /// </summary>
  public int ResourceTypeId { get; init; }

  /// <summary>The named PE resource type, or <see langword="null"/> when the type is numeric.</summary>
  public string? ResourceTypeName { get; init; }

  /// <summary>
  /// Numeric resource ID within its type category. For a named resource this remains 0 and
  /// <see cref="ResourceName"/> carries the exact selector instead.
  /// </summary>
  public int ResourceId { get; init; }

  /// <summary>The named PE resource name, or <see langword="null"/> when the resource uses a numeric ID.</summary>
  public string? ResourceName { get; init; }

  /// <summary>The numeric PE language ID, or <see langword="null"/> when not retained or when the language key is named.</summary>
  public int? LanguageId { get; init; }

  /// <summary>The named PE language key, or <see langword="null"/> for the usual numeric LANGID.</summary>
  public string? LanguageName { get; init; }

  /// <summary>The raw resource data. For Icon/Cursor this is a complete ICO/CUR file.
  /// For Bitmap this is a complete BMP file (BITMAPFILEHEADER prepended).
  /// For EmbeddedImage this is the raw bytes of the embedded image (PNG/JPEG/GIF/etc.).</summary>
  public byte[] Data { get; init; } = [];

  /// <summary>A hint about the embedded format (e.g. "png", "jpeg", "gif"). Only set for EmbeddedImage type.</summary>
  public string? FormatHint { get; init; }
}
