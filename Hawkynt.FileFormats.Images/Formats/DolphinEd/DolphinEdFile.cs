using System;
using FileFormat.Core;

namespace FileFormat.DolphinEd;

/// <summary>In-memory representation of a Dolphin Ed C64 multicolor image (Koala layout).</summary>
public readonly record struct DolphinEdFile : IImageFormatReader<DolphinEdFile>, IImageToRawImage<DolphinEdFile>, IImageFromRawImage<DolphinEdFile>, IImageFormatWriter<DolphinEdFile> {

  static string IImageFormatMetadata<DolphinEdFile>.PrimaryExtension => ".dol";
  static string[] IImageFormatMetadata<DolphinEdFile>.FileExtensions => [".dol", ".bed"];
  static DolphinEdFile IImageFormatReader<DolphinEdFile>.FromSpan(ReadOnlySpan<byte> data) => DolphinEdReader.FromSpan(data);
  static byte[] IImageFormatWriter<DolphinEdFile>.ToBytes(DolphinEdFile file) => DolphinEdWriter.ToBytes(file);

  /// <summary>The fixed width of a Dolphin Ed image in pixels.</summary>
  public const int FixedWidth = 160;

  /// <summary>The fixed height of a Dolphin Ed image in pixels.</summary>
  public const int FixedHeight = 200;

  /// <summary>The expected total file size in bytes (2 + 8000 + 1000 + 1000 + 1).</summary>
  public const int ExpectedFileSize = 10242;

  internal const int BitmapDataSize = 8000;

  /// <summary>Where the bitmap starts.</summary>
  /// The colour RAM comes first, then the video matrix a page later, then the bitmap after another
  /// page — so the three sections are in the reverse of the order most C64 formats use, each on its
  /// own page boundary rather than packed against the one before.
  public const int BitmapOffset = 2050;

  /// <summary>Where the video matrix starts.</summary>
  public const int VideoMatrixOffset = 1026;

  /// <summary>Where the colour RAM starts.</summary>
  public const int ColorRamOffset = 2;

  /// <summary>Where the shared background register sits.</summary>
  public const int BackgroundOffset = 2026;
  internal const int VideoMatrixSize = 1000;
  internal const int ColorRamSize = 1000;
  internal const int LoadAddressSize = 2;

  /// <summary>Image width, always 160.</summary>
  public int Width => FixedWidth;

  /// <summary>Image height, always 200.</summary>
  public int Height => FixedHeight;

  /// <summary>C64 memory load address.</summary>
  public ushort LoadAddress { get; init; }

  /// <summary>Multicolor bitmap data (8000 bytes).</summary>
  public byte[] BitmapData { get; init; }

  /// <summary>Video matrix / screen RAM (1000 bytes).</summary>
  public byte[] VideoMatrix { get; init; }

  /// <summary>Color RAM (1000 bytes).</summary>
  public byte[] ColorRam { get; init; }

  /// <summary>Background color index (0-15).</summary>
  public byte BackgroundColor { get; init; }

  /// <summary>Converts this Dolphin Ed image to a platform-independent <see cref="RawImage"/> in Rgb24 format.</summary>
  public static RawImage ToRawImage(DolphinEdFile file)
    => Commodore64Graphics.DecodeMulticolor(
      file.BitmapData, file.VideoMatrix, file.ColorRam, file.BackgroundColor, FixedWidth, FixedHeight);

  /// <summary>Builds a screen, choosing three of the machine's colours for every character cell.</summary>
  /// <remarks>
  /// The colour behind pattern 00 is one register the whole screen shares, so it is spent on
  /// whichever colour appears most often — every cell gets it free either way.
  /// </remarks>
  public static DolphinEdFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var rgb = image.SampleTo(FixedWidth, FixedHeight);
    var bitmap = new byte[BitmapDataSize];
    var matrix = new byte[VideoMatrixSize];
    var colors = new byte[ColorRamSize];
    var background = Commodore64Graphics.EncodeMulticolor(
      rgb.PixelData, FixedWidth, FixedHeight, bitmap, matrix, colors, -1);

    return new() {
      LoadAddress = 0x4000,
      BitmapData = bitmap,
      VideoMatrix = matrix,
      ColorRam = colors, BackgroundColor = background,
    };
  }
}
