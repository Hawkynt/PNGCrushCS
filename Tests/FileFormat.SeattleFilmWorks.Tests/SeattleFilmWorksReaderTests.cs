using System;
using System.IO;
using FileFormat.SeattleFilmWorks;

namespace FileFormat.SeattleFilmWorks.Tests;

[TestFixture]
public sealed class SeattleFilmWorksReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => SeattleFilmWorksReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => SeattleFilmWorksReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".sfw"));
    Assert.Throws<FileNotFoundException>(() => SeattleFilmWorksReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => SeattleFilmWorksReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[5];
    Assert.Throws<InvalidDataException>(() => SeattleFilmWorksReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidMagic_ThrowsInvalidDataException() {
    var data = new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xFF, 0xD8 };
    Assert.Throws<InvalidDataException>(() => SeattleFilmWorksReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_NoJpegSoi_ThrowsInvalidDataException() {
    // Valid SFW94A magic but no JPEG SOI marker after it
    var data = new byte[] { 0x53, 0x46, 0x57, 0x39, 0x34, 0x41, 0x00, 0x00 };
    Assert.Throws<InvalidDataException>(() => SeattleFilmWorksReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidSfw94A_ExtractsJpegData() {
    // SFW94A + minimal JPEG (SOI + some data)
    var data = new byte[] {
      0x53, 0x46, 0x57, 0x39, 0x34, 0x41, // "SFW94A"
      0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10   // JPEG SOI + JFIF APP0 start
    };

    var result = SeattleFilmWorksReader.FromBytes(data);

    Assert.That(result.JpegData.Length, Is.EqualTo(6));
    Assert.That(result.JpegData[0], Is.EqualTo(0xFF));
    Assert.That(result.JpegData[1], Is.EqualTo(0xD8));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidSfw95A_ExtractsJpegData() {
    // SFW95A (PWP) + minimal JPEG
    var data = new byte[] {
      0x53, 0x46, 0x57, 0x39, 0x35, 0x41, // "SFW95A"
      0xFF, 0xD8, 0xFF, 0xE0               // JPEG SOI + APP0 marker
    };

    var result = SeattleFilmWorksReader.FromBytes(data);

    Assert.That(result.JpegData.Length, Is.EqualTo(4));
    Assert.That(result.JpegData[0], Is.EqualTo(0xFF));
    Assert.That(result.JpegData[1], Is.EqualTo(0xD8));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ExtraBytesBeforeJpeg_SkipsToSoi() {
    // SFW94A + 24 extra bytes + JPEG SOI
    var data = new byte[6 + 24 + 4];
    // Magic
    data[0] = 0x53; data[1] = 0x46; data[2] = 0x57;
    data[3] = 0x39; data[4] = 0x34; data[5] = 0x41;
    // 24 extra bytes (all zeros)
    // JPEG SOI at offset 30
    data[30] = 0xFF; data[31] = 0xD8; data[32] = 0xFF; data[33] = 0xE0;

    var result = SeattleFilmWorksReader.FromBytes(data);

    Assert.That(result.JpegData[0], Is.EqualTo(0xFF));
    Assert.That(result.JpegData[1], Is.EqualTo(0xD8));
    Assert.That(result.JpegData.Length, Is.EqualTo(4));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidSfw_Parses() {
    var data = new byte[] {
      0x53, 0x46, 0x57, 0x39, 0x34, 0x41, // "SFW94A"
      0xFF, 0xD8, 0xFF, 0xD9               // JPEG SOI + EOI
    };

    using var ms = new MemoryStream(data);
    var result = SeattleFilmWorksReader.FromStream(ms);

    Assert.That(result.JpegData.Length, Is.EqualTo(4));
    Assert.That(result.JpegData[0], Is.EqualTo(0xFF));
    Assert.That(result.JpegData[1], Is.EqualTo(0xD8));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_JpegDataIsIndependentCopy() {
    var data = new byte[] {
      0x53, 0x46, 0x57, 0x39, 0x34, 0x41,
      0xFF, 0xD8, 0xFF, 0xD9
    };

    var result = SeattleFilmWorksReader.FromBytes(data);
    data[7] = 0x00; // mutate original

    Assert.That(result.JpegData[1], Is.EqualTo(0xD8));
  }
}
