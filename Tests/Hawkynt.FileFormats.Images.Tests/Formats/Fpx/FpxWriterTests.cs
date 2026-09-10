using System;
using System.Text;
using FileFormat.Fpx;

namespace FileFormat.Fpx.Tests;

[TestFixture]
public sealed class FpxWriterTests {

  private static readonly Guid _ImageViewClass = new("56616700-C154-11CE-8553-00AA00A1F95B");

  [Test]
  [Category("Unit")]
  public void ToBytes_RoundTripsSingleTileLosslessly() {
    var expected = new FpxFile {
      Width = 3,
      Height = 2,
      PixelData = [
        0, 1, 2, 10, 20, 30, 40, 50, 60,
        70, 80, 90, 100, 110, 120, 250, 240, 230,
      ],
    };

    var bytes = FpxWriter.ToBytes(expected);
    var actual = FpxReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(actual.Width, Is.EqualTo(expected.Width));
      Assert.That(actual.Height, Is.EqualTo(expected.Height));
      Assert.That(actual.PixelData, Is.EqualTo(expected.PixelData));
    });
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_RoundTripsAcrossTileBoundariesLosslessly() {
    const int width = 65;
    const int height = 65;
    var pixels = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var at = (y * width + x) * 3;
      pixels[at] = (byte)(x * 3 + y);
      pixels[at + 1] = (byte)(x + y * 5);
      pixels[at + 2] = (byte)(x * 7 + y * 11);
    }

    var expected = new FpxFile { Width = width, Height = height, PixelData = pixels };
    var actual = FpxReader.FromBytes(FpxWriter.ToBytes(expected));

    Assert.That(actual.PixelData, Is.EqualTo(expected.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_UsesNormativeNonHierarchicalResolutionNumber() {
    var file = new FpxFile {
      Width = 129,
      Height = 1,
      PixelData = new byte[129 * 3],
    };

    var bytes = FpxWriter.ToBytes(file);

    Assert.That(bytes.AsSpan().IndexOf(Encoding.Unicode.GetBytes("Resolution 0002\0")), Is.GreaterThanOrEqualTo(0));
    Assert.That(bytes.AsSpan().IndexOf(Encoding.Unicode.GetBytes("Resolution 0001\0")), Is.LessThan(0));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_WritesFlashPixImageViewRootClassId() {
    var bytes = FpxWriter.ToBytes(new FpxFile { Width = 1, Height = 1, PixelData = [1, 2, 3] });
    var rootAt = bytes.AsSpan().IndexOf(Encoding.Unicode.GetBytes("Root Entry\0"));

    Assert.That(rootAt, Is.GreaterThanOrEqualTo(0));
    Assert.That(new Guid(bytes.AsSpan(rootAt + 80, 16)), Is.EqualTo(_ImageViewClass));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_IsDeterministic() {
    var file = new FpxFile {
      Width = 2,
      Height = 2,
      PixelData = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12],
    };

    Assert.That(FpxWriter.ToBytes(file), Is.EqualTo(FpxWriter.ToBytes(file)));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_InvalidPixelLength_ThrowsArgumentException() {
    var file = new FpxFile { Width = 2, Height = 2, PixelData = [1, 2, 3] };

    Assert.Throws<ArgumentException>(() => FpxWriter.ToBytes(file));
  }
}
