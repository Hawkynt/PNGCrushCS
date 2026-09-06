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

  /// <summary>
  /// The interpretation a set of ITU-T H.273 code points states — clauses 8.1 to 8.3 and Tables 2, 3
  /// and 4.
  /// </summary>
  /// <param name="colourPrimaries">H.273 <c>ColourPrimaries</c>.</param>
  /// <param name="transferCharacteristics">H.273 <c>TransferCharacteristics</c>.</param>
  /// <param name="matrixCoefficients">H.273 <c>MatrixCoefficients</c>.</param>
  /// <param name="fullRange">H.273 <c>VideoFullRangeFlag</c>: whether the samples fill the depth.</param>
  /// <param name="chromaLocation">Where a subsampled chroma sample sits, which H.273 does not state.</param>
  /// <remarks>
  /// One set of numbers, written in a great many places: the HEVC and AVC video usability
  /// information, the AV1 sequence header, the ISOBMFF <c>colr</c> box's nclx profile, PNG's
  /// <c>cICP</c> chunk. They all mean the same thing, so they are read the same way here rather than
  /// once per format — a table copied per format is a table that drifts per format.
  /// <para/>
  /// A code point this library has no name for becomes <c>Unspecified</c> rather than an error. That
  /// is what the standard asks for: the value says the signal was meant for something outside the
  /// table, and the display is left to decide, which is exactly what an unspecified value means.
  /// </remarks>
  public static RawImageColorInfo FromCodePoints(
    int colourPrimaries,
    int transferCharacteristics,
    int matrixCoefficients,
    bool fullRange,
    RawChromaLocation chromaLocation = RawChromaLocation.Unspecified
  ) => new() {
    Range = fullRange ? RawColorRange.Full : RawColorRange.Limited,
    Primaries = colourPrimaries switch {
      1 => RawColorPrimaries.Bt709,
      4 => RawColorPrimaries.Bt470M,
      5 => RawColorPrimaries.Bt470Bg,
      6 => RawColorPrimaries.Smpte170M,
      7 => RawColorPrimaries.Smpte240M,
      8 => RawColorPrimaries.Film,
      9 => RawColorPrimaries.Bt2020,
      11 => RawColorPrimaries.DciP3,
      12 => RawColorPrimaries.DisplayP3,
      _ => RawColorPrimaries.Unspecified,
    },
    Transfer = transferCharacteristics switch {
      // 1, 6, 14 and 15 are one curve written four times: BT.709's, restated by SMPTE 170M and by
      // BT.2020 at ten and twelve bits. They differ in the precision of the constants, not in shape.
      1 or 6 or 14 or 15 => RawTransferCharacteristic.Bt709,
      4 => RawTransferCharacteristic.Gamma22,
      5 => RawTransferCharacteristic.Gamma28,
      7 => RawTransferCharacteristic.Smpte240M,
      8 => RawTransferCharacteristic.Linear,
      9 => RawTransferCharacteristic.Log100,
      10 => RawTransferCharacteristic.Log316,
      11 => RawTransferCharacteristic.Iec61966_2_4,
      12 => RawTransferCharacteristic.Bt1361,
      13 => RawTransferCharacteristic.Srgb,
      16 => RawTransferCharacteristic.Smpte2084,
      17 => RawTransferCharacteristic.Smpte428,
      18 => RawTransferCharacteristic.HybridLogGamma,
      _ => RawTransferCharacteristic.Unspecified,
    },
    Matrix = matrixCoefficients switch {
      0 => RawMatrixCoefficients.Identity,
      1 => RawMatrixCoefficients.Bt709,
      4 => RawMatrixCoefficients.Fcc,
      // BT.470BG and SMPTE 170M carry the same weights; the two entries record which document a
      // stream cited, and both are what everything else calls BT.601.
      5 or 6 => RawMatrixCoefficients.Bt601,
      7 => RawMatrixCoefficients.Smpte240M,
      8 => RawMatrixCoefficients.YCgCo,
      9 => RawMatrixCoefficients.Bt2020NonConstantLuminance,
      10 => RawMatrixCoefficients.Bt2020ConstantLuminance,
      _ => RawMatrixCoefficients.Unspecified,
    },
    ChromaLocation = chromaLocation,
  };
}
