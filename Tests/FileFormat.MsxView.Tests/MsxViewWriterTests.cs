using System;
using FileFormat.MsxView;

namespace FileFormat.MsxView.Tests;

[TestFixture]
public sealed class MsxViewWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => MsxViewWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_OutputSize_Is54272() {
    var file = new MsxViewFile { PixelData = new byte[54272] };

    var bytes = MsxViewWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(54272));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_DataPreserved() {
    var pixels = new byte[54272];
    pixels[0] = 0x3F;
    pixels[100] = 0x7F;
    pixels[54271] = 0xDE;

    var file = new MsxViewFile { PixelData = pixels };
    var bytes = MsxViewWriter.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo(0x3F));
    Assert.That(bytes[100], Is.EqualTo(0x7F));
    Assert.That(bytes[54271], Is.EqualTo(0xDE));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ShortData_PadsWithZeros() {
    var pixels = new byte[10];
    pixels[0] = 0xFF;

    var file = new MsxViewFile { PixelData = pixels };
    var bytes = MsxViewWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(54272));
    Assert.That(bytes[0], Is.EqualTo(0xFF));
    Assert.That(bytes[10], Is.EqualTo(0x00));
  }
}
