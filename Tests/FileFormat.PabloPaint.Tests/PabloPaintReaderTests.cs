using System;
using System.IO;
using FileFormat.PabloPaint;

namespace FileFormat.PabloPaint.Tests;

[TestFixture]
public sealed class PabloPaintReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => PabloPaintReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => PabloPaintReader.FromFile(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".pa3"));
    Assert.Throws<FileNotFoundException>(() => PabloPaintReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => PabloPaintReader.FromStream(null!));

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => PabloPaintReader.FromBytes(new byte[100]));

  [Test]
  [Category("Unit")]
  public void FromBytes_ExactSize_Parses() {
    var data = new byte[32000];
    data[0] = 0xFF;
    data[31999] = 0xAA;
    var result = PabloPaintReader.FromBytes(data);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(640));
      Assert.That(result.Height, Is.EqualTo(400));
      Assert.That(result.PixelData.Length, Is.EqualTo(32000));
      Assert.That(result.PixelData[0], Is.EqualTo(0xFF));
      Assert.That(result.PixelData[31999], Is.EqualTo(0xAA));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_LargerThanExpected_ParsesFirstBytes() {
    var data = new byte[33000];
    data[0] = 0xBB;
    data[32000] = 0xCC;
    var result = PabloPaintReader.FromBytes(data);

    Assert.Multiple(() => {
      Assert.That(result.PixelData.Length, Is.EqualTo(32000));
      Assert.That(result.PixelData[0], Is.EqualTo(0xBB));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidData_Parses() {
    var data = new byte[32000];
    data[42] = 0xDE;
    using var ms = new MemoryStream(data);
    var result = PabloPaintReader.FromStream(ms);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(640));
      Assert.That(result.Height, Is.EqualTo(400));
      Assert.That(result.PixelData[42], Is.EqualTo(0xDE));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_CopiesPixelData() {
    var data = new byte[32000];
    data[0] = 0x55;
    var result = PabloPaintReader.FromBytes(data);
    data[0] = 0x00;
    Assert.That(result.PixelData[0], Is.EqualTo(0x55));
  }
}
