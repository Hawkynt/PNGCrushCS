using System;
using FileFormat.Core;

namespace FileFormat.AtariPi9;

/// <summary>In-memory representation of a PI9 picture (.pi9).</summary>
/// <remarks>
/// An extension three unrelated formats ended up sharing, separated by length alone: an Atari 8-bit
/// Graphics 9 screen, an APAC one pairing Graphics 9 luminances with Graphics 11 hues, or a Falcon
/// picture at eight bitplanes against 256 freely chosen colours. Nothing but the size distinguishes
/// them, and the three do not even agree on which machine they are for.
/// <para/>
/// The Graphics 9 form has trailing bytes the picture does not use, which is why three different
/// lengths mean the same 7680-byte screen.
/// </remarks>
public readonly record struct AtariPi9File
  : IImageFormatReader<AtariPi9File>, IImageToRawImage<AtariPi9File>,
    IImageFromRawImage<AtariPi9File>, IImageFormatWriter<AtariPi9File> {

  /// <summary>Size of the Graphics 9 screen inside the file, whatever the file's own size.</summary>
  public const int Gr9Size = 7680;

  /// <summary>Size of the APAC form.</summary>
  public const int ApacSize = 7720;

  /// <summary>Colours a Falcon picture's palette holds.</summary>
  public const int FalconColorCount = 256;

  /// <summary>Size of a Falcon palette.</summary>
  public const int FalconPaletteSize = FalconColorCount * 4;

  /// <summary>Bitplanes a Falcon picture spreads a pixel over.</summary>
  public const int FalconPlanes = 8;

  static string IImageFormatMetadata<AtariPi9File>.PrimaryExtension => ".pi9";
  static string[] IImageFormatMetadata<AtariPi9File>.FileExtensions => [".pi9"];
  static AtariPi9File IImageFormatReader<AtariPi9File>.FromSpan(ReadOnlySpan<byte> data)
    => AtariPi9Reader.FromSpan(data);
  static byte[] IImageFormatWriter<AtariPi9File>.ToBytes(AtariPi9File file) => AtariPi9Writer.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<AtariPi9File>.VideoModes => [
    new("Graphics 9", [(320, IntegerRange.Any)], [16]),
    new("APAC", [(320, 192)], [256]),
    new("Falcon", [(320, 200), (320, 240), (640, 480)], [FalconColorCount]),
  ];

  /// <summary>The whole file.</summary>
  public byte[] Data { get; init; }

  /// <summary>Pixels across.</summary>
  public int Width { get; init; }

  /// <summary>Rows.</summary>
  public int Height { get; init; }

  /// <summary>Which of the three pictures the file holds.</summary>
  public AtariPi9Kind Kind { get; init; }

  /// <summary>Where the bitmap starts, past any executable header.</summary>
  public int BitmapOffset { get; init; }

  public static RawImage ToRawImage(AtariPi9File file) {
    var data = file.Data ?? [];

    switch (file.Kind) {
      case AtariPi9Kind.Falcon:
        return new() {
          Width = file.Width,
          Height = file.Height,
          Format = PixelFormat.Indexed8,
          PixelData = AtariStGraphics.UnpackBitplanes(
            data, FalconPaletteSize, file.Width, FalconPlanes, file.Width, file.Height),
          Palette = AtariStGraphics.ReadFalconPalette(data, 0, FalconColorCount),
          PaletteCount = FalconColorCount,
        };

      case AtariPi9Kind.Apac: {
        var frame = new byte[file.Width * file.Height];
        // The luminances fill the odd rows; the hues then cover the even ones and tint both.
        Atari8BitGraphics.DecodeGr9Into(data, 40, 80, frame, file.Width, file.Width * 2, file.Width, 96, 0);
        Atari8BitGraphics.BlendGr11Into(data, 0, 80, frame, file.Width, file.Height, 0);

        return new() {
          Width = file.Width,
          Height = file.Height,
          Format = PixelFormat.Rgb24,
          PixelData = Atari8BitGraphics.ApplyPalette(frame),
        };
      }

      default:
        return new() {
          Width = file.Width,
          Height = file.Height,
          Format = PixelFormat.Rgb24,
          PixelData = Atari8BitGraphics.DecodeGr9Frame(
            data, file.BitmapOffset, 40, file.Width, file.Height, 0, 0),
        };
    }
  }

  /// <summary>Pixels one stored nibble covers.</summary>
  public const int Gr9PixelsPerNibble = 4;

  /// <summary>Bytes one Graphics 9 row occupies.</summary>
  public const int Gr9Stride = 40;

  /// <summary>Builds a plain Graphics 9 screen, which is sixteen greys and nothing else.</summary>
  /// <remarks>
  /// Graphics 9 spends all four of a pixel's bits on brightness and none on hue, so the whole
  /// screen is one colour at sixteen levels — greys, since the background register is left at
  /// nought. It is the only Atari mode that can show a photograph without dithering, and it pays
  /// for that with a nibble covering four screen pixels: 320 across, but eighty of them distinct.
  /// </remarks>
  public static AtariPi9File FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Width < 1 || image.Height < 1)
      throw new ArgumentException("A picture needs at least one pixel.", nameof(image));

    const int width = 320, height = 192;
    var nibbles = width / Gr9PixelsPerNibble;
    var rgb = PixelConverter.Convert(image, PixelFormat.Rgb24);
    var greys = Atari8BitGraphics.Palette;
    var data = new byte[Gr9Size];

    for (var y = 0; y < height; ++y) {
      var sourceY = image.Height == height ? y : y * image.Height / height;

      for (var n = 0; n < nibbles; ++n) {
        var x = n * Gr9PixelsPerNibble;
        var sourceX = image.Width == width ? x : x * image.Width / width;
        var source = (sourceY * image.Width + sourceX) * 3;

        // The sixteen greys are not an even ramp — they are what the chip produces — so the level
        // is chosen by distance rather than by arithmetic.
        var best = 0;
        var bestDistance = int.MaxValue;
        for (var level = 0; level < 16; ++level) {
          var entry = level * 3;
          int dr = greys[entry] - rgb.PixelData[source];
          int dg = greys[entry + 1] - rgb.PixelData[source + 1];
          int db = greys[entry + 2] - rgb.PixelData[source + 2];
          var distance = dr * dr * 77 + dg * dg * 150 + db * db * 29;

          if (distance >= bestDistance)
            continue;

          bestDistance = distance;
          best = level;
        }

        // Two nibbles a byte, the leftmost pair of pixels in the high half.
        var at = y * Gr9Stride + (n >> 1);
        data[at] |= (byte)(best << ((n & 1) == 0 ? 4 : 0));
      }
    }

    return new() { Data = data, Width = width, Height = height, Kind = AtariPi9Kind.Graphics9, BitmapOffset = 0 };
  }
}
