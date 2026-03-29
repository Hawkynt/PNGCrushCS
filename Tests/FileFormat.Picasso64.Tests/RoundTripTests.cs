using System;
using FileFormat.Picasso64;

namespace FileFormat.Picasso64.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllFieldsPreserved() {
    var bitmapData = new byte[8000];
    for (var i = 0; i < bitmapData.Length; ++i)
      bitmapData[i] = (byte)(i * 7 % 256);

    var screenRam = new byte[1000];
    for (var i = 0; i < screenRam.Length; ++i)
      screenRam[i] = (byte)(i % 16);

    var colorRam = new byte[1000];
    for (var i = 0; i < colorRam.Length; ++i)
      colorRam[i] = (byte)((i * 3 + 1) % 16);

    var extraData = new byte[46];
    for (var i = 0; i < extraData.Length; ++i)
      extraData[i] = (byte)(i + 10);

    var original = new Picasso64File {
      LoadAddress = 0x6000,
      BitmapData = bitmapData,
      ScreenRam = screenRam,
      ColorRam = colorRam,
      BackgroundColor = 3,
      BorderColor = 14,
      ExtraData = extraData,
    };

    var bytes = Picasso64Writer.ToBytes(original);
    var restored = Picasso64Reader.FromBytes(bytes);

    Assert.That(restored.LoadAddress, Is.EqualTo(original.LoadAddress));
    Assert.That(restored.BitmapData, Is.EqualTo(original.BitmapData));
    Assert.That(restored.ScreenRam, Is.EqualTo(original.ScreenRam));
    Assert.That(restored.ColorRam, Is.EqualTo(original.ColorRam));
    Assert.That(restored.BackgroundColor, Is.EqualTo(original.BackgroundColor));
    Assert.That(restored.BorderColor, Is.EqualTo(original.BorderColor));
    Assert.That(restored.ExtraData, Is.EqualTo(original.ExtraData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_BorderColorPreserved() {
    var original = new Picasso64File {
      LoadAddress = 0x6000,
      BitmapData = new byte[8000],
      ScreenRam = new byte[1000],
      ColorRam = new byte[1000],
      BackgroundColor = 0,
      BorderColor = 14,
      ExtraData = new byte[46],
    };

    var bytes = Picasso64Writer.ToBytes(original);
    var restored = Picasso64Reader.FromBytes(bytes);

    Assert.That(restored.BorderColor, Is.EqualTo(14));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_DimensionsAlwaysFixed() {
    var original = new Picasso64File {
      LoadAddress = 0x6000,
      BitmapData = new byte[8000],
      ScreenRam = new byte[1000],
      ColorRam = new byte[1000],
      BackgroundColor = 0,
      BorderColor = 0,
      ExtraData = new byte[46],
    };

    var bytes = Picasso64Writer.ToBytes(original);
    var restored = Picasso64Reader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(160));
    Assert.That(restored.Height, Is.EqualTo(200));
  }
}
