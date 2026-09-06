using System;
using FileFormat.Core;

namespace FileFormat.Codecs.H265;

/// <summary>
/// Turns a decoded 4:2:0 picture into the packed RGB every reader in this library hands back.
/// </summary>
/// <remarks>
/// Both steps here are display conventions rather than parts of the coding standard. H.265 codes
/// samples and describes, in an optional part of the sequence parameter set nothing is obliged to
/// send, what colour primaries, transfer and matrix they were meant for; what to do with them is the
/// display's. So a decoder that hands back RGB has had to choose, and what it must not do is choose
/// against what the stream said.
/// <para/>
/// What the stream says arrives as a <see cref="RawImageColorInfo"/> — from the sequence's video
/// usability information, or from the container property that outranks it — and a null one means
/// nothing was said. Then, and only then, the conventional reading applies: studio swing and BT.601,
/// which is what every decoder assumes of a stream that describes itself no further.
/// <para/>
/// Luminance under studio swing runs 16 to 235 and chrominance 16 to 240 rather than filling the
/// depth, so the conversion expands that range — reading those samples as though they filled it
/// leaves every picture washed out by about seven per cent of its contrast, which looks like a decode
/// that worked. Reading full-range samples as though they were studio swing is the same mistake
/// pointed the other way, and it is the more common one now: libheif and x265 write full range
/// unless told otherwise, so it is what almost every HEIC in existence needs.
/// <para/>
/// The chrominance planes are half size, and where their samples sit relative to the luminance ones
/// is inherited from MPEG-2: level with the even luminance column and halfway between the two
/// luminance rows. So bringing chrominance back up is an exact copy on even columns and a halfway
/// average on odd ones, while vertically it is the three-to-one interpolation a quarter-step offset
/// calls for. Using centre siting instead shifts every colour edge half a luminance sample to the
/// left, which is small, everywhere, and looks like a decode that worked.
/// </remarks>
internal static class H265ColorConversion {

  /// <summary>
  /// How many fractional bits the fixed-point matrix carries.
  /// </summary>
  /// <remarks>
  /// Sixteen rather than the eight the classic 298/409/516 constants use. Those constants are a
  /// studio-swing BT.601 matrix rounded once and then reused at every sample depth, and the rounding
  /// they carry is worth a fifth of a level at eight bits and a whole level at twelve. Sixteen bits
  /// costs nothing here — every product still fits an <see cref="int"/> — and it keeps the matrices
  /// that are now derived rather than tabulated from each carrying a rounding error of their own.
  /// </remarks>
  private const int _FRACTION_BITS = 16;

  /// <summary>Crops a decoded picture to its displayed size and converts it to packed 8-bit RGB.</summary>
  /// <param name="picture">The reconstructed planes, which may be larger than the displayed picture.</param>
  /// <param name="left">The first displayed column, from the conformance window.</param>
  /// <param name="top">The first displayed row.</param>
  /// <param name="width">The displayed width.</param>
  /// <param name="height">The displayed height.</param>
  /// <param name="bitDepthLuma">The sequence's luminance sample depth: eight for Main, ten for Main 10.</param>
  /// <param name="bitDepthChroma">The sequence's chrominance sample depth.</param>
  /// <param name="colour">What the stream says its samples mean, or <c>null</c> where it says nothing.</param>
  internal static byte[] ToRgb24(
    H265Picture picture,
    int left,
    int top,
    int width,
    int height,
    int bitDepthLuma,
    int bitDepthChroma,
    RawImageColorInfo? colour = null
  ) {
    var rgb = new byte[width * height * 3];
    var conversion = Conversion.For(colour, bitDepthLuma, bitDepthChroma);
    var rounding = 1 << (_FRACTION_BITS - 1);

    // The chrominance samples that belong to the displayed picture, which is not the whole plane. A
    // cropped stream is coded wider or taller than it is shown, and the samples past the crop are
    // real reconstructed samples that a later picture may predict from — but they are not part of
    // this picture, so the interpolation replicates the last displayed sample rather than reaching
    // into them.
    var chromaLeft = left >> 1;
    var chromaTop = top >> 1;
    var chromaRight = chromaLeft + ((width + 1) >> 1) - 1;
    var chromaBottom = chromaTop + ((height + 1) >> 1) - 1;

    for (var y = 0; y < height; ++y) {
      var lumaRow = (top + y) * picture.Width + left;
      var target = y * width * 3;

      for (var x = 0; x < width; ++x) {
        var luma = picture.Luma[lumaRow + x];
        var cb = _Chroma(picture.Cb, picture.ChromaWidth, left + x, top + y,
          chromaLeft, chromaRight, chromaTop, chromaBottom);
        var cr = _Chroma(picture.Cr, picture.ChromaWidth, left + x, top + y,
          chromaLeft, chromaRight, chromaTop, chromaBottom);

        conversion.ToRgb(luma, cb, cr, rounding, out var r, out var g, out var b);
        rgb[target] = r;
        rgb[target + 1] = g;
        rgb[target + 2] = b;
        target += 3;
      }
    }

    return rgb;
  }

  /// <summary>One chrominance sample at a luminance position, interpolated from the four around it.</summary>
  private static int _Chroma(
    ushort[] plane, int planeWidth, int x, int y, int minX, int maxX, int minY, int maxY) {
    var nearX = x >> 1;
    var nearY = y >> 1;

    // Horizontally the sample is co-sited with the even column, so an even column reads it whole and
    // an odd one splits evenly between it and the next.
    var farX = (x & 1) == 0 ? nearX : _Clip(nearX + 1, minX, maxX);
    var nearWeightX = (x & 1) == 0 ? 2 : 1;
    var farWeightX = 2 - nearWeightX;

    // Vertically it sits halfway between the two rows it covers, so each row is a quarter step away
    // from it in one direction or the other: three parts of the near sample to one of the far.
    var farY = _Clip((y & 1) == 0 ? nearY - 1 : nearY + 1, minY, maxY);

    var topLeft = plane[nearY * planeWidth + nearX];
    var topRight = plane[nearY * planeWidth + farX];
    var bottomLeft = plane[farY * planeWidth + nearX];
    var bottomRight = plane[farY * planeWidth + farX];

    return (3 * (nearWeightX * topLeft + farWeightX * topRight)
            + (nearWeightX * bottomLeft + farWeightX * bottomRight)
            + 4) >> 3;
  }

  private static int _Clip(int value, int lowest, int highest)
    => value < lowest ? lowest : value > highest ? highest : value;

  private static byte _Clamp(int scaled, int rounding) {
    var value = (scaled + rounding) >> _FRACTION_BITS;
    return (byte)(value < 0 ? 0 : value > 255 ? 255 : value);
  }

  /// <summary>The three shapes a decoded picture's planes can have.</summary>
  /// <remarks>
  /// Three rather than one matrix, because two of the matrices ITU-T H.273 defines are not matrices
  /// at all. The identity says the planes were never chrominance differences — they are green, blue
  /// and red already, each carrying the luminance plane's own range — and YCgCo's inverse is three
  /// additions with no weights in it. Forcing either through a Kr/Kb matrix means deriving weights
  /// that do not exist.
  /// </remarks>
  private enum ConversionKind {
    /// <summary>Chrominance differences around a luminance, weighted by Kr and Kb.</summary>
    LumaChroma,

    /// <summary>H.273 MatrixCoefficients 0: the planes are green, blue and red.</summary>
    Identity,

    /// <summary>H.273 MatrixCoefficients 8: luminance with green and orange differences.</summary>
    YCgCo,
  }

  /// <summary>How one picture's three sample planes become red, green and blue.</summary>
  private readonly record struct Conversion(
    ConversionKind Kind,
    int LumaBlack,
    int LumaGain,
    int ChromaCentre,
    int ChromaGain,
    int CrToR,
    int CbToG,
    int CrToG,
    int CbToB
  ) {

    internal static Conversion For(RawImageColorInfo? colour, int bitDepthLuma, int bitDepthChroma) {
      // A stream that states no range means studio swing, and one that states no matrix means BT.601.
      // Both are the reading every other decoder gives an undescribed stream, and both are what this
      // converter did unconditionally before it could be told otherwise.
      var fullRange = colour?.Range == RawColorRange.Full;
      var matrix = colour?.Matrix ?? RawMatrixCoefficients.Unspecified;

      var lumaBlack = fullRange ? 0 : 16 << (bitDepthLuma - 8);
      var lumaRange = fullRange ? (1 << bitDepthLuma) - 1 : 219 << (bitDepthLuma - 8);
      var lumaGain = _Fixed(255.0 / lumaRange);

      // The chrominance centre is the same value either way — 128 << (N-8) is 1 << (N-1) — so only
      // the span around it moves with the range.
      var chromaCentre = 1 << (bitDepthChroma - 1);
      var chromaRange = fullRange ? (1 << bitDepthChroma) - 1 : 224 << (bitDepthChroma - 8);
      var chromaScale = 255.0 / chromaRange;

      switch (matrix) {
        // The identity carries no chrominance at all: every plane is a colour component, so every
        // plane is quantised the way luminance is. The chrominance fields go unused.
        case RawMatrixCoefficients.Identity:
          return new(ConversionKind.Identity, lumaBlack, lumaGain, 0, 0, 0, 0, 0, 0);

        case RawMatrixCoefficients.YCgCo:
          return new(ConversionKind.YCgCo, lumaBlack, lumaGain, chromaCentre, _Fixed(chromaScale), 0, 0, 0, 0);
      }

      var (kr, kb) = matrix switch {
        RawMatrixCoefficients.Bt709 => (0.2126, 0.0722),
        RawMatrixCoefficients.Fcc => (0.30, 0.11),
        RawMatrixCoefficients.Smpte240M => (0.212, 0.087),
        // Constant luminance is a different derivation, not a different pair of weights, and is
        // treated as its non-constant sibling here exactly as the shared raw converter treats it:
        // wrong in the same way in both places is better than wrong in two ways.
        RawMatrixCoefficients.Bt2020NonConstantLuminance or RawMatrixCoefficients.Bt2020ConstantLuminance
          => (0.2627, 0.0593),
        _ => (0.299, 0.114),
      };
      var kg = 1.0 - kr - kb;

      return new(
        ConversionKind.LumaChroma,
        lumaBlack,
        lumaGain,
        chromaCentre,
        0,
        _Fixed(chromaScale * 2.0 * (1.0 - kr)),
        _Fixed(-chromaScale * 2.0 * kb * (1.0 - kb) / kg),
        _Fixed(-chromaScale * 2.0 * kr * (1.0 - kr) / kg),
        _Fixed(chromaScale * 2.0 * (1.0 - kb)));
    }

    internal void ToRgb(int luma, int cb, int cr, int rounding, out byte r, out byte g, out byte b) {
      if (this.Kind == ConversionKind.Identity) {
        g = _Clamp(this.LumaGain * (luma - this.LumaBlack), rounding);
        b = _Clamp(this.LumaGain * (cb - this.LumaBlack), rounding);
        r = _Clamp(this.LumaGain * (cr - this.LumaBlack), rounding);
        return;
      }

      var scaledLuma = this.LumaGain * (luma - this.LumaBlack);
      var blueDifference = cb - this.ChromaCentre;
      var redDifference = cr - this.ChromaCentre;

      if (this.Kind == ConversionKind.YCgCo) {
        // Cb carries green, Cr carries orange, and the inverse is R = Y - Cg + Co, G = Y + Cg,
        // B = Y - Cg - Co.
        var green = this.ChromaGain * blueDifference;
        var orange = this.ChromaGain * redDifference;
        r = _Clamp(scaledLuma - green + orange, rounding);
        g = _Clamp(scaledLuma + green, rounding);
        b = _Clamp(scaledLuma - green - orange, rounding);
        return;
      }

      r = _Clamp(scaledLuma + this.CrToR * redDifference, rounding);
      g = _Clamp(scaledLuma + this.CbToG * blueDifference + this.CrToG * redDifference, rounding);
      b = _Clamp(scaledLuma + this.CbToB * blueDifference, rounding);
    }

    private static int _Fixed(double coefficient) => (int)Math.Round(coefficient * (1 << _FRACTION_BITS));
  }
}
