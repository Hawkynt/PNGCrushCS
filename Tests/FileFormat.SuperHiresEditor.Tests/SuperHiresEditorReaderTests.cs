using System;
using System.IO;
using FileFormat.SuperHiresEditor;

namespace FileFormat.SuperHiresEditor.Tests;

[TestFixture]
public sealed class SuperHiresEditorReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => SuperHiresEditorReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => SuperHiresEditorReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".she"));
    Assert.Throws<FileNotFoundException>(() => SuperHiresEditorReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => SuperHiresEditorReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[100];
    Assert.Throws<InvalidDataException>(() => SuperHiresEditorReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidFile_ParsesLoadAddress() {
    var data = _BuildValidFile(0x2000);
    var result = SuperHiresEditorReader.FromBytes(data);

    Assert.That(result.LoadAddress, Is.EqualTo(0x2000));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidFile_ParsesBitmap1() {
    var data = _BuildValidFile(0x2000);
    data[2] = 0xAB;
    data[8001] = 0xCD;

    var result = SuperHiresEditorReader.FromBytes(data);

    Assert.That(result.Bitmap1.Length, Is.EqualTo(8000));
    Assert.That(result.Bitmap1[0], Is.EqualTo(0xAB));
    Assert.That(result.Bitmap1[7999], Is.EqualTo(0xCD));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidFile_ParsesScreen1() {
    var data = _BuildValidFile(0x2000);
    var result = SuperHiresEditorReader.FromBytes(data);

    Assert.That(result.Screen1.Length, Is.EqualTo(1000));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidFile_ParsesBitmap2() {
    var data = _BuildValidFile(0x2000);
    var result = SuperHiresEditorReader.FromBytes(data);

    Assert.That(result.Bitmap2.Length, Is.EqualTo(8000));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidFile_ParsesScreen2() {
    var data = _BuildValidFile(0x2000);
    var result = SuperHiresEditorReader.FromBytes(data);

    Assert.That(result.Screen2.Length, Is.EqualTo(1000));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_LoadAddress_ParsedAsLittleEndian() {
    var data = new byte[SuperHiresEditorFile.LoadAddressSize + SuperHiresEditorFile.MinPayloadSize];
    data[0] = 0x00;
    data[1] = 0x20; // 0x2000 LE
    var result = SuperHiresEditorReader.FromBytes(data);

    Assert.That(result.LoadAddress, Is.EqualTo(0x2000));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidFile_ParsesCorrectly() {
    var data = _BuildValidFile(0x4000);
    using var ms = new MemoryStream(data);
    var result = SuperHiresEditorReader.FromStream(ms);

    Assert.That(result.LoadAddress, Is.EqualTo(0x4000));
    Assert.That(result.Bitmap1.Length, Is.EqualTo(8000));
    Assert.That(result.Screen1.Length, Is.EqualTo(1000));
    Assert.That(result.Bitmap2.Length, Is.EqualTo(8000));
    Assert.That(result.Screen2.Length, Is.EqualTo(1000));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ExactMinSize_NoTrailingData() {
    var data = new byte[SuperHiresEditorFile.LoadAddressSize + SuperHiresEditorFile.MinPayloadSize];
    var result = SuperHiresEditorReader.FromBytes(data);

    Assert.That(result.TrailingData.Length, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ExtraBytes_PreservedAsTrailingData() {
    var data = new byte[SuperHiresEditorFile.LoadAddressSize + SuperHiresEditorFile.MinPayloadSize + 5];
    data[^1] = 0xFF;
    var result = SuperHiresEditorReader.FromBytes(data);

    Assert.That(result.TrailingData.Length, Is.EqualTo(5));
    Assert.That(result.TrailingData[4], Is.EqualTo(0xFF));
  }

  private static byte[] _BuildValidFile(ushort loadAddress) {
    var data = new byte[SuperHiresEditorFile.LoadAddressSize + SuperHiresEditorFile.MinPayloadSize];
    data[0] = (byte)(loadAddress & 0xFF);
    data[1] = (byte)(loadAddress >> 8);

    for (var i = 0; i < SuperHiresEditorFile.MinPayloadSize; ++i)
      data[SuperHiresEditorFile.LoadAddressSize + i] = (byte)(i % 256);

    return data;
  }
}
