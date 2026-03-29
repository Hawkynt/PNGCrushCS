using System;
using System.IO;
using FileFormat.Din;

namespace FileFormat.Din.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllFieldsPreserved() {
    var bitmapData = new byte[8000];
    for (var i = 0; i < bitmapData.Length; ++i)
      bitmapData[i] = (byte)(i * 7 % 256);

    var screenData = new byte[1000];
    for (var i = 0; i < screenData.Length; ++i)
      screenData[i] = (byte)(i % 16);

    var colorData = new byte[1000];
    for (var i = 0; i < colorData.Length; ++i)
      colorData[i] = (byte)((i * 3 + 1) % 16);

    var original = new DinFile {
      LoadAddress = 0x4000,
      BitmapData = bitmapData,
      ScreenData = screenData,
      ColorData = colorData,
      BackgroundColor = 11,
    };

    var bytes = DinWriter.ToBytes(original);
    var restored = DinReader.FromBytes(bytes);

    Assert.That(restored.LoadAddress, Is.EqualTo(original.LoadAddress));
    Assert.That(restored.BitmapData, Is.EqualTo(original.BitmapData));
    Assert.That(restored.ScreenData, Is.EqualTo(original.ScreenData));
    Assert.That(restored.ColorData, Is.EqualTo(original.ColorData));
    Assert.That(restored.BackgroundColor, Is.EqualTo(original.BackgroundColor));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_CustomLoadAddress() {
    var original = new DinFile {
      LoadAddress = 0x6000,
      BitmapData = new byte[8000],
      ScreenData = new byte[1000],
      ColorData = new byte[1000],
      BackgroundColor = 0,
    };

    var bytes = DinWriter.ToBytes(original);
    var restored = DinReader.FromBytes(bytes);

    Assert.That(restored.LoadAddress, Is.EqualTo(0x6000));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_BackgroundColorPreserved() {
    var original = new DinFile {
      LoadAddress = 0x4000,
      BitmapData = new byte[8000],
      ScreenData = new byte[1000],
      ColorData = new byte[1000],
      BackgroundColor = 15,
    };

    var bytes = DinWriter.ToBytes(original);
    var restored = DinReader.FromBytes(bytes);

    Assert.That(restored.BackgroundColor, Is.EqualTo(15));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllBytesMaxValue() {
    var bitmapData = new byte[8000];
    Array.Fill(bitmapData, (byte)0xFF);

    var screenData = new byte[1000];
    Array.Fill(screenData, (byte)0xFF);

    var colorData = new byte[1000];
    Array.Fill(colorData, (byte)0x0F);

    var original = new DinFile {
      LoadAddress = 0xFFFF,
      BitmapData = bitmapData,
      ScreenData = screenData,
      ColorData = colorData,
      BackgroundColor = 15,
    };

    var bytes = DinWriter.ToBytes(original);
    var restored = DinReader.FromBytes(bytes);

    Assert.That(restored.LoadAddress, Is.EqualTo(0xFFFF));
    Assert.That(restored.BitmapData, Is.EqualTo(original.BitmapData));
    Assert.That(restored.ScreenData, Is.EqualTo(original.ScreenData));
    Assert.That(restored.ColorData, Is.EqualTo(original.ColorData));
    Assert.That(restored.BackgroundColor, Is.EqualTo(15));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile_PreservesData() {
    var bitmapData = new byte[8000];
    for (var i = 0; i < 8000; ++i)
      bitmapData[i] = (byte)(i % 256);

    var original = new DinFile {
      LoadAddress = 0x4000,
      BitmapData = bitmapData,
      ScreenData = new byte[1000],
      ColorData = new byte[1000],
      BackgroundColor = 3,
    };

    var tmpPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".din");
    try {
      var bytes = DinWriter.ToBytes(original);
      File.WriteAllBytes(tmpPath, bytes);
      var restored = DinReader.FromFile(new FileInfo(tmpPath));

      Assert.That(restored.LoadAddress, Is.EqualTo(original.LoadAddress));
      Assert.That(restored.BitmapData, Is.EqualTo(original.BitmapData));
      Assert.That(restored.BackgroundColor, Is.EqualTo(original.BackgroundColor));
    } finally {
      if (File.Exists(tmpPath))
        File.Delete(tmpPath);
    }
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ToRawImage_ProducesCorrectDimensions() {
    var original = new DinFile {
      LoadAddress = 0x4000,
      BitmapData = new byte[8000],
      ScreenData = new byte[1000],
      ColorData = new byte[1000],
      BackgroundColor = 0,
    };

    var raw = DinFile.ToRawImage(original);

    Assert.That(raw.Width, Is.EqualTo(160));
    Assert.That(raw.Height, Is.EqualTo(200));
    Assert.That(raw.PixelData.Length, Is.EqualTo(160 * 200 * 3));
  }
}
