namespace FileFormat.Bmp;

/// <summary>In-memory representation of a BMP image.</summary>
public sealed class BmpFile {
  public int Width { get; init; }
  public int Height { get; init; }
  public int BitsPerPixel { get; init; }
  public byte[] PixelData { get; init; } = [];
  public byte[]? Palette { get; init; }
  public int PaletteColorCount { get; init; }
  public BmpRowOrder RowOrder { get; init; }
  public BmpCompression Compression { get; init; }
  public BmpColorMode ColorMode { get; init; }
}
