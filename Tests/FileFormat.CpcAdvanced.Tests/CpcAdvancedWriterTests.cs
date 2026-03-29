using System;
using FileFormat.CpcAdvanced;

namespace FileFormat.CpcAdvanced.Tests;

[TestFixture]
public sealed class CpcAdvancedWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => CpcAdvancedWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ValidFile_OutputIsExactly16384Bytes() {
    var file = new CpcAdvancedFile { PixelData = new byte[CpcAdvancedFile.PixelHeight * CpcAdvancedFile.BytesPerRow] };

    var bytes = CpcAdvancedWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(CpcAdvancedFile.ExpectedFileSize));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_InterleavesScanlines() {
    var linearData = new byte[CpcAdvancedFile.PixelHeight * CpcAdvancedFile.BytesPerRow];
    linearData[0] = 0xAA;
    linearData[CpcAdvancedFile.BytesPerRow] = 0xBB;

    var file = new CpcAdvancedFile { PixelData = linearData };

    var bytes = CpcAdvancedWriter.ToBytes(file);

    // Line 0: address = ((0/8)*80) + ((0%8)*2048) = 0
    Assert.That(bytes[0], Is.EqualTo(0xAA));
    // Line 1: address = ((1/8)*80) + ((1%8)*2048) = 2048
    Assert.That(bytes[2048], Is.EqualTo(0xBB));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Line8_MapsToCorrectOffset() {
    var linearData = new byte[CpcAdvancedFile.PixelHeight * CpcAdvancedFile.BytesPerRow];
    linearData[8 * CpcAdvancedFile.BytesPerRow] = 0xDD;

    var file = new CpcAdvancedFile { PixelData = linearData };

    var bytes = CpcAdvancedWriter.ToBytes(file);

    // Line 8: address = ((8/8)*80) + ((8%8)*2048) = 80 + 0 = 80
    Assert.That(bytes[80], Is.EqualTo(0xDD));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_EmptyPixelData_ReturnsAllZeros() {
    var file = new CpcAdvancedFile { PixelData = [] };

    var bytes = CpcAdvancedWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(CpcAdvancedFile.ExpectedFileSize));
    Assert.That(bytes, Is.All.EqualTo((byte)0));
  }
}
