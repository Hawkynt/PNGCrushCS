namespace FileFormat.Jpeg;

/// <summary>APP/COM marker segment for lossless transcode preservation.</summary>
internal sealed class JpegMarkerSegment {
  public byte Marker { get; init; }
  public byte[] Data { get; init; } = [];
}
