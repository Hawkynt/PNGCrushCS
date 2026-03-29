using System;

namespace FileFormat.Xcf;

/// <summary>Decodes XCF tile data from RLE or uncompressed format.</summary>
internal static class XcfTileDecoder {

  private const int TILE_SIZE = 64;

  /// <summary>Decodes uncompressed tile data (identity transform, just validates size).</summary>
  internal static byte[] DecodeUncompressed(byte[] data, int bytesPerPixel, int tileWidth, int tileHeight) {
    var expectedSize = tileWidth * tileHeight * bytesPerPixel;
    if (data.Length < expectedSize)
      throw new InvalidOperationException("Tile data too small for uncompressed tile.");

    // XCF stores uncompressed tiles as channel-planar data:
    // all channel0 bytes, then all channel1 bytes, etc.
    return _DeinterleaveChannels(data, bytesPerPixel, tileWidth * tileHeight);
  }

  /// <summary>Decodes RLE-compressed tile data. XCF RLE encodes each channel separately.</summary>
  internal static byte[] DecodeRle(byte[] compressed, int bytesPerPixel, int tileWidth, int tileHeight) {
    var pixelCount = tileWidth * tileHeight;
    var result = new byte[pixelCount * bytesPerPixel];
    var srcOffset = 0;

    // Decode each channel separately
    for (var channel = 0; channel < bytesPerPixel; ++channel) {
      var decoded = 0;
      while (decoded < pixelCount && srcOffset < compressed.Length) {
        var n = compressed[srcOffset++];

        if (n <= 126) {
          // n+1 literal bytes
          var count = n + 1;
          for (var i = 0; i < count && decoded < pixelCount; ++i) {
            if (srcOffset >= compressed.Length)
              break;
            result[decoded * bytesPerPixel + channel] = compressed[srcOffset++];
            ++decoded;
          }
        } else if (n == 127) {
          // Long literal run: next 4 bytes = uint32 BE count
          if (srcOffset + 4 > compressed.Length)
            break;
          var count = (int)_ReadUInt32BE(compressed.AsSpan(srcOffset));
          srcOffset += 4;
          for (var i = 0; i < count && decoded < pixelCount; ++i) {
            if (srcOffset >= compressed.Length)
              break;
            result[decoded * bytesPerPixel + channel] = compressed[srcOffset++];
            ++decoded;
          }
        } else if (n == 128) {
          // Long repeat run: next 4 bytes = uint32 BE count, then 1 byte value
          if (srcOffset + 5 > compressed.Length)
            break;
          var count = (int)_ReadUInt32BE(compressed.AsSpan(srcOffset));
          srcOffset += 4;
          var value = compressed[srcOffset++];
          for (var i = 0; i < count && decoded < pixelCount; ++i) {
            result[decoded * bytesPerPixel + channel] = value;
            ++decoded;
          }
        } else {
          // n >= 129: repeat next byte (256 - n + 1) times
          var count = 256 - n + 1;
          if (srcOffset >= compressed.Length)
            break;
          var value = compressed[srcOffset++];
          for (var i = 0; i < count && decoded < pixelCount; ++i) {
            result[decoded * bytesPerPixel + channel] = value;
            ++decoded;
          }
        }
      }
    }

    return result;
  }

  /// <summary>Encodes tile data as RLE for a single channel.</summary>
  internal static byte[] EncodeRle(byte[] pixelData, int bytesPerPixel, int tileWidth, int tileHeight) {
    var pixelCount = tileWidth * tileHeight;
    using var ms = new System.IO.MemoryStream();

    for (var channel = 0; channel < bytesPerPixel; ++channel) {
      var pos = 0;
      while (pos < pixelCount) {
        // Check for a run of identical bytes
        var runStart = pos;
        var value = pixelData[pos * bytesPerPixel + channel];
        while (pos < pixelCount && pixelData[pos * bytesPerPixel + channel] == value && pos - runStart < 127)
          ++pos;

        var runLength = pos - runStart;
        if (runLength >= 2) {
          // Repeat run: n = 256 - (runLength - 1) = 257 - runLength
          ms.WriteByte((byte)(257 - runLength));
          ms.WriteByte(value);
        } else {
          // Literal run: collect non-repeating bytes
          pos = runStart;
          var litStart = pos;
          while (pos < pixelCount && pos - litStart < 127) {
            if (pos + 1 < pixelCount && pixelData[pos * bytesPerPixel + channel] == pixelData[(pos + 1) * bytesPerPixel + channel])
              break;
            ++pos;
          }

          if (pos == litStart)
            ++pos; // at least one byte

          var litLength = pos - litStart;
          ms.WriteByte((byte)(litLength - 1));
          for (var i = litStart; i < litStart + litLength; ++i)
            ms.WriteByte(pixelData[i * bytesPerPixel + channel]);
        }
      }
    }

    return ms.ToArray();
  }

  private static byte[] _DeinterleaveChannels(byte[] planarData, int bytesPerPixel, int pixelCount) {
    var result = new byte[pixelCount * bytesPerPixel];
    for (var channel = 0; channel < bytesPerPixel; ++channel)
      for (var i = 0; i < pixelCount; ++i)
        result[i * bytesPerPixel + channel] = planarData[channel * pixelCount + i];

    return result;
  }

  private static uint _ReadUInt32BE(ReadOnlySpan<byte> data)
    => (uint)(data[0] << 24 | data[1] << 16 | data[2] << 8 | data[3]);
}
