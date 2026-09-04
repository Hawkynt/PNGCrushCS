namespace FileFormat.JpegXl.Codec;

/// <summary>
/// The weighted predictor's parameters, as a modular group header states them
/// (ISO/IEC 18181-1 §H.3; libjxl <c>weighted::Header</c>).
/// </summary>
/// <remarks>
/// A group header may say "all default" in one bit, and most do, which is why
/// these were once assumed rather than read. An encoder that tunes them writes
/// seven coefficients of five bits and four weights of four, and a decoder that
/// reads past them and predicts with the defaults anyway gets a picture that is
/// close enough to look right and is not the one that was encoded.
/// </remarks>
internal readonly record struct JxlWpHeader {

  /// <summary>The parameters libjxl's <c>Header</c> initialises itself with.</summary>
  public static JxlWpHeader Default => new() {
    P1C = 16,
    P2C = 10,
    P3Ca = 7,
    P3Cb = 7,
    P3Cc = 7,
    P3Cd = 0,
    P3Ce = 0,
    W0 = 0xD,
    W1 = 0xC,
    W2 = 0xC,
    W3 = 0xC,
  };

  public int P1C { get; init; }
  public int P2C { get; init; }
  public int P3Ca { get; init; }
  public int P3Cb { get; init; }
  public int P3Cc { get; init; }
  public int P3Cd { get; init; }
  public int P3Ce { get; init; }

  public uint W0 { get; init; }
  public uint W1 { get; init; }
  public uint W2 { get; init; }
  public uint W3 { get; init; }
}
