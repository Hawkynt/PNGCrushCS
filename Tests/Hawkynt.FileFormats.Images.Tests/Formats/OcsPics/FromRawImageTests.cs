using FileFormat.Core;

namespace FileFormat.OcsPics.Tests;

[TestFixture]
public sealed class FromRawImageTests {

  private const int _WIDTH = 320;
  private const int _HEIGHT = 200;

  /// <summary>A screen of sixteen colours, each already on the ST's three-bit-per-channel grid.</summary>
  private static RawImage _StScreen() {
    var levels = new byte[8];
    for (var i = 0; i < 8; ++i)
      levels[i] = ChannelScaling.Expand3(i);

    var pixels = new byte[_WIDTH * _HEIGHT * 3];
    for (var y = 0; y < _HEIGHT; ++y)
      for (var x = 0; x < _WIDTH; ++x) {
        var entry = (x + y) % 16;
        var offset = (y * _WIDTH + x) * 3;
        pixels[offset] = levels[entry % 8];
        pixels[offset + 1] = levels[(entry * 3 + 1) % 8];
        pixels[offset + 2] = levels[(entry * 5 + 2) % 8];
      }

    return new() { Width = _WIDTH, Height = _HEIGHT, Format = PixelFormat.Rgb24, PixelData = pixels };
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ScreenWithinTheHardwarePalette_ReturnsEveryPixelUnchanged() {
    var source = _StScreen();

    var restored = OcsPicsFile.ToRawImage(OcsPicsReader.FromBytes(OcsPicsWriter.ToBytes(OcsPicsFile.FromRawImage(source))));

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
  public void FromRawImage_AnyOtherSize_IsSampledOntoTheScreen([Values(32, 320, 640)] int width) {
    var file = OcsPicsFile.FromRawImage(_StScreen().SampleTo(width, width));

    Assert.Multiple(() => {
      Assert.That(file.Height, Is.EqualTo(_HEIGHT));
      Assert.That(OcsPicsWriter.ToBytes(file), Has.Length.EqualTo(OcsPicsFile.FileSize));
    });
  }

  /// <summary>Every entry the writer emits is a nine-bit ST colour, three bits a channel.</summary>
  [Test]
  [Category("Unit")]
  public void FromRawImage_PaletteEntriesStayWithinNineBits() {
    foreach (var entry in OcsPicsFile.FromRawImage(_StScreen()).Palette)
      Assert.That(entry & ~0x0777, Is.Zero);
  }
}
