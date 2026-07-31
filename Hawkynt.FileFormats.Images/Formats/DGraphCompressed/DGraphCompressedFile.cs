using System;
using FileFormat.Core;

namespace FileFormat.DGraphCompressed;

/// <summary>In-memory representation of a compressed D-GRAPH picture (.p3c).</summary>
/// <remarks>
/// Two low-resolution ST screens shown alternately and averaged, each packed into its own block and
/// each preceded by its compressed length written as decimal digits closed by a carriage return.
/// Storing a length as text in the middle of a binary file is unusual enough to be most of what
/// identifies the format, and it means the second block's length can only be found by unpacking the
/// first — which is why the two screens cannot be read independently.
/// <para/>
/// Both frames share one palette, stored between the first length and the first block, so the
/// interlacing buys the mixtures between sixteen colours rather than more of them.
/// </remarks>
public readonly record struct DGraphCompressedFile
  : IImageFormatReader<DGraphCompressedFile>, IImageToRawImage<DGraphCompressedFile> {

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

  /// <summary>Size of one screen.</summary>
  public const int ScreenSize = Width / 8 * Planes * Height;

  static string IImageFormatMetadata<DGraphCompressedFile>.PrimaryExtension => ".p3c";
  static string[] IImageFormatMetadata<DGraphCompressedFile>.FileExtensions => [".p3c"];
  static DGraphCompressedFile IImageFormatReader<DGraphCompressedFile>.FromSpan(ReadOnlySpan<byte> data)
    => DGraphCompressedReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<DGraphCompressedFile>.VideoModes => [
    new("D-GRAPH", [(Width, Height)], [ColorCount])
  ];

  /// <summary>Both unpacked screens, one after the other.</summary>
  public byte[] ScreenData { get; init; }

  /// <summary>The stored palette.</summary>
  public byte[] Palette { get; init; }

  public static RawImage ToRawImage(DGraphCompressedFile file) {
    var data = file.ScreenData ?? [];
    var palette = AtariStGraphics.ReadPalette(file.Palette ?? [], 0, ColorCount);
    var stride = AtariStGraphics.BytesPerRow(Width, Planes);

    var first = AtariStGraphics.ToRgb(
      AtariStGraphics.UnpackBitplanes(data, 0, stride, Planes, Width, Height), palette, ColorCount);
    var second = AtariStGraphics.ToRgb(
      AtariStGraphics.UnpackBitplanes(data, ScreenSize, stride, Planes, Width, Height), palette, ColorCount);

    return new() {
      Width = Width,
      Height = Height,
      Format = PixelFormat.Rgb24,
      PixelData = FrameBlend.Average(first, second),
    };
  }
}
