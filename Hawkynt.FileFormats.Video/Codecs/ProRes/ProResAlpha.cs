using System;
using System.IO;

namespace FileFormat.Codecs.ProRes;

/// <summary>
/// The alpha channel of a slice: run lengths and differences, with no transform anywhere in it.
/// </summary>
/// <remarks>
/// RDD 36:2022, 5.3.3 for the syntax and 7.1.2 for the codes. Alpha is coded quite differently from
/// colour and much more simply — there is no DCT, no quantisation and no scanning. A slice's alpha
/// is a raster-scanned array of values, coded as runs of equal value with the difference between one
/// run's value and the last one's. That makes it <b>lossless</b>, which is the point: a matte that
/// had been through a transform would have soft edges, and a codec used to composite cannot afford
/// them.
/// <para/>
/// <b>Two codes, chosen by the alpha depth.</b> Differences of small magnitude get a short code;
/// everything else, including a difference of exactly zero, escapes to a fixed-length code of the
/// full sample width. The escape is also what the specification calls a <i>modulo</i> difference:
/// only the escaped form wraps, because only it can carry a value whose wrapped and unwrapped
/// readings differ. Applying the mask to the short form as well changes nothing, which 7.1.2 says
/// explicitly and this relies on to keep one path.
/// </remarks>
internal static class ProResAlpha {

  /// <summary>The longest run the code can express, RDD 36:2022, 7.1.2.</summary>
  private const int _MAXIMUM_RUN = 2048;

  /// <summary>
  /// Decodes one slice's alpha values into a plane.
  /// </summary>
  /// <param name="data">The slice's alpha data, which run to the end of the slice.</param>
  /// <param name="alphaChannelType">1 for 8-bit alpha, 2 for 16-bit.</param>
  /// <param name="target">The frame's alpha plane.</param>
  /// <param name="planeWidth">The width of that plane in samples.</param>
  /// <param name="planeHeight">The height of that plane; rows past it are discarded.</param>
  /// <param name="originX">The slice's left column in the plane.</param>
  /// <param name="originY">The slice's top row within the picture, before field mapping.</param>
  /// <param name="sliceWidth">The slice's width in samples, a whole number of macroblocks.</param>
  /// <param name="sliceHeight">The slice's height in samples: 16, or less in the last macroblock row.</param>
  /// <param name="fieldOffset">The plane row picture row 0 maps to.</param>
  /// <param name="fieldStep">1 for a frame picture, 2 for a field picture.</param>
  internal static void Decode(
    ReadOnlyMemory<byte> data,
    int alphaChannelType,
    ushort[] target,
    int planeWidth,
    int planeHeight,
    int originX,
    int originY,
    int sliceWidth,
    int sliceHeight,
    int fieldOffset,
    int fieldStep) {
    var bits = new ProResBitReader(data);
    var eightBit = alphaChannelType == 1;
    var mask = eightBit ? 0xFF : 0xFFFF;
    var count = sliceWidth * sliceHeight;

    // 5.3.3: the previous alpha of the first run is −1, so a slice that begins fully opaque codes a
    // difference of one rather than a difference of 255 or 65535.
    var previous = -1;
    var at = 0;

    while (at < count) {
      var difference = eightBit ? _ReadDifference(bits, 3, 8) : _ReadDifference(bits, 6, 16);
      var alpha = (previous + difference) & mask;
      previous = alpha;

      var run = _ReadRun(bits);
      for (var m = 0; m < run && at < count; ++m, ++at) {
        var y = at / sliceWidth;
        var x = at - y * sliceWidth;

        // 7.5.3: the array carries alpha for the excess pixels at the right of the last slice of a
        // row, and those are discarded rather than written.
        var column = originX + x;
        if (column >= planeWidth)
          continue;

        var row = fieldOffset + (originY + y) * fieldStep;
        if (row >= planeHeight)
          continue;

        target[row * planeWidth + column] = (ushort)alpha;
      }
    }
  }

  /// <summary>
  /// One run length, RDD 36:2022, Table 12.
  /// </summary>
  /// <remarks>
  /// A single '1' is a run of one, which is the common case for a matte with soft edges and costs a
  /// bit. A '0' followed by four more bits is a run of 2 to 16, the five bits together being one less
  /// than the run. Five zeroes escape to eleven bits holding one less than a run of up to 2048, which
  /// is what a slice of solid opacity uses.
  /// </remarks>
  private static int _ReadRun(ProResBitReader bits) {
    if (bits.Bit() != 0)
      return 1;

    var low = (int)bits.Bits(4);
    if (low != 0)
      return low + 1;

    var escaped = (int)bits.Bits(11) + 1;
    if (escaped > _MAXIMUM_RUN)
      throw new InvalidDataException(
        $"A ProRes alpha run of {escaped} exceeds the {_MAXIMUM_RUN} the code can express, so the slice's alpha data are damaged.");

    return escaped;
  }

  /// <summary>
  /// One alpha difference, RDD 36:2022, Tables 13 and 14.
  /// </summary>
  /// <remarks>
  /// The two tables are the same shape at two widths, so one routine serves both: a leading '0' then
  /// the magnitude less one and a sign bit, or a leading '1' then the difference itself at the full
  /// sample width. The escaped form is the one 7.1.2 calls a modulo difference, and the caller masks
  /// unconditionally because masking the short form has no effect.
  /// </remarks>
  /// <param name="magnitudeBits">Three for 8-bit alpha, six for 16-bit.</param>
  /// <param name="escapeBits">Eight for 8-bit alpha, sixteen for 16-bit.</param>
  private static int _ReadDifference(ProResBitReader bits, int magnitudeBits, int escapeBits) {
    if (bits.Bit() != 0)
      return (int)bits.Bits(escapeBits);

    var magnitude = (int)bits.Bits(magnitudeBits) + 1;

    return bits.Bit() != 0 ? -magnitude : magnitude;
  }
}
