using FileFormat.Core;

namespace FileFormat.SeqImage.Tests;

[TestFixture]
public sealed class FromRawImageTests {

  private static RawImage _Gradient(int width, int height) {
    var pixels = new byte[width * height * 3];
    for (var i = 0; i < width * height; ++i) {
      pixels[i * 3] = (byte)(i * 11 % 256);
      pixels[i * 3 + 1] = (byte)(i * 17 % 256);
      pixels[i * 3 + 2] = (byte)(i * 23 % 256);
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = pixels };
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Rgb24_ReturnsEveryPixelUnchanged() {
    var source = _Gradient(9, 6);

    var restored = SeqImageFile.ToRawImage(SeqImageReader.FromBytes(SeqImageWriter.ToBytes(SeqImageFile.FromRawImage(source))));

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(9));
      Assert.That(restored.Height, Is.EqualTo(6));
      Assert.That(restored.PixelData, Is.EqualTo(source.PixelData));
    });
  }

  /// <summary>A still picture is one frame, and the header has to say so or a reader counts none.</summary>
  [Test]
  [Category("Unit")]
  public void FromRawImage_StillPicture_DeclaresOneFrameAtTwentyFourBits() {
    var file = SeqImageFile.FromRawImage(_Gradient(4, 4));

    Assert.Multiple(() => {
      Assert.That(file.FrameCount, Is.EqualTo(1));
      Assert.That(file.Bpp, Is.EqualTo(24));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Gray8Source_IsConvertedRatherThanRefused() {
    var source = new RawImage { Width = 2, Height = 1, Format = PixelFormat.Gray8, PixelData = [0x40, 0x80] };

    var file = SeqImageFile.FromRawImage(source);

    Assert.That(file.PixelData, Is.EqualTo(new byte[] { 0x40, 0x40, 0x40, 0x80, 0x80, 0x80 }));
  }
}
