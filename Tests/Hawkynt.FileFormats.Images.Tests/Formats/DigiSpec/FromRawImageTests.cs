using FileFormat.Core;

namespace FileFormat.DigiSpec.Tests;

[TestFixture]
public sealed class FromRawImageTests {

  private const int _WIDTH = 320;
  private const int _HEIGHT = 200;

  /// <summary>A screen of sixteen colours, each already on the ST's three-bit-per-channel grid,
  /// which is what the hardware can hold exactly.</summary>
  internal static RawImage StScreen() {
    var levels = new byte[8];
    for (var i = 0; i < 8; ++i)
      levels[i] = ChannelScaling.Expand3(i);

    var palette = new byte[16 * 3];
    for (var i = 0; i < 16; ++i) {
      palette[i * 3] = levels[i % 8];
      palette[i * 3 + 1] = levels[(i * 3 + 1) % 8];
      palette[i * 3 + 2] = levels[(i * 5 + 2) % 8];
    }

    var pixels = new byte[_WIDTH * _HEIGHT * 3];
    for (var y = 0; y < _HEIGHT; ++y)
      for (var x = 0; x < _WIDTH; ++x) {
        var entry = (x / 4 + y / 5) % 16;
        var offset = (y * _WIDTH + x) * 3;
        pixels[offset] = palette[entry * 3];
        pixels[offset + 1] = palette[entry * 3 + 1];
        pixels[offset + 2] = palette[entry * 3 + 2];
      }

    return new() { Width = _WIDTH, Height = _HEIGHT, Format = PixelFormat.Rgb24, PixelData = pixels };
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ScreenWithinTheHardwarePalette_ReturnsEveryPixelUnchanged() {
    var source = StScreen();

    var restored = DigiSpecFile.ToRawImage(DigiSpecReader.FromBytes(DigiSpecWriter.ToBytes(DigiSpecFile.FromRawImage(source))));

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(_WIDTH));
      Assert.That(restored.Height, Is.EqualTo(_HEIGHT));
      Assert.That(restored.EnsureFormat(PixelFormat.Rgb24).PixelData, Is.EqualTo(source.PixelData));
    });
  }

  /// <summary>The file is a fixed size with no field for one, so a picture of another size is
  /// sampled onto the screen rather than refused.</summary>
  [Test]
  [Category("Integration")]
  public void FromRawImage_AnyOtherSize_IsSampledOntoTheScreen([Values(64, 160, 800)] int width) {
    var source = StScreen();
    var scaled = source.SampleTo(width, width * 3 / 4);

    var file = DigiSpecFile.FromRawImage(scaled);

    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(_WIDTH));
      Assert.That(file.Height, Is.EqualTo(_HEIGHT));
      Assert.That(DigiSpecWriter.ToBytes(file), Has.Length.EqualTo(DigiSpecFile.FileSize));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_WritesSixteenPaletteEntriesWhateverThePictureUses() {
    var pixels = new byte[_WIDTH * _HEIGHT * 3];
    var source = new RawImage { Width = _WIDTH, Height = _HEIGHT, Format = PixelFormat.Rgb24, PixelData = pixels };

    Assert.That(DigiSpecFile.FromRawImage(source).Palette, Has.Length.EqualTo(16));
  }
}
