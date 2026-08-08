using System;
using FileFormat.Core;

namespace FileFormat.ZxSpectrum.Tests;

[TestFixture]
public sealed class FromRawImageTests {

  /// <summary>A picture that already obeys the two-colour-per-cell rule: every 8x8 cell is either
  /// solid black or solid white, alternating in a checkerboard of cells.</summary>
  private static RawImage _Checkerboard() {
    const int width = 256, height = 192;
    var data = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var cellOn = (((x / 8) + (y / 8)) & 1) != 0;
      var o = (y * width + x) * 3;
      var value = cellOn ? (byte)255 : (byte)0;
      data[o] = value;
      data[o + 1] = value;
      data[o + 2] = value;
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = data };
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_CheckerboardOfCells_ReproducesExactly() {
    var source = _Checkerboard();
    var file = ZxSpectrumFile.FromRawImage(source);
    var restored = ZxSpectrumReader.FromBytes(ZxSpectrumWriter.ToBytes(file));
    var decoded = ZxSpectrumFile.ToRawImage(restored);

    Assert.That(decoded.PixelData, Is.EqualTo(source.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_RejectsWrongDimensions() {
    var raw = new RawImage { Width = 100, Height = 100, Format = PixelFormat.Rgb24, PixelData = new byte[100 * 100 * 3] };

    Assert.Throws<ArgumentException>(() => ZxSpectrumFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_SolidColourFile_HasNoSetBits() {
    const int width = 256, height = 192;
    var data = new byte[width * height * 3];
    for (var i = 0; i < width * height; ++i) {
      data[i * 3] = 0xCD;
    }

    var source = new RawImage { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = data };
    var file = ZxSpectrumFile.FromRawImage(source);

    foreach (var b in file.BitmapData)
      Assert.That(b, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_ThenToBytes_ProducesTheStandardFileSize()
    => Assert.That(ZxSpectrumWriter.ToBytes(ZxSpectrumFile.FromRawImage(_Checkerboard())).Length, Is.EqualTo(6912));
}
