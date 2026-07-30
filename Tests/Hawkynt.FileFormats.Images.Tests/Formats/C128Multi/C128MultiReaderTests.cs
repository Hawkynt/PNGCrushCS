using System;
using System.IO;
using FileFormat.C128Multi;

namespace FileFormat.C128Multi.Tests;

[TestFixture]
public sealed class C128MultiReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => C128MultiReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => C128MultiReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".c1m"));
    Assert.Throws<FileNotFoundException>(() => C128MultiReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => C128MultiReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[100];
    Assert.Throws<InvalidDataException>(() => C128MultiReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooLarge_ThrowsInvalidDataException() {
    var tooLarge = new byte[10241];
    Assert.Throws<InvalidDataException>(() => C128MultiReader.FromBytes(tooLarge));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ExactSize_Parses() {
    var data = new byte[C128MultiFile.ExpectedFileSize];
    data[0] = 0xAB;
    data[7999] = 0xCD;
    data[8000] = 0xEF;
    data[8999] = 0x12;
    data[9000] = 0x34;
    data[9999] = 0x56;
    data[10000] = 0x07;

    var result = C128MultiReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(160));
    Assert.That(result.Height, Is.EqualTo(200));
    Assert.That(result.BitmapData.Length, Is.EqualTo(C128MultiFile.BitmapDataSize));
    Assert.That(result.ScreenData.Length, Is.EqualTo(C128MultiFile.ScreenDataSize));
    Assert.That(result.ColorData.Length, Is.EqualTo(C128MultiFile.ColorDataSize));
    Assert.That(result.BitmapData[0], Is.EqualTo(0xAB));
    Assert.That(result.BitmapData[7999], Is.EqualTo(0xCD));
    Assert.That(result.ScreenData[0], Is.EqualTo(0xEF));
    Assert.That(result.ScreenData[999], Is.EqualTo(0x12));
    Assert.That(result.ColorData[0], Is.EqualTo(0x34));
    Assert.That(result.ColorData[999], Is.EqualTo(0x56));
    Assert.That(result.BackgroundColor, Is.EqualTo(0x07));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Valid() {
    var data = new byte[C128MultiFile.ExpectedFileSize];
    data[0] = 0xAB;
    data[10000] = 0x05;

    using var ms = new MemoryStream(data);
    var result = C128MultiReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(160));
    Assert.That(result.Height, Is.EqualTo(200));
    Assert.That(result.BitmapData[0], Is.EqualTo(0xAB));
    Assert.That(result.BackgroundColor, Is.EqualTo(0x05));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_CopiesBitmapData_NotReference() {
    var data = new byte[C128MultiFile.ExpectedFileSize];
    data[0] = 0xFF;

    var result = C128MultiReader.FromBytes(data);
    data[0] = 0x00;

    Assert.That(result.BitmapData[0], Is.EqualTo(0xFF));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_CopiesScreenData_NotReference() {
    var data = new byte[C128MultiFile.ExpectedFileSize];
    data[C128MultiFile.BitmapDataSize] = 0xAA;

    var result = C128MultiReader.FromBytes(data);
    data[C128MultiFile.BitmapDataSize] = 0x00;

    Assert.That(result.ScreenData[0], Is.EqualTo(0xAA));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_CopiesColorData_NotReference() {
    var data = new byte[C128MultiFile.ExpectedFileSize];
    data[C128MultiFile.BitmapDataSize + C128MultiFile.ScreenDataSize] = 0xBB;

    var result = C128MultiReader.FromBytes(data);
    data[C128MultiFile.BitmapDataSize + C128MultiFile.ScreenDataSize] = 0x00;

    Assert.That(result.ColorData[0], Is.EqualTo(0xBB));
  }
}
