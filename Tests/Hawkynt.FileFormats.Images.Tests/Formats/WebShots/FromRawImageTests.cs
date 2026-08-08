using FileFormat.Core;

namespace FileFormat.WebShots.Tests;

[TestFixture]
public sealed class FromRawImageTests {

  private static RawImage _Gradient(int width, int height) {
    var pixels = new byte[width * height * 3];
    for (var i = 0; i < width * height; ++i) {
      pixels[i * 3] = (byte)(i * 5 % 256);
      pixels[i * 3 + 1] = (byte)(i * 19 % 256);
      pixels[i * 3 + 2] = (byte)(i * 31 % 256);
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = pixels };
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Rgb24_ReturnsEveryPixelUnchanged() {
    var source = _Gradient(11, 5);

    var restored = WebShotsFile.ToRawImage(WebShotsReader.FromBytes(WebShotsWriter.ToBytes(WebShotsFile.FromRawImage(source))));

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(11));
      Assert.That(restored.Height, Is.EqualTo(5));
      Assert.That(restored.PixelData, Is.EqualTo(source.PixelData));
    });
  }

  /// <summary>The reader takes the body's length from the file, so the header's depth has to match what is in it.</summary>
  [Test]
  [Category("Unit")]
  public void FromRawImage_DeclaredDepthMatchesTheBodyWritten() {
    var file = WebShotsFile.FromRawImage(_Gradient(8, 8));

    Assert.Multiple(() => {
      Assert.That(file.Bpp, Is.EqualTo(24));
      Assert.That(file.PixelData, Has.Length.EqualTo(8 * 8 * file.Bpp / 8));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Rgba32Source_IsConvertedRatherThanRefused() {
    var source = new RawImage { Width = 1, Height = 1, Format = PixelFormat.Rgba32, PixelData = [10, 20, 30, 255] };

    var file = WebShotsFile.FromRawImage(source);

    Assert.That(file.PixelData, Is.EqualTo(new byte[] { 10, 20, 30 }));
  }
}
