using System;
using FileFormat.Core;

namespace FileFormat.HiPicCreator;

/// <summary>In-memory representation of a C64 Hi-Pic Creator picture (.hpc).</summary>
/// <remarks>
/// A high-resolution screen, not a multicolour one: 320 pixels across, one bit a pixel, with the
/// video matrix naming the two colours each cell may show. The load address comes first, the bitmap
/// after it, and the matrix after the bitmap's eight thousand bytes.
/// </remarks>
public readonly record struct HiPicCreatorFile : IImageFormatReader<HiPicCreatorFile>, IImageToRawImage<HiPicCreatorFile>, IImageFromRawImage<HiPicCreatorFile>, IImageFormatWriter<HiPicCreatorFile> {

  static string IImageFormatMetadata<HiPicCreatorFile>.PrimaryExtension => ".hpc";
  static string[] IImageFormatMetadata<HiPicCreatorFile>.FileExtensions => [".hpc"];
  static HiPicCreatorFile IImageFormatReader<HiPicCreatorFile>.FromSpan(ReadOnlySpan<byte> data) => HiPicCreatorReader.FromSpan(data);
  static byte[] IImageFormatWriter<HiPicCreatorFile>.ToBytes(HiPicCreatorFile file) => HiPicCreatorWriter.ToBytes(file);

  /// <summary>The fixed width of the picture in pixels.</summary>
  public const int FixedWidth = 320;

  /// <summary>The fixed height of the image in pixels.</summary>
  public const int FixedHeight = 200;

  /// <summary>Size of the load address in bytes.</summary>
  internal const int LoadAddressSize = 2;

  /// <summary>Size of the bitmap data section in bytes.</summary>
  internal const int BitmapSize = 8000;

  /// <summary>Size of the screen RAM section in bytes.</summary>
  internal const int ScreenRamSize = 1000;

  /// <summary>Size of the color RAM section in bytes.</summary>
  internal const int ColorRamSize = 1000;

  /// <summary>Minimum payload size in bytes (bitmap + screen + color).</summary>
  internal const int MinPayloadSize = BitmapSize + ScreenRamSize;

  /// <summary>Where the bitmap starts.</summary>
  public const int BitmapOffset = LoadAddressSize;

  /// <summary>Where the video matrix starts.</summary>
  public const int VideoMatrixOffset = BitmapOffset + BitmapSize;

  /// <summary>The size a file written from a picture takes.</summary>
  public const int ExpectedFileSize = VideoMatrixOffset + ScreenRamSize;

  /// <summary>Picture width, always 320.</summary>
  public int Width => FixedWidth;

  /// <summary>Image height, always 200.</summary>
  public int Height => FixedHeight;

  /// <summary>C64 memory load address (2 bytes, little-endian).</summary>
  public ushort LoadAddress { get; init; }

  /// <summary>Raw payload data (entire file content after load address).</summary>
  public byte[] RawData { get; init; }

  public static RawImage ToRawImage(HiPicCreatorFile file) {
    var data = file.RawData ?? [];
    var bitmap = data.AsSpan(0, Math.Min(data.Length, BitmapSize));
    var matrix = data.Length > BitmapSize ? data.AsSpan(BitmapSize) : [];

    var padded = new byte[BitmapSize];
    bitmap.CopyTo(padded);
    var screen = new byte[ScreenRamSize];
    matrix[..Math.Min(matrix.Length, ScreenRamSize)].CopyTo(screen);

    return Commodore64Graphics.DecodeHires(padded, screen, FixedWidth, FixedHeight);
  }

  /// <summary>Builds a screen, choosing two of the machine's colours for every character cell.</summary>
  public static HiPicCreatorFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var rgb = image.SampleTo(FixedWidth, FixedHeight);
    var payload = new byte[BitmapSize + ScreenRamSize];
    Commodore64Graphics.EncodeHires(
      rgb.PixelData, FixedWidth, FixedHeight,
      payload.AsSpan(0, BitmapSize), payload.AsSpan(BitmapSize, ScreenRamSize));

    return new() { LoadAddress = 0x4000, RawData = payload };
  }

}
