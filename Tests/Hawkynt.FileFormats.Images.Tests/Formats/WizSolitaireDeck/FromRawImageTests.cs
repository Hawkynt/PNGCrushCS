using System;
using FileFormat.Core;
using FileFormat.WizSolitaireDeck;

namespace FileFormat.WizSolitaireDeck.Tests;

/// <summary>
/// The wrapper carries an ordinary picture, so writing one is the wrapper plus that picture.
/// </summary>
[TestFixture]
public sealed class FromRawImageTests {

  private static RawImage _Picture(int width = 17, int height = 9) {
    var pixels = new byte[width * height * 3];
    for (var i = 0; i < width * height; ++i) {
      pixels[i * 3] = (byte)(i * 7);
      pixels[i * 3 + 1] = (byte)(i * 13);
      pixels[i * 3 + 2] = (byte)(i * 31);
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = pixels };
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_EveryPixelComesBack() {
    // A width that is not a multiple of eight, because a wrapper that got the payload's length
    // wrong would still pass on a tidy one.
    var source = _Picture();

    var restored = WizSolitaireDeckReader.FromBytes(WizSolitaireDeckWriter.ToBytes(WizSolitaireDeckFile.FromRawImage(source)));
    var decoded = WizSolitaireDeckFile.ToRawImage(restored);

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(source.Width));
      Assert.That(decoded.Height, Is.EqualTo(source.Height));
      Assert.That(PixelConverter.Convert(decoded, PixelFormat.Rgb24).PixelData, Is.EqualTo(source.PixelData));
    });
  }

  [Test]
  [Category("Unit")]
  public void AnySizeIsAccepted() {
    // The wrapper states no size of its own, so it refuses none.
    var decoded = WizSolitaireDeckFile.ToRawImage(WizSolitaireDeckReader.FromBytes(WizSolitaireDeckWriter.ToBytes(WizSolitaireDeckFile.FromRawImage(_Picture(1, 1)))));

    Assert.That((decoded.Width, decoded.Height), Is.EqualTo((1, 1)));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => WizSolitaireDeckFile.FromRawImage(null!));
}
