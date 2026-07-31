using System;
using FileFormat.Core;

namespace FileFormat.Apple3201;

/// <summary>In-memory representation of a 3201 picture (.3201).</summary>
/// <remarks>
/// An Apple IIGS super hi-res screen with a palette per scanline rather than the sixteen the
/// hardware holds at once — the machine can be made to reload its palette between lines, so a
/// picture can use 3200 colours where the chip offers sixteen. The name is that number.
/// <para/>
/// The palettes come first, all two hundred of them, and the bitmap is packed afterwards. Each
/// palette is stored in reverse order, which is how the hardware's registers are addressed.
/// </remarks>
public readonly record struct Apple3201File
  : IImageFormatReader<Apple3201File>, IImageToRawImage<Apple3201File> {

  /// <summary>Pixels across.</summary>
  public const int Width = 320;

  /// <summary>Rows.</summary>
  public const int Height = 200;

  /// <summary>Colours a scanline's palette holds.</summary>
  public const int ColorCount = 16;

  /// <summary>Offset of the palettes.</summary>
  public const int PalettesOffset = 4;

  /// <summary>Bytes one scanline's palette occupies.</summary>
  public const int PaletteSize = ColorCount * 2;

  /// <summary>Offset of the packed bitmap.</summary>
  public const int BitmapOffset = PalettesOffset + Height * PaletteSize;

  /// <summary>Bytes one row unpacks to: two pixels a byte.</summary>
  public const int Stride = Width / 2;

  /// <summary>The four bytes every file starts with.</summary>
  public static ReadOnlySpan<byte> Signature => [193, 208, 208, 0];

  static string IImageFormatMetadata<Apple3201File>.PrimaryExtension => ".3201";
  static string[] IImageFormatMetadata<Apple3201File>.FileExtensions => [".3201"];
  static Apple3201File IImageFormatReader<Apple3201File>.FromSpan(ReadOnlySpan<byte> data)
    => Apple3201Reader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<Apple3201File>.VideoModes => [
    new("3200 colours", [(Width, Height)], [3200])
  ];

  /// <summary>The whole file.</summary>
  public byte[] Data { get; init; }

  /// <summary>The unpacked bitmap, two pixels a byte.</summary>
  public byte[] Bitmap { get; init; }

  public static RawImage ToRawImage(Apple3201File file) {
    var data = file.Data ?? [];
    var bitmap = file.Bitmap ?? [];
    var rgb = new byte[Width * Height * 3];

    Span<byte> palette = stackalloc byte[ColorCount * 3];

    for (var y = 0; y < Height; ++y) {
      _ReadPalette(data, PalettesOffset + y * PaletteSize, palette);

      for (var x = 0; x < Width; ++x) {
        var at = y * Stride + (x >> 1);
        var b = at < bitmap.Length ? bitmap[at] : 0;
        var entry = ((x & 1) == 0 ? b >> 4 : b & 15) * 3;

        var target = (y * Width + x) * 3;
        rgb[target] = palette[entry];
        rgb[target + 1] = palette[entry + 1];
        rgb[target + 2] = palette[entry + 2];
      }
    }

    return new() { Width = Width, Height = Height, Format = PixelFormat.Rgb24, PixelData = rgb };
  }

  /// <summary>
  /// Reads one scanline's palette, whose entries are stored in the reverse of the order they are
  /// used — which is the order the hardware's registers are addressed in.
  /// </summary>
  private static void _ReadPalette(ReadOnlySpan<byte> data, int offset, Span<byte> palette) {
    for (var c = 0; c < ColorCount; ++c) {
      var at = offset + ((c ^ (ColorCount - 1)) << 1);
      if (at + 1 >= data.Length)
        break;

      var gb = data[at];
      palette[c * 3] = ChannelScaling.Expand4(data[at + 1] & 15);
      palette[c * 3 + 1] = ChannelScaling.Expand4(gb >> 4);
      palette[c * 3 + 2] = ChannelScaling.Expand4(gb & 15);
    }
  }
}
