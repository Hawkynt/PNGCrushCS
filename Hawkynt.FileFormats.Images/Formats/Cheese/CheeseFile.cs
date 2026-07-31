using System;
using FileFormat.Core;

namespace FileFormat.Cheese;

/// <summary>In-memory representation of a Commodore 64 Cheese paint image.</summary>
public readonly record struct CheeseFile : IImageFormatReader<CheeseFile>, IImageToRawImage<CheeseFile>, IImageFromRawImage<CheeseFile>, IImageFormatWriter<CheeseFile> {

  static string IImageFormatMetadata<CheeseFile>.PrimaryExtension => ".che";
  static string[] IImageFormatMetadata<CheeseFile>.FileExtensions => [".che", ".chs"];
  static CheeseFile IImageFormatReader<CheeseFile>.FromSpan(ReadOnlySpan<byte> data) => CheeseReader.FromSpan(data);
  static byte[] IImageFormatWriter<CheeseFile>.ToBytes(CheeseFile file) => CheeseWriter.ToBytes(file);

  /// <summary>The fixed width of a Cheese image in pixels.</summary>
  public const int FixedWidth = 160;

  /// <summary>The fixed height of a Cheese image in pixels.</summary>
  public const int FixedHeight = 200;

  /// <summary>The expected total file size in bytes (2 + 8000 + 1000 + 1000 + 1 + 1 + 14).</summary>
  public const int ExpectedFileSize = 20482;

  /// <summary>Size of the load address in bytes.</summary>
  internal const int LoadAddressSize = 2;

  /// <summary>Size of the bitmap data section in bytes.</summary>
  internal const int BitmapDataSize = 8000;

  /// <summary>Where the bitmap starts.</summary>
  /// <remarks>
  /// The bitmap is given far more room than a screen needs — the file reserves two whole sets of
  /// pages for it — so the video matrix does not begin until 16898, well past the eight thousand
  /// bytes the picture actually occupies.
  /// </remarks>
  public const int BitmapOffset = 2;

  /// <summary>Where the video matrix starts.</summary>
  public const int VideoMatrixOffset = 16898;

  /// <summary>Where the colour RAM starts.</summary>
  public const int ColorRamOffset = 18434;

  /// <summary>Where the shared background register sits.</summary>
  public const int BackgroundOffset = 20479;

  /// <summary>Size of the video matrix section in bytes.</summary>
  internal const int VideoMatrixSize = 1000;

  /// <summary>Size of the color RAM section in bytes.</summary>
  internal const int ColorRamSize = 1000;

  /// <summary>Size of the padding section in bytes.</summary>
  internal const int PaddingSize = 14;

  /// <summary>Image width, always 160.</summary>
  public int Width => FixedWidth;

  /// <summary>Image height, always 200.</summary>
  public int Height => FixedHeight;

  /// <summary>C64 memory load address, typically 0x2000.</summary>
  public ushort LoadAddress { get; init; }

  /// <summary>Multicolor bitmap data (8000 bytes, 2 bits per pixel).</summary>
  public byte[] BitmapData { get; init; }

  /// <summary>Video matrix / screen RAM (1000 bytes, upper/lower nybble = 2 colors per cell).</summary>
  public byte[] VideoMatrix { get; init; }

  /// <summary>Color RAM (1000 bytes, lower nybble = 3rd color per cell).</summary>
  public byte[] ColorRam { get; init; }

  /// <summary>Border color index (0-15).</summary>
  public byte BorderColor { get; init; }

  /// <summary>Background color index (0-15).</summary>
  public byte BackgroundColor { get; init; }

  /// <summary>Trailing padding bytes (14 bytes).</summary>
  public byte[] Padding { get; init; }

  /// <summary>Converts this Cheese image to a platform-independent <see cref="RawImage"/> in Rgb24 format.</summary>
  public static RawImage ToRawImage(CheeseFile file)
    => Commodore64Graphics.DecodeMulticolor(
      file.BitmapData, file.VideoMatrix, file.ColorRam, file.BackgroundColor, FixedWidth, FixedHeight);

  /// <summary>Builds a screen, choosing three of the machine's colours for every character cell.</summary>
  public static CheeseFile FromRawImage(RawImage image) {
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
      BorderColor = background,
    };
  }
}
