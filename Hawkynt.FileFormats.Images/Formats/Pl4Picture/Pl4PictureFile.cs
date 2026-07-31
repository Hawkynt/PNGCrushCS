using System;
using FileFormat.Core;

namespace FileFormat.Pl4Picture;

/// <summary>In-memory representation of a PL4 picture (.pl4).</summary>
/// <remarks>
/// Two low-resolution ST screens shown alternately and averaged, each with its own palette, packed
/// together in one LZ4 frame. The compressor is a modern one rather than a period one — a format
/// written recently for an old machine has no reason to invent a packer — which is what lets it
/// carry two whole screens and two palettes in a file smaller than one of them.
/// </remarks>
public readonly record struct Pl4PictureFile
  : IImageFormatReader<Pl4PictureFile>, IImageToRawImage<Pl4PictureFile> {

  /// <summary>Pixels across.</summary>
  public const int Width = 320;

  /// <summary>Rows.</summary>
  public const int Height = 200;

  /// <summary>Bitplanes a pixel is spread over.</summary>
  public const int Planes = 4;

  /// <summary>Colours a palette holds.</summary>
  public const int ColorCount = 1 << Planes;

  /// <summary>Bytes one screen and its palette occupy once unpacked.</summary>
  public const int ScreenSize = 32036;

  /// <summary>Bytes the whole file unpacks to.</summary>
  public const int UnpackedSize = 64070;

  /// <summary>Offset of the first palette.</summary>
  public const int FirstPaletteOffset = 2;

  /// <summary>Offset of the first bitmap.</summary>
  public const int FirstBitmapOffset = 34;

  /// <summary>Offset of the second palette.</summary>
  public const int SecondPaletteOffset = 32038;

  /// <summary>Offset of the second bitmap.</summary>
  public const int SecondBitmapOffset = 32070;

  static string IImageFormatMetadata<Pl4PictureFile>.PrimaryExtension => ".pl4";
  static string[] IImageFormatMetadata<Pl4PictureFile>.FileExtensions => [".pl4"];
  static Pl4PictureFile IImageFormatReader<Pl4PictureFile>.FromSpan(ReadOnlySpan<byte> data)
    => Pl4PictureReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<Pl4PictureFile>.VideoModes => [
    new("PL4", [(Width, Height)], [ColorCount])
  ];

  /// <summary>The unpacked pair of screens.</summary>
  public byte[] Unpacked { get; init; }

  public static RawImage ToRawImage(Pl4PictureFile file) {
    var data = file.Unpacked ?? [];
    var stride = AtariStGraphics.BytesPerRow(Width, Planes);

    var first = AtariStGraphics.ToRgb(
      AtariStGraphics.UnpackBitplanes(data, FirstBitmapOffset, stride, Planes, Width, Height),
      AtariStGraphics.ReadPalette(data, FirstPaletteOffset, ColorCount), ColorCount);

    var second = AtariStGraphics.ToRgb(
      AtariStGraphics.UnpackBitplanes(data, SecondBitmapOffset, stride, Planes, Width, Height),
      AtariStGraphics.ReadPalette(data, SecondPaletteOffset, ColorCount), ColorCount);

    return new() {
      Width = Width,
      Height = Height,
      Format = PixelFormat.Rgb24,
      PixelData = FrameBlend.Average(first, second),
    };
  }
}
