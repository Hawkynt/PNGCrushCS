using System;
using FileFormat.Core;

namespace FileFormat.Apac3;

/// <summary>
/// Chooses the four nibble streams a pair of APAC fields is built from.
/// </summary>
/// <remarks>
/// APAC pairs a Graphics 9 luminance field with a Graphics 11 hue field on alternate scanlines and
/// lets the display average two such pairs. Neither half is a picture: a hue row carries no
/// luminance of its own and takes the mean of its neighbours', and the row below it keeps its own
/// luminance and merely gains the hue — so a nibble reaches four scanlines and the choice cannot be
/// made a row at a time.
/// <para/>
/// The four streams are settled by improving one at a time against the other three, which is enough
/// because they interact only through that vertical smear: a nibble's neighbours in the same stream
/// are two scanlines away and the picture is not usually asking for something different there.
/// <para/>
/// Shared by the two formats that store exactly this — APAC 3 and Bugbiter's APAC239i — which differ
/// in where the four blocks sit and how many scanlines they cover, not in what the blocks mean.
/// </remarks>
internal static class ApacInterlaceEncoder {

  /// <summary>Screen pixels one nibble covers.</summary>
  public const int PixelsPerNibble = 4;

  /// <summary>Passes of improvement over the four streams.</summary>
  private const int _PASSES = 4;

  /// <summary>The four nibble streams of an APAC picture, one value per nibble per stored row.</summary>
  public sealed class Streams {

    public Streams(int rows, int nibbles) {
      this.Rows = rows;
      this.Nibbles = nibbles;
      this.FirstLuminance = new int[rows * nibbles];
      this.FirstHue = new int[rows * nibbles];
      this.SecondLuminance = new int[rows * nibbles];
      this.SecondHue = new int[rows * nibbles];
    }

    public int Rows { get; }
    public int Nibbles { get; }
    public int[] FirstLuminance { get; }
    public int[] FirstHue { get; }
    public int[] SecondLuminance { get; }
    public int[] SecondHue { get; }
  }

  /// <summary>Chooses the streams that draw a picture as closely as the format allows.</summary>
  /// <param name="rgb">The picture, three bytes a pixel.</param>
  /// <param name="width">Screen pixels across.</param>
  /// <param name="height">Scanlines.</param>
  public static Streams Encode(ReadOnlySpan<byte> rgb, int width, int height) {
    var nibbles = width / PixelsPerNibble;
    var rows = (height + 1) / 2;
    var streams = new Streams(rows, nibbles);
    var gtia = Atari8BitGraphics.Palette;

    // What every pair of colour bytes looks like once the display has averaged the two fields.
    var blend = new byte[256 * 256 * 3];
    for (var first = 0; first < 256; ++first)
    for (var second = 0; second < 256; ++second)
    for (var channel = 0; channel < 3; ++channel) {
      int a = gtia[first * 3 + channel], b = gtia[second * 3 + channel];
      blend[((first << 8 | second) * 3) + channel] = (byte)((a & b) + (((a ^ b) >> 1) & 0x7F));
    }

    _Initialise(rgb, width, height, gtia, streams);

    for (var pass = 0; pass < _PASSES; ++pass)
    for (var row = 0; row < rows; ++row)
    for (var nibble = 0; nibble < nibbles; ++nibble) {
      _Improve(rgb, blend, width, height, streams, row, nibble, streams.FirstLuminance, row * 2 - 1, 3);
      _Improve(rgb, blend, width, height, streams, row, nibble, streams.FirstHue, row * 2 + 1, 2);
      _Improve(rgb, blend, width, height, streams, row, nibble, streams.SecondLuminance, row * 2, 3);
      _Improve(rgb, blend, width, height, streams, row, nibble, streams.SecondHue, row * 2, 2);
    }

    // A field's luminance and hue are then moved together as well, because the first stored row
    // shows one field's luminance against the other's halved and no single move reaches it.
    for (var row = 0; row < rows; ++row)
    for (var nibble = 0; nibble < nibbles; ++nibble) {
      _ImprovePair(
        rgb, blend, width, height, streams, row, nibble,
        streams.FirstLuminance, streams.FirstHue, row * 2 - 1, 4);
      _ImprovePair(
        rgb, blend, width, height, streams, row, nibble,
        streams.SecondLuminance, streams.SecondHue, row * 2, 3);
    }

    return streams;
  }

  /// <summary>
  /// Starts both fields off holding the colour each pair of scanlines is nearest to, which is what
  /// the picture would be if the format did not smear a nibble across its neighbours.
  /// </summary>
  private static void _Initialise(
    ReadOnlySpan<byte> rgb, int width, int height, ReadOnlySpan<byte> gtia, Streams streams) {
    for (var row = 0; row < streams.Rows; ++row)
    for (var nibble = 0; nibble < streams.Nibbles; ++nibble) {
      int red = 0, green = 0, blue = 0, count = 0;

      for (var y = row * 2; y < row * 2 + 2 && y < height; ++y)
      for (var x = nibble * PixelsPerNibble; x < (nibble + 1) * PixelsPerNibble; ++x) {
        var at = (y * width + x) * 3;
        red += rgb[at];
        green += rgb[at + 1];
        blue += rgb[at + 2];
        ++count;
      }

      var colour = Atari8BitGraphics.FindNearestColorByte(
        gtia, (byte)(red / count), (byte)(green / count), (byte)(blue / count));

      var at2 = row * streams.Nibbles + nibble;
      streams.FirstLuminance[at2] = streams.SecondLuminance[at2] = colour & 15;
      streams.FirstHue[at2] = streams.SecondHue[at2] = colour >> 4;
    }
  }

  /// <summary>Moves one nibble to whichever value draws the scanlines it reaches best.</summary>
  private static void _Improve(
    ReadOnlySpan<byte> rgb, ReadOnlySpan<byte> blend, int width, int height, Streams streams,
    int row, int nibble, int[] stream, int firstRow, int rowCount) {
    var at = row * streams.Nibbles + nibble;
    var best = stream[at];
    var bestCost = long.MaxValue;

    for (var value = 0; value < 16; ++value) {
      stream[at] = value;

      long cost = 0;
      for (var y = firstRow; y < firstRow + rowCount; ++y) {
        if (y < 0 || y >= height)
          continue;

        var entry = ((_First(streams, height, y, nibble) << 8) | _Second(streams, height, y, nibble)) * 3;

        for (var x = nibble * PixelsPerNibble; x < (nibble + 1) * PixelsPerNibble; ++x) {
          var source = (y * width + x) * 3;
          int dr = blend[entry] - rgb[source];
          int dg = blend[entry + 1] - rgb[source + 1];
          int db = blend[entry + 2] - rgb[source + 2];
          cost += dr * dr + dg * dg + db * db;
        }
      }

      if (cost >= bestCost)
        continue;

      bestCost = cost;
      best = value;
    }

    stream[at] = best;
  }

  /// <summary>Moves two nibbles to whichever pair of values draws the scanlines they reach best.</summary>
  private static void _ImprovePair(
    ReadOnlySpan<byte> rgb, ReadOnlySpan<byte> blend, int width, int height, Streams streams,
    int row, int nibble, int[] left, int[] right, int firstRow, int rowCount) {
    var at = row * streams.Nibbles + nibble;
    int bestLeft = left[at], bestRight = right[at];
    var bestCost = long.MaxValue;

    for (var first = 0; first < 16; ++first) {
      left[at] = first;

      for (var second = 0; second < 16; ++second) {
        right[at] = second;

        long cost = 0;
        for (var y = firstRow; y < firstRow + rowCount; ++y) {
          if (y < 0 || y >= height)
            continue;

          var entry = ((_First(streams, height, y, nibble) << 8) | _Second(streams, height, y, nibble)) * 3;

          for (var x = nibble * PixelsPerNibble; x < (nibble + 1) * PixelsPerNibble; ++x) {
            var source = (y * width + x) * 3;
            int dr = blend[entry] - rgb[source];
            int dg = blend[entry + 1] - rgb[source + 1];
            int db = blend[entry + 2] - rgb[source + 2];
            cost += dr * dr + dg * dg + db * db;
          }
        }

        if (cost >= bestCost)
          continue;

        bestCost = cost;
        bestLeft = first;
        bestRight = second;
      }
    }

    left[at] = bestLeft;
    right[at] = bestRight;
  }

  /// <summary>
  /// The colour byte the first field shows on a scanline: luminance on the even ones, hue rows
  /// taking the mean of the luminances above and below.
  /// </summary>
  public static int _First(Streams streams, int height, int y, int nibble) {
    var row = y >> 1;

    if ((y & 1) == 0) {
      var hue = row > 0 ? streams.FirstHue[(row - 1) * streams.Nibbles + nibble] : 0;

      return (hue << 4) | streams.FirstLuminance[row * streams.Nibbles + nibble];
    }

    var above = streams.FirstLuminance[row * streams.Nibbles + nibble];
    var below = y == height - 1 ? 0 : streams.FirstLuminance[(row + 1) * streams.Nibbles + nibble];

    return (streams.FirstHue[row * streams.Nibbles + nibble] << 4) | ((above + below) >> 1);
  }

  /// <summary>
  /// The colour byte the second field shows, which is the first field's arrangement the other way
  /// up: its luminances are on the odd scanlines and its hue rows on the even ones.
  /// </summary>
  public static int _Second(Streams streams, int height, int y, int nibble) {
    var row = y >> 1;
    var hue = streams.SecondHue[row * streams.Nibbles + nibble] << 4;

    if ((y & 1) != 0)
      return hue | streams.SecondLuminance[row * streams.Nibbles + nibble];

    var above = row > 0 ? streams.SecondLuminance[(row - 1) * streams.Nibbles + nibble] : 0;
    var below = y == height - 1 ? 0 : streams.SecondLuminance[row * streams.Nibbles + nibble];

    return hue | ((above + below) >> 1);
  }

  /// <summary>Writes one stream of nibbles into a field's rows, high half of a byte first.</summary>
  public static void Pack(ReadOnlySpan<int> stream, Span<byte> target, int offset, int stride, int rows, int nibbles) {
    for (var row = 0; row < rows; ++row)
    for (var nibble = 0; nibble < nibbles; ++nibble) {
      var at = offset + row * stride + (nibble >> 1);
      if (at < 0 || at >= target.Length)
        continue;

      target[at] |= (byte)(stream[row * nibbles + nibble] << ((nibble & 1) == 0 ? 4 : 0));
    }
  }
}
