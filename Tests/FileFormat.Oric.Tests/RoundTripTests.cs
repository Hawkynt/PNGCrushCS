using System;
using System.IO;
using FileFormat.Oric;

namespace FileFormat.Oric.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllZeros() {
    var original = new OricFile {
      ScreenData = new byte[8000]
    };

    var bytes = OricWriter.ToBytes(original);
    var restored = OricReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(240));
    Assert.That(restored.Height, Is.EqualTo(200));
    Assert.That(restored.ScreenData, Is.EqualTo(original.ScreenData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Attributes() {
    var screenData = new byte[8000];
    for (var i = 0; i < 8000; i += 40)
      screenData[i] = 0x47;

    var original = new OricFile {
      ScreenData = screenData
    };

    var bytes = OricWriter.ToBytes(original);
    var restored = OricReader.FromBytes(bytes);

    Assert.That(restored.ScreenData, Is.EqualTo(original.ScreenData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_MixedData() {
    var screenData = new byte[8000];
    for (var i = 0; i < screenData.Length; ++i)
      screenData[i] = (byte)(i * 7 % 256);

    var original = new OricFile {
      ScreenData = screenData
    };

    var bytes = OricWriter.ToBytes(original);
    var restored = OricReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.ScreenData, Is.EqualTo(original.ScreenData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllOnes() {
    var screenData = new byte[8000];
    for (var i = 0; i < screenData.Length; ++i)
      screenData[i] = 0xFF;

    var original = new OricFile {
      ScreenData = screenData
    };

    var bytes = OricWriter.ToBytes(original);
    var restored = OricReader.FromBytes(bytes);

    Assert.That(restored.ScreenData, Is.EqualTo(original.ScreenData));
  }
}
