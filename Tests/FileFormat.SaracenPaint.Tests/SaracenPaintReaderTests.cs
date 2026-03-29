using System;
using System.IO;
using FileFormat.SaracenPaint;

namespace FileFormat.SaracenPaint.Tests;

[TestFixture]
public sealed class SaracenPaintReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => SaracenPaintReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => SaracenPaintReader.FromFile(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".sar"));
    Assert.Throws<FileNotFoundException>(() => SaracenPaintReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => SaracenPaintReader.FromStream(null!));

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => SaracenPaintReader.FromBytes(new byte[100]));

  [Test]
  [Category("Unit")]
  public void FromBytes_WrongSize_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => SaracenPaintReader.FromBytes(new byte[9010]));

  [Test]
  [Category("Integration")]
  public void FromBytes_ValidParsesCorrectly() {
    var data = _BuildValidSaracenPaint(0x2000);
    var result = SaracenPaintReader.FromBytes(data);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(320));
      Assert.That(result.Height, Is.EqualTo(200));
      Assert.That(result.LoadAddress, Is.EqualTo(0x2000));
      Assert.That(result.ScreenRam.Length, Is.EqualTo(1000));
      Assert.That(result.BitmapData.Length, Is.EqualTo(8000));
    });
  }

  [Test]
  [Category("Integration")]
  public void FromBytes_RoundTrip_PreservesData() {
    var original = _BuildValidSaracenPaint(0x2000);
    var file = SaracenPaintReader.FromBytes(original);
    var written = SaracenPaintWriter.ToBytes(file);
    var reread = SaracenPaintReader.FromBytes(written);

    Assert.Multiple(() => {
      Assert.That(reread.LoadAddress, Is.EqualTo(file.LoadAddress));
      Assert.That(reread.ScreenRam, Is.EqualTo(file.ScreenRam));
      Assert.That(reread.BitmapData, Is.EqualTo(file.BitmapData));
    });
  }

  [Test]
  [Category("Integration")]
  public void FromStream_ValidParsesCorrectly() {
    var data = _BuildValidSaracenPaint(0x2000);
    using var ms = new MemoryStream(data);
    var result = SaracenPaintReader.FromStream(ms);

    Assert.That(result.LoadAddress, Is.EqualTo(0x2000));
  }

  private static byte[] _BuildValidSaracenPaint(ushort loadAddress) {
    var data = new byte[SaracenPaintFile.ExpectedFileSize];
    data[0] = (byte)(loadAddress & 0xFF);
    data[1] = (byte)(loadAddress >> 8);

    // screenRam at offset 2
    for (var i = 0; i < 1000; ++i)
      data[2 + i] = (byte)((i % 15) << 4 | ((i + 1) % 15));

    // bitmapData at offset 1002
    for (var i = 0; i < 8000; ++i)
      data[1002 + i] = (byte)(i % 256);

    return data;
  }
}
