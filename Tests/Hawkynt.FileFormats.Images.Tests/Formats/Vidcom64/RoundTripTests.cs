using System;
using FileFormat.Vidcom64;

namespace FileFormat.Vidcom64.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllFieldsPreserved() {
    var headerData = new byte[47];
    for (var i = 0; i < headerData.Length; ++i)
      headerData[i] = (byte)(i + 1);

    var bitmapData = new byte[8000];
    for (var i = 0; i < bitmapData.Length; ++i)
      bitmapData[i] = (byte)(i * 7 % 256);

    var screenRam = new byte[1000];
    for (var i = 0; i < screenRam.Length; ++i)
      screenRam[i] = (byte)(i % 16);

    var colorRam = new byte[1000];
    for (var i = 0; i < colorRam.Length; ++i)
      colorRam[i] = (byte)((i * 3 + 1) % 16);

    var original = new Vidcom64File {
      LoadAddress = 0x5800,
      HeaderData = headerData,
      BitmapData = bitmapData,
      ScreenRam = screenRam,
      ColorRam = colorRam,
      BackgroundColor = 6
    };

    var bytes = Vidcom64Writer.ToBytes(original);
    var restored = Vidcom64Reader.FromBytes(bytes);

    Assert.That(restored.LoadAddress, Is.EqualTo(original.LoadAddress));
    Assert.That(restored.HeaderData, Is.EqualTo(original.HeaderData));
    Assert.That(restored.BitmapData, Is.EqualTo(original.BitmapData));
    Assert.That(restored.ScreenRam, Is.EqualTo(original.ScreenRam));
    Assert.That(restored.ColorRam, Is.EqualTo(original.ColorRam));
    Assert.That(restored.BackgroundColor, Is.EqualTo(original.BackgroundColor));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_HeaderDataPreserved() {
    var headerData = new byte[47];
    Array.Fill(headerData, (byte)0xAA);

    var original = new Vidcom64File {
      LoadAddress = 0x5800,
      HeaderData = headerData,
      BitmapData = new byte[8000],
      ScreenRam = new byte[1000],
      ColorRam = new byte[1000],
      BackgroundColor = 0
    };

    var bytes = Vidcom64Writer.ToBytes(original);
    var restored = Vidcom64Reader.FromBytes(bytes);

    Assert.That(restored.HeaderData, Is.EqualTo(headerData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_DimensionsAlwaysFixed() {
    var original = new Vidcom64File {
      LoadAddress = 0x5800,
      HeaderData = new byte[47],
      BitmapData = new byte[8000],
      ScreenRam = new byte[1000],
      ColorRam = new byte[1000],
      BackgroundColor = 0
    };

    var bytes = Vidcom64Writer.ToBytes(original);
    var restored = Vidcom64Reader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(160));
    Assert.That(restored.Height, Is.EqualTo(200));
  }
}
