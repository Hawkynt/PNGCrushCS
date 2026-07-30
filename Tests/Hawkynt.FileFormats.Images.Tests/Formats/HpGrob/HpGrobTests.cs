using System;
using System.IO;
using FileFormat.HpGrob;
using FileFormat.Core;

namespace FileFormat.HpGrob.Tests;

[TestFixture]
public class HpGrobReaderTests {

  [Test]
  public void FromFile_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => HpGrobReader.FromFile(null!));

  [Test]
  public void FromFile_MissingFile_ThrowsFileNotFoundException()
    => Assert.Throws<FileNotFoundException>(() => HpGrobReader.FromFile(new FileInfo("nonexistent.bin")));

  [Test]
  public void FromBytes_NullData_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => HpGrobReader.FromBytes(null!));

  [Test]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => HpGrobReader.FromBytes(new byte[9]));

  [Test]
  public void FromBytes_ValidHeader_Succeeds() {
    var data = new byte[10 + (131 + 7) / 8 * 64];
    data[0] = 131;
    data[1] = 0;
    data[4] = 64; data[5] = 0;
    var result = HpGrobReader.FromBytes(data);
    Assert.That(result.Width, Is.GreaterThan(0));
    Assert.That(result.Height, Is.GreaterThan(0));
  }

  [Test]
  public void FromStream_NullStream_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => HpGrobReader.FromStream(null!));
}

[TestFixture]
public class RoundTripTests {

  [Test]
  public void RoundTrip_PixelDataPreserved() {
    var file = new HpGrobFile {
      Width = 131,
      Height = 64,
      PixelData = new byte[(131 + 7) / 8 * 64],
    };
    for (var i = 0; i < file.PixelData.Length; ++i)
      file.PixelData[i] = (byte)(i & 0xFF);
    var bytes = HpGrobWriter.ToBytes(file);
    var file2 = HpGrobReader.FromBytes(bytes);
    Assert.That(file2.PixelData, Is.EqualTo(file.PixelData));
  }

  [Test]
  public void RoundTrip_ViaRawImage() {
    var file = new HpGrobFile {
      Width = 131,
      Height = 64,
      PixelData = new byte[(131 + 7) / 8 * 64],
    };
    var raw = HpGrobFile.ToRawImage(file);
    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Indexed1));
    var file2 = HpGrobFile.FromRawImage(raw);
    Assert.That(file2.PixelData, Is.EqualTo(file.PixelData));
  }
}

