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

  /// <summary>
  /// Every multicolour pixel of a screen the format can hold survives the round trip, and the seams
  /// between them are the average of the pair they sit between.
  /// </summary>
  /// <remarks>
  /// The picture is 320 across because the two fields are half a multicolour pixel apart, so a
  /// 160-wide screen cannot come back column for column and it is not a defect that it does not: the
  /// odd columns are where both fields show the same multicolour pixel, and the even ones are where
  /// the shifted field is still showing the one to the left. Asserting both is what pins the shift,
  /// which is the thing a reader and a writer can quietly agree to leave out.
  /// </remarks>
  [Test]
  [Category("Integration")]
  public void RoundTrip_ScreenWithinTheCellColourLimit_ReturnsEveryMulticolourPixelUnchanged() {
    var source = _MulticolourScreen();
    var expected = source.PixelData;

    var file = TruePaintFile.FromRawImage(source);
    var restored = TruePaintFile.ToRawImage(TruePaintReader.FromBytes(TruePaintWriter.ToBytes(file)));
    var background = Commodore64Graphics.HexColors[file.BackgroundColor];

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(_WIDTH * 2));
      Assert.That(restored.Height, Is.EqualTo(_HEIGHT));

      for (var y = 0; y < _HEIGHT; ++y)
        for (var x = 0; x < _WIDTH; ++x) {
          var from = (y * _WIDTH + x) * 3;
          var odd = (y * _WIDTH * 2 + x * 2 + 1) * 3;
          var even = (y * _WIDTH * 2 + x * 2) * 3;

          for (var channel = 0; channel < 3; ++channel) {
            var shift = 16 - channel * 8;
            var left = x > 0 ? expected[from - 3 + channel] : (byte)(background >> shift);

            Assert.That(restored.PixelData[odd + channel], Is.EqualTo(expected[from + channel]),
              $"column {x * 2 + 1} of line {y}");
            Assert.That(restored.PixelData[even + channel], Is.EqualTo((byte)((expected[from + channel] + left) / 2)),
              $"column {x * 2} of line {y}");
          }
        }
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
