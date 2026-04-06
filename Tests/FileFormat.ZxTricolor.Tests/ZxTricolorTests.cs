using System;
using System.IO;
using FileFormat.ZxTricolor;
using FileFormat.Core;

namespace FileFormat.ZxTricolor.Tests;

[TestFixture]
public class ZxTricolorReaderTests {

  [Test]
  public void FromFile_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => ZxTricolorReader.FromFile(null!));

  [Test]
  public void FromFile_MissingFile_ThrowsFileNotFoundException()
    => Assert.Throws<FileNotFoundException>(() => ZxTricolorReader.FromFile(new FileInfo("nonexistent.3cl")));

  [Test]
  public void FromBytes_NullData_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => ZxTricolorReader.FromBytes(null!));

  [Test]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => ZxTricolorReader.FromBytes(new byte[100]));

  [Test]
  public void FromBytes_ExactSize_Succeeds() {
    var data = new byte[20736];
    var result = ZxTricolorReader.FromBytes(data);
    Assert.That(result.Width, Is.EqualTo(256));
    Assert.That(result.Height, Is.EqualTo(192));
  }

  [Test]
  public void FromStream_NullStream_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => ZxTricolorReader.FromStream(null!));

  [Test]
  public void FromBytes_WrongSize_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => ZxTricolorReader.FromBytes(new byte[21000]));

  [Test]
  public void FromBytes_BitmapData1_HasCorrectLength() {
    var data = new byte[20736];
    var result = ZxTricolorReader.FromBytes(data);
    Assert.That(result.BitmapData1.Length, Is.EqualTo(6144));
  }

  [Test]
  public void FromBytes_BitmapData2_HasCorrectLength() {
    var data = new byte[20736];
    var result = ZxTricolorReader.FromBytes(data);
    Assert.That(result.BitmapData2.Length, Is.EqualTo(6144));
  }

  [Test]
  public void FromBytes_BitmapData3_HasCorrectLength() {
    var data = new byte[20736];
    var result = ZxTricolorReader.FromBytes(data);
    Assert.That(result.BitmapData3.Length, Is.EqualTo(6144));
  }

  [Test]
  public void FromBytes_AttributeData1_HasCorrectLength() {
    var data = new byte[20736];
    var result = ZxTricolorReader.FromBytes(data);
    Assert.That(result.AttributeData1.Length, Is.EqualTo(768));
  }

  [Test]
  public void FromBytes_AttributeData2_HasCorrectLength() {
    var data = new byte[20736];
    var result = ZxTricolorReader.FromBytes(data);
    Assert.That(result.AttributeData2.Length, Is.EqualTo(768));
  }

  [Test]
  public void FromBytes_AttributeData3_HasCorrectLength() {
    var data = new byte[20736];
    var result = ZxTricolorReader.FromBytes(data);
    Assert.That(result.AttributeData3.Length, Is.EqualTo(768));
  }
}

[TestFixture]
public class RoundTripTests {

  [Test]
  public void RoundTrip_AllBytes_Preserved() {
    var original = new byte[20736];
    for (var i = 0; i < original.Length; ++i)
      original[i] = (byte)(i * 7 & 0xFF);
    var file = ZxTricolorReader.FromBytes(original);
    var written = ZxTricolorWriter.ToBytes(file);
    Assert.That(written, Is.EqualTo(original));
  }

  [Test]
  public void RoundTrip_ViaFile() {
    var original = new byte[20736];
    for (var i = 0; i < original.Length; ++i)
      original[i] = (byte)(i & 0xFF);
    var tmp = Path.GetTempFileName();
    try {
      File.WriteAllBytes(tmp, original);
      var file = ZxTricolorReader.FromFile(new FileInfo(tmp));
      var written = ZxTricolorWriter.ToBytes(file);
      Assert.That(written, Is.EqualTo(original));
    } finally {
      File.Delete(tmp);
    }
  }
}

