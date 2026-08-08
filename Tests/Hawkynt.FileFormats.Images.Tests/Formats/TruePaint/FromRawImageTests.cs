using FileFormat.Core;

namespace FileFormat.TruePaint.Tests;

[TestFixture]
public sealed class FromRawImageTests {

  private const int _WIDTH = 160;
  private const int _HEIGHT = 200;

  /// <summary>
  /// A screen within one field's limits: colour 0 fills a quarter of every 4x8 cell and so wins the
  /// shared background register, leaving the cell's other three columns to the three registers the
  /// hardware gives it.
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

    var restored = TruePaintFile.ToRawImage(TruePaintReader.FromBytes(TruePaintWriter.ToBytes(TruePaintFile.FromRawImage(source))));

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(_WIDTH));
      Assert.That(restored.Height, Is.EqualTo(_HEIGHT));
      Assert.That(restored.PixelData, Is.EqualTo(source.PixelData));
    });
  }

  /// <summary>
  /// The decoder averages the two fields, so a still picture only survives if both fields hold it.
  /// Two different fields would buy extra apparent colours and never reproduce the original.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void FromRawImage_StillPicture_WritesTheSameScreenIntoBothFields() {
    var file = TruePaintFile.FromRawImage(_MulticolourScreen());

    Assert.Multiple(() => {
      Assert.That(file.BitmapData2, Is.EqualTo(file.BitmapData1));
      Assert.That(file.ScreenRam2, Is.EqualTo(file.ScreenRam1));
    });
  }

  [Test]
  [Category("Integration")]
  public void FromRawImage_AnyOtherSize_IsSampledOntoTheScreen([Values(20, 320, 800)] int width) {
    var file = TruePaintFile.FromRawImage(_MulticolourScreen().SampleTo(width, width / 4));

    Assert.That(TruePaintWriter.ToBytes(file), Has.Length.EqualTo(TruePaintFile.ExpectedFileSize));
  }
}
