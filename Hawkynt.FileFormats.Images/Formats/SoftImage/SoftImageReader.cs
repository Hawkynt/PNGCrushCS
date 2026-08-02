using System;
using System.Linq;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.SoftImage;

/// <summary>Reads Softimage PIC files from bytes, streams, or file paths.</summary>
public static class SoftImageReader {

  public static SoftImageFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Softimage PIC file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static SoftImageFile FromStream(Stream stream) {
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

  public static SoftImageFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < SoftImageFile.HeaderSize)
      throw new InvalidDataException("Data too small for a valid Softimage PIC file.");

    var header = SoftImageHeader.ReadFrom(data);

    if (header.Magic != SoftImageFile.Magic)
      throw new InvalidDataException($"Invalid Softimage PIC magic (expected 0x{SoftImageFile.Magic:X8}, got 0x{header.Magic:X8}).");

    // The four letters between the comment and the size were not accounted for, so the size was read
    // from where they sit: a 75 by 75 picture came back as 20553 by 17236, which is those letters.
    if (header.Id != SoftImageHeader.PictId)
      throw new InvalidDataException("A Softimage PIC states PICT after its comment; this file does not.");

    var version = header.Version;
    var comment = header.Comment ?? string.Empty;
    var width = header.Width;
    var height = header.Height;

    var offset = SoftImageFile.HeaderSize;

    // The picture is not one stream but a run of packets, each naming the channels it carries, and
    // every packet states one scanline at a time. Reading it as a single run of interleaved pixels,
    // as this did, decodes the first few bytes and noise after that.
    var packets = new List<(byte Type, byte Mask)>();
    while (offset + 4 <= data.Length) {
      var chained = data[offset];
      var type = data[offset + 2];
      var mask = data[offset + 3];
      offset += 4;
      packets.Add((type, mask));
      if (chained == 0)
        break;
    }

    // Bit 0x10 is alpha; 0x80 is red. Testing the wrong one called every colour picture transparent.
    var hasAlpha = packets.Any(p => (p.Mask & 0x10) != 0);
    var pixelData = _Decode(data, offset, packets, width, height, hasAlpha ? 4 : 3);

    return new SoftImageFile {
      Width = width,
      Height = height,
      PixelData = pixelData,
      Comment = comment,
      HasAlpha = hasAlpha,
      Version = version,
    };
  }

  /// <summary>The channel a mask bit stands for, in the order the file writes them.</summary>
  private static readonly (byte Bit, int Channel)[] _Channels = [(0x80, 0), (0x40, 1), (0x20, 2), (0x10, 3)];

  /// <summary>Reads every packet of every scanline into one picture of the stated channel count.</summary>
  private static byte[] _Decode(ReadOnlySpan<byte> data, int offset, List<(byte Type, byte Mask)> packets, int width, int height, int channels) {
    var pixel = new byte[4];
    var result = new byte[width * height * channels];

    // Anything the file does not state stays opaque rather than transparent.
    if (channels == 4)
      Array.Fill(result, (byte)255);

    for (var y = 0; y < height; ++y)
    foreach (var packet in packets) {
      var lanes = _Channels.Where(c => (packet.Mask & c.Bit) != 0).Select(c => c.Channel).ToArray();
      if (lanes.Length == 0)
        continue;

      var row = y * width;
      for (var x = 0; x < width;) {
        int run;
        var literal = true;

        if (packet.Type == 2) {
          if (offset >= data.Length)
            return result;

          var count = data[offset++];
          if (count >= 128) {
            run = count - 127;
            literal = false;
          } else
            run = count + 1;
        } else
          run = width - x;

        for (var i = 0; i < run && x < width; ++i, ++x) {
          if (literal || i == 0) {
            foreach (var lane in lanes) {
              if (offset >= data.Length)
                return result;

              pixel[lane] = data[offset++];
            }
          }

          foreach (var lane in lanes)
            if (lane < channels)
              result[(row + x) * channels + lane] = pixel[lane];
        }
      }
    }

    return result;
  }

  public static SoftImageFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  private static void _DecodeMixedRle(byte[] data, int offset, byte[] pixelData, int pixelCount, int channels) {
    var inIdx = offset;
    var outIdx = 0;
    var totalBytes = pixelCount * channels;

    while (outIdx < totalBytes && inIdx < data.Length) {
      var count = (int)data[inIdx++];
      if (count < 128) {
        var literalCount = count + 1;
        for (var i = 0; i < literalCount && outIdx < totalBytes; ++i)
          for (var c = 0; c < channels && outIdx < totalBytes; ++c) {
            if (inIdx < data.Length)
              pixelData[outIdx++] = data[inIdx++];
            else
              ++outIdx;
          }
      } else {
        var runCount = count - 127;
        var pixel = new byte[channels];
        for (var c = 0; c < channels; ++c)
          if (inIdx < data.Length)
            pixel[c] = data[inIdx++];

        for (var i = 0; i < runCount && outIdx < totalBytes; ++i)
          for (var c = 0; c < channels; ++c)
            pixelData[outIdx++] = pixel[c];
      }
    }
  }

  private static void _DecodeUncompressed(byte[] data, int offset, byte[] pixelData, int pixelCount, int channels) {
    var available = Math.Min(pixelData.Length, data.Length - offset);
    if (available > 0)
      data.AsSpan(offset, available).CopyTo(pixelData.AsSpan(0));
  }
}
