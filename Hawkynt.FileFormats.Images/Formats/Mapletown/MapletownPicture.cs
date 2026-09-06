using System;
using FileFormat.Core;

namespace FileFormat.Mapletown;

/// <summary>Turns a picture into the palette and runs one Mapletown image is made of.</summary>
/// <remarks>
/// ML1 and MX1 are one format wearing two skins. The header, the palette and the runs are the same
/// in both; only the layer underneath differs, ML1 taking all eight bits of a byte and MX1 seven
/// bits of a printable character. Everything above that layer lives here so the two cannot drift.
/// </remarks>
public static class MapletownPicture {

  /// <summary>Levels each channel of a colour can take.</summary>
  public const int Levels = 9;

  /// <summary>Colours a palette can hold.</summary>
  public const int PaletteSize = 128;

  /// <summary>Widest and tallest a picture may be: the corners are stated in sixteen bits each.</summary>
  public const int MaxDimension = 65536;

  /// <summary>
  /// Most pixels a picture may hold: the end of one is announced as a length one past its pixel
  /// count, and a length is twenty-one bits at the widest.
  /// </summary>
  public const int MaxPixels = MapletownEncoder.MaxLength - 1;

  /// <summary>
  /// The block of header the reader steps over: the drawing program's own state, which nothing here
  /// knows how to state and nothing reading it needs.
  /// </summary>
  private const int _OPAQUE_HEADER_BITS = 624;

  /// <summary>The mode that names each palette entry it fills and each pixel's colour outright.</summary>
  /// <remarks>
  /// Of the three, the only one that both allows a short palette and leaves the colour of a run a
  /// plain seven-bit number. The mode that codes colours as lengths pays for a rare colour with
  /// twenty bits, which is a bargain only for a picture whose colours are ordered by how much of it
  /// they cover — and reordering a palette to make that true is a saving of a few hundred bytes on
  /// a format that has already spent seventy-eight on a header it does not use.
  /// </remarks>
  private const int _NAMED_PALETTE_MODE = 1;

  /// <summary>Snaps one channel to the nine levels a colour is written in.</summary>
  public static int Level(int channel) => Math.Clamp((channel * (Levels - 1) + 127) / 255, 0, Levels - 1);

  /// <summary>What one of those nine levels shows as.</summary>
  public static byte Channel(int level) => (byte)((level * 255) >> 3);

  /// <summary>
  /// Reduces a picture to the palette a file can hold: at most 128 colours, each of them a number in
  /// base nine with a digit per channel.
  /// </summary>
  /// <remarks>
  /// Snapped to the nine levels before the palette is chosen rather than after, because the two
  /// reductions do not commute: choosing 128 colours first and rounding them afterwards can land two
  /// of them on the same level and spend an entry on nothing. Done this way a picture that already
  /// fits comes back untouched, which is what a file that has been read and is being written again
  /// needs.
  /// </remarks>
  public static (int[] Colors, int[] Indices) Reduce(ReadOnlySpan<byte> rgb, int pixels) {
    var bgra = new byte[pixels * 4];
    for (var pixel = 0; pixel < pixels; ++pixel) {
      var source = pixel * 3;
      bgra[pixel * 4] = Channel(Level(source + 2 < rgb.Length ? rgb[source + 2] : 0));
      bgra[pixel * 4 + 1] = Channel(Level(source + 1 < rgb.Length ? rgb[source + 1] : 0));
      bgra[pixel * 4 + 2] = Channel(Level(source < rgb.Length ? rgb[source] : 0));
      bgra[pixel * 4 + 3] = 255;
    }

    var quantized = ColorQuantizer.Quantize(bgra, pixels, PaletteSize);
    var colors = new int[quantized.Count];
    for (var entry = 0; entry < colors.Length; ++entry)
      colors[entry] = (Level(quantized.Palette[entry * 3]) * Levels + Level(quantized.Palette[entry * 3 + 1])) * Levels
                      + Level(quantized.Palette[entry * 3 + 2]);

    return (colors, quantized.Indices);
  }

  /// <summary>The colour a palette entry shows as, three bytes.</summary>
  public static (byte Red, byte Green, byte Blue) Expand(int color)
    => (Channel(color / (Levels * Levels)), Channel(color / Levels % Levels), Channel(color % Levels));

  /// <summary>Writes one image — header, palette, runs, end marker — into an open bit stream.</summary>
  /// <remarks>
  /// Runs are taken across the whole picture rather than a row at a time, because the reader scans
  /// it as one line of pixels and a run that reaches the end of a row simply continues on the next.
  /// </remarks>
  public static void WriteImage(MapletownEncoder encoder, int width, int height, ReadOnlySpan<byte> rgb) {
    ArgumentNullException.ThrowIfNull(encoder);

    var (colors, indices) = Reduce(rgb, width * height);

    encoder.Bits(MapletownDecoder.Signature, 32);
    encoder.Bits(0, 32);
    encoder.Bits(0, 16);

    // The picture's place on the drawing program's canvas, stated as the corners rather than a size.
    encoder.Bits(0, 16);
    encoder.Bits(0, 16);
    encoder.Bits(width - 1, 16);
    encoder.Bits(height - 1, 16);
    for (var bit = 0; bit < _OPAQUE_HEADER_BITS; ++bit)
      encoder.Bit(0);

    encoder.Bits(_NAMED_PALETTE_MODE, 2);
    encoder.Bits(colors.Length - 1, 7);
    for (var entry = 0; entry < colors.Length; ++entry) {
      encoder.Bits(entry, 7);
      encoder.Bits(colors[entry], 10);
    }

    for (var at = 0; at < indices.Length;) {
      var run = 1;
      while (at + run < indices.Length && indices[at + run] == indices[at])
        ++run;

      encoder.Length(run);
      encoder.Bits(indices[at], 7);

      // No chain: a stroke that walks down the picture is how the drawing program kept an outline
      // ahead of a fill, and a picture reduced from pixels has no outline to keep.
      encoder.Bit(0);
      at += run;
    }

    // What says the picture is over: a length one past the number of pixels it had.
    encoder.Length(width * height + 1);
  }
}
