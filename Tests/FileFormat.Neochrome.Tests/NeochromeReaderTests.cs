using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Neochrome;

namespace FileFormat.Neochrome.Tests;

[TestFixture]
public sealed class NeochromeReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => NeochromeReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => NeochromeReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".neo"));
    Assert.Throws<FileNotFoundException>(() => NeochromeReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => NeochromeReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[64];
    Assert.Throws<InvalidDataException>(() => NeochromeReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidParsesCorrectly() {
    var data = _BuildMinimalNeochrome();
    var result = NeochromeReader.FromBytes(data);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(320));
      Assert.That(result.Height, Is.EqualTo(200));
      Assert.That(result.Palette.Length, Is.EqualTo(16));
      Assert.That(result.PixelData.Length, Is.EqualTo(32000));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ParsesPalette() {
    var data = _BuildMinimalNeochrome();
    // Set palette entry 0 to 0x0777 (white in Atari ST) at offset 4
    BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(4), 0x0777);
    // Set palette entry 1 to 0x0700 (red in Atari ST) at offset 6
    BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(6), 0x0700);

    var result = NeochromeReader.FromBytes(data);

    Assert.Multiple(() => {
      Assert.That(result.Palette[0], Is.EqualTo((short)0x0777));
      Assert.That(result.Palette[1], Is.EqualTo((short)0x0700));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ParsesAnimationFields() {
    var data = _BuildMinimalNeochrome();
    data[36] = 5;  // AnimSpeed
    data[37] = 1;  // AnimDirection
    BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(38), 10); // AnimSteps
    BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(40), 16); // AnimXOffset
    BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(42), 32); // AnimYOffset
    BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(44), 64); // AnimWidth
    BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(46), 48); // AnimHeight

    var result = NeochromeReader.FromBytes(data);

    Assert.Multiple(() => {
      Assert.That(result.AnimSpeed, Is.EqualTo(5));
      Assert.That(result.AnimDirection, Is.EqualTo(1));
      Assert.That(result.AnimSteps, Is.EqualTo(10));
      Assert.That(result.AnimXOffset, Is.EqualTo(16));
      Assert.That(result.AnimYOffset, Is.EqualTo(32));
      Assert.That(result.AnimWidth, Is.EqualTo(64));
      Assert.That(result.AnimHeight, Is.EqualTo(48));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidParsesCorrectly() {
    var data = _BuildMinimalNeochrome();
    using var ms = new MemoryStream(data);
    var result = NeochromeReader.FromStream(ms);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(320));
      Assert.That(result.Height, Is.EqualTo(200));
    });
  }

  private static byte[] _BuildMinimalNeochrome() {
    var data = new byte[32128];
    // Flag = 0, Resolution = 0 (already zero)
    // Fill pixel data with a recognizable pattern
    for (var i = 0; i < 32000; ++i)
      data[128 + i] = (byte)(i * 7 % 256);

    return data;
  }
}
