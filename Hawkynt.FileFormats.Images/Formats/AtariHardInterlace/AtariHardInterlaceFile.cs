using System;
using FileFormat.Core;

namespace FileFormat.AtariHardInterlace;

/// <summary>In-memory representation of an Atari 8-bit Hard Interlace Picture (.hip, .hps).</summary>
/// <remarks>
/// Two screens shown on alternate television fields and averaged by the eye — the same idea as
/// APAC, but with the halves in different graphics modes rather than on different scanlines. One
/// field is Graphics 9, sixteen luminances of a single hue; the other is Graphics 10, whose pixels
/// index colour registers. Averaging a luminance ramp against freely chosen colours reaches shades
/// neither mode can show on its own.
/// <para/>
/// The two fields are stored one after the other at forty bytes a row, and the Graphics 10 one sits
/// a pixel to the left of the other — a consequence of how the mode is timed, and something the
/// picture is drawn to expect rather than something to correct.
/// </remarks>
public readonly record struct AtariHardInterlaceFile
  : IImageFormatReader<AtariHardInterlaceFile>, IImageToRawImage<AtariHardInterlaceFile>,
    IImageFromRawImage<AtariHardInterlaceFile>, IImageFormatWriter<AtariHardInterlaceFile> {

  /// <summary>Displayed width.</summary>
  public const int Width = 320;

  /// <summary>Bytes one field's row occupies.</summary>
  public const int RowStride = 40;

  /// <summary>Bytes one row of the picture occupies across both fields.</summary>
  public const int PairStride = RowStride * 2;

  /// <summary>Bytes of colour registers a file carries when it has room for them.</summary>
  public const int RegisterBlockSize = Atari8BitGraphics.RegisterCount;

  /// <summary>Largest picture the display can show.</summary>
  public const int MaxHeight = 240;

  /// <summary>The registers a file uses when it stores none: a plain luminance ramp.</summary>
  public static ReadOnlySpan<byte> DefaultRegisters => [0, 0, 2, 4, 6, 8, 10, 12, 14];

  static string IImageFormatMetadata<AtariHardInterlaceFile>.PrimaryExtension => ".hip";
  static string[] IImageFormatMetadata<AtariHardInterlaceFile>.FileExtensions => [".hip", ".hps"];
  static AtariHardInterlaceFile IImageFormatReader<AtariHardInterlaceFile>.FromSpan(ReadOnlySpan<byte> data)
    => AtariHardInterlaceReader.FromSpan(data);
  static byte[] IImageFormatWriter<AtariHardInterlaceFile>.ToBytes(AtariHardInterlaceFile file)
    => AtariHardInterlaceWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<AtariHardInterlaceFile>.VideoModes => [
    new("Hard Interlace", [(Width, IntegerRange.Any)], [256])
  ];

  /// <summary>Picture height in scanlines.</summary>
  public int Height { get; init; }

  /// <summary>The Graphics 9 field, one nibble per logical pixel.</summary>
  public byte[] Luminances { get; init; }

  /// <summary>The Graphics 10 field, one nibble per logical pixel.</summary>
  public byte[] Colors { get; init; }

  /// <summary>The nine colour registers the Graphics 10 field draws from.</summary>
  public byte[] Registers { get; init; }

  /// <summary>Reads a nibble; each covers four screen pixels, high half of a byte first.</summary>
  private static int _Nibble(ReadOnlySpan<byte> data, int rowOffset, int x) {
    if (x < 0 || x >= Width)
      return 0;

    var index = rowOffset + (x >> 3);
    if (index >= data.Length)
      return 0;

    return (x & 4) == 0 ? data[index] >> 4 : data[index] & 15;
  }

  public static RawImage ToRawImage(AtariHardInterlaceFile file) {
    var height = file.Height;
    var gtia = Atari8BitGraphics.Palette;
    var entries = Atari8BitGraphics.ExpandGr10Registers(file.Registers ?? []);
    var luminances = file.Luminances ?? [];
    var colors = file.Colors ?? [];

    var first = new byte[Width * height * 3];
    var second = new byte[Width * height * 3];

    for (var y = 0; y < height; ++y)
    for (var x = 0; x < Width; ++x) {
      var target = (y * Width + x) * 3;

      // Graphics 9: a luminance on a black background, one pixel right of the other field.
      _Write(first, target, gtia, _Nibble(luminances, y * RowStride, x + 1));

      // Graphics 10: an index into the sixteen entries the nine registers fill.
      _Write(second, target, gtia, entries[_Nibble(colors, y * RowStride, x - 1)]);
    }

    return new() {
      Width = Width,
      Height = height,
      Format = PixelFormat.Rgb24,
      PixelData = Atari8BitGraphics.BlendFrames(first, second),
    };
  }

  private static void _Write(byte[] rgb, int offset, ReadOnlySpan<byte> gtia, int color) {
    var entry = (color & 0xFF) * 3;
    rgb[offset] = gtia[entry];
    rgb[offset + 1] = gtia[entry + 1];
    rgb[offset + 2] = gtia[entry + 2];
  }

  /// <summary>Nibbles one field's row holds, each covering four screen pixels.</summary>
  public const int NibblesPerRow = Width / 4;

  /// <summary>
  /// Encodes a picture as a Graphics 9 luminance field averaged with a Graphics 10 colour one.
  /// </summary>
  /// <remarks>
  /// Neither field is a picture: what a pixel shows is the average of a grey and a register colour,
  /// and the two fields are displaced against each other, so a nibble of one overlaps two nibbles of
  /// the other. Written out in the order luminance nibble, colour nibble, luminance nibble, every
  /// pixel's cost falls on two neighbours and the row becomes a chain — which a single pass of
  /// dynamic programming settles exactly, rather than the guess-and-improve that a picture with
  /// several equally good readings leaves stuck.
  /// <para/>
  /// The colour field sits a pixel to the left of the luminance one, which the picture is drawn to
  /// expect rather than something to correct — so the leftmost screen column has no colour nibble
  /// behind it and the rightmost has no luminance nibble. The first entry is therefore left black:
  /// it is what the left edge falls back to, and spending it on a colour would stripe that edge with
  /// something nobody chose.
  /// </remarks>
  public static AtariHardInterlaceFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var height = Math.Clamp(image.Height, 1, MaxHeight);
    var rgb = image.SampleTo(Width, height).PixelData;
    var gtia = Atari8BitGraphics.Palette;

    var registers = _ChooseRegisters(rgb, Width * height, gtia);
    var entries = Atari8BitGraphics.ExpandGr10Registers(registers);

    // What every pairing of a grey with an entry looks like once the display has averaged them.
    var blended = new byte[16 * Atari8BitGraphics.Gr10EntryCount * 3];
    for (var luminance = 0; luminance < 16; ++luminance)
    for (var entry = 0; entry < Atari8BitGraphics.Gr10EntryCount; ++entry)
    for (var channel = 0; channel < 3; ++channel) {
      int a = gtia[luminance * 3 + channel], b = gtia[entries[entry] * 3 + channel];
      blended[(luminance * Atari8BitGraphics.Gr10EntryCount + entry) * 3 + channel] =
        (byte)((a & b) + (((a ^ b) >> 1) & 0x7F));
    }

    var fieldSize = height * RowStride;
    var luminances = new byte[fieldSize];
    var colors = new byte[fieldSize];

    var chain = new int[NibblesPerRow * 2];

    for (var y = 0; y < height; ++y) {
      _SolveRow(rgb, blended, y, chain);

      for (var nibble = 0; nibble < NibblesPerRow; ++nibble) {
        var shift = (nibble & 1) == 0 ? 4 : 0;
        luminances[y * RowStride + (nibble >> 1)] |= (byte)(chain[nibble * 2] << shift);
        colors[y * RowStride + (nibble >> 1)] |= (byte)(chain[nibble * 2 + 1] << shift);
      }
    }

    return new() { Height = height, Luminances = luminances, Colors = colors, Registers = registers };
  }

  /// <summary>
  /// Settles one row's nibbles, luminance and colour alternating, at the least total error there is.
  /// </summary>
  /// <remarks>
  /// Taken in that order every screen pixel's cost falls on two neighbouring nibbles: the two pixels
  /// after a luminance nibble starts are shared with the colour nibble of the same number, and the
  /// two after that with the next luminance nibble. So the row is a chain and one forward pass with
  /// sixteen states settles it, which matters because several pairings of a grey with an entry
  /// produce the same colour — an encoder that improves one nibble at a time settles on whichever it
  /// met first and cannot get out of it.
  /// </remarks>
  private static void _SolveRow(ReadOnlySpan<byte> rgb, ReadOnlySpan<byte> blended, int y, Span<int> chain) {
    const int states = 16;
    var length = chain.Length;
    var totals = new long[length * states];
    var previous = new int[length * states];
    var row = y * Width * 3;

    // The leftmost pixel has no colour nibble behind it and falls to the first entry.
    for (var luminance = 0; luminance < states; ++luminance)
      totals[luminance] = _Blended(blended, luminance * states, rgb[row], rgb[row + 1], rgb[row + 2]);

    for (var step = 0; step + 1 < length; ++step) {
      var luminanceFirst = (step & 1) == 0;
      var from = (step >> 1) * 4 + (luminanceFirst ? 1 : 3);

      for (var next = 0; next < states; ++next) {
        var best = 0;
        var bestTotal = long.MaxValue;

        for (var current = 0; current < states; ++current) {
          var pair = luminanceFirst ? current * states + next : next * states + current;
          var total = totals[step * states + current];
          for (var x = from; x < from + 2 && x < Width; ++x) {
            var at = row + x * 3;
            total += _Blended(blended, pair, rgb[at], rgb[at + 1], rgb[at + 2]);
          }

          if (total >= bestTotal)
            continue;

          bestTotal = total;
          best = current;
        }

        totals[(step + 1) * states + next] = bestTotal;
        previous[(step + 1) * states + next] = best;
      }
    }

    // The rightmost pixel has no luminance nibble behind it and falls to the first grey.
    var last = (length - 1) * states;
    var at319 = row + (Width - 1) * 3;
    for (var color = 0; color < states; ++color)
      totals[last + color] += _Blended(blended, color, rgb[at319], rgb[at319 + 1], rgb[at319 + 2]);

    var end = 0;
    for (var state = 1; state < states; ++state)
      if (totals[last + state] < totals[last + end])
        end = state;

    for (var step = length - 1; step >= 0; --step) {
      chain[step] = end;
      end = previous[step * states + end];
    }
  }

  private static int _Blended(ReadOnlySpan<byte> blended, int pair, byte red, byte green, byte blue) {
    var at = pair * 3;
    int dr = blended[at] - red, dg = blended[at + 1] - green, db = blended[at + 2] - blue;

    return dr * dr + dg * dg + db * db;
  }

  /// <summary>
  /// The nine colour registers, the first left black and the rest taken from what the picture asks
  /// the colour field for.
  /// </summary>
  /// <remarks>
  /// Averaging halves the distance from the grey, so what the colour field has to supply is twice as
  /// far from it as the pixel is — and which grey that is cannot be guessed from the pixel alone,
  /// because a saturated colour halfway to black reads as a mid grey and asks for a colour that is
  /// nothing like the one that produced it. Every grey is therefore tried.
  /// <para/>
  /// A pixel votes for every colour that describes it as well as the best one does, not only for the
  /// first such colour found. Several greys usually reach the same pixel exactly, each asking for a
  /// different register, and counting only one of them lets a colour that appears all over the
  /// picture lose to a dozen colours that each appear in one band of it.
  /// </remarks>
  private static byte[] _ChooseRegisters(ReadOnlySpan<byte> rgb, int pixels, ReadOnlySpan<byte> gtia) {
    var nearest = _NearestTable(gtia);
    Span<int> counts = stackalloc int[256];
    Span<int> candidates = stackalloc int[16];
    Span<int> costs = stackalloc int[16];

    for (var pixel = 0; pixel < pixels; ++pixel) {
      var at = pixel * 3;
      var bestCost = int.MaxValue;

      for (var luminance = 0; luminance < 16; ++luminance) {
        var grey = gtia[luminance * 3];
        var candidate = nearest[
          (_Twice(rgb[at], grey) >> 3 << 10) | (_Twice(rgb[at + 1], grey) >> 3 << 5) | (_Twice(rgb[at + 2], grey) >> 3)];

        var entry = candidate * 3;
        var cost = 0;
        for (var channel = 0; channel < 3; ++channel) {
          int a = grey, b = gtia[entry + channel];
          var difference = ((a & b) + (((a ^ b) >> 1) & 0x7F)) - rgb[at + channel];
          cost += difference * difference;
        }

        candidates[luminance] = candidate;
        costs[luminance] = cost;
        bestCost = Math.Min(bestCost, cost);
      }

      for (var luminance = 0; luminance < 16; ++luminance) {
        if (costs[luminance] != bestCost)
          continue;

        var seen = false;
        for (var earlier = 0; earlier < luminance && !seen; ++earlier)
          seen = costs[earlier] == bestCost && candidates[earlier] == candidates[luminance];

        if (!seen)
          ++counts[candidates[luminance]];
      }
    }

    var registers = new byte[Atari8BitGraphics.RegisterCount];
    for (var register = 1; register < registers.Length; ++register) {
      var best = 0;
      for (var value = 0; value < 256; value += 2)
        if (counts[value] > counts[best])
          best = value;

      registers[register] = (byte)best;
      counts[best] = 0;
    }

    return registers;
  }

  /// <summary>
  /// The nearest colour register value for every colour, to five bits a channel.
  /// </summary>
  /// <remarks>
  /// The colour a pixel wants of the colour field has to be looked up once per grey and there are
  /// sixteen greys, so searching all 128 register values each time costs more than the whole of the
  /// rest of the encoding. The table is built once and is exact wherever the picture's colours are
  /// register values, which is the case that has to come out right.
  /// </remarks>
  private static byte[] _NearestTable(ReadOnlySpan<byte> gtia) {
    var table = new byte[32768];
    for (var index = 0; index < table.Length; ++index) {
      var red = ((index >> 10) << 3) | 4;
      var green = (((index >> 5) & 31) << 3) | 4;
      var blue = ((index & 31) << 3) | 4;
      table[index] = Atari8BitGraphics.FindNearestColorByte(gtia, (byte)red, (byte)green, (byte)blue);
    }

    return table;
  }

  private static byte _Twice(byte target, byte grey) => (byte)Math.Clamp(target * 2 - grey, 0, 255);
}
