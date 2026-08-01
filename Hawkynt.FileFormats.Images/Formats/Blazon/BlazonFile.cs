using System;
using FileFormat.Core;

namespace FileFormat.Blazon;

/// <summary>In-memory representation of a Blazon picture (.bpl).</summary>
/// <remarks>
/// A standard multicolour screen whose sections sit on page boundaries rather than against one
/// another, with the shared background register tucked into the gap behind the video matrix instead
/// of following the colour RAM.
/// </remarks>
public readonly record struct BlazonFile
  : IImageFormatReader<BlazonFile>, IImageToRawImage<BlazonFile>,
    IImageFromRawImage<BlazonFile>, IImageFormatWriter<BlazonFile> {

  static string IImageFormatMetadata<BlazonFile>.PrimaryExtension => ".bpl";
  static string[] IImageFormatMetadata<BlazonFile>.FileExtensions => [".bpl"];
  static BlazonFile IImageFormatReader<BlazonFile>.FromSpan(ReadOnlySpan<byte> data) => BlazonReader.FromSpan(data);
  static byte[] IImageFormatWriter<BlazonFile>.ToBytes(BlazonFile file) => BlazonWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<BlazonFile>.VideoModes => [
    new("Multicolour", [(FixedWidth, FixedHeight)], [16])
  ];

  /// <summary>Pixels across.</summary>
  public const int FixedWidth = 160;

  /// <summary>Rows.</summary>
  public const int FixedHeight = 200;

  /// <summary>The exact file size.</summary>
  public const int ExpectedFileSize = 10242;

  /// <summary>Size of the bitmap.</summary>
  public const int BitmapDataSize = 8000;

  /// <summary>Size of the video matrix.</summary>
  public const int VideoMatrixSize = 1000;

  /// <summary>Size of the colour RAM.</summary>
  public const int ColorRamSize = 1000;

  /// <summary>Where the bitmap starts.</summary>
  public const int BitmapOffset = 2;

  /// <summary>Where the video matrix starts, after the bitmap's eight whole pages.</summary>
  public const int VideoMatrixOffset = 8194;

  /// <summary>Where the colour RAM starts.</summary>
  public const int ColorRamOffset = 9218;

  /// <summary>Where the background register sits: in the gap behind the bitmap, not after the colours.</summary>
  public const int BackgroundOffset = 8066;

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

  public static RawImage ToRawImage(BlazonFile file)
    => Commodore64Graphics.DecodeMulticolor(
      file.BitmapData, file.VideoMatrix, file.ColorRam, file.BackgroundColor, FixedWidth, FixedHeight);

  /// <summary>Builds a screen, choosing three of the machine's colours for every character cell.</summary>
  public static BlazonFile FromRawImage(RawImage image) {
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
