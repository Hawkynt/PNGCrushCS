namespace FileFormat.Jpeg;

/// <summary>Per-component info from SOF marker.</summary>
internal sealed class JpegComponentInfo {
  public byte Id { get; init; }
  public byte HSamplingFactor { get; init; }
  public byte VSamplingFactor { get; init; }
  public byte QuantTableId { get; init; }
}
