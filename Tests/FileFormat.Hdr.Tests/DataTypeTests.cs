using System;
using FileFormat.Hdr;

namespace FileFormat.Hdr.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void HdrOrientation_HasExpectedValues() {
    Assert.That((int)HdrOrientation.TopDownLeftRight, Is.EqualTo(0));

    var values = Enum.GetValues<HdrOrientation>();
    Assert.That(values, Has.Length.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void HdrFile_DefaultExposure_IsOne() {
    var file = new HdrFile {
      Width = 1,
      Height = 1,
      PixelData = new float[3]
    };

    Assert.That(file.Exposure, Is.EqualTo(1.0f));
  }

  [Test]
  [Category("Unit")]
  public void HdrFile_DefaultPixelData_IsEmpty() {
    var file = new HdrFile {
      Width = 0,
      Height = 0
    };

    Assert.That(file.PixelData, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void HdrFile_PropertiesRoundTrip() {
    var pixels = new float[] { 1.0f, 2.0f, 3.0f };
    var file = new HdrFile {
      Width = 1,
      Height = 1,
      Exposure = 2.5f,
      PixelData = pixels
    };

    Assert.That(file.Width, Is.EqualTo(1));
    Assert.That(file.Height, Is.EqualTo(1));
    Assert.That(file.Exposure, Is.EqualTo(2.5f));
    Assert.That(file.PixelData, Is.SameAs(pixels));
  }
}
