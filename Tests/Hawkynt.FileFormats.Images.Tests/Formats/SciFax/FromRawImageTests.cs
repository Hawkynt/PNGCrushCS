using FileFormat.Core;
using FileFormat.SciFax;

namespace FileFormat.SciFax.Tests;

[TestFixture]
public sealed class FromRawImageTests {

  /// <summary>
  /// A picture already down to the two tones the format holds has to come back pixel for pixel.
  /// The width is deliberately not a multiple of eight: the rows are padded on disk but not in a
  /// <see cref="RawImage"/>, and an encoder that forgets the padding puts every row after the
  /// first out of step.
  /// </summary>
  [Test]
  [Category("Integration")]
  public void RoundTrip_BiLevelPicture_IsExact() {
    var source = _BiLevel(13, 5);

    var bytes = SciFaxWriter.ToBytes(SciFaxFile.FromRawImage(source));
    var back = SciFaxFile.ToRawImage(SciFaxReader.FromBytes(bytes));

    Assert.Multiple(() => {
      Assert.That(back.Width, Is.EqualTo(source.Width));
      Assert.That(back.Height, Is.EqualTo(source.Height));
      Assert.That(back.Format, Is.EqualTo(PixelFormat.Rgb24));
      Assert.That(back.PixelData, Is.EqualTo(source.PixelData));
    });
  }

  /// <summary>
  /// The header states its own size, so nothing has to be scaled to fit — but a picture that is
  /// neither the right shape nor down to two colours still has to be taken rather than refused.
  /// </summary>
  [Test]
  [Category("Integration")]
  public void FromRawImage_FullColourOddSize_IsAcceptedNotRefused() {
    var source = _Colour(37, 9);

    var file = SciFaxFile.FromRawImage(source);
    var back = SciFaxFile.ToRawImage(SciFaxReader.FromBytes(SciFaxWriter.ToBytes(file)));

    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(37));
      Assert.That(file.Height, Is.EqualTo(9));
      Assert.That(file.PixelData, Has.Length.EqualTo(5 * 9));
      Assert.That((back.Width, back.Height), Is.EqualTo((37, 9)));
    });
  }

  /// <summary>A diagonal, so a picture read a few bits out of step cannot match by accident.</summary>
  private static RawImage _BiLevel(int width, int height) {
    var rgb = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var tone = (x + y) % 3 == 0 ? (byte)0 : (byte)255;
      var o = (y * width + x) * 3;
      rgb[o] = rgb[o + 1] = rgb[o + 2] = tone;
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = rgb };
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
