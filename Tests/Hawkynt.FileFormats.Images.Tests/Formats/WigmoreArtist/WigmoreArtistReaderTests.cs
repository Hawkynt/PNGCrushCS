using System;
using System.IO;
using FileFormat.WigmoreArtist;

namespace FileFormat.WigmoreArtist.Tests;

[TestFixture]
public sealed class WigmoreArtistReaderTests {

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => WigmoreArtistReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".wig"));
    Assert.Throws<FileNotFoundException>(() => WigmoreArtistReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => WigmoreArtistReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    Assert.Throws<InvalidDataException>(() => WigmoreArtistReader.FromBytes(new byte[100]));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidData_ParsesDimensions() {
    var data = _BuildValidData(0x2000);
    var result = WigmoreArtistReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(320));
    Assert.That(result.Height, Is.EqualTo(200));
    Assert.That(result.LoadAddress, Is.EqualTo(0x2000));
    Assert.That(result.RawData.Length, Is.GreaterThanOrEqualTo(WigmoreArtistFile.MinPayloadSize));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => WigmoreArtistReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidData_ParsesCorrectly() {
    var data = _BuildValidData(0x4000);
    using var ms = new MemoryStream(data);
    var result = WigmoreArtistReader.FromStream(ms);

    Assert.That(result.LoadAddress, Is.EqualTo(0x4000));
    Assert.That(result.RawData.Length, Is.GreaterThanOrEqualTo(WigmoreArtistFile.MinPayloadSize));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_LoadAddress_ParsedAsLittleEndian() {
    var data = _BuildValidData(0x6000);
    var result = WigmoreArtistReader.FromBytes(data);

    Assert.That(result.LoadAddress, Is.EqualTo(0x6000));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidData_RawDataContainsBitmapAndScreen() {
    var data = _BuildValidData(0x2000);
    data[2] = 0xAB;
    data[8001] = 0xCD;

    var result = WigmoreArtistReader.FromBytes(data);

    Assert.That(result.RawData[0], Is.EqualTo(0xAB));
    Assert.That(result.RawData[7999], Is.EqualTo(0xCD));
  }

  private static byte[] _BuildValidData(ushort loadAddress) {
    var rawData = new byte[WigmoreArtistFile.MinPayloadSize];
    for (var i = 0; i < rawData.Length; ++i)
      rawData[i] = (byte)(i % 256);

    var data = new byte[WigmoreArtistFile.LoadAddressSize + rawData.Length];
    data[0] = (byte)(loadAddress & 0xFF);
    data[1] = (byte)(loadAddress >> 8);
    Array.Copy(rawData, 0, data, WigmoreArtistFile.LoadAddressSize, rawData.Length);
    return data;
  }
}
