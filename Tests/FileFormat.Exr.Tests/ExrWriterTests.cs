using System;
using System.Buffers.Binary;
using FileFormat.Exr;

namespace FileFormat.Exr.Tests;

[TestFixture]
public sealed class ExrWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_ValidFile_StartsWithMagicBytes() {
    var file = new ExrFile {
      Width = 1,
      Height = 1,
      Compression = ExrCompression.None,
      LineOrder = ExrLineOrder.IncreasingY,
      Channels = [new ExrChannel { Name = "R", PixelType = ExrPixelType.Float, XSampling = 1, YSampling = 1 }],
      PixelData = new byte[4]
    };

    var bytes = ExrWriter.ToBytes(file);

    var magic = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(0));
    Assert.That(magic, Is.EqualTo(ExrMagicHeader.ExpectedMagic));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ValidFile_HasCorrectVersion() {
    var file = new ExrFile {
      Width = 1,
      Height = 1,
      Compression = ExrCompression.None,
      LineOrder = ExrLineOrder.IncreasingY,
      Channels = [new ExrChannel { Name = "R", PixelType = ExrPixelType.Float, XSampling = 1, YSampling = 1 }],
      PixelData = new byte[4]
    };

    var bytes = ExrWriter.ToBytes(file);

    var version = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(4));
    Assert.That(version, Is.EqualTo(ExrMagicHeader.ExpectedVersion));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => ExrWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_MultipleChannels_WritesAllChannels() {
    var file = new ExrFile {
      Width = 1,
      Height = 1,
      Compression = ExrCompression.None,
      LineOrder = ExrLineOrder.IncreasingY,
      Channels = [
        new ExrChannel { Name = "B", PixelType = ExrPixelType.Half, XSampling = 1, YSampling = 1 },
        new ExrChannel { Name = "G", PixelType = ExrPixelType.Half, XSampling = 1, YSampling = 1 },
        new ExrChannel { Name = "R", PixelType = ExrPixelType.Half, XSampling = 1, YSampling = 1 }
      ],
      PixelData = new byte[1 * 1 * 2 * 3]
    };

    var bytes = ExrWriter.ToBytes(file);
    var result = ExrReader.FromBytes(bytes);

    Assert.That(result.Channels, Has.Count.EqualTo(3));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_OutputLargerThanHeader() {
    var file = new ExrFile {
      Width = 2,
      Height = 2,
      Compression = ExrCompression.None,
      LineOrder = ExrLineOrder.IncreasingY,
      Channels = [new ExrChannel { Name = "R", PixelType = ExrPixelType.Float, XSampling = 1, YSampling = 1 }],
      PixelData = new byte[2 * 2 * 4]
    };

    var bytes = ExrWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.GreaterThan(ExrMagicHeader.StructSize));
  }
}
