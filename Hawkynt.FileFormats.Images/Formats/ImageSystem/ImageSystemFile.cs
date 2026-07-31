using System;
using FileFormat.Core;

namespace FileFormat.ImageSystem;

/// <summary>In-memory representation of a C64 Image System picture (.ish hires, .ism multicolour).</summary>
/// <remarks>
/// Two formats behind one program, told apart by their length. The high-resolution one lays the
/// bitmap out across eight whole pages and puts the video matrix after them; the multicolour one
/// puts the colour RAM first, the bitmap after its page, and the matrix last.
/// </remarks>
public readonly record struct ImageSystemFile
  : IImageFormatReader<ImageSystemFile>, IImageToRawImage<ImageSystemFile>,
    IImageFromRawImage<ImageSystemFile>, IImageFormatWriter<ImageSystemFile> {

  static string IImageFormatMetadata<ImageSystemFile>.PrimaryExtension => ".ish";
  static string[] IImageFormatMetadata<ImageSystemFile>.FileExtensions => [".ish", ".ism"];
  static ImageSystemFile IImageFormatReader<ImageSystemFile>.FromSpan(ReadOnlySpan<byte> data)
    => ImageSystemReader.FromSpan(data);
  static byte[] IImageFormatWriter<ImageSystemFile>.ToBytes(ImageSystemFile file)
    => ImageSystemWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<ImageSystemFile>.VideoModes => [
    new("Hires", [(320, 200)], [16]),
    new("Multicolour", [(160, 200)], [16]),
  ];

  /// <summary>Size of the bitmap in bytes.</summary>
  public const int BitmapDataSize = 8000;

  /// <summary>Size of the video matrix in bytes.</summary>
  public const int VideoMatrixSize = 1000;

  /// <summary>Size of the colour RAM in bytes.</summary>
  public const int ColorRamSize = 1000;

  /// <summary>The exact size of a high-resolution file.</summary>
  public const int HiresFileSize = 9194;

  /// <summary>Where a high-resolution file keeps its bitmap.</summary>
  public const int HiresBitmapOffset = 2;

  /// <summary>Where a high-resolution file keeps its matrix, after the bitmap's eight pages.</summary>
  public const int HiresVideoMatrixOffset = 8194;

  /// <summary>The exact size of a multicolour file.</summary>
  public const int MulticolorFileSize = 10218;

  /// <summary>Where a multicolour file keeps its bitmap.</summary>
  public const int MulticolorBitmapOffset = 1026;

  /// <summary>Where a multicolour file keeps its matrix.</summary>
  public const int MulticolorVideoMatrixOffset = 9218;

  /// <summary>Where a multicolour file keeps its colour RAM, which comes first.</summary>
  public const int MulticolorColorRamOffset = 2;

  /// <summary>Where a multicolour file keeps its background register.</summary>
  public const int MulticolorBackgroundOffset = 9217;

  /// <summary>Picture width: 320 for hires, 160 for multicolour.</summary>
  public int Width => this.IsHires ? 320 : 160;

  /// <summary>Picture height, always 200.</summary>
  public int Height => 200;

  /// <summary>Whether this is a high-resolution picture rather than a multicolour one.</summary>
  public bool IsHires { get; init; }

  /// <summary>C64 memory load address (2 bytes, little-endian).</summary>
  public ushort LoadAddress { get; init; }

  /// <summary>Bitmap data.</summary>
  public byte[] BitmapData { get; init; }

  /// <summary>Video matrix.</summary>
  public byte[] VideoMatrix { get; init; }

  /// <summary>Colour RAM; unused by the high-resolution form.</summary>
  public byte[]? ColorRam { get; init; }

  /// <summary>The colour shown behind pattern 00 in a multicolour picture.</summary>
  public byte BackgroundColor { get; init; }

  public static RawImage ToRawImage(ImageSystemFile file)
    => file.IsHires
      ? Commodore64Graphics.DecodeHires(file.BitmapData, file.VideoMatrix, 320, 200)
      : Commodore64Graphics.DecodeMulticolor(
          file.BitmapData, file.VideoMatrix, file.ColorRam ?? new byte[ColorRamSize],
          file.BackgroundColor, 160, 200);

  /// <summary>Builds a multicolour screen, which is the form that holds the most colour.</summary>
  public static ImageSystemFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var rgb = image.SampleTo(160, 200);
    var bitmap = new byte[BitmapDataSize];
    var matrix = new byte[VideoMatrixSize];
    var colors = new byte[ColorRamSize];
    var background = Commodore64Graphics.EncodeMulticolor(rgb.PixelData, 160, 200, bitmap, matrix, colors);

    return new() {
      IsHires = false,
      LoadAddress = 0x4000,
      BitmapData = bitmap,
      VideoMatrix = matrix,
      ColorRam = colors,
      BackgroundColor = background,
    };
  }
}
