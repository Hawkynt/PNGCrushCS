using System;
using FileFormat.Fits;

namespace FileFormat.Fits.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void FitsBitpix_HasExpectedValues() {
    Assert.That((int)FitsBitpix.UInt8, Is.EqualTo(8));
    Assert.That((int)FitsBitpix.Int16, Is.EqualTo(16));
    Assert.That((int)FitsBitpix.Int32, Is.EqualTo(32));
    Assert.That((int)FitsBitpix.Float32, Is.EqualTo(-32));
    Assert.That((int)FitsBitpix.Float64, Is.EqualTo(-64));

    var values = Enum.GetValues<FitsBitpix>();
    Assert.That(values, Has.Length.EqualTo(5));
  }

  [Test]
  [Category("Unit")]
  public void FitsKeyword_StoresFieldsCorrectly() {
    var kw = new FitsKeyword("OBJECT", "M31", "Andromeda Galaxy");

    Assert.That(kw.Name, Is.EqualTo("OBJECT"));
    Assert.That(kw.Value, Is.EqualTo("M31"));
    Assert.That(kw.Comment, Is.EqualTo("Andromeda Galaxy"));
  }

  [Test]
  [Category("Unit")]
  public void FitsKeyword_NullValueAndComment() {
    var kw = new FitsKeyword("COMMENT", null, null);

    Assert.That(kw.Name, Is.EqualTo("COMMENT"));
    Assert.That(kw.Value, Is.Null);
    Assert.That(kw.Comment, Is.Null);
  }

  [Test]
  [Category("Unit")]
  public void FitsFile_DefaultValues() {
    var file = new FitsFile {
      Width = 10,
      Height = 20,
      Bitpix = FitsBitpix.UInt8
    };

    Assert.That(file.Width, Is.EqualTo(10));
    Assert.That(file.Height, Is.EqualTo(20));
    Assert.That(file.Bitpix, Is.EqualTo(FitsBitpix.UInt8));
    Assert.That(file.Keywords, Is.Empty);
    Assert.That(file.PixelData, Is.Empty);
  }
}
