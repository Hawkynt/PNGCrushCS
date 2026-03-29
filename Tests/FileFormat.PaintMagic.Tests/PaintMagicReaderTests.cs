using System;
using System.IO;
using FileFormat.PaintMagic;

namespace FileFormat.PaintMagic.Tests;

[TestFixture]
public sealed class PaintMagicReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => PaintMagicReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => PaintMagicReader.FromFile(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".pmg"));
    Assert.Throws<FileNotFoundException>(() => PaintMagicReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => PaintMagicReader.FromStream(null!));

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => PaintMagicReader.FromBytes(new byte[100]));

  [Test]
  [Category("Unit")]
  public void FromBytes_WrongSize_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => PaintMagicReader.FromBytes(new byte[10004]));

  [Test]
  [Category("Integration")]
  public void FromBytes_ValidParsesCorrectly() {
    var data = _BuildValidPaintMagic(0x6000, 0x03);
    var result = PaintMagicReader.FromBytes(data);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(160));
      Assert.That(result.Height, Is.EqualTo(200));
      Assert.That(result.LoadAddress, Is.EqualTo(0x6000));
      Assert.That(result.BitmapData.Length, Is.EqualTo(8000));
      Assert.That(result.VideoMatrix.Length, Is.EqualTo(1000));
      Assert.That(result.ColorRam.Length, Is.EqualTo(1000));
      Assert.That(result.BackgroundColor, Is.EqualTo(0x03));
    });
  }

  [Test]
  [Category("Integration")]
  public void FromBytes_RoundTrip_PreservesData() {
    var original = _BuildValidPaintMagic(0x6000, 0x05);
    var file = PaintMagicReader.FromBytes(original);
    var written = PaintMagicWriter.ToBytes(file);
    var reread = PaintMagicReader.FromBytes(written);

    Assert.Multiple(() => {
      Assert.That(reread.LoadAddress, Is.EqualTo(file.LoadAddress));
      Assert.That(reread.BitmapData, Is.EqualTo(file.BitmapData));
      Assert.That(reread.VideoMatrix, Is.EqualTo(file.VideoMatrix));
      Assert.That(reread.ColorRam, Is.EqualTo(file.ColorRam));
      Assert.That(reread.BackgroundColor, Is.EqualTo(file.BackgroundColor));
    });
  }

  [Test]
  [Category("Integration")]
  public void FromStream_ValidParsesCorrectly() {
    var data = _BuildValidPaintMagic(0x6000, 0x05);
    using var ms = new MemoryStream(data);
    var result = PaintMagicReader.FromStream(ms);

    Assert.That(result.LoadAddress, Is.EqualTo(0x6000));
  }

  private static byte[] _BuildValidPaintMagic(ushort loadAddress, byte backgroundColor) {
    var data = new byte[PaintMagicFile.ExpectedFileSize];
    data[0] = (byte)(loadAddress & 0xFF);
    data[1] = (byte)(loadAddress >> 8);

    for (var i = 0; i < 8000; ++i)
      data[2 + i] = (byte)(i % 256);

    for (var i = 0; i < 1000; ++i)
      data[8002 + i] = (byte)(i % 16);

    for (var i = 0; i < 1000; ++i)
      data[9002 + i] = (byte)((i + 3) % 16);

    data[10002] = backgroundColor;

    return data;
  }
}
