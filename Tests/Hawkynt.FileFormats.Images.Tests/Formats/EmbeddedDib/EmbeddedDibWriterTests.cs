using System;
using System.Buffers.Binary;
using FileFormat.Core;

namespace FileFormat.EmbeddedDib.Tests;

[TestFixture]
public sealed class EmbeddedDibWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_RawImage_WritesPackedDibWithoutBitmapFileHeader() {
    var bytes = EmbeddedDibWriter.ToBytes(_Image());

    Assert.Multiple(() => {
      Assert.That(BinaryPrimitives.ReadInt32LittleEndian(bytes), Is.EqualTo(40));
      Assert.That(bytes[0], Is.Not.EqualTo((byte)'B'));
      Assert.That(bytes[1], Is.Not.EqualTo((byte)'M'));
    });
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_RawImage_RoundTripsThroughHeaderlessReader() {
    var image = _Image();

    var bytes = EmbeddedDibWriter.ToBytes(image);
    var decoded = EmbeddedDibReader.DecodeHeaderless(bytes);

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(image.Width));
      Assert.That(decoded.Height, Is.EqualTo(image.Height));
      Assert.That(decoded.Format, Is.EqualTo(image.Format));
      Assert.That(decoded.PixelData, Is.EqualTo(image.PixelData));
    });
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_File_WritesItsPreviewOnly() {
    var image = _Image();
    var file = new EmbeddedDibFile { Preview = image, Offset = 1234 };

    var bytes = EmbeddedDibWriter.ToBytes(file);

    Assert.That(bytes, Is.EqualTo(EmbeddedDibWriter.ToBytes(image)));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_DefaultFile_Throws() {
    Assert.That(
      () => EmbeddedDibWriter.ToBytes(default(EmbeddedDibFile)),
      Throws.ArgumentException.With.Property("ParamName").EqualTo("file")
    );
  }

  private static RawImage _Image() => new() {
    Width = 2,
    Height = 2,
    Format = PixelFormat.Bgr24,
    PixelData = [
      0x03, 0x02, 0x01, 0x06, 0x05, 0x04,
      0x09, 0x08, 0x07, 0x0C, 0x0B, 0x0A,
    ]
  };
}
