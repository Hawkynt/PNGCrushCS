using System;
using FileFormat.MicroIllustrator;

namespace FileFormat.MicroIllustrator.Tests;

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

    var original = new MicroIllustratorFile {
      LoadAddress = 0x6000,
      BitmapData = bitmapData,
      VideoMatrix = videoMatrix,
      ColorRam = colorRam,
      BackgroundColor = 11
    };

    var bytes = MicroIllustratorWriter.ToBytes(original);
    var restored = MicroIllustratorReader.FromBytes(bytes);

    Assert.That(restored.LoadAddress, Is.EqualTo(original.LoadAddress));
    Assert.That(restored.BitmapData, Is.EqualTo(original.BitmapData));
    Assert.That(restored.VideoMatrix, Is.EqualTo(original.VideoMatrix));
    Assert.That(restored.ColorRam, Is.EqualTo(original.ColorRam));
    Assert.That(restored.BackgroundColor, Is.EqualTo(original.BackgroundColor));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_BackgroundColorPreserved() {
    var original = new MicroIllustratorFile {
      LoadAddress = 0x6000,
      BitmapData = new byte[8000],
      VideoMatrix = new byte[1000],
      ColorRam = new byte[1000],
      BackgroundColor = 15
    };

    var bytes = MicroIllustratorWriter.ToBytes(original);
    var restored = MicroIllustratorReader.FromBytes(bytes);

    Assert.That(restored.BackgroundColor, Is.EqualTo(15));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_DimensionsAlwaysFixed() {
    var original = new MicroIllustratorFile {
      LoadAddress = 0x6000,
      BitmapData = new byte[8000],
      VideoMatrix = new byte[1000],
      ColorRam = new byte[1000],
      BackgroundColor = 0
    };

    var bytes = MicroIllustratorWriter.ToBytes(original);
    var restored = MicroIllustratorReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(160));
    Assert.That(restored.Height, Is.EqualTo(200));
  }
}
