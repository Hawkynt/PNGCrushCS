namespace FileFormat.Tiff;

/// <summary>In-memory representation of a TIFF image.</summary>
public sealed class TiffFile {
  public int Width { get; init; }
  public int Height { get; init; }
  public int SamplesPerPixel { get; init; }
  public int BitsPerSample { get; init; }
  public byte[] PixelData { get; init; } = [];
  public byte[]? ColorMap { get; init; }
  public TiffColorMode ColorMode { get; init; }
}
