namespace FileFormat.Jpeg;

/// <summary>Parsed SOF (Start of Frame) data.</summary>
internal sealed class JpegFrameHeader {
  public byte Precision { get; init; }
  public int Width { get; init; }
  public int Height { get; init; }
  public JpegComponentInfo[] Components { get; init; } = [];
  public bool IsProgressive { get; init; }
}
