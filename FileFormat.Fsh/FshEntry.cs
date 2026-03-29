namespace FileFormat.Fsh;

/// <summary>A single image entry within an FSH archive.</summary>
public sealed class FshEntry {
  /// <summary>4-character tag identifying this entry.</summary>
  public string Tag { get; init; } = "\0\0\0\0";

  /// <summary>The record code indicating the pixel format.</summary>
  public FshRecordCode RecordCode { get; init; }

  /// <summary>Width of the image in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Height of the image in pixels.</summary>
  public int Height { get; init; }

  /// <summary>Raw pixel data in the format specified by <see cref="RecordCode"/>.</summary>
  public byte[] PixelData { get; init; } = [];

  /// <summary>BGRA palette (1024 bytes, 256 entries x 4 bytes). Non-null only for <see cref="FshRecordCode.Indexed8"/>.</summary>
  public byte[]? Palette { get; init; }

  /// <summary>Center X coordinate (hotspot).</summary>
  public int CenterX { get; init; }

  /// <summary>Center Y coordinate (hotspot).</summary>
  public int CenterY { get; init; }
}
