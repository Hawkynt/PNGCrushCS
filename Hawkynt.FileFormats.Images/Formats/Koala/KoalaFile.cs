using System;
using FileFormat.Core;

namespace FileFormat.Koala;

/// <summary>In-memory representation of a Commodore 64 Koala Painter image.</summary>
public readonly record struct KoalaFile : IImageFormatReader<KoalaFile>, IImageToRawImage<KoalaFile>, IImageFromRawImage<KoalaFile>, IImageFormatWriter<KoalaFile> {

  static string IImageFormatMetadata<KoalaFile>.PrimaryExtension => ".koa";
  static string[] IImageFormatMetadata<KoalaFile>.FileExtensions => [".koa", ".koala", ".kla"];
  static KoalaFile IImageFormatReader<KoalaFile>.FromSpan(ReadOnlySpan<byte> data) => KoalaReader.FromSpan(data);
  static byte[] IImageFormatWriter<KoalaFile>.ToBytes(KoalaFile file) => KoalaWriter.ToBytes(file);

  /// <summary>The fixed width of a Koala image in pixels.</summary>
  public const int FixedWidth = 160;

  /// <summary>The fixed height of a Koala image in pixels.</summary>
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

  /// <summary>
  /// Reduces a picture to the multicolour screen Koala Painter saved.
  /// </summary>
  /// <remarks>
  /// The load address is the one Koala Painter itself wrote. It is not read back — the picture is
  /// the same wherever it was meant to land — but a C64 asked to load the file without it goes to
  /// whatever address it was told, and every tool that recognises these by their first two bytes
  /// looks for exactly this pair.
  /// </remarks>
  public static KoalaFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var rgb = image.SampleTo(FixedWidth, FixedHeight);
    var bitmap = new byte[BitmapDataSize];
    var videoMatrix = new byte[VideoMatrixSize];
    var colorRam = new byte[ColorRamSize];
    var background = Commodore64Graphics.EncodeMulticolor(
      rgb.PixelData, FixedWidth, FixedHeight, bitmap, videoMatrix, colorRam);

    return new() {
      LoadAddress = 0x6000,
      BitmapData = bitmap,
      VideoMatrix = videoMatrix,
      ColorRam = colorRam,
      BackgroundColor = background,
    };
  }

  /// <summary>Converts this Koala image to a platform-independent <see cref="RawImage"/> in Rgb24 format.</summary>
  public static RawImage ToRawImage(KoalaFile file)
    => Commodore64Graphics.DecodeMulticolor(
      file.BitmapData, file.VideoMatrix, file.ColorRam, file.BackgroundColor, FixedWidth, FixedHeight);

}
