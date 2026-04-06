using System;
using System.IO;
using FileFormat.ZxFlash;
using FileFormat.Core;

namespace FileFormat.ZxFlash.Tests;

[TestFixture]
public class ZxFlashReaderTests {

  [Test]
  public void FromFile_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => ZxFlashReader.FromFile(null!));

  [Test]
  public void FromFile_MissingFile_ThrowsFileNotFoundException()
    => Assert.Throws<FileNotFoundException>(() => ZxFlashReader.FromFile(new FileInfo("nonexistent.zfl")));

  [Test]
  public void FromBytes_NullData_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => ZxFlashReader.FromBytes(null!));

  [Test]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => ZxFlashReader.FromBytes(new byte[100]));

  [Test]
  public void FromBytes_ExactSize_Succeeds() {
    var data = new byte[6912];
    var result = ZxFlashReader.FromBytes(data);
    Assert.That(result.Width, Is.EqualTo(256));
    Assert.That(result.Height, Is.EqualTo(192));
  }

  [Test]
  public void FromStream_NullStream_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => ZxFlashReader.FromStream(null!));

  [Test]
  public void FromBytes_SingleFrame_FrameCountIs1() {
    var data = new byte[6912];
    var result = ZxFlashReader.FromBytes(data);
    Assert.That(result.FrameCount, Is.EqualTo(1));
  }

  [Test]
  public void FromBytes_TwoFrames_FrameCountIs2() {
    var data = new byte[6912 * 2];
    var result = ZxFlashReader.FromBytes(data);
    Assert.That(result.FrameCount, Is.EqualTo(2));
  }

  [Test]
  public void FromBytes_BitmapData_HasCorrectLength() {
    var data = new byte[6912];
    var result = ZxFlashReader.FromBytes(data);
    Assert.That(result.BitmapData.Length, Is.EqualTo(6144));
  }

  [Test]
  public void FromBytes_AttributeData_HasCorrectLength() {
    var data = new byte[6912];
    var result = ZxFlashReader.FromBytes(data);
    Assert.That(result.AttributeData.Length, Is.EqualTo(768));
  }
}

[TestFixture]
public class RoundTripTests {

  [Test]
  public void RoundTrip_SingleFrame_Preserved() {
    var original = new byte[6912];
    for (var i = 0; i < original.Length; ++i)
      original[i] = (byte)(i * 7 & 0xFF);
    var file = ZxFlashReader.FromBytes(original);
    var written = ZxFlashWriter.ToBytes(file);
    Assert.That(written, Is.EqualTo(original));
  }

  [Test]
  public void RoundTrip_ViaFile() {
    var original = new byte[6912];
    for (var i = 0; i < original.Length; ++i)
      original[i] = (byte)(i & 0xFF);
    var tmp = Path.GetTempFileName();
    try {
      File.WriteAllBytes(tmp, original);
      var file = ZxFlashReader.FromFile(new FileInfo(tmp));
      var written = ZxFlashWriter.ToBytes(file);
      Assert.That(written, Is.EqualTo(original));
    } finally {
      File.Delete(tmp);
    }
  }
}

