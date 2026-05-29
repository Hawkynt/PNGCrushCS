namespace FileFormat.Jpeg;

/// <summary>Parsed SOS (Start of Scan) data.</summary>
internal sealed class JpegScanHeader {
  public (byte ComponentId, byte DcTableId, byte AcTableId)[] Components { get; init; } = [];
  public byte SpectralStart { get; init; }
  public byte SpectralEnd { get; init; }
  public byte SuccessiveApproxHigh { get; init; }
  public byte SuccessiveApproxLow { get; init; }
}
