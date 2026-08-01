using System;
using System.IO;
using FileFormat.Ipl;
using FileFormat.Core;

namespace FileFormat.Ipl.Tests;

[TestFixture]
public class IplReaderTests {

  [Test]
  public void FromFile_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => IplReader.FromFile(null!));

  [Test]
  public void FromFile_MissingFile_ThrowsFileNotFoundException()
    => Assert.Throws<FileNotFoundException>(() => IplReader.FromFile(new FileInfo("nonexistent.bin")));

  [Test]
  public void FromBytes_NullData_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => IplReader.FromBytes(null!));

  [Test]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => IplReader.FromBytes(new byte[15]));

  [Test]
  public void FromBytes_WrongMagic_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => IplReader.FromBytes(new byte[64]));

  [Test]
  public void FromBytes_ValidHeader_Succeeds() {
    // Built the way the format states rather than by hand: the tags identify it, and the sizes sit
    // behind them at fixed offsets.
    var file = IplFile.FromRawImage(new() {
      Width = 320, Height = 240, Format = PixelFormat.Rgb24, PixelData = new byte[320 * 240 * 3],
    });

    var result = IplReader.FromBytes(IplWriter.ToBytes(file));

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(320));
      Assert.That(result.Height, Is.EqualTo(240));
      Assert.That(result.Channels, Is.EqualTo(3));
      Assert.That(result.SampleBits, Is.EqualTo(8));
    });
  }

  [Test]
  public void FromStream_NullStream_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => IplReader.FromStream(null!));
}

[TestFixture]
public class RoundTripTests {

  [Test]
  public void RoundTrip_PixelDataPreserved() {
    var file = new IplFile {
      Width = 320,
      Height = 240,
      Channels = 3,
      SampleBits = 8,
      PixelData = new byte[320 * 240 * 3],
    };
    for (var i = 0; i < file.PixelData.Length; ++i)
      file.PixelData[i] = (byte)(i & 0xFF);
    var bytes = IplWriter.ToBytes(file);
    var file2 = IplReader.FromBytes(bytes);
    Assert.That(file2.PixelData, Is.EqualTo(file.PixelData));
  }

  [Test]
  public void RoundTrip_ViaRawImage() {
    var file = new IplFile {
      Width = 320,
      Height = 240,
      Channels = 3,
      SampleBits = 8,
      PixelData = new byte[320 * 240 * 3],
    };
    var raw = IplFile.ToRawImage(file);
    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Rgb24));
    var file2 = IplFile.FromRawImage(raw);
    Assert.That(file2.PixelData, Is.EqualTo(file.PixelData));
  }
}

