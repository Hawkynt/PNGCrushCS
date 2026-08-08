using FileFormat.Core;

namespace FileFormat.PixelPerfect.Tests;

[TestFixture]
public sealed class FromRawImageTests {

  private const int _WIDTH = 160;
  private const int _HEIGHT = 200;

  /// <summary>
  /// A screen within what this format can hold: there is no colour RAM and no background register,
  /// so bit patterns 00 and 11 both decode as black and only the screen byte's two nibbles name a
  /// colour. Every 4x8 cell therefore gets black plus two colours of its own.
  /// </summary>
  private static RawImage _ThreeColourScreen() {
    var pixels = new byte[_WIDTH * _HEIGHT * 3];

    for (var y = 0; y < _HEIGHT; ++y)
      for (var x = 0; x < _WIDTH; ++x) {
        var cell = y / 8 * 40 + x / 4;
        var column = x % 4;
        var index = column < 2 ? 0 : (cell * 2 + column - 2) % 15 + 1;
        var colour = Commodore64Graphics.HexColors[index];
        var offset = (y * _WIDTH + x) * 3;
        pixels[offset] = (byte)(colour >> 16);
        pixels[offset + 1] = (byte)(colour >> 8);
        pixels[offset + 2] = (byte)colour;
      }

    return new() { Width = _WIDTH, Height = _HEIGHT, Format = PixelFormat.Rgb24, PixelData = pixels };
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ScreenWithinTheCellColourLimit_ReturnsEveryPixelUnchanged() {
    var source = _ThreeColourScreen();

    var restored = PixelPerfectFile.ToRawImage(PixelPerfectReader.FromBytes(PixelPerfectWriter.ToBytes(PixelPerfectFile.FromRawImage(source))));

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(_WIDTH));
      Assert.That(restored.Height, Is.EqualTo(_HEIGHT));
      Assert.That(restored.PixelData, Is.EqualTo(source.PixelData));
    });
  }

  /// <summary>The decoder only looks at the screen bytes when the payload carries them, so a body
  /// one byte short would silently fall back to two colours for the whole screen.</summary>
  [Test]
  [Category("Unit")]
  public void FromRawImage_PayloadCarriesBitmapAndScreenRam() {
    Assert.That(PixelPerfectFile.FromRawImage(_ThreeColourScreen()).RawData,
      Has.Length.EqualTo(PixelPerfectFile.StandardPayloadSize));
  }

  [Test]
  [Category("Integration")]
  public void FromRawImage_AnyOtherSize_IsSampledOntoTheScreen([Values(24, 160, 512)] int width) {
    var file = PixelPerfectFile.FromRawImage(_ThreeColourScreen().SampleTo(width, width / 2));

    Assert.That(PixelPerfectWriter.ToBytes(file),
      Has.Length.EqualTo(PixelPerfectFile.LoadAddressSize + PixelPerfectFile.StandardPayloadSize));
  }
}
