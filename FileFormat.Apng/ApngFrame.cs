namespace FileFormat.Apng;

/// <summary>A single frame in an APNG animation.</summary>
public sealed class ApngFrame {
  public int Width { get; init; }
  public int Height { get; init; }
  public int XOffset { get; init; }
  public int YOffset { get; init; }
  public ushort DelayNumerator { get; init; }
  public ushort DelayDenominator { get; init; }
  public ApngDisposeOp DisposeOp { get; init; }
  public ApngBlendOp BlendOp { get; init; }

  /// <summary>Raw pixel data for this frame (scanlines without filter bytes).</summary>
  public byte[][] PixelData { get; init; } = [];
}
