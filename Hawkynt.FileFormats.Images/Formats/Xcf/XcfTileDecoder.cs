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
  /// <remarks>
  /// An opcode below 128 opens a repeat and one at 128 or above opens a literal, which is the way
  /// round GIMP's own <c>xcf_load_tile_rle</c> reads it. This file had the two swapped, and the
  /// encoder below was swapped to match, so the pair round-tripped and no other reader could open
  /// what it wrote.
  /// </remarks>
  internal static byte[] DecodeRle(byte[] compressed, int bytesPerPixel, int tileWidth, int tileHeight) {
    var pixelCount = tileWidth * tileHeight;
    var result = new byte[pixelCount * bytesPerPixel];
    var srcOffset = 0;

    // Decode each channel separately
    for (var channel = 0; channel < bytesPerPixel; ++channel) {
      var decoded = 0;
      while (decoded < pixelCount && srcOffset < compressed.Length) {
        var n = compressed[srcOffset++];

        if (n < 128) {
          // Repeat the next byte n+1 times; n == 127 escapes to a two-byte count instead.
          var count = n + 1;
          if (count == 128) {
            if (srcOffset + 2 > compressed.Length)
              break;
            count = (compressed[srcOffset] << 8) | compressed[srcOffset + 1];
            srcOffset += 2;
          }

          if (srcOffset >= compressed.Length)
            break;

          var value = compressed[srcOffset++];
          for (var i = 0; i < count && decoded < pixelCount; ++i) {
            result[decoded * bytesPerPixel + channel] = value;
            ++decoded;
          }
        } else {
          // 256-n literal bytes; n == 128 escapes to a two-byte count instead.
          var count = 256 - n;
          if (count == 128) {
            if (srcOffset + 2 > compressed.Length)
              break;
            count = (compressed[srcOffset] << 8) | compressed[srcOffset + 1];
            srcOffset += 2;
          }

          for (var i = 0; i < count && decoded < pixelCount; ++i) {
            if (srcOffset >= compressed.Length)
              break;
            result[decoded * bytesPerPixel + channel] = compressed[srcOffset++];
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
          // A repeat of L bytes is the opcode L - 1, for L from two to 127; 127 is the escape to a
          // two-byte count, so the run is capped below it rather than emitted as one.
          ms.WriteByte((byte)(runLength - 1));
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

          // A literal of L bytes is the opcode 256 - L, for L from one to 127; 128 is the escape to
          // a two-byte count, so again the run stops short of it.
          var litLength = pos - litStart;
          ms.WriteByte((byte)(256 - litLength));
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
