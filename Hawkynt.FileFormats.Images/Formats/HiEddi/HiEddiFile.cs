using System;
using FileFormat.Core;

namespace FileFormat.HiEddi;

/// <summary>In-memory representation of a HiEddi C64 hires image (Doodle layout).</summary>
public readonly record struct HiEddiFile : IImageFormatReader<HiEddiFile>, IImageToRawImage<HiEddiFile>, IImageFromRawImage<HiEddiFile>, IImageFormatWriter<HiEddiFile> {

  static string IImageFormatMetadata<HiEddiFile>.PrimaryExtension => ".hed";
  static string[] IImageFormatMetadata<HiEddiFile>.FileExtensions => [".hed"];
  static HiEddiFile IImageFormatReader<HiEddiFile>.FromSpan(ReadOnlySpan<byte> data) => HiEddiReader.FromSpan(data);
  static byte[] IImageFormatWriter<HiEddiFile>.ToBytes(HiEddiFile file) => HiEddiWriter.ToBytes(file);

  /// <summary>The fixed width of a HiEddi image in pixels.</summary>
  public const int FixedWidth = 320;

  /// <summary>The fixed height of a HiEddi image in pixels.</summary>
  public const int FixedHeight = 200;

  /// <summary>The expected total file size: loadAddress(2) + bitmapData(8000) + screenRam(1000) + padding(216) = 9218.</summary>
  public const int ExpectedFileSize = 9218;

  internal const int BitmapDataSize = 8000;
  internal const int ScreenRamSize = 1000;
  internal const int LoadAddressSize = 2;
  internal const int PaddingSize = 216;

  /// <summary>Offset of the bitmap.</summary>
  public const int BitmapOffset = LoadAddressSize;

  /// <summary>
  /// Offset of the video matrix. The bitmap occupies the whole eight pages the machine gives it,
  /// which is 8192 bytes rather than the 8000 a screen uses, so the matrix begins after the pages.
  /// </summary>
  public const int ScreenRamOffset = 8194;

  /// <summary>Image width, always 320.</summary>
  public int Width => FixedWidth;

  /// <summary>Image height, always 200.</summary>
  public int Height => FixedHeight;

  /// <summary>C64 memory load address.</summary>
  public ushort LoadAddress { get; init; }

  /// <summary>Hires bitmap data (8000 bytes, 1 bit per pixel).</summary>
  public byte[] BitmapData { get; init; }

  /// <summary>Screen RAM (1000 bytes, upper/lower nybble = fg/bg color per 8x8 cell).</summary>
  public byte[] ScreenRam { get; init; }

  /// <summary>Converts this HiEddi image to a platform-independent <see cref="RawImage"/> in Rgb24 format.</summary>
  public static RawImage ToRawImage(HiEddiFile file)
    => Commodore64Graphics.DecodeHires(file.BitmapData, file.ScreenRam, FixedWidth, FixedHeight);

  /// <summary>Builds a screen, choosing two of the machine's colours for every character cell.</summary>
  /// <remarks>
  /// The picture is reduced cell by cell because that is the constraint the hardware imposes: eight
  /// pixels by eight may show two colours and no more, and which two is decided for that cell alone.
  /// </remarks>
  public static HiEddiFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var rgb = image.SampleTo(FixedWidth, FixedHeight);
    var bitmap = new byte[BitmapDataSize];
    var screen = new byte[ScreenRamSize];
    Commodore64Graphics.EncodeHires(rgb.PixelData, FixedWidth, FixedHeight, bitmap, screen);

    return new() { LoadAddress = 0x4000, BitmapData = bitmap, ScreenRam = screen };
  }
}
