using System;
using FileFormat.CpcSprite;

namespace FileFormat.CpcSprite.Tests;

[TestFixture]
public sealed class CpcSpriteWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => CpcSpriteWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ValidFile_OutputIsExactly64Bytes() {
    var file = new CpcSpriteFile { RawData = new byte[CpcSpriteFile.ExpectedFileSize] };

    var bytes = CpcSpriteWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(CpcSpriteFile.ExpectedFileSize));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_PreservesData() {
    var rawData = new byte[CpcSpriteFile.ExpectedFileSize];
    rawData[0] = 0xAA;
    rawData[63] = 0xBB;

    var file = new CpcSpriteFile { RawData = rawData };

    var bytes = CpcSpriteWriter.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo(0xAA));
    Assert.That(bytes[63], Is.EqualTo(0xBB));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ClonesData() {
    var rawData = new byte[CpcSpriteFile.ExpectedFileSize];
    rawData[0] = 0xCC;

    var file = new CpcSpriteFile { RawData = rawData };

    var bytes = CpcSpriteWriter.ToBytes(file);

    Assert.That(bytes, Is.Not.SameAs(rawData));
    Assert.That(bytes[0], Is.EqualTo(0xCC));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_EmptyRawData_ReturnsAllZeros() {
    var file = new CpcSpriteFile { RawData = [] };

    var bytes = CpcSpriteWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(CpcSpriteFile.ExpectedFileSize));
    Assert.That(bytes, Is.All.EqualTo((byte)0));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_AllOnesData_PreservesValues() {
    var rawData = new byte[CpcSpriteFile.ExpectedFileSize];
    Array.Fill(rawData, (byte)0xFF);

    var file = new CpcSpriteFile { RawData = rawData };

    var bytes = CpcSpriteWriter.ToBytes(file);

    Assert.That(bytes, Is.All.EqualTo((byte)0xFF));
  }
}
