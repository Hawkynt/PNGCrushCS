using System;
using System.Text;
using FileFormat.XvThumbnail;

namespace FileFormat.XvThumbnail.Tests;

[TestFixture]
public sealed class XvThumbnailWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => XvThumbnailWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_StartsWith_P7332Magic() {
    var file = new XvThumbnailFile {
      Width = 2,
      Height = 2,
      PixelData = new byte[4],
    };

    var bytes = XvThumbnailWriter.ToBytes(file);
    var magic = Encoding.ASCII.GetString(bytes, 0, 7);

    Assert.That(magic, Is.EqualTo("P7 332\n"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ContainsDimensionLine() {
    var file = new XvThumbnailFile {
      Width = 10,
      Height = 5,
      PixelData = new byte[50],
    };

    var bytes = XvThumbnailWriter.ToBytes(file);
    var headerEnd = Array.IndexOf(bytes, (byte)'\n', 7) + 1;
    var dimLine = Encoding.ASCII.GetString(bytes, 7, headerEnd - 7 - 1);

    Assert.That(dimLine, Is.EqualTo("10 5 255"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_PixelDataPreserved() {
    var pixels = new byte[] { 0xE0, 0x1C, 0x03, 0xFF };
    var file = new XvThumbnailFile {
      Width = 2,
      Height = 2,
      PixelData = pixels,
    };

    var bytes = XvThumbnailWriter.ToBytes(file);
    var headerLen = 7 + Encoding.ASCII.GetBytes("2 2 255\n").Length;

    Assert.That(bytes[headerLen], Is.EqualTo(0xE0));
    Assert.That(bytes[headerLen + 1], Is.EqualTo(0x1C));
    Assert.That(bytes[headerLen + 2], Is.EqualTo(0x03));
    Assert.That(bytes[headerLen + 3], Is.EqualTo(0xFF));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_CorrectTotalLength() {
    var file = new XvThumbnailFile {
      Width = 4,
      Height = 3,
      PixelData = new byte[12],
    };

    var bytes = XvThumbnailWriter.ToBytes(file);
    var expectedHeader = Encoding.ASCII.GetBytes("P7 332\n4 3 255\n");

    Assert.That(bytes.Length, Is.EqualTo(expectedHeader.Length + 12));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ShortPixelData_PadsWithZeros() {
    var file = new XvThumbnailFile {
      Width = 4,
      Height = 3,
      PixelData = new byte[2],
    };

    var bytes = XvThumbnailWriter.ToBytes(file);
    var expectedHeader = Encoding.ASCII.GetBytes("P7 332\n4 3 255\n");

    Assert.That(bytes.Length, Is.EqualTo(expectedHeader.Length + 12));
    Assert.That(bytes[bytes.Length - 1], Is.EqualTo(0));
  }
}
