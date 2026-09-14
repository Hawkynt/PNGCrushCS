namespace FileFormat.Codecs;

/// <summary>Entropy coder used for FFV1 prediction differences.</summary>
public enum Ffv1EntropyCoder {
  /// <summary>Adaptive Golomb-Rice coding with run mode.</summary>
  GolombRice = 0,

  /// <summary>Binary range coding with RFC 9043's default state transition table.</summary>
  Range = 1,

  /// <summary>Binary range coding with a stream-provided state transition table.</summary>
  RangeCustom = 2,
}

/// <summary>Which standard FFV1 context model is written.</summary>
public enum Ffv1ContextModel {
  /// <summary>The smaller standard context model.</summary>
  Small = 0,

  /// <summary>The larger standard context model.</summary>
  Large = 1,
}

/// <summary>Controls the interoperable FFV1 bitstream produced by <see cref="Ffv1Encoder"/>.</summary>
public sealed record Ffv1EncoderOptions {

  /// <summary>FFV1 bitstream version. RFC 9043 defines versions 0, 1 and 3.</summary>
  public int Version { get; init; } = 3;

  /// <summary>
  /// Number of significant bits in each source sample, or zero to infer it from the chosen
  /// <see cref="FileFormat.Core.PixelFormat"/>. A wider 16-bit storage format may be used for any
  /// nine-through-sixteen-bit FFV1 stream as long as all supplied sample values fit this width.
  /// </summary>
  public int BitsPerRawSample { get; init; }

  /// <summary>Entropy coder for prediction differences.</summary>
  public Ffv1EntropyCoder EntropyCoder { get; init; } = Ffv1EntropyCoder.Range;

  /// <summary>Standard context model to write.</summary>
  public Ffv1ContextModel ContextModel { get; init; } = Ffv1ContextModel.Small;

  /// <summary>Number of frames between entropy-state-reset keyframes.</summary>
  public int KeyFrameInterval { get; init; } = 1;

  /// <summary>Version 3 slice columns, or zero to choose the default grid.</summary>
  public int HorizontalSlices { get; init; }

  /// <summary>Version 3 slice rows, or zero to choose the default grid.</summary>
  public int VerticalSlices { get; init; }

  /// <summary>Whether version 3 slices carry CRC-32 protection.</summary>
  public bool SliceCrc { get; init; } = true;

  /// <summary>
  /// Optional 256-entry differences from RFC 9043's default range state transition table.
  /// Required for <see cref="Ffv1EntropyCoder.RangeCustom"/> and unused otherwise.
  /// </summary>
  public int[]? StateTransitionDelta { get; init; }
}