using System;
using FileFormat.C64Multi;

namespace FileFormat.C64Multi.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_ArtStudioHires_AllFieldsPreserved() {
    var bitmapData = new byte[8000];
    for (var i = 0; i < bitmapData.Length; ++i)
      bitmapData[i] = (byte)(i * 7 % 256);

    var screenData = new byte[1000];
    for (var i = 0; i < screenData.Length; ++i)
      screenData[i] = (byte)(i % 256);

    var original = new C64MultiFile {
      Width = 320,
      Height = 200,
      Format = C64MultiFormat.ArtStudioHires,
      LoadAddress = 0x2000,
      BitmapData = bitmapData,
      ScreenData = screenData,
      BackgroundColor = 14
    };

    var bytes = C64MultiWriter.ToBytes(original);
    var restored = C64MultiReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.Format, Is.EqualTo(C64MultiFormat.ArtStudioHires));
    Assert.That(restored.LoadAddress, Is.EqualTo(original.LoadAddress));
    Assert.That(restored.BitmapData, Is.EqualTo(original.BitmapData));
    Assert.That(restored.ScreenData, Is.EqualTo(original.ScreenData));
    Assert.That(restored.ColorData, Is.Null);
    Assert.That(restored.BackgroundColor, Is.EqualTo(original.BackgroundColor));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ArtStudioMulti_AllFieldsPreserved() {
    var bitmapData = new byte[8000];
    for (var i = 0; i < bitmapData.Length; ++i)
      bitmapData[i] = (byte)(i * 7 % 256);

    var screenData = new byte[1000];
    for (var i = 0; i < screenData.Length; ++i)
      screenData[i] = (byte)(i % 16);

    var colorData = new byte[1000];
    for (var i = 0; i < colorData.Length; ++i)
      colorData[i] = (byte)((i * 3 + 1) % 16);

    var original = new C64MultiFile {
      Width = 160,
      Height = 200,
      Format = C64MultiFormat.ArtStudioMulti,
      LoadAddress = 0x4000,
      BitmapData = bitmapData,
      ScreenData = screenData,
      ColorData = colorData,
      BackgroundColor = 11
    };

    var bytes = C64MultiWriter.ToBytes(original);
    var restored = C64MultiReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.Format, Is.EqualTo(C64MultiFormat.ArtStudioMulti));
    Assert.That(restored.LoadAddress, Is.EqualTo(original.LoadAddress));
    Assert.That(restored.BitmapData, Is.EqualTo(original.BitmapData));
    Assert.That(restored.ScreenData, Is.EqualTo(original.ScreenData));
    Assert.That(restored.ColorData, Is.EqualTo(original.ColorData));
    Assert.That(restored.BackgroundColor, Is.EqualTo(original.BackgroundColor));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Hires_CustomLoadAddress() {
    var original = new C64MultiFile {
      Width = 320,
      Height = 200,
      Format = C64MultiFormat.ArtStudioHires,
      LoadAddress = 0x6000,
      BitmapData = new byte[8000],
      ScreenData = new byte[1000],
      BackgroundColor = 0
    };

    var bytes = C64MultiWriter.ToBytes(original);
    var restored = C64MultiReader.FromBytes(bytes);

    Assert.That(restored.LoadAddress, Is.EqualTo(0x6000));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Multi_BackgroundColorPreserved() {
    var original = new C64MultiFile {
      Width = 160,
      Height = 200,
      Format = C64MultiFormat.ArtStudioMulti,
      LoadAddress = 0x4000,
      BitmapData = new byte[8000],
      ScreenData = new byte[1000],
      ColorData = new byte[1000],
      BackgroundColor = 15
    };

    var bytes = C64MultiWriter.ToBytes(original);
    var restored = C64MultiReader.FromBytes(bytes);

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

    var original = new C64MultiFile {
      Width = 160,
      Height = 200,
      Format = C64MultiFormat.ArtStudioMulti,
      LoadAddress = 0xFFFF,
      BitmapData = bitmapData,
      ScreenData = screenData,
      ColorData = colorData,
      BackgroundColor = 15
    };

    var bytes = C64MultiWriter.ToBytes(original);
    var restored = C64MultiReader.FromBytes(bytes);

    Assert.That(restored.LoadAddress, Is.EqualTo(0xFFFF));
    Assert.That(restored.BitmapData, Is.EqualTo(original.BitmapData));
    Assert.That(restored.ScreenData, Is.EqualTo(original.ScreenData));
    Assert.That(restored.ColorData, Is.EqualTo(original.ColorData));
    Assert.That(restored.BackgroundColor, Is.EqualTo(15));
  }
}
