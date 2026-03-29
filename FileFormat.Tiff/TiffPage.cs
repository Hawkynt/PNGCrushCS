namespace FileFormat.Tiff;

/// <summary>Represents a single page/IFD in a multi-page TIFF file.</summary>
public sealed class TiffPage {
  public int Width { get; init; }
  public int Height { get; init; }
  public int SamplesPerPixel { get; init; }
  public int BitsPerSample { get; init; }
  public byte[] PixelData { get; init; } = [];
  public byte[]? ColorMap { get; init; }
  public TiffColorMode ColorMode { get; init; }
}
