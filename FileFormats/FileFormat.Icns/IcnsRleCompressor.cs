using System;
using System.IO;

namespace FileFormat.Icns;

/// <summary>Handles legacy ICNS RLE compression/decompression for 24-bit icon entries (is32/il32/ih32/it32).
/// Each channel (R, G, B) is compressed separately using a PackBits-like scheme.</summary>
internal static class IcnsRleCompressor {

  /// <summary>Decompresses legacy RLE-encoded channel data into interleaved RGB pixels.</summary>
  /// <param name="data">The compressed data (all three channels concatenated).</param>
  /// <param name="pixelCount">The number of pixels (width * height).</param>
  /// <returns>Decompressed RGB data (3 bytes per pixel: R, G, B).</returns>
  public static byte[] Decompress(byte[] data, int pixelCount) {
    ArgumentNullException.ThrowIfNull(data);
    if (pixelCount <= 0)
      throw new ArgumentOutOfRangeException(nameof(pixelCount), "Pixel count must be positive.");

    var channels = new byte[3][];
    var offset = 0;

    for (var ch = 0; ch < 3; ++ch) {
      channels[ch] = new byte[pixelCount];
      var written = 0;

      while (written < pixelCount && offset < data.Length) {
        var control = data[offset++];
        if (control >= 0x80) {
          // Run: (control - 125) copies of the next byte
          var count = control - 125;
          if (offset >= data.Length)
            break;

          var value = data[offset++];
          var toCopy = Math.Min(count, pixelCount - written);
          for (var i = 0; i < toCopy; ++i)
            channels[ch][written++] = value;
        } else {
          // Literal: (control + 1) bytes
          var count = control + 1;
          var toCopy = Math.Min(count, pixelCount - written);
          var available = Math.Min(toCopy, data.Length - offset);
          Array.Copy(data, offset, channels[ch], written, available);
          offset += available;
          written += available;
        }
      }
    }

    // Interleave R, G, B channels
    var result = new byte[pixelCount * 3];
    for (var i = 0; i < pixelCount; ++i) {
      result[i * 3] = channels[0][i];
      result[i * 3 + 1] = channels[1][i];
      result[i * 3 + 2] = channels[2][i];
    }

    return result;
  }

  /// <summary>Compresses interleaved RGB pixel data using legacy ICNS RLE encoding.</summary>
  /// <param name="rgb">Interleaved RGB data (3 bytes per pixel).</param>
  /// <param name="pixelCount">The number of pixels.</param>
  /// <returns>The RLE-compressed data (all three channels concatenated).</returns>
  public static byte[] Compress(byte[] rgb, int pixelCount) {
    ArgumentNullException.ThrowIfNull(rgb);
    if (pixelCount <= 0)
      throw new ArgumentOutOfRangeException(nameof(pixelCount), "Pixel count must be positive.");
    if (rgb.Length < pixelCount * 3)
      throw new ArgumentException("RGB data too short for the specified pixel count.", nameof(rgb));

    using var ms = new MemoryStream();

    for (var ch = 0; ch < 3; ++ch) {
      // Extract channel plane
      var channel = new byte[pixelCount];
      for (var i = 0; i < pixelCount; ++i)
        channel[i] = rgb[i * 3 + ch];

      _CompressChannel(channel, ms);
    }

    return ms.ToArray();
  }

  private static void _CompressChannel(byte[] channel, MemoryStream output) {
    var i = 0;
    var length = channel.Length;

    while (i < length) {
      // Check for a run of identical bytes
      var runStart = i;
      while (i + 1 < length && channel[i] == channel[i + 1] && i - runStart < 129)
        ++i;

      var runLength = i - runStart + 1;
      ++i;

      if (runLength >= 3) {
        // Emit run: control = runLength + 125 (so runLength=3 gives 128=0x80)
        output.WriteByte((byte)(runLength + 125));
        output.WriteByte(channel[runStart]);
      } else {
        // Accumulate literals
        var litStart = runStart;
        i = runStart;

        while (i < length) {
          // Check if a run starts here
          if (i + 2 < length && channel[i] == channel[i + 1] && channel[i] == channel[i + 2])
            break;

          ++i;

          // Max literal run is 128 bytes (control = 127)
          if (i - litStart >= 128)
            break;
        }

        var litLength = i - litStart;
        output.WriteByte((byte)(litLength - 1));
        output.Write(channel, litStart, litLength);
      }
    }
  }
}
