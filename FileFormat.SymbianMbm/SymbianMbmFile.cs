using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.SymbianMbm;

/// <summary>In-memory representation of a Symbian OS MBM (multi-bitmap) container.</summary>
[FormatMagicBytes([0x37, 0x00, 0x00, 0x10])]
public sealed class SymbianMbmFile : IImageFileFormat<SymbianMbmFile> {

  static string IImageFileFormat<SymbianMbmFile>.PrimaryExtension => ".mbm";
  static string[] IImageFileFormat<SymbianMbmFile>.FileExtensions => [".mbm"];
  static SymbianMbmFile IImageFileFormat<SymbianMbmFile>.FromFile(FileInfo file) => SymbianMbmReader.FromFile(file);
  static SymbianMbmFile IImageFileFormat<SymbianMbmFile>.FromBytes(byte[] data) => SymbianMbmReader.FromBytes(data);
  static SymbianMbmFile IImageFileFormat<SymbianMbmFile>.FromStream(Stream stream) => SymbianMbmReader.FromStream(stream);
  static byte[] IImageFileFormat<SymbianMbmFile>.ToBytes(SymbianMbmFile file) => SymbianMbmWriter.ToBytes(file);

  /// <summary>UID1 value (always 0x10000037).</summary>
  public const uint Uid1 = 0x10000037;

  /// <summary>UID2 value (always 0x10000000).</summary>
  public const uint Uid2 = 0x10000000;

  /// <summary>UID3 value (always 0).</summary>
  public const uint Uid3 = 0x00000000;

  /// <summary>Size of the MBM file header in bytes.</summary>
  public const int HeaderSize = 20;

  /// <summary>Minimum size of a valid MBM file (header + trailer count).</summary>
  public const int MinimumFileSize = HeaderSize + 4;

  /// <summary>Size of each bitmap header in bytes.</summary>
  public const int BitmapHeaderSize = 40;

  /// <summary>The individual bitmap entries in this MBM container.</summary>
  public SymbianMbmBitmap[] Bitmaps { get; init; } = [];

  public static RawImage ToRawImage(SymbianMbmFile file) {
    ArgumentNullException.ThrowIfNull(file);
    if (file.Bitmaps.Length == 0)
      throw new InvalidDataException("MBM file contains no bitmaps.");

    var bmp = file.Bitmaps[0];
    var width = bmp.Width;
    var height = bmp.Height;

    return bmp.BitsPerPixel switch {
      1 or 2 or 4 or 8 => _ToGray8(bmp, width, height),
      24 => _ToRgb24(bmp, width, height),
      _ => throw new InvalidDataException($"Unsupported bits per pixel: {bmp.BitsPerPixel}.")
    };
  }

  public static SymbianMbmFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    return image.Format switch {
      PixelFormat.Gray8 => _FromGray8(image),
      PixelFormat.Rgb24 => _FromRgb24(image),
      _ => throw new ArgumentException($"Expected {PixelFormat.Gray8} or {PixelFormat.Rgb24} but got {image.Format}.", nameof(image))
    };
  }

  private static RawImage _ToGray8(SymbianMbmBitmap bmp, int width, int height) {
    var bpp = bmp.BitsPerPixel;
    var pixelsPerByte = 8 / bpp;
    var mask = (1 << bpp) - 1;
    var maxVal = (1 << bpp) - 1;
    var bytesPerRow = (width * bpp + 31) / 32 * 4;
    var pixels = new byte[width * height];

    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var byteIndex = y * bytesPerRow + x / pixelsPerByte;
        var bitShift = (x % pixelsPerByte) * bpp;
        var value = byteIndex < bmp.PixelData.Length
          ? (bmp.PixelData[byteIndex] >> bitShift) & mask
          : 0;

        pixels[y * width + x] = (byte)(value * 255 / maxVal);
      }

    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Gray8,
      PixelData = pixels,
    };
  }

  private static RawImage _ToRgb24(SymbianMbmBitmap bmp, int width, int height) {
    var bytesPerRow = (width * 24 + 31) / 32 * 4;
    var pixels = new byte[width * height * 3];

    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var srcOffset = y * bytesPerRow + x * 3;
        var dstOffset = (y * width + x) * 3;
        if (srcOffset + 2 < bmp.PixelData.Length) {
          // MBM stores BGR, convert to RGB
          pixels[dstOffset] = bmp.PixelData[srcOffset + 2];
          pixels[dstOffset + 1] = bmp.PixelData[srcOffset + 1];
          pixels[dstOffset + 2] = bmp.PixelData[srcOffset];
        }
      }

    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Rgb24,
      PixelData = pixels,
    };
  }

  private static SymbianMbmFile _FromGray8(RawImage image) {
    var width = image.Width;
    var height = image.Height;
    var bytesPerRow = (width * 8 + 31) / 32 * 4;
    var pixelData = new byte[bytesPerRow * height];

    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x)
        pixelData[y * bytesPerRow + x] = image.PixelData[y * width + x];

    var dataSize = pixelData.Length;

    return new() {
      Bitmaps = [
        new() {
          Width = width,
          Height = height,
          BitsPerPixel = 8,
          ColorMode = 0,
          Compression = 0,
          PaletteSize = 0,
          PixelData = pixelData,
          DataSize = (uint)dataSize,
        }
      ]
    };
  }

  private static SymbianMbmFile _FromRgb24(RawImage image) {
    var width = image.Width;
    var height = image.Height;
    var bytesPerRow = (width * 24 + 31) / 32 * 4;
    var pixelData = new byte[bytesPerRow * height];

    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var srcOffset = (y * width + x) * 3;
        var dstOffset = y * bytesPerRow + x * 3;
        // Convert RGB to BGR for MBM storage
        pixelData[dstOffset] = image.PixelData[srcOffset + 2];
        pixelData[dstOffset + 1] = image.PixelData[srcOffset + 1];
        pixelData[dstOffset + 2] = image.PixelData[srcOffset];
      }

    var dataSize = pixelData.Length;

    return new() {
      Bitmaps = [
        new() {
          Width = width,
          Height = height,
          BitsPerPixel = 24,
          ColorMode = 0,
          Compression = 0,
          PaletteSize = 0,
          PixelData = pixelData,
          DataSize = (uint)dataSize,
        }
      ]
    };
  }
}

/// <summary>A single bitmap entry within an MBM container.</summary>
public sealed class SymbianMbmBitmap {

  /// <summary>Width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>Bits per pixel (1, 2, 4, 8, or 24).</summary>
  public int BitsPerPixel { get; init; }

  /// <summary>Color mode identifier.</summary>
  public uint ColorMode { get; init; }

  /// <summary>Compression type (0 = uncompressed).</summary>
  public uint Compression { get; init; }

  /// <summary>Number of palette entries.</summary>
  public uint PaletteSize { get; init; }

  /// <summary>Size of the pixel data in bytes.</summary>
  public uint DataSize { get; init; }

  /// <summary>Raw pixel data bytes.</summary>
  public byte[] PixelData { get; init; } = [];
}
