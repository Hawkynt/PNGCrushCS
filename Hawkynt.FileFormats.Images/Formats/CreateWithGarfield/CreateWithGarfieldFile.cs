using System;
using FileFormat.Core;

namespace FileFormat.CreateWithGarfield;

/// <summary>In-memory representation of a Commodore 64 Create with Garfield picture (.cwg).</summary>
/// <remarks>
/// A standard multicolour screen — the bitmap, then the video matrix, then the colour RAM, then the
/// one background register the whole screen shares — behind the load address every C64 file carries.
/// The picture is 160 pixels across rather than 320: multicolour spends two bits a pixel and buys
/// the third colour per cell with half the horizontal resolution.
/// </remarks>
public readonly record struct CreateWithGarfieldFile : IImageFormatReader<CreateWithGarfieldFile>, IImageToRawImage<CreateWithGarfieldFile>, IImageFromRawImage<CreateWithGarfieldFile>, IImageFormatWriter<CreateWithGarfieldFile> {

  static string IImageFormatMetadata<CreateWithGarfieldFile>.PrimaryExtension => ".cwg";
  static string[] IImageFormatMetadata<CreateWithGarfieldFile>.FileExtensions => [".cwg"];
  static CreateWithGarfieldFile IImageFormatReader<CreateWithGarfieldFile>.FromSpan(ReadOnlySpan<byte> data) => CreateWithGarfieldReader.FromSpan(data);
  static byte[] IImageFormatWriter<CreateWithGarfieldFile>.ToBytes(CreateWithGarfieldFile file) => CreateWithGarfieldWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<CreateWithGarfieldFile>.VideoModes => [
    new("Multicolour", [(FixedWidth, FixedHeight)], [16])
  ];

  /// <summary>The fixed width of a Create with Garfield picture in pixels.</summary>
  public const int FixedWidth = 160;

  /// <summary>The fixed height of a Create with Garfield picture in pixels.</summary>
  public const int FixedHeight = 200;

  /// <summary>The expected total file size in bytes.</summary>
  public const int ExpectedFileSize = 10007;

  /// <summary>Size of the bitmap data section in bytes.</summary>
  internal const int BitmapDataSize = 8000;

  /// <summary>Size of the video matrix in bytes.</summary>
  internal const int VideoMatrixSize = 1000;

  /// <summary>Size of the colour RAM in bytes.</summary>
  internal const int ColorRamSize = 1000;

  /// <summary>Size of the load address in bytes.</summary>
  internal const int LoadAddressSize = 2;

  /// <summary>Where the bitmap starts.</summary>
  public const int BitmapOffset = 2;

  /// <summary>Where the video matrix starts.</summary>
  public const int VideoMatrixOffset = 8002;

  /// <summary>Where the colour RAM starts.</summary>
  public const int ColorRamOffset = 9002;

  /// <summary>Where the shared background register sits.</summary>
  public const int BackgroundOffset = 10002;

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

  public static RawImage ToRawImage(CreateWithGarfieldFile file)
    => Commodore64Graphics.DecodeMulticolor(
      file.BitmapData, file.VideoMatrix, file.ColorRam, file.BackgroundColor, FixedWidth, FixedHeight);

  /// <summary>Builds a screen, choosing three of the machine's colours for every character cell.</summary>
  public static CreateWithGarfieldFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var rgb = image.SampleTo(FixedWidth, FixedHeight);
    var bitmap = new byte[BitmapDataSize];
    var matrix = new byte[VideoMatrixSize];
    var colors = new byte[ColorRamSize];
    var background = Commodore64Graphics.EncodeMulticolor(
      rgb.PixelData, FixedWidth, FixedHeight, bitmap, matrix, colors);

    return new() {
      LoadAddress = 0x4000,
      BitmapData = bitmap,
      VideoMatrix = matrix,
      ColorRam = colors,
      BackgroundColor = background,
    };
  }
}
