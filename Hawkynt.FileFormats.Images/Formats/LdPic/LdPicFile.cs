using System;
using FileFormat.Core;

namespace FileFormat.LdPic;

/// <summary>In-memory representation of an LdPic picture (.bbg).</summary>
/// <remarks>
/// A BBC Micro screen in any of five display modes, packed by a run-length coder that is told how
/// wide its own fields are: the number of bits a value takes and the number a run length takes are
/// both read from the front of the file. A picture using only four colours spends two bits a value
/// rather than eight, which is most of what the format saves.
/// <para/>
/// The unpacking is interleaved by a stride the file also names — column by column rather than in
/// order — which lines up bytes that are eight scanlines apart in the machine's character-cell
/// layout, so that a flat area of screen becomes a run rather than a stripe.
/// </remarks>
public readonly record struct LdPicFile
  : IImageFormatReader<LdPicFile>, IImageToRawImage<LdPicFile>,
    IImageFromRawImage<LdPicFile>, IImageFormatWriter<LdPicFile> {

  /// <summary>
  /// The BBC Micro's eight colours, which are the corners of the colour cube and nothing else.
  /// The list repeats because the hardware's four flashing entries show the same eight.
  /// </summary>
  public static ReadOnlySpan<int> Palette => [
    0x000000, 0xFF0000, 0x00FF00, 0xFFFF00, 0x0000FF, 0xFF00FF, 0x00FFFF, 0xFFFFFF,
    0x000000, 0xFF0000, 0x00FF00, 0xFFFF00, 0x0000FF, 0xFF00FF, 0x00FFFF, 0xFFFFFF,
  ];

  static string IImageFormatMetadata<LdPicFile>.PrimaryExtension => ".bbg";
  static string[] IImageFormatMetadata<LdPicFile>.FileExtensions => [".bbg"];
  static LdPicFile IImageFormatReader<LdPicFile>.FromSpan(ReadOnlySpan<byte> data)
    => LdPicReader.FromSpan(data);
  static byte[] IImageFormatWriter<LdPicFile>.ToBytes(LdPicFile file) => LdPicWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<LdPicFile>.VideoModes => [
    new("BBC Micro", [(320, 256), (640, 512)], [16])
  ];

  /// <summary>The unpacked screen.</summary>
  public byte[] Screen { get; init; }

  /// <summary>Which of the five display modes it is.</summary>
  public int Mode { get; init; }

  /// <summary>Sixteen colours, taken from the machine's eight.</summary>
  public byte[] LogicalColors { get; init; }

  public static RawImage ToRawImage(LdPicFile file) {
    var screen = file.Screen ?? [];
    var colors = file.LogicalColors ?? [];

    // Mode 0 is the only one drawn at twice the height; modes 2 and 5 are the only ones drawn at
    // twice the width, having half as many pixels across as the others.
    var wide = file.Mode is 2 or 5;
    var width = file.Mode == 0 ? 640 : 320;
    var height = file.Mode == 0 ? 512 : 256;
    var stride = file.Mode >= 4 ? 40 : 80;
    var logical = wide ? 160 : width;

    var rgb = new byte[width * height * 3];

    for (var y = 0; y < 256; ++y)
    for (var x = 0; x < logical; ++x) {
      var index = file.Mode switch {
        // One bit a pixel, eight pixels to a byte.
        0 or 4 => (screen[(y & ~7) * stride + (x & ~7) + (y & 7)] >> (~x & 7)) & 1,

        // Two bits a pixel, but the two are four bits apart in the byte rather than adjacent —
        // the hardware shifted one plane out of each nibble.
        1 or 5 => _TwoBit(screen[(y & ~7) * stride + ((x & ~3) << 1) + (y & 7)] >> (~x & 3)),

        // Four bits a pixel, each in its own nibble-spaced position for the same reason.
        _ => _FourBit(screen[(y & ~7) * stride + ((x & ~1) << 2) + (y & 7)] >> (~x & 1)),
      };

      var entry = index * 3;
      var repeatX = wide ? 2 : 1;
      var repeatY = file.Mode == 0 ? 2 : 1;

      for (var dy = 0; dy < repeatY; ++dy)
      for (var dx = 0; dx < repeatX; ++dx) {
        var target = ((y * repeatY + dy) * width + x * repeatX + dx) * 3;
        rgb[target] = colors[entry];
        rgb[target + 1] = colors[entry + 1];
        rgb[target + 2] = colors[entry + 2];
      }
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = rgb };
  }

  private static int _TwoBit(int value) => ((value >> 3) & 2) + (value & 1);

  private static int _FourBit(int value)
    => ((value >> 3) & 8) + ((value >> 2) & 4) + ((value >> 1) & 2) + (value & 1);

  /// <summary>Builds a picture in mode 2, which is the machine's most colourful.</summary>
  /// <remarks>
  /// The BBC Micro's eight colours are the corners of the colour cube and nothing between, so a
  /// picture is reduced to those; the mode that shows all eight has half the horizontal resolution
  /// of the ones that show fewer, which is the trade the machine offers and there is no way round
  /// it. Every logical pixel is drawn twice, so the picture is still 320 across.
  /// </remarks>
  public static LdPicFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Width < 1 || image.Height < 1)
      throw new ArgumentException("A picture needs at least one pixel.", nameof(image));

    const int mode = 2, logical = 160, height = 256, stride = 80;
    var rgb = PixelConverter.Convert(image, PixelFormat.Rgb24);

    // The sixteen logical entries name the eight physical colours twice over, so an index and its
    // eighth-neighbour are the same colour and only the low three bits matter.
    var colors = new byte[16 * 3];
    for (var i = 0; i < 16; ++i) {
      var color = Palette[i & 7];
      colors[i * 3] = (byte)(color >> 16);
      colors[i * 3 + 1] = (byte)(color >> 8);
      colors[i * 3 + 2] = (byte)color;
    }

    var line = new byte[logical * 3];
    var screen = new byte[20480];

    for (var y = 0; y < height; ++y) {
      var sourceY = image.Height == height ? y : y * image.Height / height;

      for (var x = 0; x < logical; ++x) {
        // A logical pixel covers two of the picture's, so it takes the leftmost of the pair.
        var sourceX = image.Width == logical * 2 ? x * 2 : x * 2 * image.Width / (logical * 2);
        var source = (sourceY * image.Width + Math.Min(sourceX, image.Width - 1)) * 3;

        line[x * 3] = rgb.PixelData[source];
        line[x * 3 + 1] = rgb.PixelData[source + 1];
        line[x * 3 + 2] = rgb.PixelData[source + 2];
      }

      var indices = PaletteQuantizer.Quantize(line, logical, 1, colors, 8);

      for (var x = 0; x < logical; ++x) {
        var at = (y & ~7) * stride + ((x & ~1) << 2) + (y & 7);
        var shift = ~x & 1;

        // The four bits of an index are two apart in the byte, the machine having shifted one
        // plane out of each pair.
        for (var plane = 0; plane < 4; ++plane) {
          if (((indices[x] >> plane) & 1) != 0)
            screen[at] |= (byte)(1 << (shift + plane * 2));
        }
      }
    }

    return new() { Screen = screen, Mode = mode, LogicalColors = colors };
  }
}
