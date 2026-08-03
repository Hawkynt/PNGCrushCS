using System;
using System.IO;
using FileFormat.Vidcom64;

namespace FileFormat.Vidcom64.Tests;

[TestFixture]
public sealed class Vidcom64ReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => Vidcom64Reader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => Vidcom64Reader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".vid"));
    Assert.Throws<FileNotFoundException>(() => Vidcom64Reader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => Vidcom64Reader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    Assert.Throws<InvalidDataException>(() => Vidcom64Reader.FromBytes(new byte[100]));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_WrongSize_ThrowsInvalidDataException() {
    Assert.Throws<InvalidDataException>(() => Vidcom64Reader.FromBytes(new byte[10051]));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidParsesCorrectly() {
    var data = new byte[Vidcom64File.ExpectedFileSize];
    data[0] = 0x00;
    data[1] = 0x58;
    // Header starts at offset 2, bitmap at 49, screen at 8049, color at 9049, bg at 10049
    data[10049] = 0x06;

    var result = Vidcom64Reader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(160));
    Assert.That(result.Height, Is.EqualTo(200));
    Assert.That(result.LoadAddress, Is.EqualTo(0x5800));
    Assert.That(result.HeaderData.Length, Is.EqualTo(24));
    Assert.That(result.BitmapData.Length, Is.EqualTo(8000));
    Assert.That(result.ScreenRam.Length, Is.EqualTo(1000));
    Assert.That(result.ColorRam.Length, Is.EqualTo(1000));
    Assert.That(result.BackgroundColor, Is.EqualTo(0x00));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ReadsTheSectionsInTheOrderTheFileHoldsThem() {
    // Colour RAM straight after the load address, screen RAM a kilobyte on, bitmap a kilobyte after
    // that and running to the end. This used to expect a 47-byte header and then bitmap, screen,
    // colour, which is no real file — the padding between the first two sections is what the header
    // field now holds.
    var data = new byte[Vidcom64File.ExpectedFileSize];
    data[2] = 0xAA;                                        // first byte of colour RAM
    data[2 + 1000] = 0xBB;                                 // first byte of the padding after it
    data[2 + 1024] = 0xCC;                                 // first byte of screen RAM
    data[2 + 1024 + 1024] = 0xDD;                          // first byte of the bitmap
    data[Vidcom64File.ExpectedFileSize - 1] = 0xEE;        // and its last

    var result = Vidcom64Reader.FromBytes(data);

    Assert.Multiple(() => {
      Assert.That(result.ColorRam[0], Is.EqualTo(0xAA));
      Assert.That(result.HeaderData[0], Is.EqualTo(0xBB));
      Assert.That(result.ScreenRam[0], Is.EqualTo(0xCC));
      Assert.That(result.BitmapData[0], Is.EqualTo(0xDD));
      Assert.That(result.BitmapData[^1], Is.EqualTo(0xEE));
    });
  }
}
