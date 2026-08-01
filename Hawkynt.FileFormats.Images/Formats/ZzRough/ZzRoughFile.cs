using System;
using FileFormat.Core;
using FileFormat.DaliCompressed;

namespace FileFormat.ZzRough;

/// <summary>In-memory representation of a ZZ_ROUGH picture (.rgh).</summary>
/// <remarks>
/// A low-resolution ST screen packed with Dali's scheme but wrapped differently: a copyright string,
/// then the length of the count stream written as decimal digits followed by a carriage return and
/// a newline, and only then the palette and the two streams. Storing a length as text in a binary
/// format is unusual enough to serve as the rest of the signature.
/// <para/>
/// The packing itself walks the screen four bytes at a time down each column group rather than in
/// raster order, because the ST interleaves four bitplanes per sixteen pixels — so a four-byte group
/// is one screen chunk, and a vertical run of them is what a flat-coloured area actually produces.
/// </remarks>
public readonly record struct ZzRoughFile
  : IImageFormatReader<ZzRoughFile>, IImageToRawImage<ZzRoughFile>,
    IImageFromRawImage<ZzRoughFile>, IImageFormatWriter<ZzRoughFile> {

  static byte[] IImageFormatWriter<ZzRoughFile>.ToBytes(ZzRoughFile file) => ZzRoughWriter.ToBytes(file);

  /// <summary>Pixels across.</summary>
  public const int Width = 320;

  /// <summary>Rows.</summary>
  public const int Height = 200;

  /// <summary>Bitplanes a pixel is spread over.</summary>
  public const int Planes = 4;

  /// <summary>Colours the palette holds.</summary>
  public const int ColorCount = 1 << Planes;

  /// <summary>Size of the stored palette.</summary>
  public const int PaletteSize = ColorCount * 2;

  /// <summary>The string every file starts with.</summary>
  public const string Signature = "(c)F.MARCHAL";

  static string IImageFormatMetadata<ZzRoughFile>.PrimaryExtension => ".rgh";
  static string[] IImageFormatMetadata<ZzRoughFile>.FileExtensions => [".rgh"];
  static ZzRoughFile IImageFormatReader<ZzRoughFile>.FromSpan(ReadOnlySpan<byte> data)
    => ZzRoughReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<ZzRoughFile>.VideoModes => [
    new("ZZ_ROUGH", [(Width, Height)], [ColorCount])
  ];

  /// <summary>The unpacked screen.</summary>
  public byte[] ScreenData { get; init; }

  /// <summary>The stored palette.</summary>
  public byte[] Palette { get; init; }

  public static RawImage ToRawImage(ZzRoughFile file) => new() {
    Width = Width,
    Height = Height,
    Format = PixelFormat.Indexed8,
    PixelData = AtariStGraphics.UnpackBitplanes(
      file.ScreenData ?? [], 0, AtariStGraphics.BytesPerRow(Width, Planes), Planes, Width, Height),
    Palette = AtariStGraphics.ReadPalette(file.Palette ?? [], 0, ColorCount),
    PaletteCount = ColorCount,
  };

  /// <summary>Builds a picture in the sixteen colours the machine can show at this resolution.</summary>
  public static ZzRoughFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var rgb = image.SampleTo(Width, Height);
    var quantized = ColorQuantizer.Quantize(
      PixelConverter.Convert(rgb, PixelFormat.Bgra32).PixelData, Width * Height, ColorCount);

    var indices = new byte[Width * Height];
    for (var i = 0; i < indices.Length; ++i)
      indices[i] = (byte)quantized.Indices[i];

    var palette = new byte[PaletteSize];
    var stPalette = PlanarConverter.RgbToStPalette(quantized.Palette, ColorCount);
    for (var i = 0; i < ColorCount; ++i) {
      palette[i * 2] = (byte)(stPalette[i] >> 8);
      palette[i * 2 + 1] = (byte)stPalette[i];
    }

    return new() {
      ScreenData = AtariStGraphics.PackBitplanes(
        indices, AtariStGraphics.BytesPerRow(Width, Planes), Planes, Width, Height),
      Palette = palette,
    };
  }
}
