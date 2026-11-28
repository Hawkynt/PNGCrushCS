using System;
using System.Globalization;
using FileFormat.Core;

namespace FileFormat.Wic.Tests;

[TestFixture]
public sealed class WicWriterTests {

  /// <summary>Each fixture was written by the translator's own coder; this writer has to reproduce it byte for byte.</summary>
  [TestCase("synth_33x17_rgb", WicTransform.Biorthogonal97, WicGeneration.Version101, "8", "synth_33x17_rgb_t1_g101_q8")]
  [TestCase("synth_33x17_rgb", WicTransform.Daubechies4, WicGeneration.Version100, "8", "synth_33x17_rgb_t0_g100_q8")]
  [TestCase("synth_33x17_grey", WicTransform.Haar, WicGeneration.Version101, "8", "synth_33x17_grey_t2_g101_q8")]
  [TestCase("synth_33x17_grey", WicTransform.Biorthogonal97, WicGeneration.Version100, "1", "synth_33x17_grey_t1_g100_q1")]
  [Category("Integration")]
  public void Writer_ReproducesTheTranslatorsBytes(string source, WicTransform transform, WicGeneration generation, string slider, string expected) {
    var colour = source.EndsWith("rgb", StringComparison.Ordinal);
    var picture = WicTestSupport.ReadPnm(WicTestSupport.Fixture(source + (colour ? ".ppm" : ".pgm")));
    var file = new WicFile {
      Width = picture.Width,
      Height = picture.Height,
      IsGrayscale = !colour,
      Transform = transform,
      Generation = generation,
      BitsPerPixelBudget = 1.5f * float.Parse(slider, CultureInfo.InvariantCulture),
      PixelData = picture.Pixels,
    };

    Assert.That(WicWriter.ToBytes(file), Is.EqualTo(WicTestSupport.Fixture(expected + ".wic")));
  }

  [Test]
  [Category("Unit")]
  public void HaarInAVersion100Header_IsRefused() {
    var file = new WicFile { Width = 8, Height = 8, IsGrayscale = true, Transform = WicTransform.Haar, Generation = WicGeneration.Version100, PixelData = new byte[64] };
    Assert.Throws<ArgumentException>(() => WicWriter.ToBytes(file));
  }

  [Test]
  [Category("Unit")]
  public void WidestPaddedSide_IsAcceptedAndOneMoreIsRefused() {
    var widest = new WicFile { Width = WicWriter.MaxMatrixSide, Height = 8, IsGrayscale = true, PixelData = new byte[WicWriter.MaxMatrixSide * 8] };
    var tooWide = widest with { Width = WicWriter.MaxMatrixSide + 1, PixelData = new byte[(WicWriter.MaxMatrixSide + 1) * 8] };

    var decoded = WicReader.FromSpan(WicWriter.ToBytes(widest));
    Assert.That(decoded.Width, Is.EqualTo(WicWriter.MaxMatrixSide));
    Assert.Throws<ArgumentException>(() => WicWriter.ToBytes(tooWide));
  }

  [Test]
  [Category("Unit")]
  public void ShortPixelData_IsRefused() {
    var file = new WicFile { Width = 4, Height = 4, PixelData = new byte[10] };
    Assert.Throws<ArgumentException>(() => WicWriter.ToBytes(file));
  }

  [Test]
  [Category("Unit")]
  public void NonPositiveBudget_IsRefused() {
    var file = new WicFile { Width = 4, Height = 4, IsGrayscale = true, BitsPerPixelBudget = 0, PixelData = new byte[16] };
    Assert.Throws<ArgumentException>(() => WicWriter.ToBytes(file));
  }

  [TestCase(1, 1, true)]
  [TestCase(1, 1, false)]
  [TestCase(3, 2, false)]
  [TestCase(31, 9, true)]
  [Category("Unit")]
  public void SmallAndOddPictures_PadToADecodableMatrix(int width, int height, bool grey) {
    // Neutral colours, so an odd last column sharing its chroma sample with the black padding loses nothing.
    var channels = grey ? 1 : 3;
    var pixels = new byte[width * height * channels];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(40 + i / channels * 37 % 160);

    var bytes = WicWriter.ToBytes(new WicFile { Width = width, Height = height, IsGrayscale = grey, BitsPerPixelBudget = 64, PixelData = pixels });
    var matrixWidth = bytes[18] | bytes[19] << 8;
    var matrixHeight = bytes[20] | bytes[21] << 8;
    var decoded = WicReader.FromSpan(bytes);

    Assert.Multiple(() => {
      Assert.That(matrixWidth, Is.GreaterThanOrEqualTo(grey ? 8 : 16));
      Assert.That(matrixHeight, Is.GreaterThanOrEqualTo(8));
      Assert.That((decoded.Width, decoded.Height), Is.EqualTo((width, height)));
      Assert.That(WicTestSupport.MaxDifference(decoded.PixelData, pixels), Is.LessThanOrEqualTo(4));
    });
  }
}
