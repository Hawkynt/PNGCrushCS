using System;
using FileFormat.ScitexCt;

namespace FileFormat.ScitexCt.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void ScitexCtColorMode_HasExpectedValues() {
    Assert.That((int)ScitexCtColorMode.Grayscale, Is.EqualTo(1));
    Assert.That((int)ScitexCtColorMode.Rgb, Is.EqualTo(3));
    Assert.That((int)ScitexCtColorMode.Cmyk, Is.EqualTo(4));

    var values = Enum.GetValues<ScitexCtColorMode>();
    Assert.That(values, Has.Length.EqualTo(3));
  }

  [Test]
  [Category("Unit")]
  public void ScitexCtFile_DefaultValues() {
    var file = new ScitexCtFile();

    Assert.That(file.Width, Is.EqualTo(0));
    Assert.That(file.Height, Is.EqualTo(0));
    Assert.That(file.BitsPerComponent, Is.EqualTo(0));
    Assert.That(file.Description, Is.EqualTo(""));
    Assert.That(file.PixelData, Is.Empty);
  }
}
