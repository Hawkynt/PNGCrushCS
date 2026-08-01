using System;
using FileFormat.Core;

namespace FileFormat.Picasso64;

/// <summary>In-memory representation of a Commodore 64 Picasso 64 picture (.p64).</summary>
/// <remarks>
/// A standard multicolour screen: two bits a pixel, the video matrix holding two of each cell's
/// colours and the colour RAM a third, with pattern 00 taken from the one register the whole screen
/// shares. The picture is 160 across rather than 320 — multicolour buys its third colour per cell
/// with half the horizontal resolution.
/// <para/>
/// The colour RAM comes first, the video matrix a page later and the bitmap after another, each on its own page boundary.
/// </remarks>
public readonly record struct Picasso64File
  : IImageFormatReader<Picasso64File>, IImageToRawImage<Picasso64File>,
    IImageFromRawImage<Picasso64File>, IImageFormatWriter<Picasso64File> {

  static string IImageFormatMetadata<Picasso64File>.PrimaryExtension => ".p64";
  static string[] IImageFormatMetadata<Picasso64File>.FileExtensions => [".p64", ".fly"];
  static Picasso64File IImageFormatReader<Picasso64File>.FromSpan(ReadOnlySpan<byte> data) => Picasso64Reader.FromSpan(data);
  static byte[] IImageFormatWriter<Picasso64File>.ToBytes(Picasso64File file) => Picasso64Writer.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<Picasso64File>.VideoModes => [
    new("Multicolour", [(FixedWidth, FixedHeight)], [16])
  ];

  /// <summary>The fixed width of the picture in pixels.</summary>
  public const int FixedWidth = 160;

  /// <summary>The fixed height of the picture in pixels.</summary>
  public const int FixedHeight = 200;

  /// <summary>The exact file size.</summary>
  public const int ExpectedFileSize = 10050;

  /// <summary>Size of the bitmap in bytes.</summary>
  public const int BitmapDataSize = 8000;

  /// <summary>Size of the video matrix in bytes.</summary>
  public const int VideoMatrixSize = 1000;

  /// <summary>Size of the colour RAM in bytes.</summary>
  public const int ColorRamSize = 1000;

  /// <summary>Where the bitmap starts.</summary>
  public const int BitmapOffset = 2050;

  /// <summary>Where the video matrix starts.</summary>
  public const int VideoMatrixOffset = 1026;

  /// <summary>Where the colour RAM starts.</summary>
  public const int ColorRamOffset = 2;

  /// <summary>Where the shared background register sits, or -1 if the file has none.</summary>
  public const int BackgroundOffset = 2049;

  /// <summary>Picture width, always 160.</summary>
  public int Width => FixedWidth;

  /// <summary>Picture height, always 200.</summary>
  public int Height => FixedHeight;

  /// <summary>C64 memory load address (2 bytes, little-endian).</summary>
  public ushort LoadAddress { get; init; }

  /// <summary>Bitmap data, two bits a pixel within 4x8 cells.</summary>
  public byte[] BitmapData { get; init; }

  /// <summary>Video matrix: two of each cell's colours, one per nibble.</summary>
  public byte[] VideoMatrix { get; init; }

  /// <summary>Colour RAM: the third colour of each cell.</summary>
  public byte[] ColorRam { get; init; }

  /// <summary>The colour shown behind pattern 00, shared by the whole screen.</summary>
  public byte BackgroundColor { get; init; }

  public static RawImage ToRawImage(Picasso64File file)
    => Commodore64Graphics.DecodeMulticolor(
      file.BitmapData, file.VideoMatrix, file.ColorRam, file.BackgroundColor, FixedWidth, FixedHeight);

  /// <summary>Builds a screen, choosing three of the machine's colours for every character cell.</summary>
  public static Picasso64File FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var rgb = image.SampleTo(FixedWidth, FixedHeight);
    var bitmap = new byte[BitmapDataSize];
    var matrix = new byte[VideoMatrixSize];
    var colors = new byte[ColorRamSize];
    var background = Commodore64Graphics.EncodeMulticolor(
      rgb.PixelData, FixedWidth, FixedHeight, bitmap, matrix, colors, BackgroundOffset < 0 ? 0 : -1);

    return new() {
      LoadAddress = 0x4000,
      BitmapData = bitmap,
      VideoMatrix = matrix,
      ColorRam = colors,
      BackgroundColor = background,
    };
  }
}
