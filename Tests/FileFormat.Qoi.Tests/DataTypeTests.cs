using System;
using FileFormat.Qoi;

namespace FileFormat.Qoi.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void QoiChannels_HasExpectedValues() {
    Assert.That((byte)QoiChannels.Rgb, Is.EqualTo(3));
    Assert.That((byte)QoiChannels.Rgba, Is.EqualTo(4));

    var values = Enum.GetValues<QoiChannels>();
    Assert.That(values, Has.Length.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void QoiColorSpace_HasExpectedValues() {
    Assert.That((byte)QoiColorSpace.Srgb, Is.EqualTo(0));
    Assert.That((byte)QoiColorSpace.Linear, Is.EqualTo(1));

    var values = Enum.GetValues<QoiColorSpace>();
    Assert.That(values, Has.Length.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void QoiFile_DefaultPixelData_IsNull() {
    // QoiFile is a readonly record struct — uninitialized PixelData is null, not empty array
    var file = new QoiFile { Width = 1, Height = 1, Channels = QoiChannels.Rgb };
    Assert.That(file.PixelData, Is.Null);
  }

  [Test]
  [Category("Unit")]
  public void QoiFile_InitProperties_RoundTrip() {
    var pixels = new byte[] { 1, 2, 3 };
    var file = new QoiFile {
      Width = 10,
      Height = 20,
      Channels = QoiChannels.Rgba,
      ColorSpace = QoiColorSpace.Linear,
      PixelData = pixels
    };

    Assert.That(file.Width, Is.EqualTo(10));
    Assert.That(file.Height, Is.EqualTo(20));
    Assert.That(file.Channels, Is.EqualTo(QoiChannels.Rgba));
    Assert.That(file.ColorSpace, Is.EqualTo(QoiColorSpace.Linear));
    Assert.That(file.PixelData, Is.SameAs(pixels));
  }
}
