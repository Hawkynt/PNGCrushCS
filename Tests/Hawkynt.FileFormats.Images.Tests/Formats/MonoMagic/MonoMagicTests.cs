using System;
using System.IO;
using FileFormat.MonoMagic;
using FileFormat.Core;

namespace FileFormat.MonoMagic.Tests;

[TestFixture]
public class MonoMagicReaderTests {

  [Test]
  public void FromFile_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => MonoMagicReader.FromFile(null!));

  [Test]
  public void FromFile_MissingFile_ThrowsFileNotFoundException()
    => Assert.Throws<FileNotFoundException>(() => MonoMagicReader.FromFile(new FileInfo("nonexistent.bin")));

  [Test]
  public void FromBytes_NullData_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => MonoMagicReader.FromBytes(null!));

  [Test]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => MonoMagicReader.FromBytes(new byte[1]));

  [Test]
  public void FromBytes_ExactSize_Succeeds() {
    var data = new byte[8194];
    var result = MonoMagicReader.FromBytes(data);
    Assert.That(result.Width, Is.EqualTo(320));
    Assert.That(result.Height, Is.EqualTo(200));
  }

  [Test]
  public void FromStream_NullStream_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => MonoMagicReader.FromStream(null!));
}

[TestFixture]
public class RoundTripTests {

  // These used to compare whole files byte for byte, which only worked while the reader handed its
  // input straight back. The file is a load address, then the screen a character cell at a time, then
  // 192 bytes the screen does not reach into — so what survives a round trip is the picture, not every
  // byte of the file.

  [Test]
  public void RoundTrip_TheScreenSurvives() {
    var original = new byte[8194];
    original[0] = 0x00;
    original[1] = 0x20;
    for (var i = 2; i < 8002; ++i)
      original[i] = (byte)(i * 7 & 0xFF);

    var written = MonoMagicWriter.ToBytes(MonoMagicReader.FromBytes(original));

    Assert.That(written[..8002], Is.EqualTo(original[..8002]));
  }

  [Test]
  public void RoundTrip_ViaFile() {
    var original = new byte[8194];
    original[1] = 0x20;
    var tmp = Path.GetTempFileName();
    try {
      File.WriteAllBytes(tmp, original);
      var written = MonoMagicWriter.ToBytes(MonoMagicReader.FromFile(new FileInfo(tmp)));
      Assert.That(written, Is.EqualTo(original));
    } finally {
      File.Delete(tmp);
    }
  }
}

