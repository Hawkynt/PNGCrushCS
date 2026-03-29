using System;
using System.Buffers.Binary;
using System.Text;
using FileFormat.DjVu;

namespace FileFormat.DjVu.Tests;

[TestFixture]
public sealed class DjVuWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_Throws() {
    Assert.Throws<ArgumentNullException>(() => DjVuWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_StartsWithAttMagic() {
    var file = new DjVuFile {
      Width = 2,
      Height = 2,
      PixelData = new byte[2 * 2 * 3]
    };

    var bytes = DjVuWriter.ToBytes(file);

    Assert.That(Encoding.ASCII.GetString(bytes, 0, 4), Is.EqualTo("AT&T"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_HasFormTag() {
    var file = new DjVuFile {
      Width = 2,
      Height = 2,
      PixelData = new byte[2 * 2 * 3]
    };

    var bytes = DjVuWriter.ToBytes(file);

    Assert.That(Encoding.ASCII.GetString(bytes, 4, 4), Is.EqualTo("FORM"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_HasDjvuFormType() {
    var file = new DjVuFile {
      Width = 2,
      Height = 2,
      PixelData = new byte[2 * 2 * 3]
    };

    var bytes = DjVuWriter.ToBytes(file);

    Assert.That(Encoding.ASCII.GetString(bytes, 12, 4), Is.EqualTo("DJVU"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_HasInfoChunk() {
    var file = new DjVuFile {
      Width = 4,
      Height = 3,
      PixelData = new byte[4 * 3 * 3]
    };

    var bytes = DjVuWriter.ToBytes(file);

    // INFO chunk should be at offset 16
    Assert.That(Encoding.ASCII.GetString(bytes, 16, 4), Is.EqualTo("INFO"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_InfoChunkSize10() {
    var file = new DjVuFile {
      Width = 4,
      Height = 3,
      PixelData = new byte[4 * 3 * 3]
    };

    var bytes = DjVuWriter.ToBytes(file);

    var infoSize = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(20));
    Assert.That(infoSize, Is.EqualTo(10u));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_InfoContainsDimensions() {
    var file = new DjVuFile {
      Width = 320,
      Height = 240,
      PixelData = new byte[320 * 240 * 3]
    };

    var bytes = DjVuWriter.ToBytes(file);

    // INFO data starts at offset 24
    var width = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(24));
    var height = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(26));
    Assert.That(width, Is.EqualTo(320));
    Assert.That(height, Is.EqualTo(240));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_InfoContainsDpi() {
    var file = new DjVuFile {
      Width = 10,
      Height = 10,
      Dpi = 600,
      PixelData = new byte[10 * 10 * 3]
    };

    var bytes = DjVuWriter.ToBytes(file);

    var dpi = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(30));
    Assert.That(dpi, Is.EqualTo(600));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_InfoContainsGamma() {
    var file = new DjVuFile {
      Width = 10,
      Height = 10,
      Gamma = 33,
      PixelData = new byte[10 * 10 * 3]
    };

    var bytes = DjVuWriter.ToBytes(file);

    Assert.That(bytes[32], Is.EqualTo(33));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_HasBG44Chunk() {
    var file = new DjVuFile {
      Width = 2,
      Height = 2,
      PixelData = new byte[2 * 2 * 3]
    };

    var bytes = DjVuWriter.ToBytes(file);

    // After INFO (16 + 8 + 10 = 34), BG44 chunk should start at 34
    Assert.That(Encoding.ASCII.GetString(bytes, 34, 4), Is.EqualTo("BG44"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_FormSizeIsCorrect() {
    var file = new DjVuFile {
      Width = 1,
      Height = 1,
      PixelData = new byte[3]
    };

    var bytes = DjVuWriter.ToBytes(file);

    var formSize = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(8));
    // formSize = total - 12 (AT&T + FORM + size)
    Assert.That(formSize, Is.EqualTo((uint)(bytes.Length - 12)));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_EmptyPixelData_NoPixelChunk() {
    var file = new DjVuFile {
      Width = 1,
      Height = 1,
      PixelData = []
    };

    var bytes = DjVuWriter.ToBytes(file);

    // Should be: AT&T(4) + FORM(4) + size(4) + DJVU(4) + INFO chunk(8+10) = 34
    Assert.That(bytes.Length, Is.EqualTo(34));
  }
}
