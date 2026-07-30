using System;
using System.IO;
using FileFormat.HiEddi;

namespace FileFormat.HiEddi.Tests;

[TestFixture]
public sealed class HiEddiReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => HiEddiReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => HiEddiReader.FromFile(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".hed"));
    Assert.Throws<FileNotFoundException>(() => HiEddiReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => HiEddiReader.FromStream(null!));

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => HiEddiReader.FromBytes(new byte[100]));

  [Test]
  [Category("Unit")]
  public void FromBytes_WrongSize_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => HiEddiReader.FromBytes(new byte[9219]));

  [Test]
  [Category("Integration")]
  public void FromBytes_ValidParsesCorrectly() {
    var data = _BuildValidHiEddi(0x5C00);
    var result = HiEddiReader.FromBytes(data);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(320));
      Assert.That(result.Height, Is.EqualTo(200));
      Assert.That(result.LoadAddress, Is.EqualTo(0x5C00));
      Assert.That(result.BitmapData.Length, Is.EqualTo(8000));
      Assert.That(result.ScreenRam.Length, Is.EqualTo(1000));
    });
  }

  [Test]
  [Category("Integration")]
  public void FromBytes_RoundTrip_PreservesData() {
    var original = _BuildValidHiEddi(0x5C00);
    var file = HiEddiReader.FromBytes(original);
    var written = HiEddiWriter.ToBytes(file);
    var reread = HiEddiReader.FromBytes(written);

    Assert.Multiple(() => {
      Assert.That(reread.LoadAddress, Is.EqualTo(file.LoadAddress));
      Assert.That(reread.BitmapData, Is.EqualTo(file.BitmapData));
      Assert.That(reread.ScreenRam, Is.EqualTo(file.ScreenRam));
    });
  }

  [Test]
  [Category("Integration")]
  public void FromStream_ValidParsesCorrectly() {
    var data = _BuildValidHiEddi(0x5C00);
    using var ms = new MemoryStream(data);
    var result = HiEddiReader.FromStream(ms);

    Assert.That(result.LoadAddress, Is.EqualTo(0x5C00));
  }

  private static byte[] _BuildValidHiEddi(ushort loadAddress) {
    var data = new byte[HiEddiFile.ExpectedFileSize];
    data[0] = (byte)(loadAddress & 0xFF);
    data[1] = (byte)(loadAddress >> 8);

    for (var i = 0; i < 8000; ++i)
      data[2 + i] = (byte)(i % 256);

    for (var i = 0; i < 1000; ++i)
      data[8002 + i] = (byte)((i % 15) << 4 | ((i + 1) % 15));

    return data;
  }
}
