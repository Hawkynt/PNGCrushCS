namespace FileFormat.Core;

/// <summary>The numeric range used by component samples.</summary>
public enum RawColorRange {
  Unspecified,
  Limited,
  Full,
}

/// <summary>Colour primaries associated with raw component samples.</summary>
public enum RawColorPrimaries {
  Unspecified,
  Bt709,
  Bt470M,
  Bt470Bg,
  Smpte170M,
  Smpte240M,
  Film,
  Bt2020,
  DisplayP3,
  DciP3,
  AdobeRgb,
}

/// <summary>Transfer characteristic associated with raw component samples.</summary>
public enum RawTransferCharacteristic {
  Unspecified,
  Linear,
  Srgb,
  Bt709,
  Gamma22,
  Gamma28,
  Smpte240M,
  Log100,
  Log316,
  Iec61966_2_4,
  Bt1361,
  Smpte2084,
  Smpte428,
  HybridLogGamma,
}

/// <summary>Matrix used to derive Y/Cb/Cr from primary colour components.</summary>
public enum RawMatrixCoefficients {
  Unspecified,
  Identity,
  Bt709,
  Fcc,
  Bt601,
  Smpte240M,
  YCgCo,
  Bt2020NonConstantLuminance,
  Bt2020ConstantLuminance,
}

/// <summary>Where a subsampled chroma sample is located relative to the luma grid.</summary>
public enum RawChromaLocation {
  Unspecified,
  Left,
  Center,
  TopLeft,
  Top,
  BottomLeft,
  Bottom,
}

/// <summary>
/// Describes how the numbers in a <see cref="RawImage"/> are meant to be interpreted as colour.
/// </summary>
/// <remarks>
/// This is deliberately separate from <see cref="PixelFormat"/>. <c>Yuv420P10</c> says how samples
/// are laid out and how wide they are; it does not say whether code value 64 is black, which matrix
/// produced chroma, or whether the signal is SDR, PQ or HLG. Keeping those facts beside the pixels
/// lets a decoder hand out native YUV/HDR without baking a display conversion into the decode.
/// Writers and viewers may then preserve the metadata when their target format can express it, or
/// explicitly convert when it cannot.
/// </remarks>
public sealed record RawImageColorInfo {
  public RawColorRange Range { get; init; } = RawColorRange.Unspecified;
  public RawColorPrimaries Primaries { get; init; } = RawColorPrimaries.Unspecified;
  public RawTransferCharacteristic Transfer { get; init; } = RawTransferCharacteristic.Unspecified;
  public RawMatrixCoefficients Matrix { get; init; } = RawMatrixCoefficients.Unspecified;
  public RawChromaLocation ChromaLocation { get; init; } = RawChromaLocation.Unspecified;

  /// <summary>The conventional interpretation used by legacy 8-bit SD video when no VUI says otherwise.</summary>
  public static RawImageColorInfo Bt601Limited { get; } = new() {
    Range = RawColorRange.Limited,
    Primaries = RawColorPrimaries.Smpte170M,
    Transfer = RawTransferCharacteristic.Bt709,
    Matrix = RawMatrixCoefficients.Bt601,
    ChromaLocation = RawChromaLocation.Left,
  };

  /// <summary>The conventional interpretation used by 8-bit HD video when no more specific metadata is available.</summary>
  public static RawImageColorInfo Bt709Limited { get; } = new() {
    Range = RawColorRange.Limited,
    Primaries = RawColorPrimaries.Bt709,
    Transfer = RawTransferCharacteristic.Bt709,
    Matrix = RawMatrixCoefficients.Bt709,
    ChromaLocation = RawChromaLocation.Left,
  };
}
