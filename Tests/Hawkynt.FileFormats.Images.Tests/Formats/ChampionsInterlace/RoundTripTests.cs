using System;
using System.IO;
using FileFormat.ChampionsInterlace;

namespace FileFormat.ChampionsInterlace.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllFieldsPreserved() {
    var bitmap1 = new byte[8000];
    for (var i = 0; i < bitmap1.Length; ++i)
      bitmap1[i] = (byte)(i * 7 % 256);

    var screen1 = new byte[1000];
    for (var i = 0; i < screen1.Length; ++i)
      screen1[i] = (byte)(i % 16);

    var colorData = new byte[1000];
    for (var i = 0; i < colorData.Length; ++i)
      colorData[i] = (byte)((i * 3 + 1) % 16);

    var bitmap2 = new byte[8000];
    for (var i = 0; i < bitmap2.Length; ++i)
      bitmap2[i] = (byte)((i * 5 + 2) % 256);

    var screen2 = new byte[1000];
    for (var i = 0; i < screen2.Length; ++i)
      screen2[i] = (byte)((i + 9) % 16);

    var original = new ChampionsInterlaceFile {
      LoadAddress = 0x4000,
      Bitmap1 = bitmap1,
      Screen1 = screen1,
      ColorData = colorData,
      Bitmap2 = bitmap2,
      Screen2 = screen2,
      BackgroundColor = 11,
    };

    var bytes = ChampionsInterlaceWriter.ToBytes(original);
    var restored = ChampionsInterlaceReader.FromBytes(bytes);

    Assert.That(restored.LoadAddress, Is.EqualTo(original.LoadAddress));
    Assert.That(restored.Bitmap1, Is.EqualTo(original.Bitmap1));
    Assert.That(restored.Screen1, Is.EqualTo(original.Screen1));
    Assert.That(restored.ColorData, Is.EqualTo(original.ColorData));
    Assert.That(restored.Bitmap2, Is.EqualTo(original.Bitmap2));
    Assert.That(restored.Screen2, Is.EqualTo(original.Screen2));
    Assert.That(restored.BackgroundColor, Is.EqualTo(original.BackgroundColor));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_CustomLoadAddress() {
    var original = new ChampionsInterlaceFile {
      LoadAddress = 0x6000,
      Bitmap1 = new byte[8000],
      Screen1 = new byte[1000],
      ColorData = new byte[1000],
      Bitmap2 = new byte[8000],
      Screen2 = new byte[1000],
      BackgroundColor = 0,
    };

    var bytes = ChampionsInterlaceWriter.ToBytes(original);
    var restored = ChampionsInterlaceReader.FromBytes(bytes);

    Assert.That(restored.LoadAddress, Is.EqualTo(0x6000));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile_PreservesData() {
    var bitmap1 = new byte[8000];
    for (var i = 0; i < 8000; ++i)
      bitmap1[i] = (byte)(i % 256);

    var original = new ChampionsInterlaceFile {
      LoadAddress = 0x4000,
      Bitmap1 = bitmap1,
      Screen1 = new byte[1000],
      ColorData = new byte[1000],
      Bitmap2 = new byte[8000],
      Screen2 = new byte[1000],
      BackgroundColor = 3,
    };

    var tmpPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".cin");
    try {
      var bytes = ChampionsInterlaceWriter.ToBytes(original);
      File.WriteAllBytes(tmpPath, bytes);
      var restored = ChampionsInterlaceReader.FromFile(new FileInfo(tmpPath));

      Assert.That(restored.LoadAddress, Is.EqualTo(original.LoadAddress));
      Assert.That(restored.Bitmap1, Is.EqualTo(original.Bitmap1));
      Assert.That(restored.BackgroundColor, Is.EqualTo(original.BackgroundColor));
    } finally {
      if (File.Exists(tmpPath))
        File.Delete(tmpPath);
    }
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ToRawImage_ProducesCorrectDimensions() {
    var original = new ChampionsInterlaceFile {
      LoadAddress = 0x4000,
      Bitmap1 = new byte[8000],
      Screen1 = new byte[1000],
      ColorData = new byte[1000],
      Bitmap2 = new byte[8000],
      Screen2 = new byte[1000],
      BackgroundColor = 0,
    };

    var raw = ChampionsInterlaceFile.ToRawImage(original);

    Assert.That(raw.Width, Is.EqualTo(160));
    Assert.That(raw.Height, Is.EqualTo(200));
    Assert.That(raw.PixelData.Length, Is.EqualTo(160 * 200 * 3));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ToRawImage_FormatIsRgb24() {
    var original = new ChampionsInterlaceFile {
      LoadAddress = 0x4000,
      Bitmap1 = new byte[8000],
      Screen1 = new byte[1000],
      ColorData = new byte[1000],
      Bitmap2 = new byte[8000],
      Screen2 = new byte[1000],
      BackgroundColor = 0,
    };

    var raw = ChampionsInterlaceFile.ToRawImage(original);

    Assert.That(raw.Format, Is.EqualTo(FileFormat.Core.PixelFormat.Rgb24));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllBytesMaxValue() {
    var bitmap1 = new byte[8000];
    Array.Fill(bitmap1, (byte)0xFF);

    var screen1 = new byte[1000];
    Array.Fill(screen1, (byte)0xFF);

    var colorData = new byte[1000];
    Array.Fill(colorData, (byte)0x0F);

    var bitmap2 = new byte[8000];
    Array.Fill(bitmap2, (byte)0xFF);

    var screen2 = new byte[1000];
    Array.Fill(screen2, (byte)0xFF);

    var original = new ChampionsInterlaceFile {
      LoadAddress = 0xFFFF,
      Bitmap1 = bitmap1,
      Screen1 = screen1,
      ColorData = colorData,
      Bitmap2 = bitmap2,
      Screen2 = screen2,
      BackgroundColor = 15,
    };

    var bytes = ChampionsInterlaceWriter.ToBytes(original);
    var restored = ChampionsInterlaceReader.FromBytes(bytes);

    Assert.That(restored.LoadAddress, Is.EqualTo(0xFFFF));
    Assert.That(restored.Bitmap1, Is.EqualTo(original.Bitmap1));
    Assert.That(restored.Screen1, Is.EqualTo(original.Screen1));
    Assert.That(restored.ColorData, Is.EqualTo(original.ColorData));
    Assert.That(restored.Bitmap2, Is.EqualTo(original.Bitmap2));
    Assert.That(restored.Screen2, Is.EqualTo(original.Screen2));
    Assert.That(restored.BackgroundColor, Is.EqualTo(15));
  }
}
