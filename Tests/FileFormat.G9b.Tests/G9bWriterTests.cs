using System;
using FileFormat.G9b;

namespace FileFormat.G9b.Tests;

[TestFixture]
public sealed class G9bWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => G9bWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Mode3_OutputSizeMatchesExpected() {
    var file = new G9bFile {
      Width = 8,
      Height = 4,
      ScreenMode = G9bScreenMode.Indexed8,
      ColorMode = 0,
      PixelData = new byte[32],
    };

    var bytes = G9bWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(G9bReader.DefaultHeaderSize + 32));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_MagicBytesWritten() {
    var file = new G9bFile {
      Width = 1,
      Height = 1,
      ScreenMode = G9bScreenMode.Indexed8,
      PixelData = new byte[1],
    };

    var bytes = G9bWriter.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo(0x47));
    Assert.That(bytes[1], Is.EqualTo(0x39));
    Assert.That(bytes[2], Is.EqualTo(0x42));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_HeaderSizeWrittenAsLE() {
    var file = new G9bFile {
      Width = 1,
      Height = 1,
      ScreenMode = G9bScreenMode.Indexed8,
      PixelData = new byte[1],
    };

    var bytes = G9bWriter.ToBytes(file);

    Assert.That(bytes[3], Is.EqualTo(11));
    Assert.That(bytes[4], Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ScreenModeWritten() {
    var file = new G9bFile {
      Width = 1,
      Height = 1,
      ScreenMode = G9bScreenMode.Rgb555,
      PixelData = new byte[2],
    };

    var bytes = G9bWriter.ToBytes(file);

    Assert.That(bytes[5], Is.EqualTo(5));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_WidthWrittenAsLE() {
    var file = new G9bFile {
      Width = 0x0102,
      Height = 1,
      ScreenMode = G9bScreenMode.Indexed8,
      PixelData = new byte[0x0102],
    };

    var bytes = G9bWriter.ToBytes(file);

    Assert.That(bytes[7], Is.EqualTo(0x02));
    Assert.That(bytes[8], Is.EqualTo(0x01));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_HeightWrittenAsLE() {
    var file = new G9bFile {
      Width = 1,
      Height = 0x0304,
      ScreenMode = G9bScreenMode.Indexed8,
      PixelData = new byte[0x0304],
    };

    var bytes = G9bWriter.ToBytes(file);

    Assert.That(bytes[9], Is.EqualTo(0x04));
    Assert.That(bytes[10], Is.EqualTo(0x03));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_PixelDataWrittenAfterHeader() {
    var pixels = new byte[] { 0xAA, 0xBB, 0xCC, 0xDD };
    var file = new G9bFile {
      Width = 2,
      Height = 2,
      ScreenMode = G9bScreenMode.Indexed8,
      PixelData = pixels,
    };

    var bytes = G9bWriter.ToBytes(file);

    Assert.That(bytes[11], Is.EqualTo(0xAA));
    Assert.That(bytes[12], Is.EqualTo(0xBB));
    Assert.That(bytes[13], Is.EqualTo(0xCC));
    Assert.That(bytes[14], Is.EqualTo(0xDD));
  }
}
