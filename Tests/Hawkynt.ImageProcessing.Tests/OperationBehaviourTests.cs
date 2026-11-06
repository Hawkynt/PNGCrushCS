using System;
using FileFormat.Core;

namespace Hawkynt.ImageProcessing.Tests;

/// <summary>
/// The behaviour the ImageMagick comparison cannot reach: transparency, the verbs that differ only
/// in what they are told, refusals, and red-eye, which ImageMagick does not have.
/// </summary>
[TestFixture]
[Category("Unit")]
public sealed class OperationBehaviourTests {

  [Test]
  public void Resize_AtTransparentEdge_DoesNotDragInTheColourOfInvisiblePixels() {
    // Opaque white beside transparent black: a straight-alpha resample would grey the white.
    byte[] pixels = [255, 255, 255, 255, 255, 255, 255, 255, 0, 0, 0, 0, 0, 0, 0, 0];
    var source = new RawImage { Width = 4, Height = 1, Format = PixelFormat.Rgba32, PixelData = pixels };

    var result = ImageScaler.Resize(source, 3, 1, ResampleKernel.Bilinear);

    for (var x = 0; x < 3; ++x) {
      var at = x * 4;
      if (result.PixelData[at + 3] == 0)
        continue;

      Assert.That(result.PixelData[at], Is.EqualTo(255), $"red of pixel {x} (alpha {result.PixelData[at + 3]})");
    }
  }

  [TestCase(ResampleKernel.Box)]
  [TestCase(ResampleKernel.Nearest)]
  [TestCase(ResampleKernel.Bilinear)]
  [TestCase(ResampleKernel.Bicubic)]
  [TestCase(ResampleKernel.Lanczos)]
  public void Regenerate_ThroughAnInterpolatingKernel_IsTheIdentity(ResampleKernel kernel) {
    var source = TestPictures.Photo(24, 16);

    var result = ImageScaler.Regenerate(source, kernel);

    Assert.That(result.PixelData, Is.EqualTo(source.PixelData));
  }

  [Test]
  public void Regenerate_ThroughMitchell_DoesChangeThePicture() {
    var source = TestPictures.Photo(24, 16);

    Assert.That(ImageScaler.Regenerate(source, ResampleKernel.Mitchell).PixelData, Is.Not.EqualTo(source.PixelData));
  }

  [Test]
  public void Resize_ToTheSameSize_ReturnsThePictureUntouched() {
    var source = TestPictures.Photo(24, 16);

    Assert.That(ImageScaler.Resize(source, 24, 16, ResampleKernel.Lanczos), Is.SameAs(source));
  }

  [Test]
  public void Fit_KeepsTheAspectRatioInsideTheBox() {
    var result = ImageScaler.Fit(TestPictures.Photo(64, 48), 32, 32, ResampleKernel.Mitchell);

    Assert.That((result.Width, result.Height), Is.EqualTo((32, 24)));
  }

  [Test]
  public void Resample_KeepsThePhysicalSizeAndRecordsTheNewDensity() {
    var photo = TestPictures.Photo(60, 30);
    var source = new RawImage {
      Width = photo.Width, Height = photo.Height, Format = photo.Format, PixelData = photo.PixelData,
      Metadata = new() { DpiX = 300, DpiY = 300 },
    };

    var result = ImageScaler.Resample(source, 150, 100, ResampleKernel.Lanczos);

    Assert.Multiple(() => {
      Assert.That((result.Width, result.Height), Is.EqualTo((30, 10)));
      Assert.That(result.Metadata!.DpiX, Is.EqualTo(150));
      Assert.That(result.Metadata!.DpiY, Is.EqualTo(100));
    });
  }

  [Test]
  public void Resample_WithoutAKnownDensity_IsRefusedRatherThanGuessed()
    => Assert.Throws<ArgumentException>(() => ImageScaler.Resample(TestPictures.Photo(8, 8), 72, 72, ResampleKernel.Box));

  [Test]
  public void Contrast_OfOneHundredPercent_IsRefused()
    => Assert.Throws<ArgumentOutOfRangeException>(() => ColorCorrection.Contrast(TestPictures.Photo(4, 4), 100));

  [Test]
  public void Modulate_WithNoChange_ReturnsThePictureUntouched() {
    var source = TestPictures.Photo(8, 8);

    Assert.That(ColorCorrection.Modulate(source, 100, 100, 360), Is.SameAs(source));
  }

  [Test]
  public void Saturation_OfZero_MakesEveryPixelGrey() {
    var result = ColorCorrection.Saturation(TestPictures.Photo(16, 16), 0).ToRgb24();

    for (var i = 0; i < result.Length; i += 3)
      Assert.That((result[i + 1], result[i + 2]), Is.EqualTo((result[i], result[i])), $"pixel {i / 3}");
  }

  [Test]
  public void AutoLevels_OnAFlatPicture_ReturnsItUntouched() {
    var source = new RawImage { Width = 2, Height = 2, Format = PixelFormat.Gray8, PixelData = [90, 90, 90, 90] };

    Assert.That(ColorCorrection.AutoLevels(source), Is.SameAs(source));
  }

  [Test]
  public void Sharpen_LeavesAlphaAlone() {
    var bytes = new byte[12 * 12 * 4];
    new Random(3).NextBytes(bytes);
    var source = new RawImage { Width = 12, Height = 12, Format = PixelFormat.Bgra32, PixelData = bytes };

    var result = ImageEffects.Sharpen(source, 2, 1);

    for (var i = 3; i < bytes.Length; i += 4)
      Assert.That(result.PixelData[i], Is.EqualTo(bytes[i]), $"alpha of pixel {i / 4}");
  }

  [Test]
  public void Median_RemovesAnIsolatedSpeck() {
    var pixels = new byte[25];
    Array.Fill(pixels, (byte)100);
    pixels[12] = 255;
    var source = new RawImage { Width = 5, Height = 5, Format = PixelFormat.Gray8, PixelData = pixels };

    var result = ImageEffects.Median(source, 3);

    Assert.That(result.Format, Is.EqualTo(PixelFormat.Gray8));
    Assert.That(result.PixelData, Is.All.EqualTo(100));
  }

  [Test]
  public void DetectEdges_OnAFlatPicture_IsBlack() {
    var source = new RawImage { Width = 4, Height = 4, Format = PixelFormat.Rgb24, PixelData = new byte[48] };
    Array.Fill(source.PixelData, (byte)180);

    Assert.That(ImageEffects.DetectEdges(source).PixelData, Is.All.EqualTo(0));
  }

  [Test]
  public void ApplySeparable_WithAUnitImpulse_IsTheIdentity() {
    var source = TestPictures.Photo(10, 10);

    var result = Convolution.ApplySeparable(source, [0, 1, 0], [0, 1, 0]);

    Assert.That(result.PixelData, Is.EqualTo(source.PixelData));
  }

  [Test]
  public void Apply_ReflectsTheKernel() {
    // A kernel whose only weight is its right-hand cell: as a convolution it takes the pixel to the
    // left, which is what reflecting the kernel means.
    byte[] pixels = [10, 20, 30, 40];
    var source = new RawImage { Width = 4, Height = 1, Format = PixelFormat.Gray8, PixelData = pixels };

    var result = Convolution.Apply(source, new ConvolutionKernel(3, 1, [0, 0, 1]));

    Assert.That(result.PixelData, Is.EqualTo(new byte[] { 10, 10, 20, 30 }));
  }

  [Test]
  public void RedEye_RemovesTheCastInsideTheEllipseAndNowhereElse() {
    const int size = 20;
    var pixels = new byte[size * size * 3];
    for (var i = 0; i < pixels.Length; i += 3) {
      pixels[i] = 200;
      pixels[i + 1] = 30;
      pixels[i + 2] = 40;
    }

    var source = new RawImage { Width = size, Height = size, Format = PixelFormat.Rgb24, PixelData = pixels };

    var result = RedEyeReduction.Apply(source, 5, 5, 10, 10);

    byte RedAt(int x, int y) => result.PixelData[(y * size + x) * 3];
    var expected = (byte)Math.Round((30 / 255.0 + 0.1933333 * 40 / 255.0) / (2 * 0.5133333) * 255);

    Assert.Multiple(() => {
      Assert.That(RedAt(10, 10), Is.EqualTo(expected), "the centre of the marked eye");
      Assert.That(RedAt(5, 5), Is.EqualTo(200), "a corner of the box, outside the ellipse");
      Assert.That(RedAt(2, 10), Is.EqualTo(200), "outside the box");
      Assert.That(result.PixelData[(10 * size + 10) * 3 + 1], Is.EqualTo(30), "green is never changed");
      Assert.That(result.PixelData[(10 * size + 10) * 3 + 2], Is.EqualTo(40), "blue is never changed");
    });
  }

  [Test]
  public void RedEye_LeavesAPixelThatIsNotRedAlone() {
    // Skin: red leads, but weighted red (0.513 x 220 = 113) is below green (170).
    byte[] pixels = [220, 170, 140];
    var source = new RawImage { Width = 1, Height = 1, Format = PixelFormat.Rgb24, PixelData = pixels };

    Assert.That(RedEyeReduction.Apply(source, 0, 0, 1, 1), Is.SameAs(source));
  }
}
