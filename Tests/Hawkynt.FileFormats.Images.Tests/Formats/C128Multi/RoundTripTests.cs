using System;
using System.IO;
using FileFormat.C128Multi;

namespace FileFormat.C128Multi.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllFieldsPreserved() {
    var bitmap = new byte[C128MultiFile.BitmapDataSize];
    var screen = new byte[C128MultiFile.ScreenDataSize];
    var color = new byte[C128MultiFile.ColorDataSize];
    for (var i = 0; i < bitmap.Length; ++i)
      bitmap[i] = (byte)(i * 3 % 256);
    for (var i = 0; i < screen.Length; ++i)
      screen[i] = (byte)(i * 7 % 256);
    for (var i = 0; i < color.Length; ++i)
      color[i] = (byte)(i * 11 % 256);

    var original = new C128MultiFile {
      BitmapData = bitmap,
      ScreenData = screen,
      ColorData = color,
      BackgroundColor = 9,
    };

    var bytes = C128MultiWriter.ToBytes(original);
    var restored = C128MultiReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.BitmapData, Is.EqualTo(original.BitmapData));
    Assert.That(restored.ScreenData, Is.EqualTo(original.ScreenData));
    Assert.That(restored.ColorData, Is.EqualTo(original.ColorData));
    Assert.That(restored.BackgroundColor, Is.EqualTo(original.BackgroundColor));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllZeros() {
    var original = new C128MultiFile {
      BitmapData = new byte[C128MultiFile.BitmapDataSize],
      ScreenData = new byte[C128MultiFile.ScreenDataSize],
      ColorData = new byte[C128MultiFile.ColorDataSize],
      BackgroundColor = 0,
    };

    var bytes = C128MultiWriter.ToBytes(original);
    var restored = C128MultiReader.FromBytes(bytes);

    Assert.That(restored.BitmapData, Is.EqualTo(original.BitmapData));
    Assert.That(restored.ScreenData, Is.EqualTo(original.ScreenData));
    Assert.That(restored.ColorData, Is.EqualTo(original.ColorData));
    Assert.That(restored.BackgroundColor, Is.EqualTo(0));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllMaxValues() {
    var bitmap = new byte[C128MultiFile.BitmapDataSize];
    var screen = new byte[C128MultiFile.ScreenDataSize];
    var color = new byte[C128MultiFile.ColorDataSize];
    for (var i = 0; i < bitmap.Length; ++i)
      bitmap[i] = 0xFF;
    for (var i = 0; i < screen.Length; ++i)
      screen[i] = 0xFF;
    for (var i = 0; i < color.Length; ++i)
      color[i] = 0xFF;

    var original = new C128MultiFile {
      BitmapData = bitmap,
      ScreenData = screen,
      ColorData = color,
      BackgroundColor = 15,
    };

    var bytes = C128MultiWriter.ToBytes(original);
    var restored = C128MultiReader.FromBytes(bytes);

    Assert.That(restored.BitmapData, Is.EqualTo(original.BitmapData));
    Assert.That(restored.ScreenData, Is.EqualTo(original.ScreenData));
    Assert.That(restored.ColorData, Is.EqualTo(original.ColorData));
    Assert.That(restored.BackgroundColor, Is.EqualTo(15));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var bitmap = new byte[C128MultiFile.BitmapDataSize];
    var screen = new byte[C128MultiFile.ScreenDataSize];
    var color = new byte[C128MultiFile.ColorDataSize];
    for (var i = 0; i < bitmap.Length; ++i)
      bitmap[i] = (byte)(i * 13 % 256);
    for (var i = 0; i < screen.Length; ++i)
      screen[i] = (byte)(i * 17 % 256);
    for (var i = 0; i < color.Length; ++i)
      color[i] = (byte)(i * 19 % 256);

    var original = new C128MultiFile {
      BitmapData = bitmap,
      ScreenData = screen,
      ColorData = color,
      BackgroundColor = 3,
    };

    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".c1m");
    try {
      var bytes = C128MultiWriter.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);

      var restored = C128MultiReader.FromFile(new FileInfo(tempPath));

      Assert.That(restored.Width, Is.EqualTo(original.Width));
      Assert.That(restored.Height, Is.EqualTo(original.Height));
      Assert.That(restored.BitmapData, Is.EqualTo(original.BitmapData));
      Assert.That(restored.ScreenData, Is.EqualTo(original.ScreenData));
      Assert.That(restored.ColorData, Is.EqualTo(original.ColorData));
      Assert.That(restored.BackgroundColor, Is.EqualTo(original.BackgroundColor));
    } finally {
      if (File.Exists(tempPath))
        File.Delete(tempPath);
    }
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_BackgroundColorPreserved() {
    for (byte bg = 0; bg < 16; ++bg) {
      var original = new C128MultiFile {
        BitmapData = new byte[C128MultiFile.BitmapDataSize],
        ScreenData = new byte[C128MultiFile.ScreenDataSize],
        ColorData = new byte[C128MultiFile.ColorDataSize],
        BackgroundColor = bg,
      };

      var bytes = C128MultiWriter.ToBytes(original);
      var restored = C128MultiReader.FromBytes(bytes);

      Assert.That(restored.BackgroundColor, Is.EqualTo(bg), $"Background color {bg} not preserved");
    }
  }

  [Test]
  [Category("Integration")]
  public void ToRawImage_ReturnsRgb24() {
    var file = new C128MultiFile {
      BitmapData = new byte[C128MultiFile.BitmapDataSize],
      ScreenData = new byte[C128MultiFile.ScreenDataSize],
      ColorData = new byte[C128MultiFile.ColorDataSize],
      BackgroundColor = 0,
    };

    var raw = C128MultiFile.ToRawImage(file);

    Assert.That(raw.Format, Is.EqualTo(FileFormat.Core.PixelFormat.Rgb24));
    Assert.That(raw.Width, Is.EqualTo(160));
    Assert.That(raw.Height, Is.EqualTo(200));
    Assert.That(raw.PixelData.Length, Is.EqualTo(160 * 200 * 3));
  }

  [Test]
  [Category("Integration")]
  public void ToRawImage_AllZeroBitmapUsesBackgroundColor() {
    var file = new C128MultiFile {
      BitmapData = new byte[C128MultiFile.BitmapDataSize],
      ScreenData = new byte[C128MultiFile.ScreenDataSize],
      ColorData = new byte[C128MultiFile.ColorDataSize],
      BackgroundColor = 1,
    };

    var raw = C128MultiFile.ToRawImage(file);

    // Background color 1 = white (0xFF, 0xFF, 0xFF) in C64 palette
    Assert.That(raw.PixelData[0], Is.EqualTo(0xFF));
    Assert.That(raw.PixelData[1], Is.EqualTo(0xFF));
    Assert.That(raw.PixelData[2], Is.EqualTo(0xFF));
  }
}
