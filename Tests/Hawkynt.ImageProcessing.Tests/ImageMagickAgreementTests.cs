using System;
using System.Collections.Generic;
using FileFormat.Core;

namespace Hawkynt.ImageProcessing.Tests;

/// <summary>
/// Every operation, set against ImageMagick 7 doing the same thing to the same picture.
/// </summary>
/// <remarks>
/// <para>Measured against ImageMagick 7.1.2-31 Q16-HDRI, every case agrees exactly except a handful
/// of components that differ by one level: saturation 150 (1.1 % of components), hue -135 (0.1 %),
/// auto-level (1.7 %), and upscaling through Catmull-Rom and Mitchell (a few components in 45 000).
/// Those are rounding, not method: both sides compute in floating point and round once to 8 bits,
/// but ImageMagick's HSL round trip and its intermediate between the two resample passes are held
/// at 16-bit quantum precision, so a value sitting on a half-level boundary can land either side.</para>
/// <para>The ceilings — one level at worst, 0.05 on average — leave exactly that room and no more,
/// so another build's rounding passes and a changed method does not.</para>
/// </remarks>
[TestFixture]
[Category("Oracle")]
public sealed class ImageMagickAgreementTests {

  private static readonly RawImage _Photo = TestPictures.Photo();

  /// <summary>A flat picture, its range squeezed into 40..200, for the stretches to find.</summary>
  private static readonly RawImage _Flat = TestPictures.Photo(low: 40, high: 200);

  public static IEnumerable<TestCaseData> ColourCorrections() {
    yield return _Case("brightness-contrast 10x20", p => ColorCorrection.BrightnessContrast(p, 10, 20), "-brightness-contrast 10x20", 1, 0.05);
    yield return _Case("brightness-contrast -15x-30", p => ColorCorrection.BrightnessContrast(p, -15, -30), "-brightness-contrast -15x-30", 1, 0.05);
    yield return _Case("brightness 25", p => ColorCorrection.Brightness(p, 25), "-brightness-contrast 25x0", 1, 0.05);
    yield return _Case("contrast 40", p => ColorCorrection.Contrast(p, 40), "-brightness-contrast 0x40", 1, 0.05);
    yield return _Case("gamma 1.8", p => ColorCorrection.Gamma(p, 1.8), "-gamma 1.8", 1, 0.05);
    yield return _Case("gamma 0.6", p => ColorCorrection.Gamma(p, 0.6), "-gamma 0.6", 1, 0.05);
    yield return _Case("levels 10%,90%,1.5", p => ColorCorrection.Levels(p, 0.1, 0.9, 1.5), "-level 10%,90%,1.5", 1, 0.05);
    yield return _Case("levels 0,75%,0.7", p => ColorCorrection.Levels(p, 0.0, 0.75, 0.7), "-level 0,75%,0.7", 1, 0.05);
    yield return _Case("saturation 150", p => ColorCorrection.Saturation(p, 150), "-modulate 100,150,100", 1, 0.05);
    yield return _Case("saturation 30", p => ColorCorrection.Saturation(p, 30), "-modulate 100,30,100", 1, 0.05);
    // -modulate takes hue as a percentage where 200 points is a full turn: 100 + degrees * 200 / 360.
    yield return _Case("hue +60", p => ColorCorrection.HueRotation(p, 60), "-modulate 100,100,133.33333333333333", 1, 0.05);
    yield return _Case("hue -135", p => ColorCorrection.HueRotation(p, -135), "-modulate 100,100,25", 1, 0.05);
    yield return _Case("modulate 120,80,-30deg", p => ColorCorrection.Modulate(p, 120, 80, -30), "-modulate 120,80,83.33333333333333", 1, 0.05);
    yield return _Case("equalize", p => ColorCorrection.Equalize(p), "-equalize", 1, 0.05);
    yield return _Case("equalize per channel", p => ColorCorrection.Equalize(p, ChannelScope.PerChannel), "-channel RGB -equalize +channel", 1, 0.05);
  }

  public static IEnumerable<TestCaseData> AutoLevelCases() {
    yield return new TestCaseData(_Flat, (Func<RawImage, RawImage>)(p => ColorCorrection.AutoLevels(p)), "-auto-level", 1, 0.05)
      .SetArgDisplayNames("auto-level joint");
    yield return new TestCaseData(_Flat, (Func<RawImage, RawImage>)(p => ColorCorrection.AutoLevels(p, ChannelScope.PerChannel)), "-channel R -auto-level -channel G -auto-level -channel B -auto-level +channel", 1, 0.05)
      .SetArgDisplayNames("auto-level per channel");
  }

  public static IEnumerable<TestCaseData> Resamples() {
    var kernels = new (ResampleKernel Kernel, string Filter)[] {
      (ResampleKernel.Nearest, "Point"),
      (ResampleKernel.Box, "Box"),
      (ResampleKernel.Bilinear, "Triangle"),
      (ResampleKernel.Bicubic, "Catrom"),
      (ResampleKernel.Mitchell, "Mitchell"),
      (ResampleKernel.Lanczos, "Lanczos"),
    };

    foreach (var (kernel, filter) in kernels) {
      yield return _Case($"{kernel} down 37x29", p => ImageScaler.Resize(p, 37, 29, kernel), $"-filter {filter} -resize 37x29!", 1, 0.05);
      yield return _Case($"{kernel} up 150x101", p => ImageScaler.Resize(p, 150, 101, kernel), $"-filter {filter} -resize 150x101!", 1, 0.05);
      yield return _Case($"{kernel} half 32x24", p => ImageScaler.Scale(p, 0.5, kernel), $"-filter {filter} -resize 32x24!", 1, 0.05);
    }
  }

  public static IEnumerable<TestCaseData> Effects() {
    yield return _Case("blur 3x1.5", p => ImageEffects.Blur(p, 3, 1.5), "-blur 3x1.5", 1, 0.05);
    yield return _Case("blur 0x2", p => ImageEffects.Blur(p, 0, 2), "-blur 0x2", 1, 0.05);
    yield return _Case("gaussian-blur 3x1.5", p => ImageEffects.GaussianBlur(p, 3, 1.5), "-gaussian-blur 3x1.5", 1, 0.05);
    yield return _Case("gaussian-blur 0x1", p => ImageEffects.GaussianBlur(p, 0, 1), "-gaussian-blur 0x1", 1, 0.05);
    yield return _Case("box blur 2", p => ImageEffects.BoxBlur(p, 2), "-statistic Mean 5x5", 1, 0.05);
    yield return _Case("sharpen 2x1", p => ImageEffects.Sharpen(p, 2, 1), "-sharpen 2x1", 1, 0.05);
    yield return _Case("sharpen 0x0.8", p => ImageEffects.Sharpen(p, 0, 0.8), "-sharpen 0x0.8", 1, 0.05);
    yield return _Case("unsharp 2x1+1.5+0.02", p => ImageEffects.UnsharpMask(p, 2, 1, 1.5, 0.02), "-unsharp 2x1+1.5+0.02", 1, 0.05);
    yield return _Case("unsharp 0x2+0.8+0", p => ImageEffects.UnsharpMask(p, 0, 2, 0.8, 0), "-unsharp 0x2+0.8+0", 1, 0.05);
    yield return _Case("edge 1", p => ImageEffects.DetectEdges(p, 1), "-edge 1", 1, 0.05);
    yield return _Case("edge 2", p => ImageEffects.DetectEdges(p, 2), "-edge 2", 1, 0.05);
    yield return _Case("emboss 2x1", p => ImageEffects.Emboss(p, 2, 1), "-emboss 2x1", 1, 0.05);
    yield return _Case("median 3", p => ImageEffects.Median(p, 3), "-statistic Median 3x3", 1, 0.05);
    yield return _Case("median 5", p => ImageEffects.Median(p, 5), "-statistic Median 5x5", 1, 0.05);
  }

  [TestCaseSource(nameof(ColourCorrections))]
  [TestCaseSource(nameof(AutoLevelCases))]
  [TestCaseSource(nameof(Resamples))]
  [TestCaseSource(nameof(Effects))]
  public void AgreesWithImageMagick(RawImage source, Func<RawImage, RawImage> ours, string arguments, int maxDifference, double meanDifference)
    => ImageMagickOracle.AssertAgrees(source, ours(source), arguments, maxDifference, meanDifference);

  private static TestCaseData _Case(string name, Func<RawImage, RawImage> ours, string arguments, int maxDifference, double meanDifference)
    => new TestCaseData(_Photo, ours, arguments, maxDifference, meanDifference).SetArgDisplayNames(name);
}
