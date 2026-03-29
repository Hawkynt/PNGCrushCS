using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace FileFormat.SoftImage;

/// <summary>Assembles Softimage PIC file bytes from a SoftImageFile model.</summary>
public static class SoftImageWriter {

  public static byte[] ToBytes(SoftImageFile file) {
    ArgumentNullException.ThrowIfNull(file);

    using var ms = new MemoryStream();

    _WriteHeader(ms, file);
    _WriteChannelInfo(ms, file);
    _WriteMixedRle(ms, file.PixelData, file.HasAlpha ? 4 : 3);

    return ms.ToArray();
  }

  private static void _WriteHeader(MemoryStream ms, SoftImageFile file) {
    var header = new byte[SoftImageFile.HeaderSize];

    BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(0), SoftImageFile.Magic);

    var versionBits = BitConverter.SingleToInt32Bits(file.Version);
    BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), versionBits);

    var commentBytes = Encoding.ASCII.GetBytes(file.Comment ?? string.Empty);
    var commentLength = Math.Min(commentBytes.Length, SoftImageFile.CommentSize);
    commentBytes.AsSpan(0, commentLength).CopyTo(header.AsSpan(8));

    BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(88), (ushort)file.Width);
    BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(90), (ushort)file.Height);

    ms.Write(header, 0, 96);
  }

  private static void _WriteChannelInfo(MemoryStream ms, SoftImageFile file) {
    if (file.HasAlpha) {
      ms.WriteByte(1);
      ms.WriteByte(8);
      ms.WriteByte(2);
      ms.WriteByte(0x80);

      ms.WriteByte(0);
      ms.WriteByte(8);
      ms.WriteByte(2);
      ms.WriteByte(0x40 | 0x20 | 0x10);
    } else {
      ms.WriteByte(0);
      ms.WriteByte(8);
      ms.WriteByte(2);
      ms.WriteByte(0x40 | 0x20 | 0x10);
    }
  }

  internal static void _WriteMixedRle(MemoryStream ms, byte[] pixelData, int channels) {
    if (pixelData.Length == 0)
      return;

    var pixelCount = pixelData.Length / channels;
    var i = 0;

    while (i < pixelCount) {
      var runStart = i;
      var maxRun = Math.Min(pixelCount - i, 128);

      if (maxRun >= 2 && _PixelsEqual(pixelData, i, i + 1, channels)) {
        var runLength = 1;
        while (runLength < maxRun && _PixelsEqual(pixelData, runStart, runStart + runLength, channels))
          ++runLength;

        ms.WriteByte((byte)(runLength + 127));
        for (var c = 0; c < channels; ++c)
          ms.WriteByte(pixelData[runStart * channels + c]);

        i += runLength;
      } else {
        var literalCount = 1;
        var maxLiteral = Math.Min(pixelCount - i, 128);
        while (literalCount < maxLiteral) {
          if (literalCount + 1 < maxLiteral && _PixelsEqual(pixelData, runStart + literalCount, runStart + literalCount + 1, channels))
            break;
          ++literalCount;
        }

        ms.WriteByte((byte)(literalCount - 1));
        for (var j = 0; j < literalCount; ++j)
          for (var c = 0; c < channels; ++c)
            ms.WriteByte(pixelData[(runStart + j) * channels + c]);

        i += literalCount;
      }
    }
  }

  private static bool _PixelsEqual(byte[] data, int pixelA, int pixelB, int channels) {
    var offsetA = pixelA * channels;
    var offsetB = pixelB * channels;
    for (var c = 0; c < channels; ++c)
      if (data[offsetA + c] != data[offsetB + c])
        return false;
    return true;
  }
}
