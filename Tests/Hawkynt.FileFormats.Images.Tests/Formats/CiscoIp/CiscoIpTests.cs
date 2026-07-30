using System;
using System.IO;
using FileFormat.CiscoIp;
using FileFormat.Core;

namespace FileFormat.CiscoIp.Tests;

[TestFixture]
public class CiscoIpReaderTests {

  [Test]
  public void FromFile_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => CiscoIpReader.FromFile(null!));

  [Test]
  public void FromFile_MissingFile_ThrowsFileNotFoundException()
    => Assert.Throws<FileNotFoundException>(() => CiscoIpReader.FromFile(new FileInfo("nonexistent.bin")));

  [Test]
  public void FromBytes_NullData_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => CiscoIpReader.FromBytes(null!));

  [Test]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => CiscoIpReader.FromBytes(new byte[79]));

  [Test]
  public void FromBytes_ValidHeader_Succeeds() {
    var data = new byte[80 + 320 * 240 * 3];
    data[0] = 64;
    data[1] = 1;
    data[4] = 240; data[5] = 0;
    var result = CiscoIpReader.FromBytes(data);
    Assert.That(result.Width, Is.GreaterThan(0));
    Assert.That(result.Height, Is.GreaterThan(0));
  }

  [Test]
  public void FromStream_NullStream_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => CiscoIpReader.FromStream(null!));
}

[TestFixture]
public class RoundTripTests {

  [Test]
  public void RoundTrip_PixelDataPreserved() {
    var file = new CiscoIpFile {
      Width = 320,
      Height = 240,
      PixelData = new byte[320 * 240 * 3],
    };
    for (var i = 0; i < file.PixelData.Length; ++i)
      file.PixelData[i] = (byte)(i & 0xFF);
    var bytes = CiscoIpWriter.ToBytes(file);
    var file2 = CiscoIpReader.FromBytes(bytes);
    Assert.That(file2.PixelData, Is.EqualTo(file.PixelData));
  }

  [Test]
  public void RoundTrip_ViaRawImage() {
    var file = new CiscoIpFile {
      Width = 320,
      Height = 240,
      PixelData = new byte[320 * 240 * 3],
    };
    var raw = CiscoIpFile.ToRawImage(file);
    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Rgb24));
    var file2 = CiscoIpFile.FromRawImage(raw);
    Assert.That(file2.PixelData, Is.EqualTo(file.PixelData));
  }
}

