using System;
using FileFormat.AmstradCpc;

namespace FileFormat.AmstradCpc.Tests;

[TestFixture]
public sealed class AmstradCpcWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_ValidFile_OutputIsExactly16384Bytes() {
    var file = new AmstradCpcFile {
      Width = 320,
      Height = 200,
      Mode = AmstradCpcMode.Mode1,
      PixelData = new byte[16000]
    };

    var bytes = AmstradCpcWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(16384));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => AmstradCpcWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Mode0_OutputIsExactly16384Bytes() {
    var file = new AmstradCpcFile {
      Width = 160,
      Height = 200,
      Mode = AmstradCpcMode.Mode0,
      PixelData = new byte[16000]
    };

    var bytes = AmstradCpcWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(16384));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_InterleavesScanlines() {
    var linearData = new byte[16000];
    // Write a recognizable pattern to line 0 (first byte)
    linearData[0] = 0xAA;
    // Write a recognizable pattern to line 1 (byte 80)
    linearData[80] = 0xBB;

    var file = new AmstradCpcFile {
      Width = 320,
      Height = 200,
      Mode = AmstradCpcMode.Mode1,
      PixelData = linearData
    };

    var bytes = AmstradCpcWriter.ToBytes(file);

    // Line 0: address = ((0/8)*80) + ((0%8)*2048) = 0
    Assert.That(bytes[0], Is.EqualTo(0xAA));
    // Line 1: address = ((1/8)*80) + ((1%8)*2048) = 0 + 2048 = 2048
    Assert.That(bytes[2048], Is.EqualTo(0xBB));
  }
}
