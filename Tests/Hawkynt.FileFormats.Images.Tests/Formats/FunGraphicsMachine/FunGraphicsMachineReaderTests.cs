using System;
using System.IO;
using FileFormat.FunGraphicsMachine;

namespace FileFormat.FunGraphicsMachine.Tests;

[TestFixture]
public sealed class FunGraphicsMachineReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => FunGraphicsMachineReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => FunGraphicsMachineReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".fgs"));
    Assert.Throws<FileNotFoundException>(() => FunGraphicsMachineReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => FunGraphicsMachineReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    Assert.Throws<InvalidDataException>(() => FunGraphicsMachineReader.FromBytes(new byte[100]));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_WrongSize_ThrowsInvalidDataException() {
    Assert.Throws<InvalidDataException>(() => FunGraphicsMachineReader.FromBytes(new byte[9010]));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidParsesCorrectly() {
    var data = new byte[FunGraphicsMachineFile.ExpectedFileSize];
    data[0] = 0x00;
    data[1] = 0x5C;

    var result = FunGraphicsMachineReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(320));
    Assert.That(result.Height, Is.EqualTo(200));
    Assert.That(result.LoadAddress, Is.EqualTo(0x5C00));
    Assert.That(result.ScreenRam.Length, Is.EqualTo(1000));
    Assert.That(result.BitmapData.Length, Is.EqualTo(8000));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_LoadAddress_ParsedAsLittleEndian() {
    var data = new byte[FunGraphicsMachineFile.ExpectedFileSize];
    data[0] = 0x00;
    data[1] = 0x5C;

    var result = FunGraphicsMachineReader.FromBytes(data);

    Assert.That(result.LoadAddress, Is.EqualTo(0x5C00));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ScreenRam_CopiedCorrectly() {
    var data = new byte[FunGraphicsMachineFile.ExpectedFileSize];
    data[2] = 0xAB;
    data[1001] = 0xCD;

    var result = FunGraphicsMachineReader.FromBytes(data);

    Assert.That(result.ScreenRam[0], Is.EqualTo(0xAB));
    Assert.That(result.ScreenRam[999], Is.EqualTo(0xCD));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidParsesCorrectly() {
    var data = new byte[FunGraphicsMachineFile.ExpectedFileSize];
    data[0] = 0x00;
    data[1] = 0x5C;

    using var ms = new MemoryStream(data);
    var result = FunGraphicsMachineReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(320));
    Assert.That(result.Height, Is.EqualTo(200));
    Assert.That(result.LoadAddress, Is.EqualTo(0x5C00));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllFieldsPreserved() {
    var screenRam = new byte[1000];
    for (var i = 0; i < screenRam.Length; ++i)
      screenRam[i] = (byte)(i % 256);

    var bitmapData = new byte[8000];
    for (var i = 0; i < bitmapData.Length; ++i)
      bitmapData[i] = (byte)(i * 7 % 256);

    var original = new FunGraphicsMachineFile {
      LoadAddress = 0x5C00,
      ScreenRam = screenRam,
      BitmapData = bitmapData,
    };

    var bytes = FunGraphicsMachineWriter.ToBytes(original);
    var restored = FunGraphicsMachineReader.FromBytes(bytes);

    Assert.That(restored.LoadAddress, Is.EqualTo(original.LoadAddress));
    Assert.That(restored.ScreenRam, Is.EqualTo(original.ScreenRam));
    Assert.That(restored.BitmapData, Is.EqualTo(original.BitmapData));
  }
}
