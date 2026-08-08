using FileFormat.Core;
using FileFormat.Rlc2;

namespace FileFormat.Rlc2.Tests;

[TestFixture]
public sealed class FromRawImageTests {

  /// <summary>
  /// The body is plain Rgb24 both ways, so a full-colour picture has to come back byte for byte.
  /// </summary>
  [Test]
  [Category("Integration")]
  public void RoundTrip_ColourPicture_IsExact() {
    var source = _Colour(13, 5);
    var rgb = PixelConverter.Convert(source, PixelFormat.Rgb24);

    var file = Rlc2File.FromRawImage(source);
    var back = Rlc2File.ToRawImage(Rlc2Reader.FromBytes(Rlc2Writer.ToBytes(file)));

    Assert.Multiple(() => {
      Assert.That(back.Width, Is.EqualTo(13));
      Assert.That(back.Height, Is.EqualTo(5));
      Assert.That(back.Format, Is.EqualTo(PixelFormat.Rgb24));
      Assert.That(back.PixelData, Is.EqualTo(rgb.PixelData));
      Assert.That(file.Bpp, Is.EqualTo(24));
    });
  }

  /// <summary>
  /// The header states its own size, so an awkward one is taken as it is rather than refused.
  /// </summary>
  [Test]
  [Category("Integration")]
  public void FromRawImage_OddSize_IsAcceptedNotRefused() {
    var file = Rlc2File.FromRawImage(_Colour(37, 9));

    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(37));
      Assert.That(file.Height, Is.EqualTo(9));
      Assert.That(file.PixelData, Has.Length.EqualTo(37 * 9 * 3));
    });
  }

  private static RawImage _Colour(int width, int height) {
    var rgba = new byte[width * height * 4];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var o = (y * width + x) * 4;
      rgba[o] = (byte)(x * 7);
      rgba[o + 1] = (byte)(y * 29);
      rgba[o + 2] = (byte)(x * y);
      rgba[o + 3] = 255;
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgba32, PixelData = rgba };
  }
}
