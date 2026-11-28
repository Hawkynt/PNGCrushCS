namespace FileFormat.LuraWave;

/// <summary>The colour model a LuraWave stream was coded in — the first byte after the dimensions in its <c>GE</c> chunk.</summary>
/// <remarks>
/// LuraTech never published names for these values. The descriptions follow what LuraTech's encoder and decoders do
/// with the channels; the colour model each stands for is the one LuraWave SmartCompress 3.0 lists for it (for example
/// "3x8 bit YUV", "3x8 bit LAB", "4x8 bit CMYK"). Three further values its encoder writes are refused by name: 5 (four
/// 8-bit channels without a colour model), 20 ("3x16 bit LAB") and 21 ("4x16 bit YUVK"); LuraTech's decoders turn
/// none of them into RGB.
/// </remarks>
public enum LuraWaveColourMode : byte {

  /// <summary>One channel, 8-bit grey stored with a bias of -128.</summary>
  Grey = 1,

  /// <summary>Three channels ("3x8 bit YUV"): four times a luminance that weighs green twice, plus two colour differences.</summary>
  ColourDifference = 2,

  /// <summary>Three channels ("3x8 bit raw"), plain red/green/blue each biased by -128.</summary>
  Rgb = 4,

  /// <summary>
  /// Four plain 8-bit channels, each biased by -128 and coded without a colour transform: the four-channel counterpart
  /// of <see cref="Rgb"/>, read as red, green, blue and alpha.
  /// </summary>
  RawFourChannel = 5,

  /// <summary>Three channels ("3x8 bit YUV") in the reversible integer colour transform (the one JPEG 2000 later called RCT).</summary>
  ReversibleColour = 6,

  /// <summary>Four channels ("4x8 bit CMYK"): cyan, magenta and yellow in the reversible colour transform plus black, mapped to RGB by a quadratic polynomial.</summary>
  FourChannel = 7,

  /// <summary>Three channels ("3x8 bit LAB"): CIE L*a*b* with a and b signed, mapped to RGB by a quadratic polynomial.</summary>
  Polynomial = 8,

  /// <summary>One channel ("16 bit grey scale"), unsigned grey wider than 8 bits; its range is announced by an <c>MV</c> chunk.</summary>
  WideGrey = 17,

  /// <summary>Three channels ("3x16 bit YUV") wider than 8 bits in the colour-difference model of <see cref="ColourDifference"/>.</summary>
  WideColourDifference = 18,

  /// <summary>Three channels ("3x16 bit YUV") wider than 8 bits in the reversible colour transform.</summary>
  WideReversibleColour = 19,

  /// <summary>
  /// Three channels ("3x16 bit LAB"): unsigned wide CIE L*, a* and b* passed through unchanged, a* and b* centred on
  /// half the range; the wide counterpart of <see cref="Polynomial"/>.
  /// </summary>
  WideLab = 20,

  /// <summary>
  /// Four channels ("4x16 bit YUVK"): cyan, magenta and yellow wider than 8 bits in the reversible colour transform,
  /// plus black; the wide counterpart of <see cref="FourChannel"/>.
  /// </summary>
  WideFourChannel = 21,

  /// <summary>One channel ("16 bit grey scale"), grey wider than 8 bits stored with a bias of half its range.</summary>
  WideSignedGrey = 22,
}
