using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.ExtendedGemImg;

/// <summary>Reads Extended GEM Bit Image (XIMG) files from bytes, streams, or file paths.</summary>
public static class ExtendedGemImgReader {

  public static ExtendedGemImgFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("XIMG file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static ExtendedGemImgFile FromStream(Stream stream) {
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

  public static ExtendedGemImgFile FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static ExtendedGemImgFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < ExtendedGemImgHeader.StructSize)
      throw new InvalidDataException("Data too small for a valid XIMG file.");

    var span = data.AsSpan();
    var header = ExtendedGemImgHeader.ReadFrom(span);

    var dataOffset = header.HeaderLength * 2;
    var width = header.ScanWidth;
    var height = header.ScanLines;
    var numPlanes = header.NumPlanes;
    var patternLength = header.PatternLength;

    // Parse XIMG extension: after the 16-byte standard header, check for "XIMG" marker
    var colorModel = ExtendedGemImgColorModel.Rgb;
    var paletteData = Array.Empty<short>();
    var paletteCount = 1 << numPlanes;

    if (dataOffset > ExtendedGemImgHeader.StructSize + ExtendedGemImgHeader.XimgExtensionFixedSize) {
      var extOffset = ExtendedGemImgHeader.StructSize;
      if (extOffset + 4 <= data.Length) {
        var marker1 = BinaryPrimitives.ReadInt16BigEndian(span.Slice(extOffset));
        var marker2 = BinaryPrimitives.ReadInt16BigEndian(span.Slice(extOffset + 2));

        if (marker1 == ExtendedGemImgHeader.XimgMarker1 && marker2 == ExtendedGemImgHeader.XimgMarker2) {
          if (extOffset + 6 <= data.Length)
            colorModel = (ExtendedGemImgColorModel)BinaryPrimitives.ReadInt16BigEndian(span.Slice(extOffset + 4));

          // Read palette entries: each is 3 big-endian shorts (R, G, B in range 0-1000)
          var palOffset = extOffset + 6;
          var availableWords = (dataOffset - palOffset) / 2;
          var palEntries = Math.Min(paletteCount, availableWords / 3);
          if (palEntries > 0) {
            paletteData = new short[palEntries * 3];
            for (var i = 0; i < palEntries * 3; ++i)
              paletteData[i] = BinaryPrimitives.ReadInt16BigEndian(span.Slice(palOffset + i * 2));
          }
        }
      }
    }

    // Decode scan-line encoded pixel data (same as GEM IMG)
    var bytesPerRow = (width + 7) / 8;
    var pixelData = new byte[numPlanes * bytesPerRow * height];

    if (dataOffset < data.Length) {
      var pos = dataOffset;
      for (var plane = 0; plane < numPlanes; ++plane) {
        var planeOffset = plane * bytesPerRow * height;
        var row = 0;
        while (row < height && pos < data.Length) {
          var opcode = data[pos];

          if (opcode == 0x00 && pos + 1 < data.Length) {
            ++pos;
            var count = data[pos];
            ++pos;
            var srcRowOffset = planeOffset + (row > 0 ? (row - 1) * bytesPerRow : 0);
            for (var r = 0; r < count && row < height; ++r) {
              var dstRowOffset = planeOffset + row * bytesPerRow;
              pixelData.AsSpan(srcRowOffset, bytesPerRow).CopyTo(pixelData.AsSpan(dstRowOffset));
              ++row;
            }
          } else if (opcode == 0x80 && pos + 1 < data.Length) {
            ++pos;
            var count = data[pos];
            ++pos;
            var dstRowOffset = planeOffset + row * bytesPerRow;
            var toCopy = Math.Min(count, Math.Min(data.Length - pos, bytesPerRow));
            data.AsSpan(pos, toCopy).CopyTo(pixelData.AsSpan(dstRowOffset));
            pos += count;
            ++row;
          } else if (opcode == 0xFF && pos + 1 < data.Length) {
            ++pos;
            var count = data[pos];
            ++pos;
            var patLen = Math.Min(patternLength, data.Length - pos);
            var dstRowOffset = planeOffset + row * bytesPerRow;
            var dstPos = 0;
            for (var r = 0; r < count && dstPos < bytesPerRow; ++r)
              for (var p = 0; p < patLen && dstPos < bytesPerRow; ++p)
                pixelData[dstRowOffset + dstPos++] = data[pos + p];
            pos += patLen;
            ++row;
          } else {
            ++pos;
          }
        }
      }
    }

    return new ExtendedGemImgFile {
      Version = header.Version,
      Width = width,
      Height = height,
      NumPlanes = numPlanes,
      PatternLength = patternLength,
      PixelWidth = header.PixelWidth,
      PixelHeight = header.PixelHeight,
      ColorModel = colorModel,
      PaletteData = paletteData,
      PixelData = pixelData
    };
  }
}
