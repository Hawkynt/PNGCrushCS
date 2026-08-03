using System;
using System.IO;
using FileFormat.Core;
using FileFormat.EggPaint;

namespace FileFormat.EggPaint.Tests;

/// <summary>
/// What a TruePaint picture is.
/// </summary>
/// <remarks>
/// These used to describe a Commodore 64 screen — a load address, a bitmap, a video matrix, a colour
/// RAM and a background in exactly 10003 bytes — because that is what the reader expected. No .trp is
/// anything of the kind, so the tests and the reader agreed with each other and with no real file.
/// </remarks>
[TestFixture]
public sealed class EggPaintReaderTests {

  /// <summary>Builds a picture: the magic, the two sizes, and one big-endian colour per pixel.</summary>
  private static byte[] _BuildValidFile(int width, int height, ushort fill) {
    var data = new byte[EggPaintFile.HeaderSize + width * height * 2];
    EggPaintFile.Magic.CopyTo(data);
    data[4] = (byte)(width >> 8);
    data[5] = (byte)width;
    data[6] = (byte)(height >> 8);
    data[7] = (byte)height;

    for (var i = 0; i < width * height; ++i) {
      data[EggPaintFile.HeaderSize + i * 2] = (byte)(fill >> 8);
      data[EggPaintFile.HeaderSize + i * 2 + 1] = (byte)fill;
    }

    return data;
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => EggPaintReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => EggPaintReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".trp"));
    Assert.Throws<FileNotFoundException>(() => EggPaintReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => EggPaintReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    Assert.Throws<InvalidDataException>(() => EggPaintReader.FromBytes(new byte[4]));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_WithoutTheMagic_ThrowsInvalidDataException() {
    Assert.Throws<InvalidDataException>(() => EggPaintReader.FromBytes(new byte[64]));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ShorterThanItsOwnSize_ThrowsInvalidDataException() {
    var truncated = _BuildValidFile(16, 16, 0)[..100];
    Assert.Throws<InvalidDataException>(() => EggPaintReader.FromBytes(truncated));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TakesTheSizeFromTheHeader() {
    var result = EggPaintReader.FromBytes(_BuildValidFile(320, 120, 0x1234));

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(320));
      Assert.That(result.Height, Is.EqualTo(120));
      Assert.That(result.PixelData.Length, Is.EqualTo(320 * 120 * 2));
    });
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_WidensEachFieldByRepeatingItsOwnBits() {
    // Five bits of red, six of green, five of blue. All ones must come out 255 rather than 248 or
    // 252, which is what dropping the low bits instead of repeating the high ones would give.
    var image = EggPaintFile.ToRawImage(EggPaintReader.FromBytes(_BuildValidFile(2, 2, 0xFFFF)));
    var rgb = PixelConverter.Convert(image, PixelFormat.Rgb24).PixelData;

    Assert.That(rgb[..3], Is.EqualTo(new byte[] { 255, 255, 255 }));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidParsesCorrectly() {
    using var ms = new MemoryStream(_BuildValidFile(64, 32, 0x0000));
    var result = EggPaintReader.FromStream(ms);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(64));
      Assert.That(result.Height, Is.EqualTo(32));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllFieldsPreserved() {
    var original = EggPaintReader.FromBytes(_BuildValidFile(40, 24, 0xABCD));
    var restored = EggPaintReader.FromBytes(EggPaintWriter.ToBytes(original));

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(original.Width));
      Assert.That(restored.Height, Is.EqualTo(original.Height));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    });
  }
}
