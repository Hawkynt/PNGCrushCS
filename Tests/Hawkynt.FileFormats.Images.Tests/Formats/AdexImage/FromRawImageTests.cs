using System;
using FileFormat.Core;

namespace FileFormat.AdexImage.Tests;

[TestFixture]
public sealed class FromRawImageTests {

  /// <summary>A size that is neither square nor a multiple of anything, to catch a stride assumption.</summary>
  private static RawImage _Gradient(int width, int height) {
    var data = new byte[width * height * 3];
    for (var i = 0; i < width * height; ++i) {
      data[i * 3] = (byte)(i * 7);
      data[i * 3 + 1] = (byte)(i * 13);
      data[i * 3 + 2] = (byte)(i * 29);
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = data };
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Gradient_ReproducesExactly() {
    var source = _Gradient(37, 11);
    var file = AdexImageFile.FromRawImage(source);
    var restored = AdexImageReader.FromBytes(AdexImageWriter.ToBytes(file));
    var decoded = AdexImageFile.ToRawImage(restored);

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(37));
      Assert.That(decoded.Height, Is.EqualTo(11));
      Assert.That(decoded.PixelData, Is.EqualTo(source.PixelData));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_KeepsWhateverSizeItIsGiven() {
    // The header carries the dimensions, so there is nothing to scale a picture to.
    var wide = AdexImageFile.FromRawImage(_Gradient(200, 3));
    var tall = AdexImageFile.FromRawImage(_Gradient(3, 200));

    Assert.Multiple(() => {
      Assert.That((wide.Width, wide.Height), Is.EqualTo((200, 3)));
      Assert.That((tall.Width, tall.Height), Is.EqualTo((3, 200)));
      Assert.That(wide.Bpp, Is.EqualTo(24));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_AcceptsAFormatOtherThanItsOwn() {
    var gray = new RawImage {
      Width = 4, Height = 4, Format = PixelFormat.Gray8, PixelData = new byte[16]
    };

    var file = AdexImageFile.FromRawImage(gray);

    Assert.That(file.PixelData, Has.Length.EqualTo(4 * 4 * 3));
  }
}
