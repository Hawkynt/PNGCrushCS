using System;
using FileFormat.Core;

namespace FileFormat.Uimg;

/// <summary>In-memory representation of a UIMG picture (.bp1, .bp2, .bp4, .c01, .c02, .c04).</summary>
/// <remarks>
/// A deliberately general container for Atari pictures: a header naming which kind of palette, how
/// many bits a pixel, and how the pixels are arranged, and then the data. It exists because the ST,
/// the TT and the Falcon store colours three different ways and pixels four, and a program wanting
/// to write any of them would otherwise need a format for each combination.
/// <para/>
/// The extension says the same thing as the header — b for bitplanes, c for chunky, and the digit
/// for the bytes a pixel takes — which is how a program could pick a file without opening it.
/// </remarks>
public readonly record struct UimgFile
  : IImageFormatReader<UimgFile>, IImageToRawImage<UimgFile>,
    IImageFromRawImage<UimgFile>, IImageFormatWriter<UimgFile> {

  static byte[] IImageFormatWriter<UimgFile>.ToBytes(UimgFile file) => UimgWriter.ToBytes(file);

  /// <summary>The text every file starts with.</summary>
  public const string Signature = "UIMG";

  /// <summary>Offset of the palette, or of the pixels when there is none.</summary>
  public const int PaletteOffset = 14;

  static string IImageFormatMetadata<UimgFile>.PrimaryExtension => ".bp1";
  static string[] IImageFormatMetadata<UimgFile>.FileExtensions => [".bp1", ".bp2", ".bp4", ".bp6", ".bp8", ".c01", ".c02", ".c04", ".c06", ".c08", ".c24", ".c32"];
  static UimgFile IImageFormatReader<UimgFile>.FromSpan(ReadOnlySpan<byte> data) => UimgReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<UimgFile>.VideoModes => [
    new("UIMG", [(IntegerRange.Any, IntegerRange.Any)], [16777216])
  ];

  /// <summary>The whole file.</summary>
  public byte[] Data { get; init; }

  /// <summary>Pixels across.</summary>
  public int Width { get; init; }

  /// <summary>Rows.</summary>
  public int Height { get; init; }

  /// <summary>Bits a pixel occupies.</summary>
  public int Depth { get; init; }

  /// <summary>Which kind of palette the file carries: none, ST, TT or Falcon.</summary>
  public int PaletteKind { get; init; }

  /// <summary>How the pixels are arranged.</summary>
  public int Chunk { get; init; }

  /// <summary>Offset of the pixels.</summary>
  public int BitmapOffset { get; init; }

  public static RawImage ToRawImage(UimgFile file) {
    var data = file.Data ?? [];
    var count = file.Width * file.Height;

    // The three true-colour arrangements carry their colours in the pixels themselves.
    switch (file.Chunk) {
      case 2: {
        var rgb = new byte[count * 3];
        for (var i = 0; i < count; ++i)
          AtariStGraphics.FalconTrueColorToRgb(data, PaletteOffset + (i << 1), rgb.AsSpan(i * 3, 3));

        return new() { Width = file.Width, Height = file.Height, Format = PixelFormat.Rgb24, PixelData = rgb };
      }

      case 3:
        return new() {
          Width = file.Width,
          Height = file.Height,
          Format = PixelFormat.Rgb24,
          PixelData = data[PaletteOffset..(PaletteOffset + count * 3)],
        };

      case 4: {
        // Four bytes a pixel with the padding first, so the colours start one byte later.
        var rgb = new byte[count * 3];
        for (var i = 0; i < count; ++i)
          data.AsSpan(PaletteOffset + 1 + (i << 2), 3).CopyTo(rgb.AsSpan(i * 3));

        return new() { Width = file.Width, Height = file.Height, Format = PixelFormat.Rgb24, PixelData = rgb };
      }
    }

    var colors = 1 << file.Depth;
    var palette = file.PaletteKind == 3
      ? AtariStGraphics.ReadFalconPalette(data, PaletteOffset, colors)
      : AtariStGraphics.ReadPalette(data, PaletteOffset, colors);

    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Indexed8,
      PixelData = _ReadIndices(file, data),
      Palette = palette,
      PaletteCount = colors,
    };
  }

  private static byte[] _ReadIndices(UimgFile file, ReadOnlySpan<byte> data) {
    var count = file.Width * file.Height;

    switch (file.Chunk) {
      case 0:
        return AtariStGraphics.UnpackBitplanes(
          data, file.BitmapOffset, (file.Width >> 3) * file.Depth, file.Depth, file.Width, file.Height);

      case 1:
        return data.Slice(file.BitmapOffset, count).ToArray();

      default:
        break;
    }

    // The remaining arrangement packs its pixels without regard to where a row begins.
    var indices = new byte[count];

    switch (file.Depth) {
      case 1:
        return AtariStGraphics.UnpackBitplanes(
          data, file.BitmapOffset, file.Width >> 3, 1, file.Width, file.Height);

      case 2:
        for (var i = 0; i < count; ++i) {
          var at = file.BitmapOffset + (i >> 2);
          indices[i] = (byte)(at < data.Length ? (data[at] >> ((~i & 3) << 1)) & 3 : 0);
        }

        return indices;

      default:
        for (var y = 0; y < file.Height; ++y)
        for (var x = 0; x < file.Width; ++x)
          indices[y * file.Width + x] = (byte)MsxGraphics.GetNibble(data, file.BitmapOffset + y * (file.Width >> 1), x);

        return indices;
    }
  }

  /// <summary>Builds a picture in the twenty-four-bit arrangement, which keeps every colour.</summary>
  public static UimgFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var rgb = image.EnsureFormat(PixelFormat.Rgb24);
    var pixels = rgb.Width * rgb.Height;
    var data = new byte[PaletteOffset + pixels * UimgWriter.TrueColor24];
    rgb.PixelData.AsSpan(0, Math.Min(rgb.PixelData.Length, pixels * UimgWriter.TrueColor24))
      .CopyTo(data.AsSpan(PaletteOffset));

    return new() {
      Width = rgb.Width,
      Height = rgb.Height,
      Depth = UimgWriter.TrueColor24 << 3,
      PaletteKind = 0,
      Chunk = UimgWriter.TrueColor24,
      BitmapOffset = PaletteOffset,
      Data = data,
    };
  }
}
