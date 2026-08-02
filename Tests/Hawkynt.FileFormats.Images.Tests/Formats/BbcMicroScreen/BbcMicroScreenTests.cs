using System;
using System.IO;
using FileFormat.Core;
using FileFormat.BbcMicroScreen;

namespace FileFormat.BbcMicroScreen.Tests;

[TestFixture]
public sealed class BbcMicroScreenTests {

  private static BbcMicroScreenFile _Sample(BbcMicroMode mode) {
    var data = new byte[BbcMicroScreenFile.FileSizeFor(mode)];
    for (var i = 0; i < data.Length; ++i)
      data[i] = (byte)(i * 29 % 256);

    return new() { Mode = mode, ScreenData = data };
  }

  [Test]
  [Category("Unit")]
  [TestCase(BbcMicroMode.Mode0, 20480)]
  [TestCase(BbcMicroMode.Mode1, 20480)]
  [TestCase(BbcMicroMode.Mode2, 20480)]
  [TestCase(BbcMicroMode.Mode4, 10240)]
  [TestCase(BbcMicroMode.Mode5, 10240)]
  public void FileSize_MatchesTheMode(BbcMicroMode mode, int expected)
    => Assert.That(BbcMicroScreenFile.FileSizeFor(mode), Is.EqualTo(expected));

  [Test]
  [Category("Unit")]
  [TestCase(BbcMicroMode.Mode0, 2)]
  [TestCase(BbcMicroMode.Mode1, 4)]
  [TestCase(BbcMicroMode.Mode2, 16)]
  [TestCase(BbcMicroMode.Mode4, 2)]
  [TestCase(BbcMicroMode.Mode5, 4)]
  public void ColorCount_FollowsTheBitDepth(BbcMicroMode mode, int expected)
    => Assert.That(BbcMicroScreenFile.ColorCount(mode), Is.EqualTo(expected));

  [Test]
  [Category("Unit")]
  [TestCase(BbcMicroMode.Mode0)]
  [TestCase(BbcMicroMode.Mode1)]
  [TestCase(BbcMicroMode.Mode2)]
  [TestCase(BbcMicroMode.Mode4)]
  [TestCase(BbcMicroMode.Mode5)]
  public void RoundTrip_PreservesScreenData(BbcMicroMode mode) {
    var original = _Sample(mode);
    var restored = BbcMicroScreenReader.FromBytes(BbcMicroScreenWriter.ToBytes(original), mode);

    Assert.That(restored.ScreenData, Is.EqualTo(original.ScreenData));
  }

  [Test]
  [Category("Unit")]
  [TestCase(BbcMicroMode.Mode0)]
  [TestCase(BbcMicroMode.Mode2)]
  [TestCase(BbcMicroMode.Mode4)]
  public void ToRawImage_ProducesTheDisplayedResolution(BbcMicroMode mode) {
    var raw = BbcMicroScreenFile.ToRawImage(_Sample(mode));

    Assert.Multiple(() => {
      Assert.That(raw.Width, Is.EqualTo(BbcMicroScreenFile.DisplayWidth(mode)));
      Assert.That(raw.Height, Is.EqualTo(BbcMicroScreenFile.DisplayHeight(mode)));
      Assert.That(raw.PaletteCount, Is.EqualTo(BbcMicroScreenFile.ColorCount(mode)));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_RejectsAnyOtherLength()
    => Assert.Throws<InvalidDataException>(() => BbcMicroScreenReader.FromBytes(new byte[1234]));

  [Test]
  [Category("Unit")]
  /// <summary>
  /// The mode is chosen from the picture's colours and size.
  /// </summary>
  /// <remarks>
  /// This used to assert mode 4 for everything, which is what the encoder wrote whatever it was
  /// given: every picture came out black and white at half the bytes the original held, and RECOIL
  /// would not read one back. The mode now follows the picture — two colours take the monochrome
  /// screen, four the one showing black, red, yellow and white, and more the sixteen-colour screen.
  /// </remarks>
  public void FromRawImage_ChoosesTheModeTheColoursCallFor() {
    static RawImage Picture(int width, int height, int colours) {
      var data = new byte[width * height * 4];
      for (var i = 0; i < data.Length; i += 4) {
        var shade = (byte)(i / 4 % colours * (255 / Math.Max(1, colours - 1)));
        data[i] = data[i + 1] = data[i + 2] = shade;
        data[i + 3] = 255;
      }

      return new() { Width = width, Height = height, Format = PixelFormat.Rgba32, PixelData = data };
    }

    Assert.Multiple(() => {
      Assert.That(BbcMicroScreenFile.FromRawImage(Picture(320, 256, 2)).Mode, Is.EqualTo(BbcMicroMode.Mode4));
      Assert.That(BbcMicroScreenFile.FromRawImage(Picture(320, 256, 8)).Mode, Is.EqualTo(BbcMicroMode.Mode2));
      Assert.That(BbcMicroScreenFile.FromRawImage(Picture(640, 512, 2)).Mode, Is.EqualTo(BbcMicroMode.Mode0),
        "only mode 0 draws 640 across");
    });
  }
}
