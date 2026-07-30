using System;
using System.IO;
using FileFormat.AtariTt;
using FileFormat.Core;

namespace FileFormat.AtariTt.Tests;

[TestFixture]
public sealed class AtariTtTests {

  /// <summary>A gradient, which needs far more than sixteen colours.</summary>
  private static RawImage _Gradient(int width, int height) {
    var data = new byte[width * height * 4];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var o = (y * width + x) * 4;
      data[o] = (byte)(x * 255 / (width - 1));
      data[o + 1] = (byte)(y * 255 / (height - 1));
      data[o + 2] = (byte)((x + y) % 256);
      data[o + 3] = 255;
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgba32, PixelData = data };
  }

  /// <summary>A picture drawn from a handful of colours, which fits the sixteen-colour mode.</summary>
  private static RawImage _Blocks(int width, int height) {
    var data = new byte[width * height * 4];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var o = (y * width + x) * 4;
      var shade = (byte)((x / 64 + y / 64) % 2 == 0 ? 255 : 0);
      data[o] = data[o + 1] = data[o + 2] = shade;
      data[o + 3] = 255;
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgba32, PixelData = data };
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_PicksTheWiderModeWhenSixteenColoursSuffice() {
    var file = AtariTtFile.FromRawImage(_Blocks(640, 480));

    Assert.That(file.Resolution, Is.EqualTo(AtariTtResolution.Medium));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_DropsToHalfWidthWhenThePictureNeedsMoreColours() {
    var file = AtariTtFile.FromRawImage(_Gradient(640, 480));

    Assert.That(file.Resolution, Is.EqualTo(AtariTtResolution.Low));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_UsesTheMonochromeModeAtItsOwnSize() {
    var file = AtariTtFile.FromRawImage(_Blocks(1280, 960));

    Assert.That(file.Resolution, Is.EqualTo(AtariTtResolution.High));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_RejectsOtherSizes()
    => Assert.Throws<ArgumentException>(() => AtariTtFile.FromRawImage(_Blocks(320, 200)));

  [TestCase(AtariTtResolution.Low, 154114)]
  [TestCase(AtariTtResolution.Medium, 153634)]
  [TestCase(AtariTtResolution.High, 153606)]
  [Category("Unit")]
  public void FileSizeFor_MatchesTheModesOnDisk(AtariTtResolution resolution, int expected)
    => Assert.That(AtariTtFile.FileSizeFor(resolution), Is.EqualTo(expected));

  [TestCase(640, 480)]
  [TestCase(1280, 960)]
  [Category("Unit")]
  public void RoundTrip_PreservesTheModeAndBitmap(int width, int height) {
    var file = AtariTtFile.FromRawImage(_Gradient(width, height));
    var restored = AtariTtReader.FromBytes(AtariTtWriter.ToBytes(file));

    Assert.Multiple(() => {
      Assert.That(restored.Resolution, Is.EqualTo(file.Resolution));
      Assert.That(restored.BitmapData, Is.EqualTo(file.BitmapData));
      Assert.That(restored.Palette, Is.EqualTo(file.Palette));
    });
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_ShowsTheHalfWidthModeAcrossTheFullScreen() {
    var raw = AtariTtFile.ToRawImage(AtariTtFile.FromRawImage(_Gradient(640, 480)));

    Assert.Multiple(() => {
      Assert.That(raw.Width, Is.EqualTo(640));
      Assert.That(raw.Height, Is.EqualTo(480));
      Assert.That(raw.PixelData[1], Is.EqualTo(raw.PixelData[0]), "each stored pixel covers two on screen");
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_RejectsAnUnknownMode() {
    var bytes = new byte[153634];
    bytes[1] = 3;

    Assert.Throws<InvalidDataException>(() => AtariTtReader.FromBytes(bytes));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_RejectsAWronglySizedFile() {
    var bytes = new byte[1024];
    bytes[1] = (byte)AtariTtResolution.Medium;

    Assert.Throws<InvalidDataException>(() => AtariTtReader.FromBytes(bytes));
  }

  [TestCase((short)0x0F00, (byte)255, (byte)0, (byte)0)]
  [TestCase((short)0x00F0, (byte)0, (byte)255, (byte)0)]
  [TestCase((short)0x000F, (byte)0, (byte)0, (byte)255)]
  [Category("Unit")]
  public void UnpackColor_SpreadsFourBitsAcrossAWholeByte(short packed, byte red, byte green, byte blue) {
    var rgb = new byte[3];
    AtariTtFile.UnpackColor(packed, rgb);

    Assert.That(rgb, Is.EqualTo(new[] { red, green, blue }));
  }
}
