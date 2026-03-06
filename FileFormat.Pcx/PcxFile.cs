namespace FileFormat.Pcx;

/// <summary>In-memory representation of a PCX image.</summary>
public sealed class PcxFile {
  public int Width { get; init; }
  public int Height { get; init; }
  public int BitsPerPixel { get; init; }
  public byte[] PixelData { get; init; } = [];
  public byte[]? Palette { get; init; }
  public int PaletteColorCount { get; init; }
  public PcxColorMode ColorMode { get; init; }
  public PcxPlaneConfig PlaneConfig { get; init; }
}
