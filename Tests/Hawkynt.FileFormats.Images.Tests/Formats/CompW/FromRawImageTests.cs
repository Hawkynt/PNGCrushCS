using FileFormat.Core;

namespace FileFormat.CompW.Tests;

[TestFixture]
public sealed class FromRawImageTests {

  /// <summary>A picture of at most 256 colours, which is what an eight-bit palette can hold exactly.</summary>
  private static RawImage _TwoHundredFiftySixColours(int width, int height) {
    var pixels = new byte[width * height * 3];
    for (var i = 0; i < width * height; ++i) {
      var value = (byte)(i % 256);
      pixels[i * 3] = value;
      pixels[i * 3 + 1] = (byte)(255 - value);
      pixels[i * 3 + 2] = (byte)(value / 2);
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = pixels };
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_PictureWithinThePaletteSize_ReturnsEveryPixelUnchanged() {
    var source = _TwoHundredFiftySixColours(32, 8);

    var restored = CompWFile.ToRawImage(CompWReader.FromBytes(CompWWriter.ToBytes(CompWFile.FromRawImage(source))));

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(32));
      Assert.That(restored.Height, Is.EqualTo(8));
      Assert.That(restored.PixelData, Is.EqualTo(source.PixelData));
    });
  }

  /// <summary>The reader slices a fixed 768 bytes off the end of the file, so a shorter table would
  /// put the tail of the pixels into the palette.</summary>
  [Test]
  [Category("Unit")]
  public void FromRawImage_FewColours_StillWritesTheFullPaletteSlot() {
    var source = new RawImage {
      Width = 2, Height = 1, Format = PixelFormat.Rgb24, PixelData = [1, 2, 3, 4, 5, 6]
    };

    Assert.That(CompWFile.FromRawImage(source).Palette, Has.Length.EqualTo(768));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_OddSizes_AreKept([Values(1, 5, 129)] int width) {
    Assert.That(CompWFile.FromRawImage(_TwoHundredFiftySixColours(width, 3)).Width, Is.EqualTo(width));
  }
}
