using System;
using FileFormat.WigmoreArtist;

namespace FileFormat.WigmoreArtist.Tests;

[TestFixture]
public sealed class WigmoreArtistWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => WigmoreArtistWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_CorrectOutputSize() {
    var rawData = new byte[WigmoreArtistFile.MinPayloadSize];
    var file = new WigmoreArtistFile { LoadAddress = 0x2000, RawData = rawData };
    var bytes = WigmoreArtistWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(WigmoreArtistFile.LoadAddressSize + WigmoreArtistFile.MinPayloadSize));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_LoadAddress_IsLittleEndian() {
    var rawData = new byte[WigmoreArtistFile.MinPayloadSize];
    var file = new WigmoreArtistFile { LoadAddress = 0x2000, RawData = rawData };
    var bytes = WigmoreArtistWriter.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo(0x00));
    Assert.That(bytes[1], Is.EqualTo(0x20));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_RawData_StartsAtByte2() {
    var rawData = new byte[WigmoreArtistFile.MinPayloadSize];
    rawData[0] = 0xAA;
    rawData[8999] = 0xBB;

    var file = new WigmoreArtistFile { LoadAddress = 0x2000, RawData = rawData };
    var bytes = WigmoreArtistWriter.ToBytes(file);

    Assert.That(bytes[2], Is.EqualTo(0xAA));
    Assert.That(bytes[9001], Is.EqualTo(0xBB));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ExtraData_Preserved() {
    var rawData = new byte[WigmoreArtistFile.MinPayloadSize + 50];
    for (var i = 0; i < rawData.Length; ++i)
      rawData[i] = (byte)(i % 256);

    var file = new WigmoreArtistFile { LoadAddress = 0x2000, RawData = rawData };
    var bytes = WigmoreArtistWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(WigmoreArtistFile.LoadAddressSize + rawData.Length));
  }
}
