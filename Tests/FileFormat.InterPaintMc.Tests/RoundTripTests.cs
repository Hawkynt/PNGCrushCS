using System;
using FileFormat.InterPaintMc;

namespace FileFormat.InterPaintMc.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllFieldsPreserved() {
    var bitmapData = new byte[8000];
    for (var i = 0; i < bitmapData.Length; ++i)
      bitmapData[i] = (byte)(i * 7 % 256);

    var videoMatrix = new byte[1000];
    for (var i = 0; i < videoMatrix.Length; ++i)
      videoMatrix[i] = (byte)(i % 16);

    var colorRam = new byte[1000];
    for (var i = 0; i < colorRam.Length; ++i)
      colorRam[i] = (byte)((i * 3 + 1) % 16);

    var original = new InterPaintMcFile {
      LoadAddress = 0x6000,
      BitmapData = bitmapData,
      VideoMatrix = videoMatrix,
      ColorRam = colorRam,
      BackgroundColor = 11
    };

    var bytes = InterPaintMcWriter.ToBytes(original);
    var restored = InterPaintMcReader.FromBytes(bytes);

    Assert.That(restored.LoadAddress, Is.EqualTo(original.LoadAddress));
    Assert.That(restored.BitmapData, Is.EqualTo(original.BitmapData));
    Assert.That(restored.VideoMatrix, Is.EqualTo(original.VideoMatrix));
    Assert.That(restored.ColorRam, Is.EqualTo(original.ColorRam));
    Assert.That(restored.BackgroundColor, Is.EqualTo(original.BackgroundColor));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_BackgroundColorPreserved() {
    var original = new InterPaintMcFile {
      LoadAddress = 0x6000,
      BitmapData = new byte[8000],
      VideoMatrix = new byte[1000],
      ColorRam = new byte[1000],
      BackgroundColor = 15
    };

    var bytes = InterPaintMcWriter.ToBytes(original);
    var restored = InterPaintMcReader.FromBytes(bytes);

    Assert.That(restored.BackgroundColor, Is.EqualTo(15));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_DimensionsAlwaysFixed() {
    var original = new InterPaintMcFile {
      LoadAddress = 0x6000,
      BitmapData = new byte[8000],
      VideoMatrix = new byte[1000],
      ColorRam = new byte[1000],
      BackgroundColor = 0
    };

    var bytes = InterPaintMcWriter.ToBytes(original);
    var restored = InterPaintMcReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(160));
    Assert.That(restored.Height, Is.EqualTo(200));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllBytesMaxValue() {
    var bitmapData = new byte[8000];
    Array.Fill(bitmapData, (byte)0xFF);

    var videoMatrix = new byte[1000];
    Array.Fill(videoMatrix, (byte)0xFF);

    var colorRam = new byte[1000];
    Array.Fill(colorRam, (byte)0x0F);

    var original = new InterPaintMcFile {
      LoadAddress = 0xFFFF,
      BitmapData = bitmapData,
      VideoMatrix = videoMatrix,
      ColorRam = colorRam,
      BackgroundColor = 15
    };

    var bytes = InterPaintMcWriter.ToBytes(original);
    var restored = InterPaintMcReader.FromBytes(bytes);

    Assert.That(restored.LoadAddress, Is.EqualTo(0xFFFF));
    Assert.That(restored.BitmapData, Is.EqualTo(original.BitmapData));
    Assert.That(restored.VideoMatrix, Is.EqualTo(original.VideoMatrix));
    Assert.That(restored.ColorRam, Is.EqualTo(original.ColorRam));
    Assert.That(restored.BackgroundColor, Is.EqualTo(15));
  }
}
