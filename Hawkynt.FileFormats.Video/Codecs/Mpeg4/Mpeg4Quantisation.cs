using System;

namespace FileFormat.Codecs.Mpeg4;

/// <summary>
/// The scan orders of ISO/IEC 14496-2 clause 7.4.2 and the two inverse quantisation methods of
/// clause 7.4.4.
/// </summary>
/// <remarks>
/// Two methods, chosen by <c>quant_type</c> in the video object layer, and they are not
/// approximations of each other. The H.263 method has one step size for the whole block and a
/// formula; the MPEG method weights every coefficient by its position in a matrix. Reading a stream
/// coded with one as though it used the other gives a picture whose detail is at the wrong contrast
/// everywhere — which still looks like a picture, and is the reason the flag is read rather than
/// assumed.
/// <para/>
/// Three scan orders rather than one, because an intra block whose coefficients were predicted from a
/// neighbour is scanned in the direction the prediction came from. Using the zig-zag for all three
/// leaves every AC-predicted block's coefficients in the wrong places, which is a block that decodes
/// and is wrong.
/// </remarks>
internal static class Mpeg4Quantisation {

  /// <summary>
  /// The zig-zag scan of ISO/IEC 14496-2 Figure 7-2: scan position to raster position within a block.
  /// </summary>
  internal static readonly int[] ZigZag = [
     0,  1,  8, 16,  9,  2,  3, 10,
    17, 24, 32, 25, 18, 11,  4,  5,
    12, 19, 26, 33, 40, 48, 41, 34,
    27, 20, 13,  6,  7, 14, 21, 28,
    35, 42, 49, 56, 57, 50, 43, 36,
    29, 22, 15, 23, 30, 37, 44, 51,
    58, 59, 52, 45, 38, 31, 39, 46,
    53, 60, 61, 54, 47, 55, 62, 63,
  ];

  /// <summary>
  /// The alternate-horizontal scan of ISO/IEC 14496-2 Figure 7-3, used by a block whose coefficients
  /// were predicted from the block above it.
  /// </summary>
  internal static readonly int[] AlternateHorizontal = [
     0,  1,  2,  3,  8,  9, 16, 17,
    10, 11,  4,  5,  6,  7, 15, 14,
    13, 12, 19, 18, 24, 25, 32, 33,
    26, 27, 20, 21, 22, 23, 28, 29,
    30, 31, 34, 35, 40, 41, 48, 49,
    42, 43, 36, 37, 38, 39, 44, 45,
    46, 47, 50, 51, 56, 57, 58, 59,
    52, 53, 54, 55, 60, 61, 62, 63,
  ];

  /// <summary>
  /// The alternate-vertical scan of ISO/IEC 14496-2 Figure 7-4, used by a block whose coefficients
  /// were predicted from the block to its left.
  /// </summary>
  internal static readonly int[] AlternateVertical = [
     0,  8, 16, 24,  1,  9,  2, 10,
    17, 25, 32, 40, 48, 56, 57, 49,
    41, 33, 26, 18,  3, 11,  4, 12,
    19, 27, 34, 42, 50, 58, 35, 43,
    51, 59, 20, 28,  5, 13,  6, 14,
    21, 29, 36, 44, 52, 60, 37, 45,
    53, 61, 22, 30,  7, 15, 23, 31,
    38, 46, 54, 62, 39, 47, 55, 63,
  ];

  /// <summary>The default intra weighting matrix of ISO/IEC 14496-2 clause 7.4.4, in raster order.</summary>
  internal static readonly byte[] DefaultIntraMatrix = [
     8, 17, 18, 19, 21, 23, 25, 27,
    17, 18, 19, 21, 23, 25, 27, 28,
    20, 21, 22, 23, 24, 26, 28, 30,
    21, 22, 23, 24, 26, 28, 30, 32,
    22, 23, 24, 26, 28, 30, 32, 35,
    23, 24, 26, 28, 30, 32, 35, 38,
    25, 26, 28, 30, 32, 35, 38, 41,
    27, 28, 30, 32, 35, 38, 41, 45,
  ];

  /// <summary>The default non-intra weighting matrix, in raster order.</summary>
  internal static readonly byte[] DefaultNonIntraMatrix = [
    16, 17, 18, 19, 20, 21, 22, 23,
    17, 18, 19, 20, 21, 22, 23, 24,
    18, 19, 20, 21, 22, 23, 24, 25,
    19, 20, 21, 22, 23, 24, 26, 27,
    20, 21, 22, 23, 25, 26, 27, 28,
    21, 22, 23, 24, 26, 27, 28, 30,
    22, 23, 24, 26, 27, 28, 30, 31,
    23, 24, 25, 27, 28, 30, 31, 33,
  ];

  /// <summary>
  /// The step the DC coefficient of an intra block is quantised with (ISO/IEC 14496-2, Table 7-3).
  /// </summary>
  /// <remarks>
  /// Not a constant, and not the quantiser either. MPEG-4 keeps the DC finer than the rest of the
  /// block at every quantiser and finer still at the coarse end, because the DC is what the next
  /// block's own DC is predicted from and an error in it spreads sideways across the picture rather
  /// than staying in its block. Using the quantiser here instead leaves flat regions banded in a way
  /// that grows towards the right and bottom of every picture.
  /// </remarks>
  internal static int DcScaler(int quantiser, bool isLuminance) {
    if (isLuminance)
      return quantiser switch {
        < 5 => 8,
        < 9 => 2 * quantiser,
        < 25 => quantiser + 8,
        _ => 2 * quantiser - 16,
      };

    return quantiser switch {
      < 5 => 8,
      < 25 => (quantiser + 13) / 2,
      _ => quantiser - 6,
    };
  }

  /// <summary>
  /// Reconstructs one coefficient by the H.263 method (<c>quant_type</c> zero).
  /// </summary>
  /// <remarks>
  /// Two rules in one: the reconstruction level is always an odd multiple of the step size, and at an
  /// even step size the result is pulled one towards zero. The first is what keeps two conforming
  /// inverse transforms from drifting apart; the second is what makes the reconstruction points of an
  /// even step size fall halfway between those of the odd one below it.
  /// </remarks>
  internal static int DequantiseH263(int level, int quantiser) {
    if (level == 0)
      return 0;

    var magnitude = quantiser * (2 * (level < 0 ? -level : level) + 1);
    if ((quantiser & 1) == 0)
      --magnitude;

    return Clamp(level < 0 ? -magnitude : magnitude);
  }

  /// <summary>Reconstructs one coefficient of an intra block by the MPEG method (<c>quant_type</c> one).</summary>
  internal static int DequantiseMpegIntra(int level, int quantiser, int weight) {
    if (level == 0)
      return 0;

    return Clamp(level * weight * quantiser * 2 / 16);
  }

  /// <summary>
  /// Reconstructs one coefficient of a non-intra block by the MPEG method.
  /// </summary>
  /// <remarks>
  /// The extra <c>+ sign(level)</c> before the multiply is the non-intra rule and the whole of the
  /// difference from the intra one: it biases each level away from zero by half a step, because a
  /// non-intra level of <c>n</c> stands for the interval whose centre is at <c>n</c> plus a half
  /// rather than at <c>n</c>. Leaving it out darkens every predicted picture slightly and
  /// progressively, which looks like drift rather than like a missing term.
  /// </remarks>
  internal static int DequantiseMpegNonIntra(int level, int quantiser, int weight) {
    if (level == 0)
      return 0;

    var sign = level < 0 ? -1 : 1;
    return Clamp((2 * level + sign) * weight * quantiser / 16);
  }

  /// <summary>Saturates to the range a reconstructed coefficient is defined over (ISO/IEC 14496-2, 7.4.4).</summary>
  internal static int Clamp(int value) => value < -2048 ? -2048 : value > 2047 ? 2047 : value;

  /// <summary>
  /// The mismatch control of ISO/IEC 14496-2 clause 7.4.4.5, which the MPEG quantisation method needs
  /// and the H.263 one does not.
  /// </summary>
  /// <remarks>
  /// Two conforming inverse transforms are allowed to disagree in the last bit, and prediction would
  /// carry that disagreement forward from picture to picture. The H.263 method stops it by making
  /// every reconstruction level an odd multiple of the step size; the MPEG method's weighting
  /// matrices make that impossible, so it stops it here instead, by forcing the sum of a block's
  /// coefficients to be odd — which it does by moving the very last coefficient one, in whichever
  /// direction keeps it from being zero when it was not.
  /// <para/>
  /// <b>Applied to non-intra blocks only, which the standard's own summary does not say.</b> Clause
  /// 7.4.4.5 and the pseudo-code of 7.4.4.6 both run this over every block of a layer using the MPEG
  /// method, intra and non-intra alike. The reference decoder this library is measured against
  /// applies it to non-intra blocks alone, and the difference is not a matter of taste: over a
  /// twenty-five frame stream coded with the MPEG method, leaving it off for intra blocks reproduces
  /// the reference decoder's luminance plane exactly on every frame, and turning it on for them puts
  /// fourteen thousand samples one level out — in the predicted pictures as well as the intra ones,
  /// because the error is in the reference they predict from. An encoder's own reconstruction loop is
  /// what a predicted picture is built against, so following the reference decoder here is what keeps
  /// this decoder in step with the encoders that exist.
  /// </remarks>
  internal static void ControlMismatch(Span<int> coefficients) {
    var sum = 0;
    for (var i = 0; i < 64; ++i)
      sum += coefficients[i];

    if ((sum & 1) != 0)
      return;

    coefficients[63] = (coefficients[63] & 1) != 0 ? coefficients[63] - 1 : coefficients[63] + 1;
  }
}
