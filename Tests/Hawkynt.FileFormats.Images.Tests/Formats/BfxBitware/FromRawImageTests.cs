using FileFormat.Core;

namespace FileFormat.BfxBitware.Tests;

[TestFixture]
public sealed class FromRawImageTests {

  /// <summary>Black and white only, which is all a fax page can hold.</summary>
  private static RawImage _Checkerboard(int width, int height) {
    var pixels = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var value = (x + y) % 2 == 0 ? (byte)0 : (byte)255;
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
    var source = _Checkerboard(19, 7);

    var restored = BfxBitwareFile.ToRawImage(BfxBitwareReader.FromBytes(BfxBitwareWriter.ToBytes(BfxBitwareFile.FromRawImage(source))));

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(19));
      Assert.That(restored.Height, Is.EqualTo(7));
      Assert.That(restored.PixelData, Is.EqualTo(source.PixelData));
    });
  }

  /// <summary>A fax is ink on paper, so the set bit is the black one.</summary>
  [Test]
  [Category("Unit")]
  public void FromRawImage_BlackPixel_SetsItsBit() {
    var source = new RawImage {
      Width = 8, Height = 1, Format = PixelFormat.Gray8, PixelData = [0, 255, 255, 255, 255, 255, 255, 255]
    };

    Assert.That(BfxBitwareFile.FromRawImage(source).PixelData[0], Is.EqualTo(0x80));
  }

  /// <summary>Rows are padded to a byte, so a width that is not a multiple of eight still works.</summary>
  [Test]
  [Category("Unit")]
  public void FromRawImage_WidthNotAMultipleOfEight_PadsEachRow() {
    var file = BfxBitwareFile.FromRawImage(_Checkerboard(11, 4));

    Assert.That(file.PixelData, Has.Length.EqualTo(2 * 4));
  }
}
