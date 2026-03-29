using System;
using FileFormat.Dicom;

namespace FileFormat.Dicom.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void DicomPhotometricInterpretation_HasExpectedValues() {
    Assert.That((int)DicomPhotometricInterpretation.Monochrome1, Is.EqualTo(0));
    Assert.That((int)DicomPhotometricInterpretation.Monochrome2, Is.EqualTo(1));
    Assert.That((int)DicomPhotometricInterpretation.Rgb, Is.EqualTo(2));
    Assert.That((int)DicomPhotometricInterpretation.PaletteColor, Is.EqualTo(3));

    var values = Enum.GetValues<DicomPhotometricInterpretation>();
    Assert.That(values, Has.Length.EqualTo(4));
  }
}
