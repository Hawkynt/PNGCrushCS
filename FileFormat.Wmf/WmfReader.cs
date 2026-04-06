using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Bmp;

namespace FileFormat.Wmf;

/// <summary>Reads WMF files from bytes, streams, or file paths.</summary>
public static class WmfReader {

  private const int _MIN_FILE_SIZE = WmfPlaceableHeader.StructSize + WmfStandardHeader.StructSize;
  private const uint _PLACEABLE_MAGIC = 0x9AC6CDD7;
  private const ushort _META_STRETCHDIB = 0x0F43;
  private const ushort _META_EOF = 0x0000;
  private const int _STRETCHDIB_DIB_OFFSET = 14 * 2; // 14 words = 28 bytes from record start

  public static WmfFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("WMF file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static WmfFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.CanSeek) {
      var data = new byte[stream.Length - stream.Position];
      stream.ReadExactly(data);
      return FromBytes(data);
    }
    using var ms = new MemoryStream();
    stream.CopyTo(ms);
    return FromBytes(ms.ToArray());
  }

  public static WmfFile FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static WmfFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < _MIN_FILE_SIZE)
      throw new InvalidDataException("Data too small for a valid WMF file.");

    var placeable = WmfPlaceableHeader.ReadFrom(data.AsSpan(0, WmfPlaceableHeader.StructSize));
    if (placeable.Magic != _PLACEABLE_MAGIC)
      throw new InvalidDataException($"Invalid WMF magic: 0x{placeable.Magic:X8}, expected 0x{_PLACEABLE_MAGIC:X8}.");

    // Skip placeable header, skip standard header, scan records
    var offset = WmfPlaceableHeader.StructSize + WmfStandardHeader.StructSize;

    while (offset + 6 <= data.Length) {
      var recordSizeInWords = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset));
      var function = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset + 4));

      if (function == _META_EOF)
        break;

      if (function == _META_STRETCHDIB) {
        var dibOffset = offset + _STRETCHDIB_DIB_OFFSET;
        if (dibOffset + BitmapInfoHeader.StructSize > data.Length)
          throw new InvalidDataException("META_STRETCHDIB record too small for BITMAPINFOHEADER.");

        return _ParseDib(data, dibOffset);
      }

      var recordSizeInBytes = (int)(recordSizeInWords * 2);
      if (recordSizeInBytes < 6)
        throw new InvalidDataException("Invalid WMF record size.");

      offset += recordSizeInBytes;
    }

    throw new InvalidDataException("No META_STRETCHDIB record found in WMF file.");
  }

  private static WmfFile _ParseDib(byte[] data, int dibOffset) {
    var bih = BitmapInfoHeader.ReadFrom(data.AsSpan(dibOffset, BitmapInfoHeader.StructSize));
    var width = bih.Width;
    var height = bih.Height;
    var bitCount = bih.BitsPerPixel;

    if (width <= 0)
      throw new InvalidDataException($"Invalid DIB width: {width}.");

    // Height can be negative (top-down) in a DIB
    var isTopDown = height < 0;
    var absHeight = isTopDown ? -height : height;

    if (absHeight <= 0)
      throw new InvalidDataException($"Invalid DIB height: {height}.");
    if (bitCount != 24)
      throw new InvalidDataException($"Only 24-bit DIBs are supported, got {bitCount}-bit.");

    var stride = (width * 3 + 3) & ~3; // 4-byte aligned
    var pixelDataOffset = dibOffset + BitmapInfoHeader.StructSize;
    var expectedPixelBytes = stride * absHeight;

    if (pixelDataOffset + expectedPixelBytes > data.Length)
      throw new InvalidDataException("DIB pixel data extends beyond file.");

    // Convert from padded BGR bottom-up to packed RGB24 top-down
    var pixelData = new byte[width * absHeight * 3];
    for (var y = 0; y < absHeight; ++y) {
      var srcRow = isTopDown ? y : absHeight - 1 - y;
      var srcOffset = pixelDataOffset + srcRow * stride;
      var dstOffset = y * width * 3;
      for (var x = 0; x < width; ++x) {
        var si = srcOffset + x * 3;
        var di = dstOffset + x * 3;
        pixelData[di] = data[si + 2];     // R
        pixelData[di + 1] = data[si + 1]; // G
        pixelData[di + 2] = data[si];     // B
      }
    }

    return new WmfFile {
      Width = width,
      Height = absHeight,
      PixelData = pixelData
    };
  }
}
