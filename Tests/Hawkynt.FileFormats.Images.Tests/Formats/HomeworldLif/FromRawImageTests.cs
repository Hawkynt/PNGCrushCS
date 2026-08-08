using FileFormat.Core;

namespace FileFormat.HomeworldLif.Tests;

[TestFixture]
public sealed class FromRawImageTests {

  private static RawImage _Gradient(int width, int height) {
    var pixels = new byte[width * height * 3];
    for (var i = 0; i < width * height; ++i) {
      pixels[i * 3] = (byte)(i * 9 % 256);
      pixels[i * 3 + 1] = (byte)(i * 15 % 256);
      pixels[i * 3 + 2] = (byte)(i * 21 % 256);
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = pixels };
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Rgb24_ReturnsEveryPixelUnchanged() {
    var source = _Gradient(8, 4);

    var restored = HomeworldLifFile.ToRawImage(HomeworldLifReader.FromBytes(HomeworldLifWriter.ToBytes(HomeworldLifFile.FromRawImage(source))));

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(8));
      Assert.That(restored.Height, Is.EqualTo(4));
      Assert.That(restored.PixelData, Is.EqualTo(source.PixelData));
    });
  }

  /// <summary>The body is RGBA in that byte order, and a source without alpha gains an opaque one
  /// rather than having its colours shifted along by a byte.</summary>
  [Test]
  [Category("Unit")]
  public void FromRawImage_OpaqueSource_StoresRgbaInThatOrder() {
    var source = new RawImage { Width = 1, Height = 1, Format = PixelFormat.Rgb24, PixelData = [0x11, 0x22, 0x33] };

    var file = HomeworldLifFile.FromRawImage(source);

    Assert.That(file.PixelData, Is.EqualTo(new byte[] { 0x11, 0x22, 0x33, 0xFF }));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_KeepsTheAlphaItIsGiven() {
    var source = new RawImage { Width = 1, Height = 1, Format = PixelFormat.Rgba32, PixelData = [1, 2, 3, 0x80] };

    Assert.That(HomeworldLifFile.FromRawImage(source).PixelData[3], Is.EqualTo(0x80));
  }
}
