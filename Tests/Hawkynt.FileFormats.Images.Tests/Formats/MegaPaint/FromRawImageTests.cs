using FileFormat.Core;

namespace FileFormat.MegaPaint.Tests;

[TestFixture]
public sealed class FromRawImageTests {

  private static RawImage _Stripes(int width, int height) {
    var pixels = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var value = (x / 3 + y) % 2 == 0 ? (byte)0 : (byte)255;
        var offset = (y * width + x) * 3;
        pixels[offset] = value;
        pixels[offset + 1] = value;
        pixels[offset + 2] = value;
      }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = pixels };
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_TwoTonePicture_ReturnsEveryPixelUnchanged() {
    var source = _Stripes(24, 9);

    var restored = MegaPaintFile.ToRawImage(MegaPaintReader.FromBytes(MegaPaintWriter.ToBytes(MegaPaintFile.FromRawImage(source))));

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(24));
      Assert.That(restored.Height, Is.EqualTo(9));
      Assert.That(restored.PixelData, Is.EqualTo(source.PixelData));
    });
  }

  /// <summary>The header states the last column and row rather than the counts, so a size written
  /// as a count would come back one pixel too large in each direction.</summary>
  [Test]
  [Category("Integration")]
  public void RoundTrip_HeaderStatesTheLastColumnAndRow() {
    var bytes = MegaPaintWriter.ToBytes(MegaPaintFile.FromRawImage(_Stripes(480, 17)));

    Assert.Multiple(() => {
      Assert.That((bytes[0] << 8) | bytes[1], Is.EqualTo(479));
      Assert.That((bytes[2] << 8) | bytes[3], Is.EqualTo(16));
      Assert.That(MegaPaintReader.FromBytes(bytes).Width, Is.EqualTo(480));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_BlackPixel_SetsItsBit() {
    var source = new RawImage {
      Width = 8, Height = 1, Format = PixelFormat.Gray8, PixelData = [255, 255, 255, 255, 255, 255, 255, 0]
    };

    Assert.That(MegaPaintFile.FromRawImage(source).PixelData[0], Is.EqualTo(0x01));
  }
}
