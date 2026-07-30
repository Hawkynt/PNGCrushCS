using System;
using System.IO;
using FileFormat.ZxChrd;
using FileFormat.Core;

namespace FileFormat.ZxChrd.Tests;

[TestFixture]
public class ZxChrdReaderTests {

  [Test]
  public void FromFile_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => ZxChrdReader.FromFile(null!));

  [Test]
  public void FromFile_MissingFile_ThrowsFileNotFoundException()
    => Assert.Throws<FileNotFoundException>(() => ZxChrdReader.FromFile(new FileInfo("nonexistent.chr")));

  [Test]
  public void FromBytes_NullData_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => ZxChrdReader.FromBytes(null!));

  [Test]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => ZxChrdReader.FromBytes(new byte[100]));

  [Test]
  public void FromBytes_ExactSize_Succeeds() {
    var data = new byte[2048];
    var result = ZxChrdReader.FromBytes(data);
    Assert.That(result.Width, Is.EqualTo(128));
    Assert.That(result.Height, Is.EqualTo(128));
  }

  [Test]
  public void FromStream_NullStream_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => ZxChrdReader.FromStream(null!));

  [Test]
  public void FromBytes_WrongSize_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => ZxChrdReader.FromBytes(new byte[3000]));

  [Test]
  public void FromBytes_CharacterData_HasCorrectLength() {
    var data = new byte[2048];
    var result = ZxChrdReader.FromBytes(data);
    Assert.That(result.CharacterData.Length, Is.EqualTo(2048));
  }

  [Test]
  public void FromBytes_CharacterData_IsCopy() {
    var data = new byte[2048];
    data[0] = 0xAA;
    var result = ZxChrdReader.FromBytes(data);
    Assert.That(result.CharacterData[0], Is.EqualTo(0xAA));
    data[0] = 0x00;
    Assert.That(result.CharacterData[0], Is.EqualTo(0xAA));
  }
}

[TestFixture]
public class RoundTripTests {

  [Test]
  public void RoundTrip_AllBytes_Preserved() {
    var original = new byte[2048];
    for (var i = 0; i < original.Length; ++i)
      original[i] = (byte)(i * 7 & 0xFF);
    var file = ZxChrdReader.FromBytes(original);
    var written = ZxChrdWriter.ToBytes(file);
    Assert.That(written, Is.EqualTo(original));
  }

  [Test]
  public void RoundTrip_ViaFile() {
    var original = new byte[2048];
    for (var i = 0; i < original.Length; ++i)
      original[i] = (byte)(i & 0xFF);
    var tmp = Path.GetTempFileName();
    try {
      File.WriteAllBytes(tmp, original);
      var file = ZxChrdReader.FromFile(new FileInfo(tmp));
      var written = ZxChrdWriter.ToBytes(file);
      Assert.That(written, Is.EqualTo(original));
    } finally {
      File.Delete(tmp);
    }
  }
}

