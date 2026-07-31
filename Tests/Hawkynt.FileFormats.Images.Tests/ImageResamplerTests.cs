using System;
using FileFormat.Core;

namespace Hawkynt.FileFormats.Images.Tests;

/// <summary>
/// The resampler that replaced GDI+, which is the part of a resize nobody notices until it is wrong.
/// </summary>
/// <remarks>
/// These check the properties a scaler must have rather than exact output. Comparing against
/// specific bytes would pin the kernel rather than the behaviour, and the point of doing this
/// ourselves was that the result stops depending on whichever graphics library is installed.
/// </remarks>
[TestFixture]
public sealed class ImageResamplerTests {

  private static readonly Resampling[] _All = [Resampling.Nearest, Resampling.Bilinear, Resampling.Bicubic];

  /// <summary>Scaling to the size it already is must change nothing.</summary>
  [TestCaseSource(nameof(_All))]
  [Category("Unit")]
  public void SameSize_ReturnsThePictureUnchanged(Resampling kind) {
    var source = _Checkerboard(16, 12);

    var actual = ImageResampler.Resample(source, 16, 12, kind);

    Assert.That(actual.PixelData, Is.EqualTo(source.PixelData), $"{kind} altered a picture it did not resize");
  }

  /// <summary>
  /// A picture of one colour has to stay that colour at any size, under any kernel.
  /// </summary>
  /// <remarks>
  /// This is the check that catches a spline overshooting: Catmull-Rom can swing past both ends of
  /// its range, so an unclamped bicubic turns the edge of a flat area into a bright or dark rim
  /// that was never in the source.
  /// </remarks>
  [TestCaseSource(nameof(_All))]
  [Category("Unit")]
  public void SolidColor_StaysSolidAtEverySize(Resampling kind) {
    var source = _Solid(9, 7, 30, 200, 120, 255);

    foreach (var (width, height) in new[] { (3, 2), (9, 7), (40, 33), (1, 1) }) {
      var actual = ImageResampler.Resample(source, width, height, kind);

      for (var i = 0; i < actual.PixelData.Length; i += 4) {
        if (actual.PixelData[i] == 30 && actual.PixelData[i + 1] == 200
            && actual.PixelData[i + 2] == 120 && actual.PixelData[i + 3] == 255)
          continue;

        Assert.Fail(
          $"{kind} at {width}x{height}: pixel {i / 4} came out "
          + $"({actual.PixelData[i + 2]},{actual.PixelData[i + 1]},{actual.PixelData[i]},{actual.PixelData[i + 3]})");
      }
    }
  }

  [TestCaseSource(nameof(_All))]
  [Category("Unit")]
  public void Resample_ReturnsTheSizeAskedFor(Resampling kind) {
    var source = _Checkerboard(13, 5);

    var actual = ImageResampler.Resample(source, 40, 31, kind);

    Assert.Multiple(() => {
      Assert.That(actual.Width, Is.EqualTo(40));
      Assert.That(actual.Height, Is.EqualTo(31));
      Assert.That(actual.PixelData, Has.Length.EqualTo(40 * 31 * 4));
      Assert.That(actual.Format, Is.EqualTo(PixelFormat.Bgra32));
    });
  }

  /// <summary>Doubling by nearest neighbour gives each source pixel a two-by-two block, exactly.</summary>
  /// <remarks>
  /// Nearest is the one kernel whose output is fully determined, so it is the one that can pin the
  /// sampling grid. An off-by-half-pixel error in the mapping shows here as a shifted picture.
  /// </remarks>
  [Test]
  [Category("Unit")]
  public void Nearest_DoublingRepeatsEachPixelExactly() {
    var source = _Checkerboard(4, 4);

    var actual = ImageResampler.Resample(source, 8, 8, Resampling.Nearest);

    for (var y = 0; y < 8; ++y)
    for (var x = 0; x < 8; ++x) {
      var from = ((y / 2) * 4 + x / 2) * 4;
      var to = (y * 8 + x) * 4;

      if (actual.PixelData[to] == source.PixelData[from]
          && actual.PixelData[to + 1] == source.PixelData[from + 1]
          && actual.PixelData[to + 2] == source.PixelData[from + 2])
        continue;

      Assert.Fail($"pixel {x},{y} did not come from source pixel {x / 2},{y / 2}");
    }
  }

  [Test]
  [Category("Unit")]
  public void ZeroOrNegativeSize_Throws() {
    var source = _Solid(4, 4, 0, 0, 0, 255);

    Assert.Multiple(() => {
      Assert.Throws<ArgumentOutOfRangeException>(() => ImageResampler.Resample(source, 0, 4, Resampling.Bilinear));
      Assert.Throws<ArgumentOutOfRangeException>(() => ImageResampler.Resample(source, 4, -1, Resampling.Bilinear));
    });
  }

  private static RawImage _Solid(int width, int height, byte b, byte g, byte r, byte a) {
    var pixels = new byte[width * height * 4];
    for (var i = 0; i < pixels.Length; i += 4) {
      pixels[i] = b;
      pixels[i + 1] = g;
      pixels[i + 2] = r;
      pixels[i + 3] = a;
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Bgra32, PixelData = pixels };
  }

  private static RawImage _Checkerboard(int width, int height) {
    var pixels = new byte[width * height * 4];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var at = (y * width + x) * 4;
      var on = (x + y) % 2 == 0;
      pixels[at] = (byte)(on ? 240 : 10);
      pixels[at + 1] = (byte)(on ? 20 : 200);
      pixels[at + 2] = (byte)(on ? 130 : 60);
      pixels[at + 3] = 255;
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Bgra32, PixelData = pixels };
  }
}
