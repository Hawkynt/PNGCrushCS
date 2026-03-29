using System;
using System.Buffers.Binary;
using FileFormat.Exr;

namespace FileFormat.Exr.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_SingleChannelFloat() {
    var pixelData = new byte[4 * 3 * 4]; // 4x3, 1 float channel
    for (var i = 0; i < 4 * 3; ++i)
      BinaryPrimitives.WriteSingleLittleEndian(pixelData.AsSpan(i * 4), i * 0.1f);

    var original = new ExrFile {
      Width = 4,
      Height = 3,
      Compression = ExrCompression.None,
      LineOrder = ExrLineOrder.IncreasingY,
      Channels = [new ExrChannel { Name = "R", PixelType = ExrPixelType.Float, XSampling = 1, YSampling = 1 }],
      PixelData = pixelData
    };

    var bytes = ExrWriter.ToBytes(original);
    var restored = ExrReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.Compression, Is.EqualTo(ExrCompression.None));
    Assert.That(restored.Channels, Has.Count.EqualTo(1));
    Assert.That(restored.Channels[0].Name, Is.EqualTo("R"));
    Assert.That(restored.Channels[0].PixelType, Is.EqualTo(ExrPixelType.Float));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_RgbHalf() {
    var width = 2;
    var height = 2;
    var bytesPerPixelPerChannel = 2; // Half = 2 bytes
    var channelCount = 3;
    var pixelData = new byte[width * height * bytesPerPixelPerChannel * channelCount];

    // Fill with recognizable pattern
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 13 % 256);

    var original = new ExrFile {
      Width = width,
      Height = height,
      Compression = ExrCompression.None,
      LineOrder = ExrLineOrder.IncreasingY,
      Channels = [
        new ExrChannel { Name = "B", PixelType = ExrPixelType.Half, XSampling = 1, YSampling = 1 },
        new ExrChannel { Name = "G", PixelType = ExrPixelType.Half, XSampling = 1, YSampling = 1 },
        new ExrChannel { Name = "R", PixelType = ExrPixelType.Half, XSampling = 1, YSampling = 1 }
      ],
      PixelData = pixelData
    };

    var bytes = ExrWriter.ToBytes(original);
    var restored = ExrReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(width));
    Assert.That(restored.Height, Is.EqualTo(height));
    Assert.That(restored.Channels, Has.Count.EqualTo(channelCount));
    Assert.That(restored.Channels[0].Name, Is.EqualTo("B"));
    Assert.That(restored.Channels[1].Name, Is.EqualTo("G"));
    Assert.That(restored.Channels[2].Name, Is.EqualTo("R"));
    Assert.That(restored.Channels[0].PixelType, Is.EqualTo(ExrPixelType.Half));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_SingleChannelUInt() {
    var width = 3;
    var height = 2;
    var pixelData = new byte[width * height * 4]; // UInt = 4 bytes
    for (var i = 0; i < width * height; ++i)
      BinaryPrimitives.WriteUInt32LittleEndian(pixelData.AsSpan(i * 4), (uint)(i * 1000));

    var original = new ExrFile {
      Width = width,
      Height = height,
      Compression = ExrCompression.None,
      LineOrder = ExrLineOrder.IncreasingY,
      Channels = [new ExrChannel { Name = "A", PixelType = ExrPixelType.UInt, XSampling = 1, YSampling = 1 }],
      PixelData = pixelData
    };

    var bytes = ExrWriter.ToBytes(original);
    var restored = ExrReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(width));
    Assert.That(restored.Height, Is.EqualTo(height));
    Assert.That(restored.Channels, Has.Count.EqualTo(1));
    Assert.That(restored.Channels[0].PixelType, Is.EqualTo(ExrPixelType.UInt));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_PreservesLineOrder() {
    var original = new ExrFile {
      Width = 1,
      Height = 1,
      Compression = ExrCompression.None,
      LineOrder = ExrLineOrder.DecreasingY,
      Channels = [new ExrChannel { Name = "R", PixelType = ExrPixelType.Float, XSampling = 1, YSampling = 1 }],
      PixelData = new byte[4]
    };

    var bytes = ExrWriter.ToBytes(original);
    var restored = ExrReader.FromBytes(bytes);

    Assert.That(restored.LineOrder, Is.EqualTo(ExrLineOrder.DecreasingY));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_PreservesCompression() {
    var original = new ExrFile {
      Width = 1,
      Height = 1,
      Compression = ExrCompression.Zip,
      LineOrder = ExrLineOrder.IncreasingY,
      Channels = [new ExrChannel { Name = "R", PixelType = ExrPixelType.Float, XSampling = 1, YSampling = 1 }],
      PixelData = new byte[4]
    };

    var bytes = ExrWriter.ToBytes(original);
    var restored = ExrReader.FromBytes(bytes);

    Assert.That(restored.Compression, Is.EqualTo(ExrCompression.Zip));
  }
}
