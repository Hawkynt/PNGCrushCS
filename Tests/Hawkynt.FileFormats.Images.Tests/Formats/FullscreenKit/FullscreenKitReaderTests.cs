using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.FullscreenKit;

namespace FileFormat.FullscreenKit.Tests;

[TestFixture]
public sealed class FullscreenKitReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => FullscreenKitReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => FullscreenKitReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".kid"));
    Assert.Throws<FileNotFoundException>(() => FullscreenKitReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => FullscreenKitReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[16];
    Assert.Throws<InvalidDataException>(() => FullscreenKitReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidSize_ThrowsInvalidDataException() {
    // 32 byte header + some random amount that matches neither variant
    var wrongSize = new byte[40000];
    Assert.Throws<InvalidDataException>(() => FullscreenKitReader.FromBytes(wrongSize));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_PrimarySize_ParsesAs416x274() {
    var data = new byte[FullscreenKitFile.PrimaryFileSize];
    var result = FullscreenKitReader.FromBytes(data);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(416));
      Assert.That(result.Height, Is.EqualTo(274));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_AlternateSize_ParsesAs448x272() {
    var data = new byte[FullscreenKitFile.AlternateFileSize];
    var result = FullscreenKitReader.FromBytes(data);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(448));
      Assert.That(result.Height, Is.EqualTo(272));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ParsesPalette() {
    var data = new byte[FullscreenKitFile.PrimaryFileSize];
    BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(0), 0x0777);
    BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(2), 0x0700);
    BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(30), 0x0007);

    var result = FullscreenKitReader.FromBytes(data);

    Assert.Multiple(() => {
      Assert.That(result.Palette[0], Is.EqualTo((short)0x0777));
      Assert.That(result.Palette[1], Is.EqualTo((short)0x0700));
      Assert.That(result.Palette[15], Is.EqualTo((short)0x0007));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_CopiesPixelData() {
    var data = new byte[FullscreenKitFile.PrimaryFileSize];
    data[32] = 0xAA;
    data[33] = 0xBB;

    var result = FullscreenKitReader.FromBytes(data);

    Assert.Multiple(() => {
      Assert.That(result.PixelData[0], Is.EqualTo(0xAA));
      Assert.That(result.PixelData[1], Is.EqualTo(0xBB));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidParsesCorrectly() {
    var data = new byte[FullscreenKitFile.PrimaryFileSize];
    using var ms = new MemoryStream(data);
    var result = FullscreenKitReader.FromStream(ms);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(416));
      Assert.That(result.Height, Is.EqualTo(274));
      Assert.That(result.Palette.Length, Is.EqualTo(16));
    });
  }
}
