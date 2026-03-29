using System;
using System.IO;
using FileFormat.Jpeg2000;

namespace FileFormat.Jpeg2000.Tests;

[TestFixture]
public sealed class Jpeg2000ReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => Jpeg2000Reader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => Jpeg2000Reader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".jp2"));
    Assert.Throws<FileNotFoundException>(() => Jpeg2000Reader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => Jpeg2000Reader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[5];
    Assert.Throws<InvalidDataException>(() => Jpeg2000Reader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidSignature_ThrowsInvalidDataException() {
    var bad = new byte[20];
    bad[0] = 0xDE;
    bad[1] = 0xAD;
    Assert.Throws<InvalidDataException>(() => Jpeg2000Reader.FromBytes(bad));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidJp2Rgb_ParsesDimensions() {
    var jp2 = _BuildMinimalJp2(4, 3, 3);
    var result = Jpeg2000Reader.FromBytes(jp2);

    Assert.That(result.Width, Is.EqualTo(4));
    Assert.That(result.Height, Is.EqualTo(3));
    Assert.That(result.ComponentCount, Is.EqualTo(3));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidJp2Grayscale_ParsesComponentCount() {
    var jp2 = _BuildMinimalJp2(2, 2, 1);
    var result = Jpeg2000Reader.FromBytes(jp2);

    Assert.That(result.ComponentCount, Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidJp2_ParsesCorrectly() {
    var jp2 = _BuildMinimalJp2(3, 2, 3);
    using var ms = new MemoryStream(jp2);
    var result = Jpeg2000Reader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(3));
    Assert.That(result.Height, Is.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidJp2_BitsPerComponentIs8() {
    var jp2 = _BuildMinimalJp2(2, 2, 3);
    var result = Jpeg2000Reader.FromBytes(jp2);

    Assert.That(result.BitsPerComponent, Is.EqualTo(8));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidJp2_DecompositionLevelsPreserved() {
    var file = new Jpeg2000File {
      Width = 4,
      Height = 4,
      ComponentCount = 1,
      BitsPerComponent = 8,
      DecompositionLevels = 2,
      PixelData = new byte[4 * 4],
    };
    var bytes = Jpeg2000Writer.ToBytes(file);
    var result = Jpeg2000Reader.FromBytes(bytes);

    Assert.That(result.DecompositionLevels, Is.EqualTo(2));
  }

  private static byte[] _BuildMinimalJp2(int width, int height, int componentCount) {
    var pixelData = new byte[width * height * componentCount];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i % 256);

    var file = new Jpeg2000File {
      Width = width,
      Height = height,
      ComponentCount = componentCount,
      BitsPerComponent = 8,
      PixelData = pixelData,
    };

    return Jpeg2000Writer.ToBytes(file);
  }
}
