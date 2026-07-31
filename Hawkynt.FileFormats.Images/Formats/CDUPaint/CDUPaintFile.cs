using System;
using FileFormat.Core;

namespace FileFormat.CDUPaint;

/// <summary>In-memory representation of a Commodore 64 CDU-Paint multicolor image.</summary>
public readonly record struct CDUPaintFile : IImageFormatReader<CDUPaintFile>, IImageToRawImage<CDUPaintFile>, IImageFromRawImage<CDUPaintFile>, IImageFormatWriter<CDUPaintFile> {

  static string IImageFormatMetadata<CDUPaintFile>.PrimaryExtension => ".cdu";
  static string[] IImageFormatMetadata<CDUPaintFile>.FileExtensions => [".cdu"];
  static CDUPaintFile IImageFormatReader<CDUPaintFile>.FromSpan(ReadOnlySpan<byte> data) => CDUPaintReader.FromSpan(data);
  static byte[] IImageFormatWriter<CDUPaintFile>.ToBytes(CDUPaintFile file) => CDUPaintWriter.ToBytes(file);

  /// <summary>The fixed width of a CDU-Paint image in pixels.</summary>
  public const int FixedWidth = 160;

  /// <summary>The fixed height of a CDU-Paint image in pixels.</summary>
  public const int FixedHeight = 200;

  /// <summary>The expected total file size in bytes (2 + 8000 + 1000 + 1000 + 1).</summary>
  public const int ExpectedFileSize = 10277;

  /// <summary>Size of the bitmap data section in bytes.</summary>
  internal const int BitmapDataSize = 8000;

  /// <summary>Where the bitmap starts.</summary>
  /// A 275-byte preamble the program wrote before the screen, then the three sections back to back
  /// and the background register at the very end.
  public const int BitmapOffset = 275;

  /// <summary>Where the video matrix starts.</summary>
  public const int VideoMatrixOffset = 8275;

  /// <summary>Where the colour RAM starts.</summary>
  public const int ColorRamOffset = 9275;

  /// <summary>Where the shared background register sits.</summary>
  public const int BackgroundOffset = 10275;

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

  /// <summary>Background color index (0-15).</summary>
  public byte BackgroundColor { get; init; }

  /// <summary>Converts this CDU-Paint image to a platform-independent <see cref="RawImage"/> in Rgb24 format.</summary>
  public static RawImage ToRawImage(CDUPaintFile file)
    => Commodore64Graphics.DecodeMulticolor(
      file.BitmapData, file.VideoMatrix, file.ColorRam, file.BackgroundColor, FixedWidth, FixedHeight);

  /// <summary>Builds a screen, choosing three of the machine's colours for every character cell.</summary>
  /// <remarks>
  /// The colour behind pattern 00 is one register the whole screen shares, so it is spent on
  /// whichever colour appears most often — every cell gets it free either way.
  /// </remarks>
  public static CDUPaintFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var rgb = image.SampleTo(FixedWidth, FixedHeight);
    var bitmap = new byte[BitmapDataSize];
    var matrix = new byte[VideoMatrixSize];
    var colors = new byte[ColorRamSize];
    var background = Commodore64Graphics.EncodeMulticolor(
      rgb.PixelData, FixedWidth, FixedHeight, bitmap, matrix, colors, -1);

    return new() {
      LoadAddress = 0x6000,
      BitmapData = bitmap,
      VideoMatrix = matrix,
      ColorRam = colors, BackgroundColor = background,
    };
  }
}
