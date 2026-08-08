using System;
using FileFormat.Core;

namespace FileFormat.ZxUlaPlus.Tests;

[TestFixture]
public sealed class FromRawImageTests {

  /// <summary>Pure black and white survive the 3-3-2 GRB palette encoding exactly (0 and max in every
  /// channel), so a checkerboard built from them round-trips bit-exactly.</summary>
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
    var file = ZxUlaPlusFile.FromRawImage(source);
    var restored = ZxUlaPlusReader.FromBytes(ZxUlaPlusWriter.ToBytes(file));
    var decoded = ZxUlaPlusFile.ToRawImage(restored);

    Assert.That(decoded.PixelData, Is.EqualTo(source.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_RejectsWrongDimensions() {
    var raw = new RawImage { Width = 100, Height = 100, Format = PixelFormat.Rgb24, PixelData = new byte[100 * 100 * 3] };

    Assert.Throws<ArgumentException>(() => ZxUlaPlusFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_MirrorsThePaletteAcrossAllFourGroups() {
    var file = ZxUlaPlusFile.FromRawImage(_Checkerboard());

    for (var i = 0; i < 16; ++i) {
      Assert.That(file.PaletteData[i + 16], Is.EqualTo(file.PaletteData[i]));
      Assert.That(file.PaletteData[i + 32], Is.EqualTo(file.PaletteData[i]));
      Assert.That(file.PaletteData[i + 48], Is.EqualTo(file.PaletteData[i]));
    }
  }
}
