using System;
using FileFormat.Core;

namespace FileFormat.InShape.Tests;

[TestFixture]
public sealed class FromRawImageTests {

  /// <summary>A width that is not a multiple of eight, to catch a stride assumption.</summary>
  private const int _WIDTH = 37;

  private const int _HEIGHT = 11;

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
  public void RoundTrip_TrueColor_ReproducesExactly() {
    var source = _Gradient(_WIDTH, _HEIGHT);
    var file = InShapeFile.FromRawImage(source);
    var decoded = InShapeFile.ToRawImage(InShapeReader.FromBytes(InShapeWriter.ToBytes(file)));

    Assert.Multiple(() => {
      Assert.That((decoded.Width, decoded.Height), Is.EqualTo((_WIDTH, _HEIGHT)));
      Assert.That(decoded.PixelData, Is.EqualTo(source.PixelData));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_KeepsWhateverSizeItIsGiven() {
    // The header carries the size, so any picture is stored as it stands.
    var wide = InShapeFile.FromRawImage(_Gradient(200, 3));
    var tall = InShapeFile.FromRawImage(_Gradient(3, 200));

    Assert.Multiple(() => {
      Assert.That((wide.Width, wide.Height), Is.EqualTo((200, 3)));
      Assert.That((tall.Width, tall.Height), Is.EqualTo((3, 200)));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_ChoosesTrueColorOverTheInvertedGreyscale() {
    // A grey picture would fit the greyscale form, whose samples run the other way; a reader that
    // does not know that shows the negative, so the form that means what it says is written instead.
    var gray = new RawImage { Width = 4, Height = 4, Format = PixelFormat.Gray8, PixelData = new byte[16] };
    var file = InShapeFile.FromRawImage(gray);

    Assert.Multiple(() => {
      Assert.That(file.Mode, Is.EqualTo(InShapeFile.TrueColorMode));
      Assert.That(file.Data, Has.Length.EqualTo(InShapeFile.PixelsOffset + 4 * 4 * 3));
    });
  }
}
