namespace FileFormat.Wad2;

/// <summary>A single texture stored in a WAD2 file (Quake 1 miptex).</summary>
public sealed class Wad2Texture {
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
}
