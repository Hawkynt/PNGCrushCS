namespace FileFormat.Blp;

/// <summary>Specifies the pixel encoding mode used in a BLP2 file.</summary>
public enum BlpEncoding : byte {
  /// <summary>Palette-indexed pixels with a 256-entry BGRA palette.</summary>
  Palette = 1,
  /// <summary>DXT/BCn block-compressed pixels (DXT1, DXT3, or DXT5).</summary>
  Dxt = 2,
  /// <summary>Uncompressed BGRA pixels (4 bytes per pixel).</summary>
  UncompressedBgra = 3,
}
