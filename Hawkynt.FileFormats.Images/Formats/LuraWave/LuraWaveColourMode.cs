namespace FileFormat.LuraWave;

/// <summary>The colour model a LuraWave stream was coded in — the first byte after the dimensions in its <c>GE</c> chunk.</summary>
/// <remarks>
/// The names describe what the decoder does with the channels; LuraTech never published names for these values.
/// </remarks>
public enum LuraWaveColourMode : byte {

  /// <summary>One channel, 8-bit grey stored with a bias of -128.</summary>
  Grey = 1,

  /// <summary>Three channels: a luminance stored at four times its scale plus two colour differences.</summary>
  ColourDifference = 2,

  /// <summary>Three channels, plain red/green/blue each biased by -128.</summary>
  Rgb = 4,

  /// <summary>Three channels in the reversible integer colour transform (the one JPEG 2000 later called RCT).</summary>
  ReversibleColour = 6,

  /// <summary>Four channels: the reversible colour transform plus a fourth channel, mapped to RGB by a quadratic polynomial (presumably CMYK).</summary>
  FourChannel = 7,

  /// <summary>Three channels mapped to RGB by a quadratic polynomial (presumably a Lab-like opponent space).</summary>
  Polynomial = 8,

  /// <summary>One channel, unsigned grey wider than 8 bits; its range is announced by an <c>MV</c> chunk.</summary>
  WideGrey = 17,

  /// <summary>Three channels wider than 8 bits in the reversible colour transform.</summary>
  WideReversibleColour = 19,

  /// <summary>One channel, grey wider than 8 bits stored with a bias of half its range.</summary>
  WideSignedGrey = 22,
}
