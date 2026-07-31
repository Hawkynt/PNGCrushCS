using System;
using FileFormat.Core;

namespace FileFormat.InterPaintMc;

/// <summary>In-memory representation of a Commodore 64 InterPaint Multicolor image.</summary>
public readonly record struct InterPaintMcFile : IImageFormatReader<InterPaintMcFile>, IImageToRawImage<InterPaintMcFile>, IImageFromRawImage<InterPaintMcFile>, IImageFormatWriter<InterPaintMcFile> {

  static string IImageFormatMetadata<InterPaintMcFile>.PrimaryExtension => ".ipt";
  static string[] IImageFormatMetadata<InterPaintMcFile>.FileExtensions => [".ipt"];
  static InterPaintMcFile IImageFormatReader<InterPaintMcFile>.FromSpan(ReadOnlySpan<byte> data) => InterPaintMcReader.FromSpan(data);
  static byte[] IImageFormatWriter<InterPaintMcFile>.ToBytes(InterPaintMcFile file) => InterPaintMcWriter.ToBytes(file);

  /// <summary>The fixed width of an InterPaint Multicolor image in pixels.</summary>
  public const int FixedWidth = 160;

  /// <summary>The fixed height of an InterPaint Multicolor image in pixels.</summary>
  public const int FixedHeight = 200;

  /// <summary>The expected total file size in bytes (2 + 8000 + 1000 + 1000 + 1).</summary>
  public const int ExpectedFileSize = 10003;

  /// <summary>Size of the bitmap data section in bytes.</summary>
  internal const int BitmapDataSize = 8000;

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

  /// <summary>C64 memory load address, typically 0x6000.</summary>
  public ushort LoadAddress { get; init; }

  /// <summary>Multicolor bitmap data (8000 bytes, 2 bits per pixel).</summary>
  public byte[] BitmapData { get; init; }

  /// <summary>Video matrix / screen RAM (1000 bytes, upper/lower nybble = 2 colors per cell).</summary>
  public byte[] VideoMatrix { get; init; }

  /// <summary>Color RAM (1000 bytes, lower nybble = 3rd color per cell).</summary>
  public byte[] ColorRam { get; init; }

  /// <summary>Background color index (0-15).</summary>
  public byte BackgroundColor { get; init; }

  /// <summary>Converts this InterPaint Multicolor image to a platform-independent <see cref="RawImage"/> in Rgb24 format.</summary>
  public static RawImage ToRawImage(InterPaintMcFile file)
    => Commodore64Graphics.DecodeMulticolor(
      file.BitmapData, file.VideoMatrix, file.ColorRam, file.BackgroundColor, FixedWidth, FixedHeight);

  /// <summary>Builds a screen, choosing three of the machine's colours for every character cell.</summary>
  public static InterPaintMcFile FromRawImage(RawImage image) {
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
