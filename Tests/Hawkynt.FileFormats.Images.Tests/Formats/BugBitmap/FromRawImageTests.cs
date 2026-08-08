using FileFormat.Core;

namespace FileFormat.BugBitmap.Tests;

[TestFixture]
public sealed class FromRawImageTests {

  private const int _WIDTH = 160;
  private const int _HEIGHT = 200;

  /// <summary>
  /// A screen the VIC-II can hold exactly: colour 0 fills a quarter of every 4x8 cell so it wins the
  /// shared background register outright, and the cell's other three columns take three colours of
  /// its own, which is the most the hardware allows beside the background.
  /// </summary>
  private static RawImage _MulticolourScreen() {
    var pixels = new byte[_WIDTH * _HEIGHT * 3];

    for (var y = 0; y < _HEIGHT; ++y)
      for (var x = 0; x < _WIDTH; ++x) {
        var cell = y / 8 * 40 + x / 4;
        var column = x % 4;
        var index = column == 0 ? 0 : (cell * 3 + column - 1) % 15 + 1;
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
    var source = _MulticolourScreen();

    var restored = BugBitmapFile.ToRawImage(BugBitmapReader.FromBytes(BugBitmapWriter.ToBytes(BugBitmapFile.FromRawImage(source))));

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(_WIDTH));
      Assert.That(restored.Height, Is.EqualTo(_HEIGHT));
      Assert.That(restored.PixelData, Is.EqualTo(source.PixelData));
    });
  }

  /// <summary>The file is a fixed 10018 bytes with nowhere to state a size, so a picture of another
  /// size is sampled onto the screen rather than refused.</summary>
  [Test]
  [Category("Integration")]
  public void FromRawImage_AnyOtherSize_IsSampledOntoTheScreen([Values(16, 320, 640)] int width) {
    var file = BugBitmapFile.FromRawImage(_MulticolourScreen().SampleTo(width, width / 2));

    Assert.That(BugBitmapWriter.ToBytes(file), Has.Length.EqualTo(BugBitmapFile.ExpectedFileSize));
  }

  /// <summary>The background register is shared by the whole screen, so it has to be the colour that
  /// most of the screen wants.</summary>
  [Test]
  [Category("Unit")]
  public void FromRawImage_BackgroundIsTheCommonestColour() {
    Assert.That(BugBitmapFile.FromRawImage(_MulticolourScreen()).BackgroundColor, Is.Zero);
  }
}
