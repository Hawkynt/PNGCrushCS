using FileFormat.Core;

namespace FileFormat.DbwRender.Tests;

[TestFixture]
public sealed class FromRawImageTests {

  private static RawImage _Gradient(int width, int height) {
    var pixels = new byte[width * height * 3];
    for (var i = 0; i < width * height; ++i) {
      pixels[i * 3] = (byte)(i * 7 % 256);
      pixels[i * 3 + 1] = (byte)(i * 13 % 256);
      pixels[i * 3 + 2] = (byte)(i * 29 % 256);
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = pixels };
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Rgb24_ReturnsEveryPixelUnchanged() {
    var source = _Gradient(13, 7);

    var restored = DbwRenderFile.ToRawImage(DbwRenderReader.FromBytes(DbwRenderWriter.ToBytes(DbwRenderFile.FromRawImage(source))));

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(13));
      Assert.That(restored.Height, Is.EqualTo(7));
      Assert.That(restored.PixelData, Is.EqualTo(source.PixelData));
    });
  }

  /// <summary>The header states the size, so no size is the wrong one.</summary>
  [Test]
  [Category("Unit")]
  public void FromRawImage_OddSizes_AreKept([Values(1, 3, 64, 257)] int width) {
    var file = DbwRenderFile.FromRawImage(_Gradient(width, 5));

    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(width));
      Assert.That(file.PixelData, Has.Length.EqualTo(width * 5 * 3));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Bgra32Source_IsConvertedRatherThanRefused() {
    var pixels = new byte[] { 1, 2, 3, 255, 4, 5, 6, 255 };
    var source = new RawImage { Width = 2, Height = 1, Format = PixelFormat.Bgra32, PixelData = pixels };

    var file = DbwRenderFile.FromRawImage(source);

    Assert.That(file.PixelData, Is.EqualTo(new byte[] { 3, 2, 1, 6, 5, 4 }));
  }
}
