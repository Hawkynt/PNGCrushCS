using System;
using FileFormat.Core;

namespace FileFormat.Wic.Tests;

/// <summary>
/// JWIC is lossy several times over: the encoder truncates every wavelet coefficient to an integer,
/// the decoder truncates its single-precision reconstruction to a byte, and colour travels as YIQ
/// with one chroma sample per horizontal pixel pair. With a budget large enough to code every bit
/// plane only that rounding is left; the bounds below are what it measured on smooth test pictures
/// (grey within 4 per sample, colour within 8), and a smaller budget trades accuracy for size.
/// </summary>
[TestFixture]
public sealed class WicRoundTripTests {

  private const int _GREY_BOUND = 4;
  private const int _COLOUR_BOUND = 8;

  /// <summary>A smooth picture whose colour changes only every second column, so halving the chroma loses nothing.</summary>
  private static RawImage _Picture(int width, int height, PixelFormat format) {
    var channels = format == PixelFormat.Gray8 ? 1 : 3;
    var pixels = new byte[width * height * channels];
    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x)
        for (var c = 0; c < channels; ++c)
          pixels[(y * width + x) * channels + c] = (byte)(128 + 90 * Math.Sin((x >> 1) * 0.21 + y * 0.13 + c * 1.7));

    return new() { Width = width, Height = height, Format = format, PixelData = pixels };
  }

  /// <summary>Every pixel layout, transform and generation the writer offers; Haar arrived with 1.01 only.</summary>
  private static System.Collections.Generic.IEnumerable<TestCaseData> _Modes() {
    foreach (var format in new[] { PixelFormat.Gray8, PixelFormat.Rgb24 })
    foreach (var transform in Enum.GetValues<WicTransform>())
    foreach (var generation in Enum.GetValues<WicGeneration>())
      if (transform != WicTransform.Haar || generation == WicGeneration.Version101)
        yield return new TestCaseData(format, transform, generation);
  }

  [TestCaseSource(nameof(_Modes))]
  [Category("Integration")]
  public void RawImage_RoundTripsWithinTheCodecsBound(PixelFormat format, WicTransform transform, WicGeneration generation) {
    var source = _Picture(46, 29, format);
    var file = WicFile.FromRawImage(source) with { Transform = transform, Generation = generation, BitsPerPixelBudget = 64 };
    var restored = WicFile.ToRawImage(WicReader.FromSpan(WicWriter.ToBytes(file)));

    Assert.Multiple(() => {
      Assert.That(restored.Format, Is.EqualTo(format));
      Assert.That((restored.Width, restored.Height), Is.EqualTo((46, 29)));
      Assert.That(WicTestSupport.MaxDifference(restored.PixelData, source.PixelData), Is.LessThanOrEqualTo(format == PixelFormat.Gray8 ? _GREY_BOUND : _COLOUR_BOUND));
    });
  }

  /// <summary>
  /// Like the translators, the writer pads with black, and an odd-width colour picture's last column
  /// shares its chroma sample with the first padding column — so that column keeps only half its
  /// colour. Everything left of it stays within the usual bound.
  /// </summary>
  [Test]
  [Category("Integration")]
  public void OddWidthColour_KeepsTheBoundLeftOfItsLastColumn() {
    const int width = 45, height = 29;
    var source = _Picture(width, height, PixelFormat.Rgb24);
    var restored = WicReader.FromSpan(WicWriter.ToBytes(WicFile.FromRawImage(source) with { BitsPerPixelBudget = 64 }));

    var worst = 0;
    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width - 1; ++x)
        for (var c = 0; c < 3; ++c) {
          var i = (y * width + x) * 3 + c;
          worst = Math.Max(worst, Math.Abs(restored.PixelData[i] - source.PixelData[i]));
        }

    Assert.That(worst, Is.LessThanOrEqualTo(_COLOUR_BOUND));
  }

  [Test]
  [Category("Integration")]
  public void DefaultBudget_StaysCloseToThePicture() {
    var source = _Picture(64, 64, PixelFormat.Rgb24);
    var bytes = WicWriter.ToBytes(WicFile.FromRawImage(source));
    var restored = WicReader.FromSpan(bytes);

    Assert.That(bytes.Length, Is.LessThanOrEqualTo(32 + (int)(64 * 64 * WicFile.DefaultBitsPerPixelBudget / 8) + 16));
    Assert.That(WicTestSupport.Psnr(restored.PixelData, source.PixelData), Is.GreaterThan(30));
  }

  [Test]
  [Category("Integration")]
  public void HeaderMetadata_SurvivesReadWriteRead() {
    var file = WicFile.FromRawImage(_Picture(20, 12, PixelFormat.Rgb24)) with {
      Dpi = 300,
      Generation = WicGeneration.Version100,
      Transform = WicTransform.Daubechies4,
      ChromaBudgetScale = 40,
    };

    var first = WicReader.FromSpan(WicWriter.ToBytes(file));
    var second = WicReader.FromSpan(WicWriter.ToBytes(first));

    Assert.Multiple(() => {
      foreach (var read in new[] { first, second }) {
        Assert.That(read.Dpi, Is.EqualTo(300));
        Assert.That(read.Generation, Is.EqualTo(WicGeneration.Version100));
        Assert.That(read.Transform, Is.EqualTo(WicTransform.Daubechies4));
        Assert.That(read.ChromaBudgetScale, Is.EqualTo(40));
        Assert.That(read.IsGrayscale, Is.False);
      }
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_KeepsGreyAsGreyAndWidensEverythingElseToRgb() {
    var grey = WicFile.FromRawImage(_Picture(4, 4, PixelFormat.Gray8));
    var rgba = WicFile.FromRawImage(new RawImage { Width = 2, Height = 1, Format = PixelFormat.Rgba32, PixelData = [1, 2, 3, 255, 4, 5, 6, 255] });

    Assert.Multiple(() => {
      Assert.That(grey.IsGrayscale, Is.True);
      Assert.That(rgba.IsGrayscale, Is.False);
      Assert.That(rgba.PixelData, Is.EqualTo(new byte[] { 1, 2, 3, 4, 5, 6 }));
      Assert.That(rgba.Generation, Is.EqualTo(WicGeneration.Version101));
      Assert.That(rgba.Transform, Is.EqualTo(WicTransform.Biorthogonal97));
    });
  }

  [Test]
  [Category("Unit")]
  public void EnumValues_MatchTheHeaderCodes() {
    Assert.Multiple(() => {
      Assert.That((byte)WicTransform.Daubechies4, Is.EqualTo(0));
      Assert.That((byte)WicTransform.Biorthogonal97, Is.EqualTo(1));
      Assert.That((byte)WicTransform.Haar, Is.EqualTo(2));
    });
  }
}
