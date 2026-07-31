using System;
using FileFormat.Core;

namespace FileFormat.FacePainter;

/// <summary>In-memory representation of a Commodore 64 Face Painter multicolor image.</summary>
public readonly record struct FacePainterFile : IImageFormatReader<FacePainterFile>, IImageToRawImage<FacePainterFile>, IImageFromRawImage<FacePainterFile>, IImageFormatWriter<FacePainterFile> {

  static string IImageFormatMetadata<FacePainterFile>.PrimaryExtension => ".fpt";
  static string[] IImageFormatMetadata<FacePainterFile>.FileExtensions => [".fpt"];
  static FacePainterFile IImageFormatReader<FacePainterFile>.FromSpan(ReadOnlySpan<byte> data) => FacePainterReader.FromSpan(data);
  static byte[] IImageFormatWriter<FacePainterFile>.ToBytes(FacePainterFile file) => FacePainterWriter.ToBytes(file);

  /// <summary>The fixed width of a Face Painter image in pixels.</summary>
  public const int FixedWidth = 160;

  /// <summary>The fixed height of a Face Painter image in pixels.</summary>
  public const int FixedHeight = 200;

  /// <summary>The expected total file size in bytes (2 + 8000 + 1000 + 1000).</summary>
  public const int ExpectedFileSize = 10004;

  /// <summary>Size of the bitmap data section in bytes.</summary>
  internal const int BitmapDataSize = 8000;

  /// <summary>Where the bitmap starts.</summary>
  public const int BitmapOffset = 2;

  /// <summary>Where the video matrix starts.</summary>
  public const int VideoMatrixOffset = 8002;

  /// <summary>Where the colour RAM starts.</summary>
  public const int ColorRamOffset = 9002;

  /// <summary>Where the shared background register sits.</summary>
  public const int BackgroundOffset = 10002;

  /// <summary>Size of the video matrix section in bytes.</summary>
  internal const int VideoMatrixSize = 1000;

  /// <summary>Size of the color RAM section in bytes.</summary>
  internal const int ColorRamSize = 1000;

  /// <summary>Size of the load address in bytes.</summary>
  internal const int LoadAddressSize = 2;

  /// <summary>Image width, always 160.</summary>
  public int Width => FixedWidth;

  /// <summary>Image height, always 200.</summary>
  public int Height => FixedHeight;

  /// <summary>C64 memory load address (2 bytes, little-endian).</summary>
  public ushort LoadAddress { get; init; }

  /// <summary>Multicolor bitmap data (8000 bytes, 2 bits per pixel).</summary>
  public byte[] BitmapData { get; init; }

  /// <summary>Video matrix / screen RAM (1000 bytes, upper/lower nybble = 2 colors per cell).</summary>
  public byte[] VideoMatrix { get; init; }

  /// <summary>Color RAM (1000 bytes, lower nybble = 3rd color per cell).</summary>
  public byte[] ColorRam { get; init; }

  /// <summary>Converts this Face Painter image to a platform-independent <see cref="RawImage"/> in Rgb24 format.</summary>
  /// <summary>The colour shown behind pattern 00, which the file stores after the colour RAM.</summary>
  public byte BackgroundColor { get; init; }

  public static RawImage ToRawImage(FacePainterFile file)
    => Commodore64Graphics.DecodeMulticolor(
      file.BitmapData, file.VideoMatrix, file.ColorRam, file.BackgroundColor, FixedWidth, FixedHeight);

  /// <summary>Builds a screen, choosing three of the machine's colours for every character cell.</summary>
  public static FacePainterFile FromRawImage(RawImage image) {
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
