using System;
using FileFormat.SamCoupe;

namespace FileFormat.SamCoupe.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void SamCoupeMode_HasExpectedValues() {
    Assert.That((int)SamCoupeMode.Mode3, Is.EqualTo(3));
    Assert.That((int)SamCoupeMode.Mode4, Is.EqualTo(4));

    var values = Enum.GetValues<SamCoupeMode>();
    Assert.That(values, Has.Length.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void SamCoupeFile_DefaultValues() {
    var file = new SamCoupeFile();

    Assert.That(file.Width, Is.EqualTo(0));
    Assert.That(file.Height, Is.EqualTo(0));
    Assert.That(file.Mode, Is.EqualTo(default(SamCoupeMode)));
    Assert.That(file.PixelData, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void SamCoupeReader_Constants() {
    Assert.That(SamCoupeReader.FileSize, Is.EqualTo(24576));
    Assert.That(SamCoupeReader.BytesPerRow, Is.EqualTo(128));
    Assert.That(SamCoupeReader.RowCount, Is.EqualTo(192));
    Assert.That(SamCoupeReader.PageSize, Is.EqualTo(12288));
    Assert.That(SamCoupeReader.LinesPerPage, Is.EqualTo(96));
  }
}
