using System;
using FileFormat.Doodle;

namespace FileFormat.Doodle.Tests;

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
      screenRam[i] = (byte)(i % 256);

    var original = new DoodleFile {
      LoadAddress = 0x5C00,
      BitmapData = bitmapData,
      ScreenRam = screenRam,
    };

    var bytes = DoodleWriter.ToBytes(original);
    var restored = DoodleReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.LoadAddress, Is.EqualTo(original.LoadAddress));
    Assert.That(restored.BitmapData, Is.EqualTo(original.BitmapData));
    Assert.That(restored.ScreenRam, Is.EqualTo(original.ScreenRam));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_CustomLoadAddress() {
    var original = new DoodleFile {
      LoadAddress = 0x4000,
      BitmapData = new byte[8000],
      ScreenRam = new byte[1000],
    };

    var bytes = DoodleWriter.ToBytes(original);
    var restored = DoodleReader.FromBytes(bytes);

    Assert.That(restored.LoadAddress, Is.EqualTo(0x4000));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllBytesMaxValue() {
    var bitmapData = new byte[8000];
    Array.Fill(bitmapData, (byte)0xFF);

    var screenRam = new byte[1000];
    Array.Fill(screenRam, (byte)0xFF);

    var original = new DoodleFile {
      LoadAddress = 0xFFFF,
      BitmapData = bitmapData,
      ScreenRam = screenRam,
    };

    var bytes = DoodleWriter.ToBytes(original);
    var restored = DoodleReader.FromBytes(bytes);

    Assert.That(restored.LoadAddress, Is.EqualTo(0xFFFF));
    Assert.That(restored.BitmapData, Is.EqualTo(original.BitmapData));
    Assert.That(restored.ScreenRam, Is.EqualTo(original.ScreenRam));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_DimensionsAlwaysFixed() {
    var original = new DoodleFile {
      LoadAddress = 0x5C00,
      BitmapData = new byte[8000],
      ScreenRam = new byte[1000],
    };

    var bytes = DoodleWriter.ToBytes(original);
    var restored = DoodleReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(320));
    Assert.That(restored.Height, Is.EqualTo(200));
  }
}
