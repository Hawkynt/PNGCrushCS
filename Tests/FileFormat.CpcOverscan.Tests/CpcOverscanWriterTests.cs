using System;
using FileFormat.CpcOverscan;

namespace FileFormat.CpcOverscan.Tests;

[TestFixture]
public sealed class CpcOverscanWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => CpcOverscanWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ValidFile_OutputIsExactly32768Bytes() {
    var file = new CpcOverscanFile { PixelData = new byte[CpcOverscanFile.PixelHeight * CpcOverscanFile.BytesPerRow] };

    var bytes = CpcOverscanWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(CpcOverscanFile.ExpectedFileSize));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_InterleavesBank0Line0() {
    var linearData = new byte[CpcOverscanFile.PixelHeight * CpcOverscanFile.BytesPerRow];
    linearData[0] = 0xAA;

    var file = new CpcOverscanFile { PixelData = linearData };

    var bytes = CpcOverscanWriter.ToBytes(file);

    // Bank 0, line 0: address = 0
    Assert.That(bytes[0], Is.EqualTo(0xAA));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_InterleavesBank0Line1() {
    var linearData = new byte[CpcOverscanFile.PixelHeight * CpcOverscanFile.BytesPerRow];
    // Linear line 1: offset = 96
    linearData[CpcOverscanFile.BytesPerRow] = 0xBB;

    var file = new CpcOverscanFile { PixelData = linearData };

    var bytes = CpcOverscanWriter.ToBytes(file);

    // Bank 0, local line 1: address = ((1/8)*96) + ((1%8)*2048) = 2048
    Assert.That(bytes[2048], Is.EqualTo(0xBB));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_InterleavesBank1Line0() {
    var linearData = new byte[CpcOverscanFile.PixelHeight * CpcOverscanFile.BytesPerRow];
    // Linear line 136 (first line of bank 1): offset = 136 * 96 = 13056
    linearData[136 * CpcOverscanFile.BytesPerRow] = 0xCC;

    var file = new CpcOverscanFile { PixelData = linearData };

    var bytes = CpcOverscanWriter.ToBytes(file);

    // Bank 1, local line 0: bankOffset + 0 = 16384
    Assert.That(bytes[16384], Is.EqualTo(0xCC));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_EmptyPixelData_ReturnsAllZeros() {
    var file = new CpcOverscanFile { PixelData = [] };

    var bytes = CpcOverscanWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(CpcOverscanFile.ExpectedFileSize));
    Assert.That(bytes, Is.All.EqualTo((byte)0));
  }
}
