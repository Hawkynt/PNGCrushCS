using System;
using System.Buffers.Binary;
using System.Text;
using FileFormat.Xwd;

namespace FileFormat.Xwd.Tests;

[TestFixture]
public sealed class XwdWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => XwdWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_HasHeader() {
    var file = new XwdFile {
      Width = 4,
      Height = 2,
      BitsPerPixel = 24,
      BytesPerLine = 12,
      PixmapFormat = XwdPixmapFormat.ZPixmap,
      PixmapDepth = 24,
      VisualClass = XwdVisualClass.TrueColor,
      RedMask = 0x00FF0000,
      GreenMask = 0x0000FF00,
      BlueMask = 0x000000FF,
      PixelData = new byte[24]
    };

    var bytes = XwdWriter.ToBytes(file);

    var version = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(4));
    Assert.That(version, Is.EqualTo(7));
    Assert.That(bytes.Length, Is.GreaterThanOrEqualTo(XwdHeader.StructSize));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ContainsWindowName() {
    var file = new XwdFile {
      Width = 2,
      Height = 1,
      BitsPerPixel = 24,
      BytesPerLine = 6,
      PixmapFormat = XwdPixmapFormat.ZPixmap,
      PixmapDepth = 24,
      VisualClass = XwdVisualClass.TrueColor,
      WindowName = "hello",
      PixelData = new byte[6]
    };

    var bytes = XwdWriter.ToBytes(file);

    var headerSize = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(0));
    var expectedHeaderSize = XwdHeader.StructSize + 5 + 1; // "hello" + null
    Assert.That(headerSize, Is.EqualTo((uint)expectedHeaderSize));

    var nameSpan = bytes.AsSpan(XwdHeader.StructSize, 5);
    var name = Encoding.ASCII.GetString(nameSpan);
    Assert.That(name, Is.EqualTo("hello"));
    Assert.That(bytes[XwdHeader.StructSize + 5], Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_PixelDataPresent() {
    var pixelData = new byte[] { 0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF };
    var file = new XwdFile {
      Width = 2,
      Height = 1,
      BitsPerPixel = 24,
      BytesPerLine = 6,
      PixmapFormat = XwdPixmapFormat.ZPixmap,
      PixmapDepth = 24,
      VisualClass = XwdVisualClass.TrueColor,
      WindowName = "",
      PixelData = pixelData
    };

    var bytes = XwdWriter.ToBytes(file);

    var headerSize = (int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(0));
    var pixelStart = headerSize; // no colormap
    Assert.That(bytes[pixelStart], Is.EqualTo(0xAA));
    Assert.That(bytes[pixelStart + 1], Is.EqualTo(0xBB));
    Assert.That(bytes[pixelStart + 5], Is.EqualTo(0xFF));
  }
}
