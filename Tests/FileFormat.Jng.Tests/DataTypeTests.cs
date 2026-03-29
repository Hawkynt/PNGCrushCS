using System;
using FileFormat.Jng;

namespace FileFormat.Jng.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void JngAlphaCompression_HasExpectedValues() {
    Assert.That((byte)JngAlphaCompression.PngDeflate, Is.EqualTo(0));
    Assert.That((byte)JngAlphaCompression.Jpeg, Is.EqualTo(8));

    var values = Enum.GetValues<JngAlphaCompression>();
    Assert.That(values, Has.Length.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void JngFile_DefaultJpegData_IsEmptyArray() {
    var file = new JngFile { Width = 1, Height = 1, ColorType = 10, ImageSampleDepth = 8 };
    Assert.That(file.JpegData, Is.Not.Null);
    Assert.That(file.JpegData, Has.Length.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void JngFile_DefaultAlphaData_IsNull() {
    var file = new JngFile { Width = 1, Height = 1, ColorType = 10, ImageSampleDepth = 8 };
    Assert.That(file.AlphaData, Is.Null);
  }
}
