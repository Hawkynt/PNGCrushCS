using System;
using System.IO;
using FileFormat.C64Multi;

namespace FileFormat.C64Multi.Tests;

[TestFixture]
public sealed class C64MultiReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => C64MultiReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => C64MultiReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".ocp"));
    Assert.Throws<FileNotFoundException>(() => C64MultiReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => C64MultiReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[100];
    Assert.Throws<InvalidDataException>(() => C64MultiReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidHires_ParsesCorrectly() {
    var data = _BuildValidHiresFile(0x2000, 0x0E);
    var result = C64MultiReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(320));
    Assert.That(result.Height, Is.EqualTo(200));
    Assert.That(result.Format, Is.EqualTo(C64MultiFormat.ArtStudioHires));
    Assert.That(result.LoadAddress, Is.EqualTo(0x2000));
    Assert.That(result.BitmapData.Length, Is.EqualTo(8000));
    Assert.That(result.ScreenData.Length, Is.EqualTo(1000));
    Assert.That(result.ColorData, Is.Null);
    Assert.That(result.BackgroundColor, Is.EqualTo(0x0E));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidMulticolor_ParsesCorrectly() {
    var data = _BuildValidMulticolorFile(0x4000, 0x03);
    var result = C64MultiReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(160));
    Assert.That(result.Height, Is.EqualTo(200));
    Assert.That(result.Format, Is.EqualTo(C64MultiFormat.ArtStudioMulti));
    Assert.That(result.LoadAddress, Is.EqualTo(0x4000));
    Assert.That(result.BitmapData.Length, Is.EqualTo(8000));
    Assert.That(result.ScreenData.Length, Is.EqualTo(1000));
    Assert.That(result.ColorData, Is.Not.Null);
    Assert.That(result.ColorData!.Length, Is.EqualTo(1000));
    Assert.That(result.BackgroundColor, Is.EqualTo(0x03));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_LoadAddress_ParsedAsLittleEndian() {
    var data = new byte[C64MultiFile.ArtStudioHiresFileSize];
    data[0] = 0x00;
    data[1] = 0x20; // 0x2000 LE
    var result = C64MultiReader.FromBytes(data);

    Assert.That(result.LoadAddress, Is.EqualTo(0x2000));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_BitmapData_CopiedCorrectly() {
    var data = _BuildValidHiresFile(0x2000, 0x00);
    data[2] = 0xAB;
    data[8001] = 0xCD;

    var result = C64MultiReader.FromBytes(data);

    Assert.That(result.BitmapData[0], Is.EqualTo(0xAB));
    Assert.That(result.BitmapData[7999], Is.EqualTo(0xCD));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidParsesCorrectly() {
    var data = _BuildValidMulticolorFile(0x4000, 0x05);
    using var ms = new MemoryStream(data);
    var result = C64MultiReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(160));
    Assert.That(result.Height, Is.EqualTo(200));
    Assert.That(result.LoadAddress, Is.EqualTo(0x4000));
    Assert.That(result.BackgroundColor, Is.EqualTo(0x05));
  }

  private static byte[] _BuildValidHiresFile(ushort loadAddress, byte borderColor) {
    var data = new byte[C64MultiFile.ArtStudioHiresFileSize];
    data[0] = (byte)(loadAddress & 0xFF);
    data[1] = (byte)(loadAddress >> 8);

    for (var i = 0; i < 8000; ++i)
      data[2 + i] = (byte)(i % 256);

    for (var i = 0; i < 1000; ++i)
      data[8002 + i] = (byte)(i % 256);

    data[9002] = borderColor;

    return data;
  }

  private static byte[] _BuildValidMulticolorFile(ushort loadAddress, byte backgroundColor) {
    var data = new byte[C64MultiFile.ArtStudioMultiFileSize];
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
