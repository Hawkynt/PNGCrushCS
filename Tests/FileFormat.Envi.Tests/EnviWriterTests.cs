using System;
using System.Text;
using FileFormat.Envi;

namespace FileFormat.Envi.Tests;

[TestFixture]
public sealed class EnviWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => EnviWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_StartsWithEnviMagic() {
    var file = new EnviFile {
      Width = 4,
      Height = 2,
      Bands = 1,
      DataType = 1,
      Interleave = EnviInterleave.Bsq,
      PixelData = new byte[4 * 2]
    };

    var bytes = EnviWriter.ToBytes(file);
    var prefix = Encoding.ASCII.GetString(bytes, 0, 4);

    Assert.That(prefix, Is.EqualTo("ENVI"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ContainsNewlineAfterMagic() {
    var file = new EnviFile {
      Width = 2,
      Height = 1,
      Bands = 1,
      DataType = 1,
      PixelData = new byte[2]
    };

    var bytes = EnviWriter.ToBytes(file);

    Assert.That(bytes[4], Is.EqualTo((byte)'\n'));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ContainsSamplesField() {
    var file = new EnviFile {
      Width = 10,
      Height = 5,
      Bands = 1,
      DataType = 1,
      PixelData = new byte[50]
    };

    var bytes = EnviWriter.ToBytes(file);
    var headerText = Encoding.ASCII.GetString(bytes);

    Assert.That(headerText, Does.Contain("samples = 10"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ContainsLinesField() {
    var file = new EnviFile {
      Width = 4,
      Height = 7,
      Bands = 1,
      DataType = 1,
      PixelData = new byte[28]
    };

    var bytes = EnviWriter.ToBytes(file);
    var headerText = Encoding.ASCII.GetString(bytes);

    Assert.That(headerText, Does.Contain("lines = 7"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ContainsBandsField() {
    var file = new EnviFile {
      Width = 2,
      Height = 1,
      Bands = 3,
      DataType = 1,
      Interleave = EnviInterleave.Bip,
      PixelData = new byte[6]
    };

    var bytes = EnviWriter.ToBytes(file);
    var headerText = Encoding.ASCII.GetString(bytes);

    Assert.That(headerText, Does.Contain("bands = 3"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ContainsDataTypeField() {
    var file = new EnviFile {
      Width = 2,
      Height = 1,
      Bands = 1,
      DataType = 12,
      PixelData = new byte[4]
    };

    var bytes = EnviWriter.ToBytes(file);
    var headerText = Encoding.ASCII.GetString(bytes);

    Assert.That(headerText, Does.Contain("data type = 12"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ContainsInterleaveField() {
    var file = new EnviFile {
      Width = 2,
      Height = 1,
      Bands = 3,
      DataType = 1,
      Interleave = EnviInterleave.Bil,
      PixelData = new byte[6]
    };

    var bytes = EnviWriter.ToBytes(file);
    var headerText = Encoding.ASCII.GetString(bytes);

    Assert.That(headerText, Does.Contain("interleave = bil"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ContainsByteOrderField() {
    var file = new EnviFile {
      Width = 2,
      Height = 1,
      Bands = 1,
      DataType = 1,
      ByteOrder = 1,
      PixelData = new byte[2]
    };

    var bytes = EnviWriter.ToBytes(file);
    var headerText = Encoding.ASCII.GetString(bytes);

    Assert.That(headerText, Does.Contain("byte order = 1"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ContainsHeaderOffsetField() {
    var file = new EnviFile {
      Width = 2,
      Height = 1,
      Bands = 1,
      DataType = 1,
      PixelData = new byte[2]
    };

    var bytes = EnviWriter.ToBytes(file);
    var headerText = Encoding.ASCII.GetString(bytes);

    Assert.That(headerText, Does.Contain("header offset = 0"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_PixelDataFollowsHeader() {
    var pixels = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
    var file = new EnviFile {
      Width = 2,
      Height = 2,
      Bands = 1,
      DataType = 1,
      PixelData = pixels
    };

    var bytes = EnviWriter.ToBytes(file);

    // last 4 bytes should be our pixel data
    var tail = new byte[4];
    Array.Copy(bytes, bytes.Length - 4, tail, 0, 4);
    Assert.That(tail, Is.EqualTo(pixels));
  }
}
