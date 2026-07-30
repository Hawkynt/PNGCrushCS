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
  public void FromRawImage_ProducesAMode4Screen() {
    var width = BbcMicroScreenFile.DisplayWidth(BbcMicroMode.Mode4);
    var height = BbcMicroScreenFile.DisplayHeight(BbcMicroMode.Mode4);
    var data = new byte[width * height * 4];
    for (var i = 0; i < data.Length; i += 4) {
      data[i] = data[i + 1] = data[i + 2] = (byte)(i % 251);
      data[i + 3] = 255;
    }

    var raw = new RawImage { Width = width, Height = height, Format = PixelFormat.Rgba32, PixelData = data };
    var file = BbcMicroScreenFile.FromRawImage(raw);

    Assert.Multiple(() => {
      Assert.That(file.Mode, Is.EqualTo(BbcMicroMode.Mode4));
      Assert.That(BbcMicroScreenWriter.ToBytes(file), Has.Length.EqualTo(BbcMicroScreenFile.FileSizeFor(BbcMicroMode.Mode4)));
    });
  }
}
