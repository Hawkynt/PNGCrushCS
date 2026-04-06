using System;
using System.IO;
using FileFormat.ZxUlaPlus;
using FileFormat.Core;

namespace FileFormat.ZxUlaPlus.Tests;

[TestFixture]
public class ZxUlaPlusReaderTests {

  [Test]
  public void FromFile_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => ZxUlaPlusReader.FromFile(null!));

  [Test]
  public void FromFile_MissingFile_ThrowsFileNotFoundException()
    => Assert.Throws<FileNotFoundException>(() => ZxUlaPlusReader.FromFile(new FileInfo("nonexistent.ulp")));

  [Test]
  public void FromBytes_NullData_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => ZxUlaPlusReader.FromBytes(null!));

  [Test]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => ZxUlaPlusReader.FromBytes(new byte[100]));

  [Test]
  public void FromBytes_ExactSize_Succeeds() {
    var data = new byte[6976];
    var result = ZxUlaPlusReader.FromBytes(data);
    Assert.That(result.Width, Is.EqualTo(256));
    Assert.That(result.Height, Is.EqualTo(192));
  }

  [Test]
  public void FromStream_NullStream_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => ZxUlaPlusReader.FromStream(null!));

  [Test]
  public void FromBytes_WrongSize_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => ZxUlaPlusReader.FromBytes(new byte[7000]));

  [Test]
  public void FromBytes_BitmapData_HasCorrectLength() {
    var data = new byte[6976];
    var result = ZxUlaPlusReader.FromBytes(data);
    Assert.That(result.BitmapData.Length, Is.EqualTo(6144));
  }

  [Test]
  public void FromBytes_AttributeData_HasCorrectLength() {
    var data = new byte[6976];
    var result = ZxUlaPlusReader.FromBytes(data);
    Assert.That(result.AttributeData.Length, Is.EqualTo(768));
  }

  [Test]
  public void FromBytes_PaletteData_HasCorrectLength() {
    var data = new byte[6976];
    var result = ZxUlaPlusReader.FromBytes(data);
    Assert.That(result.PaletteData.Length, Is.EqualTo(64));
  }

  [Test]
  public void FromBytes_PaletteData_IsCopied() {
    var data = new byte[6976];
    data[6912] = 0xAB;
    var result = ZxUlaPlusReader.FromBytes(data);
    Assert.That(result.PaletteData[0], Is.EqualTo(0xAB));
    data[6912] = 0x00;
    Assert.That(result.PaletteData[0], Is.EqualTo(0xAB));
  }
}

[TestFixture]
public class RoundTripTests {

  [Test]
  public void RoundTrip_AllBytes_Preserved() {
    var original = new byte[6976];
    for (var i = 0; i < original.Length; ++i)
      original[i] = (byte)(i * 7 & 0xFF);
    var file = ZxUlaPlusReader.FromBytes(original);
    var written = ZxUlaPlusWriter.ToBytes(file);
    Assert.That(written, Is.EqualTo(original));
  }

  [Test]
  public void RoundTrip_ViaFile() {
    var original = new byte[6976];
    for (var i = 0; i < original.Length; ++i)
      original[i] = (byte)(i & 0xFF);
    var tmp = Path.GetTempFileName();
    try {
      File.WriteAllBytes(tmp, original);
      var file = ZxUlaPlusReader.FromFile(new FileInfo(tmp));
      var written = ZxUlaPlusWriter.ToBytes(file);
      Assert.That(written, Is.EqualTo(original));
    } finally {
      File.Delete(tmp);
    }
  }
}

