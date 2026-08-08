using FileFormat.Core;

namespace FileFormat.Jng.Tests;

[TestFixture]
public sealed class FromRawImageTests {

  /// <summary>Flat blocks rather than noise: JPEG is lossy, and a picture with no detail to lose is
  /// the one whose colours can be checked at all.</summary>
  private static RawImage _Blocks(int width, int height) {
    var pixels = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var offset = (y * width + x) * 3;
        pixels[offset] = x < width / 2 ? (byte)200 : (byte)40;
        pixels[offset + 1] = y < height / 2 ? (byte)200 : (byte)40;
        pixels[offset + 2] = 128;
      }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = pixels };
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_KeepsTheSizeAndApproximatelyTheColours() {
    var source = _Blocks(64, 64);

    var restored = JngFile.ToRawImage(JngReader.FromBytes(JngWriter.ToBytes(JngFile.FromRawImage(source)))).EnsureFormat(PixelFormat.Rgb24);

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(64));
      Assert.That(restored.Height, Is.EqualTo(64));

      // The centre of each quadrant is far enough from an edge that ringing does not reach it.
      foreach (var (x, y) in new[] { (16, 16), (48, 16), (16, 48), (48, 48) }) {
        var at = (y * 64 + x) * 3;
        var from = (y * 64 + x) * 3;
        Assert.That(restored.PixelData[at], Is.EqualTo(source.PixelData[from]).Within(24));
        Assert.That(restored.PixelData[at + 1], Is.EqualTo(source.PixelData[from + 1]).Within(24));
      }
    });
  }

  /// <summary>The alpha is a second, greyscale JPEG in a JDAA chunk, which is the branch the decoder
  /// reads; a JNG written with PNG-coded alpha instead could not be opened again here.</summary>
  [Test]
  [Category("Integration")]
  public void RoundTrip_Rgba32Source_CarriesItsAlphaAsAJpegCodedChannel() {
    var pixels = new byte[32 * 32 * 4];
    for (var i = 0; i < 32 * 32; ++i) {
      pixels[i * 4] = 120;
      pixels[i * 4 + 1] = 120;
      pixels[i * 4 + 2] = 120;
      pixels[i * 4 + 3] = i < 32 * 16 ? (byte)255 : (byte)0;
    }

    var file = JngFile.FromRawImage(new() { Width = 32, Height = 32, Format = PixelFormat.Rgba32, PixelData = pixels });
    var restored = JngFile.ToRawImage(JngReader.FromBytes(JngWriter.ToBytes(file))).EnsureFormat(PixelFormat.Rgba32);

    Assert.Multiple(() => {
      Assert.That(file.AlphaCompression, Is.EqualTo(JngAlphaCompression.Jpeg));
      Assert.That(file.ColorType, Is.EqualTo(14));
      Assert.That(restored.PixelData[3], Is.EqualTo(255).Within(24));
      Assert.That(restored.PixelData[(32 * 24 * 4) + 3], Is.EqualTo(0).Within(24));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_OpaqueColourSource_DeclaresNoAlpha() {
    var file = JngFile.FromRawImage(_Blocks(16, 16));

    Assert.Multiple(() => {
      Assert.That(file.ColorType, Is.EqualTo(10));
      Assert.That(file.AlphaSampleDepth, Is.Zero);
      Assert.That(file.AlphaData, Is.Null);
    });
  }
}
