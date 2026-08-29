using System;
using FileFormat.Core;
using FileFormat.Fff;
using FileFormat.Hta;
using FileFormat.PicturePublisher;
using FileFormat.PicturePublisher4;
using FileFormat.PostScript;
using FileFormat.SonyPmp;

namespace Hawkynt.FileFormats.Images.Tests.Formats.GapClosures;

[TestFixture]
public sealed class AdditionalWriterClosureTests {

  private static RawImage _Pattern(int width, int height) {
    var pixels = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var at = (y * width + x) * 3;
      pixels[at] = (byte)(x * 17 + y * 3);
      pixels[at + 1] = (byte)(x * 5 + y * 29);
      pixels[at + 2] = (byte)(255 - x * 7 - y * 11);
    }
    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = pixels };
  }

  [Test]
  [Category("Unit")]
  public void Hta_OneMemberPngRoundTrip_IsPixelExact() {
    var source = _Pattern(23, 17);
    var bytes = HtaWriter.ToBytes(HtaFile.FromRawImage(source));
    var decodedFile = HtaReader.FromBytes(bytes);
    var decoded = HtaFile.ToRawImage(decodedFile);

    Assert.Multiple(() => {
      Assert.That(HtaFile.ImageCount(decodedFile), Is.EqualTo(1));
      Assert.That(decoded.Width, Is.EqualTo(source.Width));
      Assert.That(decoded.Height, Is.EqualTo(source.Height));
      Assert.That(decoded.ToRgb24(), Is.EqualTo(source.PixelData));
    });
  }

  [Test]
  [Category("Unit")]
  public void PicturePublisher4_TiffWrapperRoundTrip_IsPixelExact() {
    var source = _Pattern(23, 17);
    var bytes = PicturePublisher4Writer.ToBytes(PicturePublisher4File.FromRawImage(source));
    var decoded = PicturePublisher4File.ToRawImage(PicturePublisher4Reader.FromBytes(bytes));
    Assert.That(decoded.ToRgb24(), Is.EqualTo(source.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void PicturePublisher5_SingleObjectRoundTrip_IsPixelExact() {
    var source = _Pattern(23, 17);
    var bytes = PicturePublisherWriter.ToBytes(PicturePublisherFile.FromRawImage(source));
    var decoded = PicturePublisherFile.ToRawImage(PicturePublisherReader.FromBytes(bytes));
    Assert.That(decoded.PixelData, Is.EqualTo(source.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void PostScript_ColorImageRoundTrip_PreservesDeclaredRasterSize() {
    var source = _Pattern(23, 17);
    var bytes = PostScriptWriter.ToBytes(PostScriptFile.FromRawImage(source));
    var parsed = PostScriptReader.FromBytes(bytes);
    var rendering = PostScriptRenderer.Render(parsed);

    Assert.Multiple(() => {
      Assert.That(rendering.Image.Width, Is.EqualTo(source.Width));
      Assert.That(rendering.Image.Height, Is.EqualTo(source.Height));
      Assert.That(rendering.HasInk, Is.True);
      Assert.That(rendering.PagesShown, Is.GreaterThanOrEqualTo(1));
    });
  }

  [TestCase("SonyPmp")]
  [TestCase("MaggiFff")]
  [Category("Unit")]
  public void FixedOffsetJpegRecords_PreserveImageDimensions(string format) {
    var source = _Pattern(19, 13);
    RawImage decoded = format switch {
      "SonyPmp" => SonyPmpFile.ToRawImage(SonyPmpReader.FromBytes(SonyPmpWriter.ToBytes(SonyPmpFile.FromRawImage(source)))),
      "MaggiFff" => FffFile.ToRawImage(FffReader.FromBytes(FffWriter.ToBytes(FffFile.FromRawImage(source)))),
      _ => throw new ArgumentOutOfRangeException(nameof(format)),
    };

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(source.Width));
      Assert.That(decoded.Height, Is.EqualTo(source.Height));
    });
  }
}
