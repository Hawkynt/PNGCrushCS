using System;
using FileFormat.Miff;

namespace FileFormat.Miff.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void MiffColorClass_HasExpectedValues() {
    Assert.That((int)MiffColorClass.DirectClass, Is.EqualTo(0));
    Assert.That((int)MiffColorClass.PseudoClass, Is.EqualTo(1));

    var values = Enum.GetValues<MiffColorClass>();
    Assert.That(values, Has.Length.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void MiffCompression_HasExpectedValues() {
    Assert.That((int)MiffCompression.None, Is.EqualTo(0));
    Assert.That((int)MiffCompression.Rle, Is.EqualTo(1));
    Assert.That((int)MiffCompression.Zip, Is.EqualTo(2));

    var values = Enum.GetValues<MiffCompression>();
    Assert.That(values, Has.Length.EqualTo(3));
  }

  [Test]
  [Category("Unit")]
  public void MiffFile_DefaultValues() {
    var file = new MiffFile {
      Width = 10,
      Height = 20
    };

    Assert.That(file.Width, Is.EqualTo(10));
    Assert.That(file.Height, Is.EqualTo(20));
    Assert.That(file.Depth, Is.EqualTo(8));
    Assert.That(file.ColorClass, Is.EqualTo(MiffColorClass.DirectClass));
    Assert.That(file.Compression, Is.EqualTo(MiffCompression.None));
    Assert.That(file.Colorspace, Is.EqualTo("sRGB"));
    Assert.That(file.Type, Is.EqualTo("TrueColor"));
    Assert.That(file.PixelData, Is.Empty);
    Assert.That(file.Palette, Is.Null);
  }
}
