namespace FileFormat.Acorn;

/// <summary>A single sprite within an Acorn RISC OS sprite file.</summary>
public sealed class AcornSprite {
  /// <summary>Sprite name (up to 12 ASCII characters, null/space padded).</summary>
  public string Name { get; init; } = "";

  /// <summary>Width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>Bits per pixel (1, 2, 4, 8, 16, or 32).</summary>
  public int BitsPerPixel { get; init; }

  /// <summary>Raw screen mode word from the sprite header.</summary>
  public int Mode { get; init; }

  /// <summary>Raw pixel data (word-aligned scanlines).</summary>
  public byte[] PixelData { get; init; } = [];

  /// <summary>Optional mask data, null if no mask present.</summary>
  public byte[]? MaskData { get; init; }

  /// <summary>Optional palette data (2 words per entry, only first word used for static display).</summary>
  public byte[]? Palette { get; init; }
}
