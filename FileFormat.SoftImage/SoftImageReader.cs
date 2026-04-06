using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

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

  public static SoftImageFile FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static SoftImageFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < SoftImageFile.HeaderSize)
      throw new InvalidDataException("Data too small for a valid Softimage PIC file.");

    var magic = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(0));
    if (magic != SoftImageFile.Magic)
      throw new InvalidDataException($"Invalid Softimage PIC magic (expected 0x{SoftImageFile.Magic:X8}, got 0x{magic:X8}).");

    var versionBits = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(4));
    var version = BitConverter.Int32BitsToSingle(versionBits);

    var commentBytes = data.AsSpan(8, SoftImageFile.CommentSize);
    var commentEnd = commentBytes.IndexOf((byte)0);
    var comment = commentEnd >= 0
      ? Encoding.ASCII.GetString(commentBytes[..commentEnd])
      : Encoding.ASCII.GetString(commentBytes);

    var width = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(88));
    var height = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(90));

    var offset = 96;
    var hasAlpha = false;
    byte compressionType = 2;

    while (offset + 4 <= data.Length) {
      var chained = data[offset];
      var size = data[offset + 1];
      var type = data[offset + 2];
      var channelMask = data[offset + 3];
      offset += 4;

      if ((channelMask & 0x80) != 0)
        hasAlpha = true;

      compressionType = type;

      if (chained == 0)
        break;
    }

    var channels = hasAlpha ? 4 : 3;
    var pixelCount = width * height;
    var pixelData = new byte[pixelCount * channels];

    if (compressionType == 2)
      _DecodeMixedRle(data, offset, pixelData, pixelCount, channels);
    else
      _DecodeUncompressed(data, offset, pixelData, pixelCount, channels);

    return new SoftImageFile {
      Width = width,
      Height = height,
      PixelData = pixelData,
      Comment = comment,
      HasAlpha = hasAlpha,
      Version = version,
    };
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
