namespace FileFormat.Jpeg;

/// <summary>In-memory representation of a JPEG image.</summary>
public sealed class JpegFile {
  public int Width { get; init; }
  public int Height { get; init; }
  public bool IsGrayscale { get; init; }
  public byte[]? RgbPixelData { get; init; }
  public byte[]? RawJpegBytes { get; init; }
}
