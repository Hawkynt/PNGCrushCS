using System;
using System.IO;
using System.IO.Compression;
using System.IO.Hashing;
using FileFormat.Core;

namespace Hawkynt.FileFormats.Images.Tests;

/// <summary>
/// PNG starts every row on a byte boundary; a <see cref="RawImage"/> in a sub-byte format runs its
/// indices continuously across the whole picture. The two agree for any width that is a multiple of
/// eight pixels, which is nearly every picture there is — so a reader that leaves PNG's padding in
/// place looks correct until it meets a narrow one, and then every row after the first is out of
/// step by the padding bits.
/// </summary>
/// <remarks>
/// The file is assembled here rather than round-tripped through our own encoder on purpose. A
/// round trip only shows that the reader and the writer agree with each other, which they did
/// while both were wrong; what has to hold is that the reader agrees with the specification.
/// </remarks>
[TestFixture]
public sealed class SubByteRowPaddingTests {

  [TestCase(1, 4, 6)]
  [TestCase(1, 5, 12)]
  [TestCase(1, 8, 16)]
  [TestCase(4, 3, 7)]
  [TestCase(4, 6, 10)]
  [Category("Unit")]
  public void Png_ReadsRowsThatDoNotFillTheirLastByte(int bitDepth, int width, int height) {
    var colors = 1 << bitDepth;
    var palette = new byte[colors * 3];
    for (var i = 0; i < colors; ++i) {
      palette[i * 3] = (byte)(i * 255 / (colors - 1));
      palette[i * 3 + 1] = (byte)(255 - i * 255 / (colors - 1));
      palette[i * 3 + 2] = (byte)(i * 37);
    }

    // A diagonal, so a picture read a few bits out of step cannot match by accident.
    int Index(int x, int y) => (x + y) % colors;

    var image = FormatRegistry.Read(_BuildPng(bitDepth, width, height, palette, Index));
    Assert.That(image, Is.Not.Null, "our reader rejected a well-formed PNG");

    var rgb = PixelConverter.Convert(image!, PixelFormat.Rgb24);
    Assert.Multiple(() => {
      Assert.That(rgb.Width, Is.EqualTo(width));
      Assert.That(rgb.Height, Is.EqualTo(height));
    });

    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var entry = Index(x, y) * 3;
      var target = (y * width + x) * 3;
      if (rgb.PixelData[target] == palette[entry] && rgb.PixelData[target + 1] == palette[entry + 1])
        continue;

      Assert.Fail(
        $"{bitDepth}bpp {width}x{height}: pixel {x},{y} — expected index {Index(x, y)} " +
        $"({palette[entry]},{palette[entry + 1]}), got ({rgb.PixelData[target]},{rgb.PixelData[target + 1]})");
    }
  }

  /// <summary>Assembles a palette PNG with the row padding the specification requires.</summary>
  private static byte[] _BuildPng(int bitDepth, int width, int height, byte[] palette, Func<int, int, int> index) {
    var stride = (width * bitDepth + 7) / 8;
    var raw = new byte[(stride + 1) * height];

    for (var y = 0; y < height; ++y) {
      // Filter byte 0: the row is stored as it is, so nothing here depends on our filter code.
      var rowOffset = y * (stride + 1) + 1;
      for (var x = 0; x < width; ++x) {
        var bit = x * bitDepth;
        raw[rowOffset + (bit >> 3)] |= (byte)(index(x, y) << (8 - bitDepth - (bit & 7)));
      }
    }

    using var deflated = new MemoryStream();
    using (var zlib = new ZLibStream(deflated, CompressionLevel.Optimal, leaveOpen: true))
      zlib.Write(raw, 0, raw.Length);

    var header = new byte[13];
    _WriteBigEndian(header, 0, width);
    _WriteBigEndian(header, 4, height);
    header[8] = (byte)bitDepth;
    header[9] = 3;

    using var png = new MemoryStream();
    png.Write([0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A]);
    _WriteChunk(png, "IHDR", header);
    _WriteChunk(png, "PLTE", palette);
    _WriteChunk(png, "IDAT", deflated.ToArray());
    _WriteChunk(png, "IEND", []);

    return png.ToArray();
  }

  private static void _WriteChunk(Stream stream, string type, byte[] data) {
    var length = new byte[4];
    _WriteBigEndian(length, 0, data.Length);
    stream.Write(length);

    var body = new byte[4 + data.Length];
    for (var i = 0; i < 4; ++i)
      body[i] = (byte)type[i];
    data.CopyTo(body, 4);
    stream.Write(body);

    var crc = new byte[4];
    _WriteBigEndian(crc, 0, (int)Crc32.HashToUInt32(body));
    stream.Write(crc);
  }

  private static void _WriteBigEndian(byte[] target, int offset, int value) {
    target[offset] = (byte)(value >> 24);
    target[offset + 1] = (byte)(value >> 16);
    target[offset + 2] = (byte)(value >> 8);
    target[offset + 3] = (byte)value;
  }
}
