using System;
using FileFormat.Core;

namespace FileFormat.LuraDocument.Tests;

[TestFixture]
public sealed class LuraDocumentWriterTests {

  private static RawImage _Source(string name) {
    var (width, height, channels, pixels) = LuraDocumentFixtures.Pnm(LuraDocumentFixtures.Bytes(name));
    return new() { Width = width, Height = height, Format = channels == 3 ? PixelFormat.Rgb24 : PixelFormat.Gray8, PixelData = pixels };
  }

  private static RawImage _Bilevel(string name) {
    var grey = _Source(name);
    var indices = new byte[grey.PixelData.Length];
    for (var i = 0; i < indices.Length; ++i)
      indices[i] = (byte)(grey.PixelData[i] == 255 ? 1 : 0);
    return new() { Width = grey.Width, Height = grey.Height, Format = PixelFormat.Indexed8, PixelData = indices, Palette = [0, 0, 0, 255, 255, 255], PaletteCount = 2 };
  }

  private static byte[] _Write(params RawImage[] pages)
    => LuraDocumentWriter.ToBytes(new LuraDocumentFile { Pages = Array.ConvertAll(pages, LuraDocumentPage.FromRawImage) });

  [Test]
  [Category("Unit")]
  public void WritesTheBytesLuraTechsDecoderAccepted() {
    // The committed writer-* files are this writer's output that LuraTech's decoder composed to their .expected files;
    // this pins the writer to them.
    Assert.Multiple(() => {
      Assert.That(_Write(_Source("source-colour.ppm")), Is.EqualTo(LuraDocumentFixtures.Bytes("writer-colour.ldf")));
      Assert.That(_Write(_Source("source-grey.pgm")), Is.EqualTo(LuraDocumentFixtures.Bytes("writer-grey.ldf")));
      Assert.That(_Write(_Bilevel("source-bilevel.pgm")), Is.EqualTo(LuraDocumentFixtures.Bytes("writer-bilevel.ldf")));
      Assert.That(_Write(_Bilevel("source-bilevel.pgm"), _Source("source-colour.ppm"), _Source("source-grey.pgm")),
        Is.EqualTo(LuraDocumentFixtures.Bytes("writer-multipage.ldf")));
    });
  }

  [Test]
  [Category("Unit")]
  public void BlackAndWhiteRoundTripsExactly() {
    var source = _Bilevel("source-bilevel.pgm");
    var page = LuraDocumentReader.FromBytes(_Write(source)).Pages[0];
    Assert.Multiple(() => {
      Assert.That(page.Kind, Is.EqualTo(LuraDocumentPageKind.Bilevel));
      Assert.That(page.HasMask, Is.True);
      Assert.That(page.PixelData, Is.EqualTo(source.PixelData));
    });
  }

  [Test]
  [Category("Unit")]
  public void GreyRoundTripsExactly() {
    var source = _Source("source-grey.pgm");
    var page = LuraDocumentReader.FromBytes(_Write(source)).Pages[0];
    Assert.Multiple(() => {
      Assert.That(page.Kind, Is.EqualTo(LuraDocumentPageKind.Grey));
      Assert.That(page.HasMask, Is.False);
      Assert.That(page.PixelData, Is.EqualTo(source.PixelData));
    });
  }

  [Test]
  [Category("Unit")]
  public void ColourComesBackWithinTheLayersColourModel() {
    var random = new Random(5);
    var pixels = new byte[61 * 43 * 3];
    random.NextBytes(pixels);
    // Keep away from the saturated corners the luma/colour-difference layer cannot reach.
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(64 + pixels[i] / 2);
    var source = new RawImage { Width = 61, Height = 43, Format = PixelFormat.Rgb24, PixelData = pixels };
    var page = LuraDocumentReader.FromBytes(_Write(source)).Pages[0];
    var worst = 0;
    for (var i = 0; i < pixels.Length; ++i)
      worst = Math.Max(worst, Math.Abs(page.PixelData[i] - pixels[i]));
    Assert.Multiple(() => {
      Assert.That(page.Kind, Is.EqualTo(LuraDocumentPageKind.Colour));
      Assert.That(worst, Is.LessThanOrEqualTo(1));
    });
  }

  [TestCase(1, 1)]
  [TestCase(1, 9)]
  [TestCase(9, 1)]
  [TestCase(17, 5)]
  [Category("Unit")]
  public void TinyPagesRoundTrip(int width, int height) {
    var grey = new byte[width * height];
    new Random(width + height).NextBytes(grey);
    var bits = new byte[width * height];
    for (var i = 0; i < bits.Length; ++i)
      bits[i] = (byte)(grey[i] & 1);
    var greyImage = new RawImage { Width = width, Height = height, Format = PixelFormat.Gray8, PixelData = grey };
    var bilevel = new RawImage { Width = width, Height = height, Format = PixelFormat.Indexed8, PixelData = bits, Palette = [0, 0, 0, 255, 255, 255], PaletteCount = 2 };
    var file = LuraDocumentReader.FromBytes(_Write(greyImage, bilevel));
    Assert.Multiple(() => {
      Assert.That(file.Pages[0].PixelData, Is.EqualTo(grey));
      Assert.That(file.Pages[1].PixelData, Is.EqualTo(bits));
    });
  }

  [Test]
  [Category("Unit")]
  public void ResolutionIsKept() {
    var page = LuraDocumentPage.FromRawImage(_Bilevel("source-bilevel.pgm")) with { HorizontalDpi = 600, VerticalDpi = 200 };
    var back = LuraDocumentReader.FromBytes(LuraDocumentWriter.ToBytes(new LuraDocumentFile { Pages = [page] })).Pages[0];
    Assert.Multiple(() => {
      Assert.That(back.HorizontalDpi, Is.EqualTo(600));
      Assert.That(back.VerticalDpi, Is.EqualTo(200));
      Assert.That(back.PixelData, Is.EqualTo(page.PixelData));
    });
  }

  [Test]
  [Category("Unit")]
  public void NoPagesIsRefused()
    => Assert.Throws<ArgumentException>(() => LuraDocumentWriter.ToBytes(new LuraDocumentFile()));

  [Test]
  [Category("Unit")]
  public void PixelsThatDoNotMatchTheSizeAreRefused()
    => Assert.Throws<ArgumentException>(() => LuraDocumentWriter.ToBytes(new LuraDocumentFile {
      Pages = [new LuraDocumentPage { Width = 4, Height = 4, Kind = LuraDocumentPageKind.Colour, PixelData = new byte[47] }],
    }));
}
