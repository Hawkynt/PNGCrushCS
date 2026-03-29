using System;
using FileFormat.Oric;

namespace FileFormat.Oric.Tests;

[TestFixture]
public sealed class OricWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Size8000() {
    var file = new OricFile {
      ScreenData = new byte[8000]
    };

    var bytes = OricWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(8000));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => OricWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_DataPreserved() {
    var screenData = new byte[8000];
    screenData[0] = 0x3F;
    screenData[1] = 0x40;
    screenData[39] = 0x7F;
    screenData[7999] = 0xDE;

    var file = new OricFile {
      ScreenData = screenData
    };

    var bytes = OricWriter.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo(0x3F));
    Assert.That(bytes[1], Is.EqualTo(0x40));
    Assert.That(bytes[39], Is.EqualTo(0x7F));
    Assert.That(bytes[7999], Is.EqualTo(0xDE));
  }
}
