using FileFormat.Core;

namespace FileFormat.Mng.Tests;

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

  /// <summary>A frame is a whole PNG, so nothing about the picture is lost on the way through.</summary>
  [Test]
  [Category("Integration")]
  public void RoundTrip_Rgb24_ReturnsEveryPixelUnchanged() {
    var source = _Gradient(23, 11);

    var restored = MngFile.ToRawImage(MngReader.FromBytes(MngWriter.ToBytes(MngFile.FromRawImage(source))));

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(23));
      Assert.That(restored.Height, Is.EqualTo(11));
      Assert.That(restored.EnsureFormat(PixelFormat.Rgb24).PixelData, Is.EqualTo(source.PixelData));
    });
  }

  /// <summary>A still picture is one frame; the header has to agree, since a reader takes the size
  /// from MHDR rather than from the frame.</summary>
  [Test]
  [Category("Integration")]
  public void RoundTrip_StillPicture_IsOneFrameAndTheHeaderSaysSo() {
    var restored = MngReader.FromBytes(MngWriter.ToBytes(MngFile.FromRawImage(_Gradient(8, 5))));

    Assert.Multiple(() => {
      Assert.That(restored.Frames, Has.Count.EqualTo(1));
      Assert.That(restored.Width, Is.EqualTo(8));
      Assert.That(restored.Height, Is.EqualTo(5));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Rgba32_KeepsItsAlpha() {
    var pixels = new byte[] { 1, 2, 3, 0x40, 4, 5, 6, 0xC0 };
    var source = new RawImage { Width = 2, Height = 1, Format = PixelFormat.Rgba32, PixelData = pixels };

    var restored = MngFile.ToRawImage(MngReader.FromBytes(MngWriter.ToBytes(MngFile.FromRawImage(source))));

    Assert.That(restored.EnsureFormat(PixelFormat.Rgba32).PixelData, Is.EqualTo(pixels));
  }
}
