using System;
using System.Collections.Generic;
using FileFormat.Core;

namespace FileFormat.AtariIce;

/// <summary>In-memory representation of an Interlace Character Editor picture (.ice).</summary>
/// <remarks>
/// A character set, shown as the two alternating fields the editor displayed it in. What makes the
/// format worth its own reader is that the two fields need not be in the same graphics mode: the
/// editor's whole purpose was pairing one ANTIC mode with another, or the same one under a
/// different GTIA setting, so that the two averaged into colours neither could show. There are
/// thirty-three such pairings and the first byte of the file says which.
/// <para/>
/// Version 2.0 dropped the character screen entirely: its pictures are the character set in a fixed
/// arrangement, coloured by a multiplier that changes down the picture, and the two fields take
/// that multiplier in different orders.
/// </remarks>
public readonly record struct AtariIceFile
  : IImageFormatReader<AtariIceFile>, IImageToRawImage<AtariIceFile>,
    IImageFromRawImage<AtariIceFile>, IImageFormatWriter<AtariIceFile> {

  static string IImageFormatMetadata<AtariIceFile>.PrimaryExtension => ".ice";
  static string[] IImageFormatMetadata<AtariIceFile>.FileExtensions => [".ice", ".icn"];
  static AtariIceFile IImageFormatReader<AtariIceFile>.FromSpan(ReadOnlySpan<byte> data)
    => AtariIceReader.FromSpan(data);
  static byte[] IImageFormatWriter<AtariIceFile>.ToBytes(AtariIceFile file)
    => AtariIceWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<AtariIceFile>.VideoModes => [
    new("Atari 8-bit", [(256, 128), (256, 256), (256, 288), (320, 192)], [256])
  ];

  /// <summary>The whole file.</summary>
  public byte[] Data { get; init; }

  /// <summary>Pixels across.</summary>
  public int Width { get; init; }

  /// <summary>Rows.</summary>
  public int Height { get; init; }

  /// <summary>The two fields, in display order.</summary>
  public IceField[] Fields { get; init; }

  public static RawImage ToRawImage(AtariIceFile file) {
    var data = file.Data ?? [];
    var fields = file.Fields ?? [];

    var first = IceRenderer.Render(data, fields[0], file.Width, file.Height);
    var second = IceRenderer.Render(data, fields[1], file.Width, file.Height);

    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Rgb24,
      PixelData = FrameBlend.Average(Atari8BitGraphics.ApplyPalette(first), Atari8BitGraphics.ApplyPalette(second)),
    };
  }

  /// <summary>Width of a picture that is a character set rather than a screen.</summary>
  public const int SheetWidth = 256;

  /// <summary>Height of a picture that is a character set rather than a screen.</summary>
  public const int SheetHeight = 128;

  /// <summary>Bytes one character set occupies: 128 characters of eight rows.</summary>
  public const int CharacterSetSize = 1024;

  /// <summary>The pairing written: both fields Graphics 9 over their own character set.</summary>
  public const int Gtia9PairMode = 6;

  /// <summary>Length of that pairing: the mode byte, a background byte a field, and the two sets.</summary>
  public const int Gtia9PairSize = 3 + CharacterSetSize * 2;

  /// <summary>Nibbles across the screen; each is four pixels wide in Graphics 9.</summary>
  private const int _NIBBLES_ACROSS = SheetWidth / 4;

  /// <summary>Rows a character set can state freely; the other three quarters follow from them.</summary>
  private const int _FREE_ROWS = SheetHeight / 4;

  /// <summary>Which character each of the four eight-row bands of a quarter starts at.</summary>
  /// <remarks>
  /// Not in order, and not ours to choose: the editor showed a set in the arrangement its own screen
  /// used, which puts the control characters on the second band and the upper-case letters first.
  /// </remarks>
  private static ReadOnlySpan<byte> _BandStarts => [64, 0, 32, 96];

  /// <summary>Hue pairings carried forward from the cheap search to the exact one.</summary>
  private const int _HUE_CANDIDATES = 6;

  /// <summary>Distinct colours the cheap search judges a hue pairing by.</summary>
  private const int _SAMPLED_COLORS = 128;

  /// <summary>
  /// Encodes a picture as two character sets shown against each other in Graphics 9.
  /// </summary>
  /// <remarks>
  /// A character set is all the file holds, so the picture is not free: the editor showed the set as
  /// a fixed arrangement 256 by 128, and that arrangement spends each character four times. The
  /// lower half of the screen repeats the upper, and within each half the second quarter is the
  /// photographic negative of the first — so a quarter of the screen, 32 rows, is everything an
  /// encoder gets to say, and the other three quarters are consequences.
  /// <para/>
  /// The two fields are not consequences of each other, though, and that is what makes the format
  /// worth encoding at all. The four quarters show the pair as it stands, both negated, and each
  /// negated alone; averaged, one nibble of each set therefore fixes four screen colours at once.
  /// Every pairing of the two nibbles is tried against all four, which is exact because sixteen by
  /// sixteen is small — a greedy pass would settle one quarter and ruin the negative of it.
  /// <para/>
  /// Graphics 9 gives sixteen luminances of one hue a field, and only the hue is a free choice, one
  /// byte per field. Which two hues average closest to a picture cannot be read off it — a hue the
  /// picture never shows can be half of one it shows everywhere — so the hues are searched, cheaply
  /// over the colours the picture actually contains and then exactly over the few that survive.
  /// </remarks>
  public static AtariIceFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var rgb = image.SampleTo(SheetWidth, SheetHeight).PixelData;
    var statistics = _CellStatistics(rgb);

    var best = new byte[CharacterSetSize * 2];
    var bestHues = 0;
    var bestCost = long.MaxValue;
    var sets = new byte[CharacterSetSize * 2];

    foreach (var hues in _ShortlistHues(rgb)) {
      var cost = _Solve(statistics, hues >> 4, hues & 15, sets);
      if (cost >= bestCost)
        continue;

      bestCost = cost;
      bestHues = hues;
      sets.CopyTo(best, 0);
    }

    var data = new byte[Gtia9PairSize];
    data[0] = Gtia9PairMode;
    data[1] = (byte)((bestHues >> 4) << 4);
    data[2] = (byte)((bestHues & 15) << 4);
    best.CopyTo(data, 3);

    return AtariIceReader.FromSpan(data);
  }

  /// <summary>
  /// What each nibble's four pixels sum to in each of the four quarters, which is all the search
  /// needs of the picture.
  /// </summary>
  /// <remarks>
  /// The error of a flat colour against four pixels expands into the colour's own square, its
  /// product with their sums, and their sum of squares — so the pixels themselves are never touched
  /// again once these are taken, and a hue pairing costs a table lookup a nibble rather than a pass
  /// over the picture.
  /// </remarks>
  private static long[] _CellStatistics(ReadOnlySpan<byte> rgb) {
    var statistics = new long[_FREE_ROWS * _NIBBLES_ACROSS * 4 * 4];
    var at = 0;

    for (var row = 0; row < _FREE_ROWS; ++row)
    for (var nibble = 0; nibble < _NIBBLES_ACROSS; ++nibble)
    for (var quarter = 0; quarter < 4; ++quarter) {
      long red = 0, green = 0, blue = 0, squares = 0;

      for (var pixel = 0; pixel < 4; ++pixel) {
        var source = ((row + quarter * _FREE_ROWS) * SheetWidth + nibble * 4 + pixel) * 3;
        int r = rgb[source], g = rgb[source + 1], b = rgb[source + 2];
        red += r;
        green += g;
        blue += b;
        squares += r * r + g * g + b * b;
      }

      statistics[at++] = red;
      statistics[at++] = green;
      statistics[at++] = blue;
      statistics[at++] = squares;
    }

    return statistics;
  }

  /// <summary>The sixteen by sixteen colours a hue pairing averages to, and each one's own square.</summary>
  private static (int[] Colors, long[] Squares) _BlendTable(int firstHue, int secondHue) {
    var gtia = Atari8BitGraphics.Palette;
    var colors = new int[256 * 3];
    var squares = new long[256];

    for (var first = 0; first < 16; ++first)
    for (var second = 0; second < 16; ++second) {
      var entry = (first * 16 + second) * 3;
      long square = 0;

      for (var channel = 0; channel < 3; ++channel) {
        int a = gtia[((firstHue << 4) | first) * 3 + channel], b = gtia[((secondHue << 4) | second) * 3 + channel];
        var blended = (a & b) + (((a ^ b) >> 1) & 0x7F);
        colors[entry + channel] = blended;
        square += blended * blended;
      }

      squares[first * 16 + second] = square * 4;
    }

    return (colors, squares);
  }

  /// <summary>
  /// Settles both character sets for one hue pairing, and says what the picture costs under it.
  /// </summary>
  private static long _Solve(ReadOnlySpan<long> statistics, int firstHue, int secondHue, Span<byte> sets) {
    var (colors, squares) = _BlendTable(firstHue, secondHue);
    var quarters = new long[4 * 256];
    var at = 0;
    var total = 0L;
    sets.Clear();

    for (var row = 0; row < _FREE_ROWS; ++row)
    for (var nibble = 0; nibble < _NIBBLES_ACROSS; ++nibble) {
      for (var quarter = 0; quarter < 4; ++quarter) {
        long red = statistics[at++], green = statistics[at++], blue = statistics[at++], constant = statistics[at++];

        for (var pair = 0; pair < 256; ++pair) {
          var entry = pair * 3;
          quarters[quarter * 256 + pair] = squares[pair] + constant
            - 2 * (colors[entry] * red + colors[entry + 1] * green + colors[entry + 2] * blue);
        }
      }

      var bestPair = 0;
      var bestCost = long.MaxValue;
      for (var first = 0; first < 16; ++first)
      for (var second = 0; second < 16; ++second) {
        // The quarters show the pair as it stands, both negated, the second negated, the first.
        var cost = quarters[first * 16 + second]
                   + quarters[256 + (15 - first) * 16 + (15 - second)]
                   + quarters[512 + first * 16 + (15 - second)]
                   + quarters[768 + (15 - first) * 16 + second];

        if (cost >= bestCost)
          continue;

        bestCost = cost;
        bestPair = first * 16 + second;
      }

      total += bestCost;
      _Place(sets, row, nibble, bestPair >> 4, bestPair & 15);
    }

    return total;
  }

  /// <summary>Puts one nibble of each character set where the fixed arrangement will show it.</summary>
  private static void _Place(Span<byte> sets, int row, int nibble, int first, int second) {
    var character = _BandStarts[row >> 3] + (nibble >> 1);
    var offset = character * 8 + (row & 7);
    var shift = (nibble & 1) == 0 ? 4 : 0;

    sets[offset] |= (byte)(first << shift);
    sets[CharacterSetSize + offset] |= (byte)(second << shift);
  }

  /// <summary>
  /// The hue pairings worth settling exactly, judged by how close their averages come to the colours
  /// the picture is actually made of.
  /// </summary>
  /// <remarks>
  /// Judged without the arrangement, because that is what makes it cheap — the question here is only
  /// whether two hues can reach the picture's colours at all, and a pairing that cannot will not be
  /// rescued by placing its nibbles well. The exact search then decides between the survivors, which
  /// it must: this measure cannot tell a pairing from the same pairing with its fields exchanged,
  /// and the arrangement is not symmetric between them.
  /// </remarks>
  private static int[] _ShortlistHues(ReadOnlySpan<byte> rgb) {
    var histogram = new Dictionary<int, int>();
    for (var at = 0; at + 2 < rgb.Length; at += 3) {
      var key = (rgb[at] << 16) | (rgb[at + 1] << 8) | rgb[at + 2];
      histogram[key] = histogram.GetValueOrDefault(key) + 1;
    }

    var colors = new List<KeyValuePair<int, int>>(histogram);
    colors.Sort((left, right) => right.Value != left.Value ? right.Value - left.Value : left.Key - right.Key);
    var sampled = Math.Min(colors.Count, _SAMPLED_COLORS);

    var costs = new long[256];
    for (var pairing = 0; pairing < 256; ++pairing) {
      var (blended, _) = _BlendTable(pairing >> 4, pairing & 15);
      var total = 0L;

      for (var index = 0; index < sampled; ++index) {
        var color = colors[index].Key;
        int red = (color >> 16) & 255, green = (color >> 8) & 255, blue = color & 255;
        var nearest = long.MaxValue;

        for (var pair = 0; pair < 256; ++pair) {
          var entry = pair * 3;
          long dr = blended[entry] - red, dg = blended[entry + 1] - green, db = blended[entry + 2] - blue;
          nearest = Math.Min(nearest, dr * dr + dg * dg + db * db);
        }

        total += nearest * colors[index].Value;
      }

      costs[pairing] = total;
    }

    var shortlist = new int[_HUE_CANDIDATES];
    for (var slot = 0; slot < shortlist.Length; ++slot) {
      var best = 0;
      for (var pairing = 1; pairing < 256; ++pairing)
        if (costs[pairing] < costs[best])
          best = pairing;

      shortlist[slot] = best;
      costs[best] = long.MaxValue;
    }

    return shortlist;
  }
}
