using System;
using FileFormat.Core;

namespace FileFormat.AtariPi8;

/// <summary>In-memory representation of an Atari 8-bit PI8 picture (.pi8).</summary>
/// <remarks>
/// One extension covering two unrelated screens, told apart by length alone: a Graphics 15 picture
/// at four colours, or a Graphics 8 one at two. Neither stores a palette — Graphics 15 falls back
/// to the registers the operating system sets up, and Graphics 8 to black against white.
/// <para/>
/// Either may be wrapped in an Atari executable header, which is six bytes naming the memory the
/// picture would load into. It is present only when the address range it declares accounts for the
/// rest of the file exactly, so the picture is a row shorter when it is.
/// </remarks>
public readonly record struct AtariPi8File
  : IImageFormatReader<AtariPi8File>, IImageToRawImage<AtariPi8File> {

  /// <summary>Screen pixels across.</summary>
  public const int Width = 320;

  /// <summary>Bytes one row occupies in either mode.</summary>
  public const int Stride = Width / 8;

  /// <summary>Size of the Graphics 15 form.</summary>
  public const int ColorSize = 7680;

  /// <summary>Size of the Graphics 8 form.</summary>
  public const int MonochromeSize = 7685;

  /// <summary>The registers a Graphics 15 screen falls back to: background, PF0, PF1 and PF2.</summary>
  public static ReadOnlySpan<byte> DefaultRegisters => [0, 4, 8, 12];

  /// <summary>The playfield register a Graphics 8 screen falls back to.</summary>
  public const byte MonochromeBackground = 0;

  /// <summary>The luminance a Graphics 8 screen's foreground falls back to.</summary>
  public const byte MonochromeForeground = 14;

  static string IImageFormatMetadata<AtariPi8File>.PrimaryExtension => ".pi8";
  static string[] IImageFormatMetadata<AtariPi8File>.FileExtensions => [".pi8"];
  static AtariPi8File IImageFormatReader<AtariPi8File>.FromSpan(ReadOnlySpan<byte> data)
    => AtariPi8Reader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<AtariPi8File>.VideoModes => [
    new("Graphics 15", [(Width, IntegerRange.Any)], [4]),
    new("Graphics 8", [(Width, IntegerRange.Any)], [2]),
  ];

  /// <summary>The whole file.</summary>
  public byte[] Data { get; init; }

  /// <summary>Where the bitmap starts, past any executable header.</summary>
  public int BitmapOffset { get; init; }

  /// <summary>Rows.</summary>
  public int Height { get; init; }

  /// <summary>Whether the picture is the two-colour form.</summary>
  public bool IsMonochrome { get; init; }

  public static RawImage ToRawImage(AtariPi8File file) {
    var data = file.Data ?? [];

    if (!file.IsMonochrome)
      return new() {
        Width = Width,
        Height = file.Height,
        Format = PixelFormat.Rgb24,
        PixelData = Atari8BitGraphics.DecodeGr15Frame(
          data, file.BitmapOffset, Stride, Width, file.Height, DefaultRegisters),
      };

    var gtia = Atari8BitGraphics.Palette;
    var pixels = new byte[Width * file.Height];

    for (var y = 0; y < file.Height; ++y)
    for (var x = 0; x < Width; ++x) {
      var at = file.BitmapOffset + y * Stride + (x >> 3);
      if (at < data.Length && ((data[at] >> (~x & 7)) & 1) != 0)
        pixels[y * Width + x] = 1;
    }

    // The foreground keeps the playfield register's hue and takes only the other's luminance.
    var foreground = (MonochromeBackground & 240) | (MonochromeForeground & 14);
    var palette = new byte[6];
    gtia.Slice(MonochromeBackground * 3, 3).CopyTo(palette);
    gtia.Slice(foreground * 3, 3).CopyTo(palette.AsSpan(3));

    return new() {
      Width = Width,
      Height = file.Height,
      Format = PixelFormat.Indexed8,
      PixelData = pixels,
      Palette = palette,
      PaletteCount = 2,
    };
  }
}
