using System;
using System.IO;
using FileFormat.Core;
using FileFormat.EccHeader;

namespace FileFormat.EccHeader.Tests;

[TestFixture]
public sealed class EccHeaderTests {

  private static RawImage _Picture(int width, int height) {
    var pixels = new byte[width * height * 3];
    for (var i = 0; i < width * height; ++i) {
      pixels[i * 3] = (byte)(i * 7);
      pixels[i * 3 + 1] = (byte)(i * 13);
      pixels[i * 3 + 2] = (byte)(i * 31);
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = pixels };
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => EccHeaderReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromBytes_NotEcch_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => EccHeaderReader.FromBytes(new byte[128]));

  [Test]
  [Category("Unit")]
  public void FromBytes_NoPng_ThrowsInvalidDataException() {
    var data = new byte[128];
    EccHeaderFile.Magic.CopyTo(data);
    data[EccHeaderFile.WidthAt] = 64;
    data[EccHeaderFile.HeightAt] = 32;

    Assert.Throws<InvalidDataException>(() => EccHeaderReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_SizeDisagreesWithThePng_ThrowsInvalidDataException() {
    // Taking the picture on its signature alone would draw whatever eight bytes happened to match.
    var data = EccHeaderWriter.ToBytes(EccHeaderFile.FromRawImage(_Picture(16, 8)));
    data[EccHeaderFile.WidthAt] = 99;

    Assert.Throws<InvalidDataException>(() => EccHeaderReader.FromBytes(data));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_TheSizeAndTheEveryPixelComeBack() {
    var source = _Picture(16, 8);

    var restored = EccHeaderReader.FromBytes(EccHeaderWriter.ToBytes(EccHeaderFile.FromRawImage(source)));
    var decoded = EccHeaderFile.ToRawImage(restored);

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(16));
      Assert.That(restored.Height, Is.EqualTo(8));
      Assert.That(PixelConverter.Convert(decoded, PixelFormat.Rgb24).PixelData, Is.EqualTo(source.PixelData));
    });
  }
}
