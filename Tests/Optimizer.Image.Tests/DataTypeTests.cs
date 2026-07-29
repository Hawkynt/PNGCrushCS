using System;
using Optimizer.Image;

namespace Optimizer.Image.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  public void ImageFormat_HasEveryFormatAndNoDuplicates() {
    // Not a fixed count: the enum is generated from whichever format libraries are present, so a
    // hard number fails on the commit that adds the next format rather than on anything being
    // wrong -- which is how this sat at 540 while the enum had grown to 586. What actually has to
    // hold is that the enum is well-formed: every member distinct, and Unknown present exactly once.
    var values = Enum.GetValues<ImageFormat>();

    Assert.That(values, Is.Not.Empty);
    Assert.That(values, Is.Unique);
    Assert.That(Enum.GetNames<ImageFormat>(), Is.Unique);
    Assert.That(values, Does.Contain(ImageFormat.Unknown));
  }

  [Test]
  public void ImageFormat_UnknownIsZero() {
    Assert.That((int)ImageFormat.Unknown, Is.EqualTo(0));
  }

  [Test]
  public void ImageOptimizationOptions_DefaultsAreCorrect() {
    var opts = new ImageOptimizationOptions();
    Assert.Multiple(() => {
      Assert.That(opts.AllowLossy, Is.False);
      Assert.That(opts.AllowFormatConversion, Is.True);
      Assert.That(opts.ForceFormat, Is.Null);
      Assert.That(opts.MaxParallelTasks, Is.EqualTo(0));
      Assert.That(opts.StripMetadata, Is.False);
      Assert.That(opts.PngOptions, Is.Null);
      Assert.That(opts.GifOptions, Is.Null);
      Assert.That(opts.TiffOptions, Is.Null);
      Assert.That(opts.BmpOptions, Is.Null);
      Assert.That(opts.TgaOptions, Is.Null);
      Assert.That(opts.PcxOptions, Is.Null);
      Assert.That(opts.JpegOptions, Is.Null);
      Assert.That(opts.IcoOptions, Is.Null);
      Assert.That(opts.CurOptions, Is.Null);
      Assert.That(opts.AniOptions, Is.Null);
      Assert.That(opts.WebPOptions, Is.Null);
    });
  }

  [Test]
  public void ImageOptimizationResult_FieldsStored() {
    var result = new ImageOptimizationResult(
      ImageFormat.Png,
      ImageFormat.Bmp,
      ".bmp",
      1234,
      TimeSpan.FromSeconds(5),
      [1, 2, 3],
      "test details"
    );

    Assert.Multiple(() => {
      Assert.That(result.OriginalFormat, Is.EqualTo(ImageFormat.Png));
      Assert.That(result.OutputFormat, Is.EqualTo(ImageFormat.Bmp));
      Assert.That(result.OutputExtension, Is.EqualTo(".bmp"));
      Assert.That(result.CompressedSize, Is.EqualTo(1234));
      Assert.That(result.ProcessingTime, Is.EqualTo(TimeSpan.FromSeconds(5)));
      Assert.That(result.FileContents, Has.Length.EqualTo(3));
      Assert.That(result.Details, Is.EqualTo("test details"));
    });
  }

  [Test]
  public void ImageOptimizationOptions_ForceFormatOverridesConversion() {
    var opts = new ImageOptimizationOptions(ForceFormat: ImageFormat.Jpeg);
    Assert.That(opts.ForceFormat, Is.EqualTo(ImageFormat.Jpeg));
  }

  [Test]
  public void ImageOptimizationOptions_SubOptionsPreserved() {
    var pngOpts = new Optimizer.Png.PngOptimizationOptions { TryInterlacing = false };
    var opts = new ImageOptimizationOptions(PngOptions: pngOpts);
    Assert.That(opts.PngOptions, Is.Not.Null);
    Assert.That(opts.PngOptions!.TryInterlacing, Is.False);
  }
}
