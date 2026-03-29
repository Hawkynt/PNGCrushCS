using System;
using System.Text;
using FileFormat.Cals;

namespace FileFormat.Cals.Tests;

[TestFixture]
public sealed class CalsWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_HeaderSize768() {
    var file = new CalsFile {
      Width = 8,
      Height = 1,
      PixelData = new byte[1]
    };

    var bytes = CalsWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.GreaterThanOrEqualTo(768));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ContainsRpelcnt() {
    var file = new CalsFile {
      Width = 640,
      Height = 480,
      PixelData = new byte[(640 + 7) / 8 * 480]
    };

    var bytes = CalsWriter.ToBytes(file);
    var headerText = Encoding.ASCII.GetString(bytes, 0, 768);

    Assert.That(headerText, Does.Contain("rpelcnt: 640,480"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_Throws() {
    Assert.Throws<ArgumentNullException>(() => CalsWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_FileSizeCorrect() {
    var file = new CalsFile {
      Width = 16,
      Height = 4,
      PixelData = new byte[8] // 2 bytes/row * 4 rows
    };

    var bytes = CalsWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(768 + 8));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ContainsRdensty() {
    var file = new CalsFile {
      Width = 8,
      Height = 1,
      Dpi = 400,
      PixelData = new byte[1]
    };

    var bytes = CalsWriter.ToBytes(file);
    var headerText = Encoding.ASCII.GetString(bytes, 0, 768);

    Assert.That(headerText, Does.Contain("rdensty: 400"));
  }
}
