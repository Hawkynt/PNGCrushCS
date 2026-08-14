using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Core;

namespace FileFormat.SymbianMbm;

/// <summary>In-memory representation of a Symbian OS MBM (multi-bitmap) container.</summary>
[FormatMagicBytes([0x37, 0x00, 0x00, 0x10])]
public readonly record struct SymbianMbmFile : IImageFormatReader<SymbianMbmFile>, IImageToRawImage<SymbianMbmFile>, IImageFromRawImage<SymbianMbmFile>, IImageFormatWriter<SymbianMbmFile> {

  static string IImageFormatMetadata<SymbianMbmFile>.PrimaryExtension => ".mbm";
  static string[] IImageFormatMetadata<SymbianMbmFile>.FileExtensions => [".mbm"];
  static SymbianMbmFile IImageFormatReader<SymbianMbmFile>.FromSpan(ReadOnlySpan<byte> data) => SymbianMbmReader.FromSpan(data);
  static byte[] IImageFormatWriter<SymbianMbmFile>.ToBytes(SymbianMbmFile file) => SymbianMbmWriter.ToBytes(file);

  /// <summary>UID1: Symbian's direct file store layout UID, on every MBM.</summary>
  public const uint Uid1 = 0x10000037;

  /// <summary>UID2: Symbian's multi-bitmap file image UID, on every MBM.</summary>
  public const uint Uid2 = 0x10000042;

  /// <summary>UID3 value (always 0).</summary>
  public const uint Uid3 = 0x00000000;

  /// <summary>Size of the MBM file header in bytes.</summary>
  public const int HeaderSize = 20;

  /// <summary>Minimum size of a valid MBM file (header + trailer count).</summary>
  public const int MinimumFileSize = HeaderSize + 4;

  /// <summary>Size of each bitmap header in bytes.</summary>
  public const int BitmapHeaderSize = 40;

  /// <summary>The individual bitmap entries in this MBM container.</summary>
  public SymbianMbmBitmap[] Bitmaps { get; init; }

  /// <summary>Length in bytes of one stored scanline, which is Symbian's CBitwiseBitmap::ByteWidth.</summary>
  /// <remarks>
  /// A scanline is a whole number of 32-bit words. At 24 bits the word count is rounded up to a
  /// multiple of three as well, so that a group of four pixels - twelve bytes, three words - is
  /// never split across the end of a row. That is the one depth where this differs from plain word
  /// alignment, and it differs whenever the width is not a multiple of four: 61 pixels are 183
  /// bytes, word alignment would make the row 184, and Symbian makes it 192.
  /// </remarks>
  public static int ScanLineLength(int widthInPixels, int bitsPerPixel) => bitsPerPixel switch {
    1 => (widthInPixels + 31) / 32 * 4,
    2 => (widthInPixels + 15) / 16 * 4,
    4 => (widthInPixels + 7) / 8 * 4,
    8 => (widthInPixels + 3) / 4 * 4,
    12 or 16 => (widthInPixels + 1) / 2 * 4,
    24 => (widthInPixels * 3 + 11) / 12 * 12,
    32 => widthInPixels * 4,
    _ => throw new InvalidDataException($"Unsupported MBM bits per pixel: {bitsPerPixel}.")
  };

  /// <summary>The checksum Symbian's TCheckedUid stores over the three UIDs.</summary>
  /// <remarks>
  /// A CCITT CRC runs over the even-numbered and the odd-numbered of the twelve UID bytes
  /// separately, and the two halves are packed odd first. Since every MBM carries the same three
  /// UIDs, every MBM carries the same checksum, 0x47396439.
  /// </remarks>
  public static uint UidChecksum(uint uid1, uint uid2, uint uid3) {
    Span<byte> bytes = stackalloc byte[12];
    BinaryPrimitives.WriteUInt32LittleEndian(bytes, uid1);
    BinaryPrimitives.WriteUInt32LittleEndian(bytes[4..], uid2);
    BinaryPrimitives.WriteUInt32LittleEndian(bytes[8..], uid3);

    var even = 0u;
    var odd = 0u;
    for (var i = 0; i < bytes.Length; i += 2) {
      even = _CrcCcitt(even, bytes[i]);
      odd = _CrcCcitt(odd, bytes[i + 1]);
    }

    return (odd << 16) | even;
  }

  private static uint _CrcCcitt(uint crc, byte value) {
    crc ^= (uint)value << 8;
    for (var bit = 0; bit < 8; ++bit)
      crc = (crc & 0x8000) != 0 ? ((crc << 1) ^ 0x1021) & 0xFFFF : (crc << 1) & 0xFFFF;

    return crc;
  }

  public static RawImage ToRawImage(SymbianMbmFile file) {
    if (file.Bitmaps.Length == 0)
      throw new InvalidDataException("MBM file contains no bitmaps.");

    var bmp = file.Bitmaps[0];
    var width = bmp.Width;
    var height = bmp.Height;

    // Symbian names the display mode from the depth and the colour flag together. Below 24 bits the
    // flag decides whether the bytes are grey levels or indices into one of Symbian's fixed
    // palettes, and those palettes are not implemented here - reading an indexed bitmap as grey
    // hands back the indices, so it is refused instead. At 24 bits there is only EColor16M whatever
    // the flag says, which matters because XnView's converter leaves the flag at 0 there.
    if (bmp.BitsPerPixel <= 8 && bmp.ColorMode != 0)
      throw new InvalidDataException(
        $"Unsupported MBM display mode: {bmp.BitsPerPixel}bpp colour is palette-indexed, and Symbian's palettes are not implemented."
      );

    return bmp.BitsPerPixel switch {
      1 or 2 or 4 or 8 => _ToGray8(bmp, width, height),
      24 => _ToRgb24(bmp, width, height),
      _ => throw new InvalidDataException($"Unsupported bits per pixel: {bmp.BitsPerPixel}.")
    };
  }

  public static SymbianMbmFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    image = image.EnsureAnyFormat(PixelFormat.Rgb24, PixelFormat.Gray8);

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
    var bytesPerRow = ScanLineLength(width, bpp);
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
    var bytesPerRow = ScanLineLength(width, 24);
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
    var bytesPerRow = ScanLineLength(width, 8);
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
    var bytesPerRow = ScanLineLength(width, 24);
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
          // Symbian's IsColor for EColor16M. The converters leave this at 0, which names no display
          // mode at all; 24 bits is EColor16M either way, but what we write says so.
          ColorMode = 1,
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

  /// <summary>Width in twips, a twentieth of a point. Zero when nothing recorded a physical size.</summary>
  public int WidthInTwips { get; init; }

  /// <summary>Height in twips.</summary>
  public int HeightInTwips { get; init; }

  /// <summary>Bits per pixel (1, 2, 4, 8, 12, 16, 24 or 32).</summary>
  public int BitsPerPixel { get; init; }

  /// <summary>Symbian's iColor: 0 greyscale, 1 colour, 2 and 3 colour with alpha at 32 bits.</summary>
  public uint ColorMode { get; init; }

  /// <summary>Compression type (0 = uncompressed).</summary>
  public uint Compression { get; init; }

  /// <summary>Number of palette entries.</summary>
  public uint PaletteSize { get; init; }

  /// <summary>Size of the pixel data in bytes.</summary>
  public uint DataSize { get; init; }

  /// <summary>Raw pixel data bytes.</summary>
  public byte[] PixelData { get; init; }
}
