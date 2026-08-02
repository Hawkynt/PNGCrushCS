using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace FileFormat.SoftImage;

/// <summary>Assembles Softimage PIC file bytes from a SoftImageFile model.</summary>
/// <remarks>
/// What was written before had the size four bytes early, where the letters <c>PICT</c> belong, named
/// red as the alpha channel and green, blue and alpha as the colour ones, and ran its coding straight
/// through the picture instead of restarting each scanline. Nothing else could open the result.
/// </remarks>
public static class SoftImageWriter {

  /// <summary>Channel bits: red, green and blue in one packet, alpha in its own.</summary>
  private const byte _COLOUR_CHANNELS = 0x80 | 0x40 | 0x20;

  private const byte _ALPHA_CHANNEL = 0x10;

  /// <summary>The coding every packet here uses, which is the run-length one.</summary>
  private const byte _RUN_LENGTH = 2;

  public static byte[] ToBytes(SoftImageFile file) {
    using var ms = new MemoryStream();

    var channels = file.HasAlpha ? 4 : 3;
    _WriteHeader(ms, file);

    // Colour first, then alpha if there is any; the last packet says nothing follows it.
    _WritePacket(ms, chained: file.HasAlpha, _COLOUR_CHANNELS);
    if (file.HasAlpha)
      _WritePacket(ms, chained: false, _ALPHA_CHANNEL);

    var pixels = file.PixelData ?? [];
    for (var y = 0; y < file.Height; ++y) {
      _WriteScanline(ms, pixels, y, file.Width, channels, 0, 3);
      if (file.HasAlpha)
        _WriteScanline(ms, pixels, y, file.Width, channels, 3, 1);
    }

    return ms.ToArray();
  }

  private static void _WriteHeader(MemoryStream ms, SoftImageFile file) {
    var header = new byte[SoftImageFile.HeaderSize];

    BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(0), SoftImageFile.Magic);
    BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), BitConverter.SingleToInt32Bits(file.Version));

    var comment = Encoding.ASCII.GetBytes(file.Comment ?? string.Empty);
    comment.AsSpan(0, Math.Min(comment.Length, SoftImageFile.CommentSize)).CopyTo(header.AsSpan(8));

    BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(88), SoftImageHeader.PictId);
    BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(92), (ushort)file.Width);
    BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(94), (ushort)file.Height);
    BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(96), BitConverter.SingleToInt32Bits(1f));
    BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(100), 3);

    ms.Write(header, 0, header.Length);
  }

  private static void _WritePacket(MemoryStream ms, bool chained, byte channels) {
    ms.WriteByte((byte)(chained ? 1 : 0));
    ms.WriteByte(8);
    ms.WriteByte(_RUN_LENGTH);
    ms.WriteByte(channels);
  }

  /// <summary>
  /// Codes one scanline of one packet: a count below 128 introduces that many plus one pixels
  /// written out, anything above it repeats the pixel that follows.
  /// </summary>
  private static void _WriteScanline(MemoryStream ms, byte[] pixels, int y, int width, int stride, int first, int count) {
    var at = 0;
    while (at < width) {
      var run = 1;
      while (run < 127 && at + run < width && _Same(pixels, y, width, stride, at, at + run, first, count))
        ++run;

      if (run > 1) {
        ms.WriteByte((byte)(127 + run));
        _WritePixel(ms, pixels, y, width, stride, at, first, count);
        at += run;
        continue;
      }

      // A stretch of pixels that differ, gathered so each is not spelled out on its own.
      var literal = 1;
      while (literal < 128 && at + literal < width && !_Same(pixels, y, width, stride, at + literal - 1, at + literal, first, count))
        ++literal;

      ms.WriteByte((byte)(literal - 1));
      for (var i = 0; i < literal; ++i)
        _WritePixel(ms, pixels, y, width, stride, at + i, first, count);

      at += literal;
    }
  }

  private static bool _Same(byte[] pixels, int y, int width, int stride, int a, int b, int first, int count) {
    var pa = ((y * width) + a) * stride + first;
    var pb = ((y * width) + b) * stride + first;
    for (var i = 0; i < count; ++i) {
      var left = pa + i < pixels.Length ? pixels[pa + i] : (byte)0;
      var right = pb + i < pixels.Length ? pixels[pb + i] : (byte)0;
      if (left != right)
        return false;
    }

    return true;
  }

  private static void _WritePixel(MemoryStream ms, byte[] pixels, int y, int width, int stride, int x, int first, int count) {
    var at = ((y * width) + x) * stride + first;
    for (var i = 0; i < count; ++i)
      ms.WriteByte(at + i < pixels.Length ? pixels[at + i] : (byte)0);
  }
}
