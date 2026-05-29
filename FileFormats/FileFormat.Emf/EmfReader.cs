using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Bmp;

namespace FileFormat.Emf;

/// <summary>Reads EMF files from bytes, streams, or file paths.</summary>
public static class EmfReader {

  private const int _MIN_HEADER_SIZE = EmfHeaderRecord.StructSize;
  private const uint _EMR_HEADER = 1;
  private const uint _EMF_SIGNATURE = 0x464D4520; // " EMF" as LE uint32
  private const uint _EMR_STRETCHDIBITS = 81;

  public static EmfFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("EMF file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static EmfFile FromStream(Stream stream) {
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

  public static EmfFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < _MIN_HEADER_SIZE)
      throw new InvalidDataException("Data too small for a valid EMF file.");

    // Parse and validate EMR_HEADER
    var header = EmfHeaderRecord.ReadFrom(data[..EmfHeaderRecord.StructSize]);
    if (header.RecordType != _EMR_HEADER)
      throw new InvalidDataException($"Invalid EMF: first record type is {header.RecordType}, expected {_EMR_HEADER}.");

    if (header.Signature != _EMF_SIGNATURE)
      throw new InvalidDataException("Invalid EMF signature.");

    // Scan records for EMR_STRETCHDIBITS
    var offset = 0;
    while (offset + 8 <= data.Length) {
      var recordType = BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]);
      var recordSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(data[(offset + 4)..]);

      if (recordSize < 8 || offset + recordSize > data.Length)
        break;

      if (recordType == _EMR_STRETCHDIBITS)
        return _ParseStretchDiBits(data, offset, recordSize);

      offset += recordSize;
    }

    throw new InvalidDataException("No EMR_STRETCHDIBITS record found in EMF file.");
  
  }

  public static EmfFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  private static EmfFile _ParseStretchDiBits(ReadOnlySpan<byte> data, int recordOffset, int recordSize) {
    var span = data[recordOffset..];

    // Parse StretchDIBits fixed portion
    var stretch = EmfStretchDiBitsRecord.ReadFrom(span[..EmfStretchDiBitsRecord.StructSize]);
    var offBmiSrc = (int)stretch.OffBmiSrc;
    var cbBmiSrc = (int)stretch.CbBmiSrc;
    var offBitsSrc = (int)stretch.OffBitsSrc;
    var cbBitsSrc = (int)stretch.CbBitsSrc;

    if (offBmiSrc + cbBmiSrc > recordSize || offBitsSrc + cbBitsSrc > recordSize)
      throw new InvalidDataException("EMR_STRETCHDIBITS offsets exceed record size.");

    // Parse BITMAPINFOHEADER at offBmiSrc
    var bih = BitmapInfoHeader.ReadFrom(span[offBmiSrc..]);
    var biWidth = bih.Width;
    var biHeight = bih.Height;
    var biBitCount = bih.BitsPerPixel;

    if (biWidth <= 0)
      throw new InvalidDataException($"Invalid DIB width: {biWidth}.");

    var isBottomUp = biHeight > 0;
    var absHeight = Math.Abs(biHeight);

    if (absHeight <= 0)
      throw new InvalidDataException($"Invalid DIB height: {biHeight}.");

    if (biBitCount != 24)
      throw new InvalidDataException($"Unsupported DIB bit count: {biBitCount}. Only 24-bit is supported.");

    // Extract pixel data: BMP rows are 4-byte aligned, bottom-up by default
    var srcStride = (biWidth * 3 + 3) & ~3;
    var dstStride = biWidth * 3;
    var pixelData = new byte[dstStride * absHeight];

    var bitsSpan = span[offBitsSrc..];
    for (var y = 0; y < absHeight; ++y) {
      var srcRow = isBottomUp ? absHeight - 1 - y : y;
      var srcOffset = srcRow * srcStride;
      var dstOffset = y * dstStride;

      for (var x = 0; x < biWidth; ++x) {
        var si = srcOffset + x * 3;
        var di = dstOffset + x * 3;
        // BMP stores BGR, convert to RGB
        pixelData[di] = bitsSpan[si + 2];
        pixelData[di + 1] = bitsSpan[si + 1];
        pixelData[di + 2] = bitsSpan[si];
      }
    }

    return new EmfFile {
      Width = biWidth,
      Height = absHeight,
      PixelData = pixelData
    };
  }
}
