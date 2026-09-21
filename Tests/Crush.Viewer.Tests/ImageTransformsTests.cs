using FileFormat.Core;

namespace Crush.Viewer.Tests;

/// <summary>The two whole-picture colour operations the viewer owns itself.</summary>
[TestFixture]
public sealed class ImageTransformsTests {

  private static RawImage _OnePixel(byte blue, byte green, byte red, byte alpha)
    => new() { Width = 1, Height = 1, Format = PixelFormat.Bgra32, PixelData = [blue, green, red, alpha] };

  [TestCase((byte)0, (byte)0, (byte)0, (byte)0)]
  [TestCase((byte)255, (byte)255, (byte)255, (byte)255)]
  [TestCase((byte)0, (byte)0, (byte)255, (byte)76)]
  [TestCase((byte)0, (byte)255, (byte)0, (byte)149)]
  [TestCase((byte)255, (byte)0, (byte)0, (byte)28)]
  [TestCase((byte)16, (byte)32, (byte)64, (byte)39)]
  public void Grayscale_UsesTheBt601Weights(byte blue, byte green, byte red, byte expected) {
    var result = ImageTransforms.Grayscale(_OnePixel(blue, green, red, 200));

    Assert.That(result.PixelData[0], Is.EqualTo(expected));
    Assert.That(result.PixelData[1], Is.EqualTo(expected));
    Assert.That(result.PixelData[2], Is.EqualTo(expected));
  }

  [Test]
  public void Grayscale_LeavesAlphaAlone() {
    var result = ImageTransforms.Grayscale(_OnePixel(10, 200, 30, 77));

    Assert.That(result.PixelData[3], Is.EqualTo(77));
  }

  [Test]
  public void Grayscale_DoesNotWriteThroughTheSourcesOwnBuffer() {
    // ToBgra32 hands back the source's array unchanged when the picture already is BGRA32, so an
    // in-place loop would edit the picture the caller still holds — and the viewer still shows.
    var source = _OnePixel(10, 200, 30, 255);

    _ = ImageTransforms.Grayscale(source);

    Assert.That(source.PixelData, Is.EqualTo(new byte[] { 10, 200, 30, 255 }));
  }

  [Test]
  public void Invert_FlipsTheColourChannelsAndNotAlpha() {
    var result = ImageTransforms.Invert(_OnePixel(0, 100, 255, 40));

    Assert.That(result.PixelData, Is.EqualTo(new byte[] { 255, 155, 0, 40 }));
  }

  [Test]
  public void Invert_AppliedTwice_GivesTheOriginalBack() {
    var source = _OnePixel(3, 200, 91, 128);

    var result = ImageTransforms.Invert(ImageTransforms.Invert(source));

    Assert.That(result.PixelData, Is.EqualTo(source.PixelData));
  }

  [Test]
  public void Invert_DoesNotWriteThroughTheSourcesOwnBuffer() {
    var source = _OnePixel(3, 200, 91, 128);

    _ = ImageTransforms.Invert(source);

    Assert.That(source.PixelData, Is.EqualTo(new byte[] { 3, 200, 91, 128 }));
  }

  [Test]
  public void BothKeepTheMetadataTheSourceCarried() {
    var metadata = new ImageMetadata { DpiX = 300, DpiY = 300 };
    var source = new RawImage {
      Width = 1,
      Height = 1,
      Format = PixelFormat.Bgra32,
      PixelData = [1, 2, 3, 255],
      Metadata = metadata,
    };

    Assert.That(ImageTransforms.Grayscale(source).Metadata, Is.SameAs(metadata));
    Assert.That(ImageTransforms.Invert(source).Metadata, Is.SameAs(metadata));
  }

  [Test]
  public void Grayscale_ConvertsAnIndexedPictureRatherThanRefusingIt() {
    var source = new RawImage {
      Width = 2,
      Height = 1,
      Format = PixelFormat.Indexed8,
      PixelData = [0, 1],
      Palette = [255, 0, 0, 0, 0, 255],
      PaletteCount = 2,
    };

    var result = ImageTransforms.Grayscale(source);

    Assert.That(result.Format, Is.EqualTo(PixelFormat.Bgra32));
    Assert.That(result.PixelData[0], Is.EqualTo(result.PixelData[2]), "the first pixel is not grey");
    Assert.That(result.PixelData[4], Is.EqualTo(result.PixelData[6]), "the second pixel is not grey");
    Assert.That(result.PixelData[0], Is.Not.EqualTo(result.PixelData[4]), "red and blue came out the same grey");
  }
}
