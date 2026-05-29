namespace FileFormat.Jpeg2000.Codec;

/// <summary>Tile-level parameters extracted from SIZ and COD markers needed for packet parsing.</summary>
internal sealed class TileInfo {
  /// <summary>Tile width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Tile height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>Number of DWT decomposition levels.</summary>
  public int DecompLevels { get; init; }

  /// <summary>Number of image components.</summary>
  public int ComponentCount { get; init; }

  /// <summary>Nominal code-block width (power of 2, default 64).</summary>
  public int CodeBlockWidth { get; init; } = 64;

  /// <summary>Nominal code-block height (power of 2, default 64).</summary>
  public int CodeBlockHeight { get; init; } = 64;

  /// <summary>Number of quality layers (from COD SGcod).</summary>
  public int Layers { get; init; } = 1;

  /// <summary>Whether multi-component transform is used.</summary>
  public bool UseMct { get; init; }

  /// <summary>Bits per component (from SIZ Ssiz).</summary>
  public int BitsPerComponent { get; init; } = 8;
}
