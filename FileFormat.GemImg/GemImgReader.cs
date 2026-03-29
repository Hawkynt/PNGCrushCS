using System;
using System.IO;

namespace FileFormat.GemImg;

/// <summary>Reads GEM IMG files from bytes, streams, or file paths.</summary>
public static class GemImgReader {

  public static GemImgFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("GEM IMG file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static GemImgFile FromStream(Stream stream) {
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

  public static GemImgFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < GemImgHeader.StructSize)
      throw new InvalidDataException("Data too small for a valid GEM IMG file.");

    var span = data.AsSpan();
    var header = GemImgHeader.ReadFrom(span);

    var dataOffset = header.HeaderLength * 2;
    var width = header.ScanWidth;
    var height = header.ScanLines;
    var numPlanes = header.NumPlanes;
    var patternLength = header.PatternLength;
    var bytesPerRow = (width + 7) / 8;
    var pixelData = new byte[numPlanes * bytesPerRow * height];

    if (dataOffset >= data.Length)
      return new GemImgFile {
        Version = header.Version,
        Width = width,
        Height = height,
        NumPlanes = numPlanes,
        PatternLength = patternLength,
        PixelWidth = header.PixelWidth,
        PixelHeight = header.PixelHeight,
        PixelData = pixelData
      };

    var pos = dataOffset;
    for (var plane = 0; plane < numPlanes; ++plane) {
      var planeOffset = plane * bytesPerRow * height;
      var row = 0;
      while (row < height && pos < data.Length) {
        var opcode = data[pos];

        if (opcode == 0x00 && pos + 1 < data.Length) {
          // Vertical replication: repeat previous scan line 'count' times
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
          // Bit string: literal data
          ++pos;
          var count = data[pos];
          ++pos;
          var dstRowOffset = planeOffset + row * bytesPerRow;
          var toCopy = Math.Min(count, Math.Min(data.Length - pos, bytesPerRow));
          data.AsSpan(pos, toCopy).CopyTo(pixelData.AsSpan(dstRowOffset));
          pos += count;
          ++row;
        } else if (opcode == 0xFF && pos + 1 < data.Length) {
          // Pattern run: repeat pattern 'count' times
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
          // Unknown opcode, skip
          ++pos;
        }
      }
    }

    return new GemImgFile {
      Version = header.Version,
      Width = width,
      Height = height,
      NumPlanes = numPlanes,
      PatternLength = patternLength,
      PixelWidth = header.PixelWidth,
      PixelHeight = header.PixelHeight,
      PixelData = pixelData
    };
  }
}
