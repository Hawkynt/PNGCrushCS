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

  /// <summary>The resource type.</summary>
  public PeImageResourceType ResourceType { get; init; }

  /// <summary>The resource ID within its type category.</summary>
  public int ResourceId { get; init; }

  /// <summary>The raw resource data. For Icon/Cursor this is a complete ICO/CUR file.
  /// For Bitmap this is a complete BMP file (BITMAPFILEHEADER prepended).
  /// For EmbeddedImage this is the raw bytes of the embedded image (PNG/JPEG/GIF/etc.).</summary>
  public byte[] Data { get; init; } = [];

  /// <summary>A hint about the embedded format (e.g. "png", "jpeg", "gif"). Only set for EmbeddedImage type.</summary>
  public string? FormatHint { get; init; }
}
