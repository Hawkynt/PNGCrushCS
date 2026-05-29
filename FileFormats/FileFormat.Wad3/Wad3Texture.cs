namespace FileFormat.Wad3;

/// <summary>A single texture stored in a WAD3 file (Half-Life miptex).</summary>
public sealed class Wad3Texture {
  /// <summary>Texture name (up to 16 ASCII characters).</summary>
  public string Name { get; init; } = "";

  /// <summary>Texture width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Texture height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>Mip level 0 pixel data (8-bit indexed).</summary>
  public byte[] PixelData { get; init; } = [];

  /// <summary>Mip levels 1-3 pixel data, null if not present.</summary>
  public byte[][]? MipMaps { get; init; }

  /// <summary>256-entry RGB palette (768 bytes).</summary>
  public byte[] Palette { get; init; } = [];
}
