using System;
using FileFormat.Core;

namespace FileFormat.Iwc.Tests;

/// <summary>
/// The writer quantises every band with step 1 whenever its values fit 253 steps, so the only loss on
/// ordinary pictures is the integer YCbCr the applet converts back: within 3 per sample on the test
/// pictures. Grey pictures need no colour difference at all and come back exactly.
/// </summary>
[TestFixture]
public sealed class IwcRoundTripTests {

  private const int _BOUND = 3;

  private static RawImage _Picture(int width, int height) {
    var pixels = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x)
        for (var c = 0; c < 3; ++c)
          pixels[(y * width + x) * 3 + c] = (byte)(128 + 100 * Math.Sin(x * 0.19 + y * 0.07 + c * 2.0));

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = pixels };
  }

  [TestCase(2, 2)]
  [TestCase(3, 2)]
  [TestCase(31, 17)]
  [TestCase(64, 48)]
  [TestCase(257, 5)]
  [Category("Integration")]
  public void RawImage_RoundTripsWithinTheBound(int width, int height) {
    var source = _Picture(width, height);
    var restored = IwcFile.ToRawImage(IwcReader.FromSpan(IwcWriter.ToBytes(IwcFile.FromRawImage(source))));

    Assert.Multiple(() => {
      Assert.That(restored.Format, Is.EqualTo(PixelFormat.Rgb24));
      Assert.That((restored.Width, restored.Height), Is.EqualTo((width, height)));
      Assert.That(IwcTestSupport.MaxDifference(restored.PixelData, source.PixelData), Is.LessThanOrEqualTo(_BOUND));
    });
  }

  [Test]
  [Category("Integration")]
  public void HeaderFields_SurviveReadWriteRead() {
    var file = IwcFile.FromRawImage(_Picture(40, 30)) with { Levels = 2, Quality = 40 };
    var first = IwcReader.FromSpan(IwcWriter.ToBytes(file));
    var second = IwcReader.FromSpan(IwcWriter.ToBytes(first));

    Assert.Multiple(() => {
      foreach (var read in new[] { first, second }) {
        Assert.That(read.Levels, Is.EqualTo(2));
        Assert.That(read.Quality, Is.EqualTo(40));
      }
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_WidensToRgb() {
    var grey = new RawImage { Width = 2, Height = 1, Format = PixelFormat.Gray8, PixelData = [10, 200] };
    var file = IwcFile.FromRawImage(grey);
    Assert.Multiple(() => {
      Assert.That(file.PixelData, Is.EqualTo(new byte[] { 10, 10, 10, 200, 200, 200 }));
      Assert.That(file.Quality, Is.EqualTo(IwcFile.DefaultQuality));
      Assert.That(file.Levels, Is.Zero);
    });
  }
}
