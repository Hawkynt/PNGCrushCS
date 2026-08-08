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
  : IImageFormatReader<Pl4PictureFile>, IImageToRawImage<Pl4PictureFile>,
    IImageFromRawImage<Pl4PictureFile>, IImageFormatWriter<Pl4PictureFile> {

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
  static byte[] IImageFormatWriter<Pl4PictureFile>.ToBytes(Pl4PictureFile file) => Pl4PictureWriter.ToBytes(file);
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

  /// <summary>Writes a picture as the same screen twice, which is what the averaging leaves alone.</summary>
  /// <remarks>
  /// The two screens are shown alternately and averaged, so a picture drawn on one of them and not
  /// the other comes out half way to the other's colours. Writing the same screen and the same
  /// palette into both makes the average the picture itself; the second screen's freedom is what the
  /// format offers an artist working by eye, and it is not something an encoder can spend usefully
  /// without deciding what the flicker should look like.
  /// <para/>
  /// The palette is the plain ST form of three bits a channel. The STE's four are stored in bits the
  /// ST left clear, so a file using them is only recognised as an STE one when some channel happens
  /// to be odd — which is a property of the picture rather than a decision, and not one to hang the
  /// meaning of every colour on.
  /// </remarks>
  public static Pl4PictureFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var source = image.SampleTo(Width, Height);
    var indexed = source.EnsureIndexedAtMost(ColorCount);
    var chosen = indexed.Palette ?? [];

    var unpacked = new byte[UnpackedSize];
    var stride = AtariStGraphics.BytesPerRow(Width, Planes);
    var bitmap = AtariStGraphics.PackBitplanes(indexed.PixelData, stride, Planes, Width, Height);

    for (var i = 0; i < ColorCount; ++i) {
      var entry = i * 3;
      var word = (_ToThreeBits(chosen, entry) << 8)
                 | (_ToThreeBits(chosen, entry + 1) << 4)
                 | _ToThreeBits(chosen, entry + 2);

      unpacked[FirstPaletteOffset + i * 2] = unpacked[SecondPaletteOffset + i * 2] = (byte)(word >> 8);
      unpacked[FirstPaletteOffset + i * 2 + 1] = unpacked[SecondPaletteOffset + i * 2 + 1] = (byte)word;
    }

    bitmap.CopyTo(unpacked, FirstBitmapOffset);
    bitmap.CopyTo(unpacked, SecondBitmapOffset);

    return new() { Unpacked = unpacked };
  }

  /// <summary>
  /// A channel on the eight levels the ST has, rounded rather than truncated so that the level the
  /// reader expands back out is the one nearest what was asked for.
  /// </summary>
  private static int _ToThreeBits(ReadOnlySpan<byte> palette, int entry)
    => entry < palette.Length ? (palette[entry] * 7 + 127) / 255 : 0;
}
