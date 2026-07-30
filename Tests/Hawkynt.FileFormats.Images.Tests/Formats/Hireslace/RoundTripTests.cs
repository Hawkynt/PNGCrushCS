using System;
using System.IO;
using FileFormat.Hireslace;

namespace FileFormat.Hireslace.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllZeros() {
    var original = new HireslaceFile {
      LoadAddress = 0x2000,
      Bitmap1 = new byte[HireslaceFile.BitmapDataSize],
      Screen1 = new byte[HireslaceFile.ScreenDataSize],
      Bitmap2 = new byte[HireslaceFile.BitmapDataSize],
      Screen2 = new byte[HireslaceFile.ScreenDataSize],
    };

    var bytes = HireslaceWriter.ToBytes(original);
    var restored = HireslaceReader.FromBytes(bytes);

    Assert.That(restored.LoadAddress, Is.EqualTo(original.LoadAddress));
    Assert.That(restored.Bitmap1, Is.EqualTo(original.Bitmap1));
    Assert.That(restored.Screen1, Is.EqualTo(original.Screen1));
    Assert.That(restored.Bitmap2, Is.EqualTo(original.Bitmap2));
    Assert.That(restored.Screen2, Is.EqualTo(original.Screen2));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_PatternData() {
    var bitmap1 = new byte[HireslaceFile.BitmapDataSize];
    var screen1 = new byte[HireslaceFile.ScreenDataSize];
    var bitmap2 = new byte[HireslaceFile.BitmapDataSize];
    var screen2 = new byte[HireslaceFile.ScreenDataSize];

    for (var i = 0; i < bitmap1.Length; ++i)
      bitmap1[i] = (byte)(i % 256);
    for (var i = 0; i < screen1.Length; ++i)
      screen1[i] = (byte)((i * 3) % 256);
    for (var i = 0; i < bitmap2.Length; ++i)
      bitmap2[i] = (byte)((i * 7) % 256);
    for (var i = 0; i < screen2.Length; ++i)
      screen2[i] = (byte)((i * 13) % 256);

    var original = new HireslaceFile {
      LoadAddress = 0x4000,
      Bitmap1 = bitmap1,
      Screen1 = screen1,
      Bitmap2 = bitmap2,
      Screen2 = screen2,
    };

    var bytes = HireslaceWriter.ToBytes(original);
    var restored = HireslaceReader.FromBytes(bytes);

    Assert.That(restored.LoadAddress, Is.EqualTo(0x4000));
    Assert.That(restored.Bitmap1, Is.EqualTo(original.Bitmap1));
    Assert.That(restored.Screen1, Is.EqualTo(original.Screen1));
    Assert.That(restored.Bitmap2, Is.EqualTo(original.Bitmap2));
    Assert.That(restored.Screen2, Is.EqualTo(original.Screen2));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var bitmap1 = new byte[HireslaceFile.BitmapDataSize];
    bitmap1[0] = 0xAA;
    var screen1 = new byte[HireslaceFile.ScreenDataSize];
    screen1[0] = 0xBB;

    var original = new HireslaceFile {
      LoadAddress = 0x2000,
      Bitmap1 = bitmap1,
      Screen1 = screen1,
      Bitmap2 = new byte[HireslaceFile.BitmapDataSize],
      Screen2 = new byte[HireslaceFile.ScreenDataSize],
    };

    var tmpPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".hle");
    try {
      var bytes = HireslaceWriter.ToBytes(original);
      File.WriteAllBytes(tmpPath, bytes);

      var restored = HireslaceReader.FromFile(new FileInfo(tmpPath));
      Assert.That(restored.LoadAddress, Is.EqualTo(original.LoadAddress));
      Assert.That(restored.Bitmap1, Is.EqualTo(original.Bitmap1));
      Assert.That(restored.Screen1, Is.EqualTo(original.Screen1));
    } finally {
      if (File.Exists(tmpPath))
        File.Delete(tmpPath);
    }
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ToRawImage_ProducesRgb24() {
    var file = new HireslaceFile {
      LoadAddress = 0x2000,
      Bitmap1 = new byte[HireslaceFile.BitmapDataSize],
      Screen1 = new byte[HireslaceFile.ScreenDataSize],
      Bitmap2 = new byte[HireslaceFile.BitmapDataSize],
      Screen2 = new byte[HireslaceFile.ScreenDataSize],
    };

    var raw = HireslaceFile.ToRawImage(file);

    Assert.That(raw.Format, Is.EqualTo(FileFormat.Core.PixelFormat.Rgb24));
    Assert.That(raw.Width, Is.EqualTo(320));
    Assert.That(raw.Height, Is.EqualTo(200));
    Assert.That(raw.PixelData.Length, Is.EqualTo(320 * 200 * 3));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_IdenticalFrames_AverageEqualsOriginal() {
    // When both frames are identical, the averaged result should equal each frame
    var bitmap = new byte[HireslaceFile.BitmapDataSize];
    var screen = new byte[HireslaceFile.ScreenDataSize];
    // All ink pixels with screen byte = 0x10 => fg=1(white), bg=0(black)
    Array.Fill(bitmap, (byte)0xFF);
    Array.Fill(screen, (byte)0x10);

    var file = new HireslaceFile {
      LoadAddress = 0x2000,
      Bitmap1 = bitmap[..],
      Screen1 = screen[..],
      Bitmap2 = bitmap[..],
      Screen2 = screen[..],
    };

    var raw = HireslaceFile.ToRawImage(file);

    // All pixels should be white (C64 color 1 = 0xFFFFFF)
    Assert.That(raw.PixelData[0], Is.EqualTo(0xFF));
    Assert.That(raw.PixelData[1], Is.EqualTo(0xFF));
    Assert.That(raw.PixelData[2], Is.EqualTo(0xFF));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_CustomLoadAddress() {
    var original = new HireslaceFile {
      LoadAddress = 0xC000,
      Bitmap1 = new byte[HireslaceFile.BitmapDataSize],
      Screen1 = new byte[HireslaceFile.ScreenDataSize],
      Bitmap2 = new byte[HireslaceFile.BitmapDataSize],
      Screen2 = new byte[HireslaceFile.ScreenDataSize],
    };

    var bytes = HireslaceWriter.ToBytes(original);
    var restored = HireslaceReader.FromBytes(bytes);

    Assert.That(restored.LoadAddress, Is.EqualTo(0xC000));
  }
}
