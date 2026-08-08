using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using FileFormat.Core;
using FileFormat.Psp;

namespace FileFormat.Psp.Tests;

/// <summary>
/// A Paint Shop Pro file that stores its picture the way Paint Shop Pro stores one.
/// </summary>
/// <remarks>
/// The picture is not in the file as pixels. It is a stack of layers, each layer's colour split into
/// one channel a component, each channel its own stream — and the default every version saves with
/// is what the format calls LZ77 and everyone else calls zlib. Reading the bytes after a block
/// header as pixels could never open a real file. The files here are assembled byte by byte from the
/// published layout rather than through this project's own writer, so the reader is measured against
/// the format rather than against its counterpart.
/// </remarks>
[TestFixture]
public sealed class PspCompressedTests {

  private static ReadOnlySpan<byte> _Marker => [0x7E, 0x42, 0x4B, 0x00];

  private static void _Block(Stream stream, ushort id, byte[] data) {
    stream.Write(_Marker);
    _U16(stream, id);
    _U32(stream, (uint)data.Length);
    stream.Write(data);
  }

  private static void _U16(Stream stream, ushort value) {
    Span<byte> buffer = stackalloc byte[2];
    BinaryPrimitives.WriteUInt16LittleEndian(buffer, value);
    stream.Write(buffer);
  }

  private static void _U32(Stream stream, uint value) {
    Span<byte> buffer = stackalloc byte[4];
    BinaryPrimitives.WriteUInt32LittleEndian(buffer, value);
    stream.Write(buffer);
  }

  private static void _I32(Stream stream, int value) {
    Span<byte> buffer = stackalloc byte[4];
    BinaryPrimitives.WriteInt32LittleEndian(buffer, value);
    stream.Write(buffer);
  }

  private static byte[] _Zlib(byte[] data) {
    using var output = new MemoryStream();
    using (var zlib = new ZLibStream(output, CompressionLevel.Optimal, true))
      zlib.Write(data);

    return output.ToArray();
  }

  /// <summary>Run-length as the format states it: over 128 repeats one byte, under it copies bytes.</summary>
  private static byte[] _Rle(byte[] data) {
    using var output = new MemoryStream();
    var at = 0;
    while (at < data.Length) {
      var run = 1;
      while (at + run < data.Length && data[at + run] == data[at] && run < 127)
        ++run;

      if (run > 1) {
        output.WriteByte((byte)(128 + run));
        output.WriteByte(data[at]);
        at += run;
        continue;
      }

      var literal = 1;
      while (at + literal < data.Length && data[at + literal] != data[at + literal - 1] && literal < 127)
        ++literal;

      output.WriteByte((byte)literal);
      output.Write(data, at, literal);
      at += literal;
    }

    return output.ToArray();
  }

  private static byte[] _Channel(ushort bitmapType, ushort channelType, byte[] content, int uncompressedLength) {
    using var channel = new MemoryStream();
    _U32(channel, 16);
    _U32(channel, (uint)content.Length);
    _U32(channel, (uint)uncompressedLength);
    _U16(channel, bitmapType);
    _U16(channel, channelType);
    channel.Write(content);
    return channel.ToArray();
  }

  /// <summary>Assembles a one-layer file at the stated compression from three colour planes.</summary>
  private static byte[] _Build(int width, int height, ushort compression, byte[] red, byte[] green, byte[] blue, byte[]? alpha = null) {
    byte[] Encode(byte[] plane) => compression switch {
      2 => _Zlib(plane),
      1 => _Rle(plane),
      _ => plane,
    };

    using var layer = new MemoryStream();
    var name = "Raster 1"u8;
    _U32(layer, (uint)(4 + 2 + name.Length + 120));
    _U16(layer, (ushort)name.Length);
    layer.Write(name);
    layer.WriteByte(1); // raster
    _I32(layer, 0); _I32(layer, 0); _I32(layer, width); _I32(layer, height);
    _I32(layer, 0); _I32(layer, 0); _I32(layer, width); _I32(layer, height);
    layer.WriteByte(255); // opacity
    layer.WriteByte(0); // normal blend
    layer.WriteByte(0x01); // visible
    layer.WriteByte(0);
    layer.WriteByte(0);
    layer.Write(new byte[32]); // the two mask rectangles
    layer.WriteByte(0);
    layer.WriteByte(0);
    layer.WriteByte(0);
    _U16(layer, 0);
    layer.Write(new byte[40]); // blend ranges
    layer.WriteByte(0);
    _U32(layer, 0);

    _U32(layer, 8);
    _U16(layer, (ushort)(alpha == null ? 1 : 2));
    _U16(layer, (ushort)(alpha == null ? 3 : 4));

    var pixels = width * height;
    _Block(layer, 5, _Channel(0, 1, Encode(red), pixels));
    _Block(layer, 5, _Channel(0, 2, Encode(green), pixels));
    _Block(layer, 5, _Channel(0, 3, Encode(blue), pixels));
    if (alpha != null)
      _Block(layer, 5, _Channel(1, 0, Encode(alpha), pixels));

    using var bank = new MemoryStream();
    _Block(bank, 4, layer.ToArray());

    using var attributes = new MemoryStream();
    _U32(attributes, 46);
    _I32(attributes, width);
    _I32(attributes, height);
    Span<byte> resolution = stackalloc byte[8];
    BinaryPrimitives.WriteDoubleLittleEndian(resolution, 72.0);
    attributes.Write(resolution);
    attributes.WriteByte(1);
    _U16(attributes, compression);
    _U16(attributes, 24);
    _U16(attributes, 1);
    _U32(attributes, 16777216);
    attributes.WriteByte(0);
    _U32(attributes, (uint)(pixels * 3));
    _I32(attributes, 0);
    _U16(attributes, 1);
    _U32(attributes, 1);

    using var file = new MemoryStream();
    file.Write(new byte[32]);
    file.Position = 0;
    file.Write("Paint Shop Pro Image File\n\x1a"u8);
    file.Position = 32;
    _U16(file, 5);
    _U16(file, 0);
    _Block(file, 0, attributes.ToArray());
    _Block(file, 3, bank.ToArray());
    return file.ToArray();
  }

  private static (byte[] Red, byte[] Green, byte[] Blue) _Planes(int width, int height) {
    var pixels = width * height;
    var red = new byte[pixels];
    var green = new byte[pixels];
    var blue = new byte[pixels];
    for (var i = 0; i < pixels; ++i) {
      red[i] = (byte)(i * 7);
      green[i] = (byte)(255 - i * 3);
      blue[i] = (byte)(i * 11 + 5);
    }

    return (red, green, blue);
  }

  [TestCase((ushort)0, TestName = "Uncompressed")]
  [TestCase((ushort)1, TestName = "RunLength")]
  [TestCase((ushort)2, TestName = "Lz77")]
  [Category("Unit")]
  public void ChannelsBecomeThePicture(ushort compression) {
    const int WIDTH = 8;
    const int HEIGHT = 5;
    var (red, green, blue) = _Planes(WIDTH, HEIGHT);

    var file = PspReader.FromBytes(_Build(WIDTH, HEIGHT, compression, red, green, blue));
    var image = PspFile.ToRawImage(file).ToRgb24();

    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(WIDTH));
      Assert.That(file.Height, Is.EqualTo(HEIGHT));
      Assert.That(file.HasAlpha, Is.False);
      for (var i = 0; i < WIDTH * HEIGHT; ++i) {
        Assert.That(image[i * 3], Is.EqualTo(red[i]), $"red of pixel {i}");
        Assert.That(image[i * 3 + 1], Is.EqualTo(green[i]), $"green of pixel {i}");
        Assert.That(image[i * 3 + 2], Is.EqualTo(blue[i]), $"blue of pixel {i}");
      }
    });
  }

  [Test]
  [Category("Unit")]
  public void TheTransparencyMaskBecomesTheAlpha() {
    const int WIDTH = 4;
    const int HEIGHT = 4;
    var (red, green, blue) = _Planes(WIDTH, HEIGHT);
    var alpha = new byte[WIDTH * HEIGHT];
    for (var i = 0; i < alpha.Length; ++i)
      alpha[i] = (byte)(i % 2 == 0 ? 255 : 0);

    var file = PspReader.FromBytes(_Build(WIDTH, HEIGHT, 2, red, green, blue, alpha));

    Assert.Multiple(() => {
      Assert.That(file.HasAlpha, Is.True);
      Assert.That(PspFile.ToRawImage(file).Format, Is.EqualTo(PixelFormat.Rgba32));
      for (var i = 0; i < alpha.Length; ++i)
        Assert.That(file.PixelData[i * 4 + 3], Is.EqualTo(alpha[i]), $"alpha of pixel {i}");
    });
  }

  [Test]
  [Category("Unit")]
  public void AFileWithNoLayerIsRefused() {
    var bytes = _Build(4, 4, 2, new byte[16], new byte[16], new byte[16]);

    // Turn the layer bank into a block nothing reads, leaving the attributes and no picture.
    var at = 0;
    for (var i = 36; i + 6 < bytes.Length; ++i)
      if (bytes[i] == 0x7E && bytes[i + 1] == 0x42 && bytes[i + 2] == 0x4B && bytes[i + 3] == 0x00 && bytes[i + 4] == 3) {
        at = i;
        break;
      }

    Assert.That(at, Is.GreaterThan(0), "the file states a layer bank");
    bytes[at + 4] = 1; // the creator block, which holds no picture
    Assert.That(() => PspReader.FromBytes(bytes), Throws.InstanceOf<InvalidDataException>());
  }

  [Test]
  [Category("Unit")]
  public void AVersionOlderThanTheLayoutIsRefused() {
    var bytes = _Build(4, 4, 2, new byte[16], new byte[16], new byte[16]);
    bytes[32] = 3;
    Assert.That(() => PspReader.FromBytes(bytes), Throws.InstanceOf<InvalidDataException>());
  }

  [Test]
  [Category("Integration")]
  public void WhatWeWriteIsWhatWeRead() {
    const int WIDTH = 9;
    const int HEIGHT = 4;
    var pixels = new byte[WIDTH * HEIGHT * 4];
    for (var i = 0; i < WIDTH * HEIGHT; ++i) {
      pixels[i * 4] = (byte)(i * 5);
      pixels[i * 4 + 1] = (byte)(i * 9);
      pixels[i * 4 + 2] = (byte)(200 - i * 4);
      pixels[i * 4 + 3] = (byte)(i % 3 == 0 ? 255 : 64);
    }

    var source = new RawImage { Width = WIDTH, Height = HEIGHT, Format = PixelFormat.Rgba32, PixelData = pixels };
    var restored = PspFile.ToRawImage(PspReader.FromBytes(PspWriter.ToBytes(PspFile.FromRawImage(source))));

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(WIDTH));
      Assert.That(restored.Height, Is.EqualTo(HEIGHT));
      Assert.That(restored.Format, Is.EqualTo(PixelFormat.Rgba32));
      Assert.That(restored.PixelData, Is.EqualTo(pixels));
    });
  }
}
