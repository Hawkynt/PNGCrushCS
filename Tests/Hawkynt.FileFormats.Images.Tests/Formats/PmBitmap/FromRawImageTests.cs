using FileFormat.Core;

namespace FileFormat.PmBitmap.Tests;

[TestFixture]
public sealed class FromRawImageTests {

  private static RawImage _Gradient(int width, int height) {
    var pixels = new byte[width * height * 3];
    for (var i = 0; i < width * height; ++i) {
      pixels[i * 3] = (byte)(i * 3 % 256);
      pixels[i * 3 + 1] = (byte)(i * 37 % 256);
      pixels[i * 3 + 2] = (byte)(i * 41 % 256);
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = pixels };
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Rgb24_ReturnsEveryPixelUnchanged() {
    var source = _Gradient(6, 9);

    var restored = PmBitmapFile.ToRawImage(PmBitmapReader.FromBytes(PmBitmapWriter.ToBytes(PmBitmapFile.FromRawImage(source))));

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(6));
      Assert.That(restored.Height, Is.EqualTo(9));
      Assert.That(restored.PixelData, Is.EqualTo(source.PixelData));
    });
  }

  /// <summary>PM holds greyscale as itself; flattening it to RGB would triple the file for nothing.</summary>
  [Test]
  [Category("Integration")]
  public void RoundTrip_Gray8_StaysEightBitsDeepAndKeepsItsTones() {
    var source = new RawImage {
      Width = 4, Height = 1, Format = PixelFormat.Gray8, PixelData = [0, 0x55, 0xAA, 0xFF]
    };

    var file = PmBitmapFile.FromRawImage(source);
    var restored = PmBitmapFile.ToRawImage(PmBitmapReader.FromBytes(PmBitmapWriter.ToBytes(file)));

    Assert.Multiple(() => {
      Assert.That(file.Depth, Is.EqualTo(8));
      Assert.That(restored.PixelData, Is.EqualTo(new byte[] {
        0, 0, 0, 0x55, 0x55, 0x55, 0xAA, 0xAA, 0xAA, 0xFF, 0xFF, 0xFF
      }));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_ColourSource_IsTwentyFourBitsDeep() {
    Assert.That(PmBitmapFile.FromRawImage(_Gradient(3, 3)).Depth, Is.EqualTo(24));
  }
}
