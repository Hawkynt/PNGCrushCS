using System;
using FileFormat.Core;

namespace FileFormat.AppleSh3;

/// <summary>In-memory representation of an unpacked 3200-colour picture (.sh3, .3200).</summary>
/// <remarks>
/// The same picture a 3201 file holds — an Apple IIGS super hi-res screen with a fresh sixteen
/// colour palette for every scanline — but written out as it sits in memory rather than packed: the
/// bitmap first at two pixels a byte, then the two hundred palettes. There is no header at all, so
/// the length is the whole of the identification.
/// <para/>
/// Each palette is stored in reverse order, which is how the hardware's registers are addressed.
/// </remarks>
public readonly record struct AppleSh3File
  : IImageFormatReader<AppleSh3File>, IImageToRawImage<AppleSh3File> {

  /// <summary>Pixels across.</summary>
  public const int Width = 320;

  /// <summary>Rows.</summary>
  public const int Height = 200;

  /// <summary>Colours a scanline's palette holds.</summary>
  public const int ColorCount = 16;

  /// <summary>Bytes one row occupies: two pixels a byte.</summary>
  public const int Stride = Width / 2;

  /// <summary>Where the palettes start, after the bitmap.</summary>
  public const int PalettesOffset = Stride * Height;

  /// <summary>Total file size.</summary>
  public const int FileSize = PalettesOffset + Height * ColorCount * 2;

  static string IImageFormatMetadata<AppleSh3File>.PrimaryExtension => ".sh3";
  static string[] IImageFormatMetadata<AppleSh3File>.FileExtensions => [".sh3", ".3200"];
  static AppleSh3File IImageFormatReader<AppleSh3File>.FromSpan(ReadOnlySpan<byte> data)
    => AppleSh3Reader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<AppleSh3File>.VideoModes => [
    new("3200 colours", [(Width, Height)], [3200])
  ];

  /// <summary>The whole file.</summary>
  public byte[] Data { get; init; }

  public static RawImage ToRawImage(AppleSh3File file) {
    var data = file.Data ?? [];
    var rgb = new byte[Width * Height * 3];

    for (var y = 0; y < Height; ++y) {
      var palette = AppleIIGSGraphics.ReadPalette(data, PalettesOffset + y * ColorCount * 2, reversed: true);

      for (var x = 0; x < Width; ++x) {
        var at = y * Stride + (x >> 1);
        var index = at < data.Length ? (x & 1) == 0 ? data[at] >> 4 : data[at] & 15 : 0;

        var entry = index * 3;
        var target = (y * Width + x) * 3;
        rgb[target] = palette[entry];
        rgb[target + 1] = palette[entry + 1];
        rgb[target + 2] = palette[entry + 2];
      }
    }

    return new() { Width = Width, Height = Height, Format = PixelFormat.Rgb24, PixelData = rgb };
  }
}
