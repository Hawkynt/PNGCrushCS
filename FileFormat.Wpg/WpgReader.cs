using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.Wpg;

/// <summary>Reads WPG files from bytes, streams, or file paths.</summary>
public static class WpgReader {

  public static WpgFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("WPG file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static WpgFile FromStream(Stream stream) {
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

  public static WpgFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length < WpgHeader.StructSize)
      throw new InvalidDataException("Data too small for a valid WPG file.");

    // Validate magic bytes
    if (data[0] != WpgHeader.MagicByte1 || data[1] != WpgHeader.MagicByte2 || data[2] != WpgHeader.MagicByte3 || data[3] != WpgHeader.MagicByte4)
      throw new InvalidDataException("Invalid WPG magic bytes.");

    var header = WpgHeader.ReadFrom(data);

    // Scan records after header
    var offset = WpgHeader.StructSize;
    int width = 0, height = 0, bitsPerPixel = 0;
    byte[]? pixelData = null;
    byte[]? palette = null;

    while (offset < data.Length) {
      if (offset >= data.Length)
        break;

      var recordType = data[offset];
      ++offset;

      // Read record size using WPG variable-length size encoding
      if (offset >= data.Length)
        break;

      var sizeByte = data[offset];
      ++offset;
      uint recordSize;

      if (sizeByte < 0xFF) {
        recordSize = sizeByte;
      } else if (sizeByte == 0xFF) {
        if (offset + 2 > data.Length)
          break;

        recordSize = BinaryPrimitives.ReadUInt16LittleEndian(data[offset..]);
        offset += 2;
      } else {
        // 0xFE: 4-byte size
        if (offset + 4 > data.Length)
          break;

        recordSize = BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]);
        offset += 4;
      }

      var recordEnd = offset + (int)recordSize;
      if (recordEnd > data.Length)
        recordEnd = data.Length;

      switch ((WpgRecordType)recordType) {
        case WpgRecordType.BitmapType1: {
          // Bitmap sub-header: width(2), height(2), depth(2), xdpi(2), ydpi(2) = 10 bytes
          if (offset + WpgBitmapSubHeader.StructSize > recordEnd)
            break;

          var bmpSub = WpgBitmapSubHeader.ReadFrom(data[offset..]);
          width = bmpSub.Width;
          height = bmpSub.Height;
          bitsPerPixel = bmpSub.Depth;
          var pixelDataOffset = offset + WpgBitmapSubHeader.StructSize;
          var pixelDataLength = recordEnd - pixelDataOffset;

          if (pixelDataLength > 0) {
            var bytesPerRow = (width * bitsPerPixel + 7) / 8;
            var expectedSize = bytesPerRow * height;

            if (pixelDataLength == expectedSize) {
              // Uncompressed: copy raw pixel data directly
              pixelData = new byte[expectedSize];
              data.Slice(pixelDataOffset, expectedSize).CopyTo(pixelData.AsSpan(0));
            } else {
              // RLE compressed
              var compressedData = new byte[pixelDataLength];
              data.Slice(pixelDataOffset, pixelDataLength).CopyTo(compressedData.AsSpan(0));
              pixelData = WpgRleCompressor.Decompress(compressedData, expectedSize);
            }
          }

          break;
        }
        case WpgRecordType.ColorMap: {
          // ColorMap: startIndex(2), numEntries(2), then R,G,B for each entry
          if (offset + WpgColorMapSubHeader.StructSize > recordEnd)
            break;

          var colorMapSub = WpgColorMapSubHeader.ReadFrom(data[offset..]);
          var numEntries = colorMapSub.NumEntries;
          var paletteOffset = offset + WpgColorMapSubHeader.StructSize;
          var paletteSize = numEntries * 3;

          if (paletteOffset + paletteSize <= recordEnd) {
            palette = new byte[paletteSize];
            data.Slice(paletteOffset, paletteSize).CopyTo(palette.AsSpan(0));
          }

          break;
        }
        case WpgRecordType.EndWpg:
          // Done scanning
          offset = data.Length;
          continue;
      }

      offset = recordEnd;
    }

    if (pixelData == null)
      throw new InvalidDataException("No bitmap record found in WPG file.");

    return new WpgFile {
      Width = width,
      Height = height,
      BitsPerPixel = bitsPerPixel,
      PixelData = pixelData,
      Palette = palette
    };
  }

  public static WpgFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
