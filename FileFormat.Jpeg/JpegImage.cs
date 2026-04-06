using System.Collections.Generic;

namespace FileFormat.Jpeg;

/// <summary>Full internal JPEG representation at the coefficient level.</summary>
internal sealed class JpegImage {
  public JpegFrameHeader Frame { get; init; } = new();
  public JpegQuantTable[] QuantTables { get; set; } = [];
  public JpegHuffmanTable[] DcHuffmanTables { get; set; } = [];
  public JpegHuffmanTable[] AcHuffmanTables { get; set; } = [];
  public JpegComponentData[] ComponentData { get; set; } = [];
  public List<JpegMarkerSegment> MarkerSegments { get; init; } = [];
  public int RestartInterval { get; set; }
}
