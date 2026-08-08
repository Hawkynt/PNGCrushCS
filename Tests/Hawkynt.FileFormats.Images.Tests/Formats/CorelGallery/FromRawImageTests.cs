using System;
using System.Text;
using FileFormat.Core;

namespace FileFormat.CorelGallery.Tests;

[TestFixture]
public sealed class FromRawImageTests {

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
  public void RoundTrip_Gradient_ReproducesEveryPixel() {
    var source = _Gradient(37, 11);
    var decoded = CorelGalleryFile.ToRawImage(CorelGalleryReader.FromBytes(CorelGalleryWriter.ToBytes(CorelGalleryFile.FromRawImage(source))));

    Assert.Multiple(() => {
      Assert.That((decoded.Width, decoded.Height), Is.EqualTo((37, 11)));
      Assert.That(PixelConverter.Convert(decoded, PixelFormat.Rgb24).PixelData, Is.EqualTo(source.PixelData));
    });
  }

  [Test]
  [Category("Integration")]
  public void FromRawImage_KeepsWhateverSizeItIsGiven() {
    var wide = CorelGalleryFile.ToRawImage(CorelGalleryReader.FromBytes(CorelGalleryWriter.ToBytes(CorelGalleryFile.FromRawImage(_Gradient(200, 3)))));
    var tall = CorelGalleryFile.ToRawImage(CorelGalleryReader.FromBytes(CorelGalleryWriter.ToBytes(CorelGalleryFile.FromRawImage(_Gradient(3, 200)))));

    Assert.Multiple(() => {
      Assert.That((wide.Width, wide.Height), Is.EqualTo((200, 3)));
      Assert.That((tall.Width, tall.Height), Is.EqualTo((3, 200)));
    });
  }

  [Test]
  [Category("Integration")]
  public void FromRawImage_AcceptsAFormatOtherThanItsOwn() {
    var grey = new RawImage { Width = 5, Height = 4, Format = PixelFormat.Gray8, PixelData = new byte[20] };
    var decoded = CorelGalleryFile.ToRawImage(CorelGalleryReader.FromBytes(CorelGalleryWriter.ToBytes(CorelGalleryFile.FromRawImage(grey))));

    Assert.That((decoded.Width, decoded.Height), Is.EqualTo((5, 4)));
  }

  /// <summary>
  /// The reader takes the bitmap at sixty-nine rather than searching for it, so the text in front of
  /// it has to be exactly the sixty-nine bytes every sample carries — a header one byte longer or
  /// shorter is a file nothing opens.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void ToBytes_PutsTheBitmapWhereTheFormatKeepsIt() {
    var bytes = CorelGalleryWriter.ToBytes(CorelGalleryFile.FromRawImage(_Gradient(37, 11)));
    var header = Encoding.ASCII.GetString(bytes, 0, CorelGalleryFile.PreviewOffset);

    Assert.Multiple(() => {
      Assert.That(bytes.AsSpan(0, CorelGalleryFile.Magic.Length).SequenceEqual(CorelGalleryFile.Magic), Is.True);
      Assert.That(header, Does.Contain("Corel Corporation"));
      Assert.That(bytes[CorelGalleryFile.PreviewOffset], Is.EqualTo(40), "and a bitmap info header stands at sixty-nine");
    });
  }
}
