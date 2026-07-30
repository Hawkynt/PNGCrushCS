using System;
using FileFormat.Core;

namespace FileFormat.Pc98Ebd;

/// <summary>In-memory representation of a PC-98 EBD picture (.ebd).</summary>
/// <remarks>
/// Sixteen colours and four bitplanes at 640 pixels across, with the height following from the file
/// length rather than being stored — the format has no header at all beyond its palette.
/// <para/>
/// That palette is written two ways. Some files store each channel as a nibble in its own byte and
/// some store it already widened to eight bits, and nothing says which. The two are told apart by
/// looking: a byte whose halves are equal is a widened four-bit value, and a file where that does
/// not hold everywhere must have its high nibbles clear or it is not a palette at all.
/// </remarks>
public readonly record struct Pc98EbdFile
  : IImageFormatReader<Pc98EbdFile>, IImageToRawImage<Pc98EbdFile> {

  /// <summary>Pixels across.</summary>
  public const int Width = 640;

  /// <summary>Bitplanes a pixel is spread over.</summary>
  public const int Planes = 4;

  /// <summary>Colours the palette holds.</summary>
  public const int ColorCount = 1 << Planes;

  /// <summary>Offset of the bitmap, after the palette.</summary>
  public const int BitmapOffset = ColorCount * 3;

  /// <summary>Bytes one row of the picture occupies across all four planes.</summary>
  public const int Stride = Width / 8 * Planes;

  static string IImageFormatMetadata<Pc98EbdFile>.PrimaryExtension => ".ebd";
  static string[] IImageFormatMetadata<Pc98EbdFile>.FileExtensions => [".ebd"];
  static Pc98EbdFile IImageFormatReader<Pc98EbdFile>.FromSpan(ReadOnlySpan<byte> data)
    => Pc98EbdReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<Pc98EbdFile>.VideoModes => [
    new("EBD", [(Width, IntegerRange.Any)], [ColorCount])
  ];

  /// <summary>The whole file.</summary>
  public byte[] Data { get; init; }

  /// <summary>Rows, derived from the file length.</summary>
  public int Height { get; init; }

  public static RawImage ToRawImage(Pc98EbdFile file) {
    var data = file.Data ?? [];

    return new() {
      Width = Width,
      Height = file.Height,
      Format = PixelFormat.Indexed8,
      PixelData = PlanarConverter.NonInterleavedPlanarToChunky(
        data.AsSpan(BitmapOffset), Width, file.Height, Planes),
      Palette = ReadPalette(data),
      PaletteCount = ColorCount,
    };
  }

  /// <summary>Reads the palette, widening its channels only when they are not already widened.</summary>
  public static byte[] ReadPalette(ReadOnlySpan<byte> data) {
    var palette = new byte[ColorCount * 3];
    for (var i = 0; i < palette.Length && i < data.Length; ++i) {
      var c = data[i];
      palette[i] = (c >> 4) == (c & 15) ? c : (byte)(c * 17);
    }

    return palette;
  }
}
