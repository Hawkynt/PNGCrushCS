using FileFormat.Cals;

namespace FileFormat.Cals.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void CalsFile_DefaultDpi_Is200() {
    var file = new CalsFile { Width = 8, Height = 1, PixelData = new byte[1] };
    Assert.That(file.Dpi, Is.EqualTo(200));
  }

  [Test]
  [Category("Unit")]
  public void CalsFile_DefaultOrientation_RunsRowsLeftToRight() {
    var file = new CalsFile { Width = 8, Height = 1, PixelData = new byte[1] };

    // The record holds the two angles the rows and columns run at, not a word.
    Assert.That(file.Orientation, Is.EqualTo("000,270"));
  }

  [Test]
  [Category("Unit")]
  public void CalsFile_DefaultSrcDocId_IsNone() {
    var file = new CalsFile { Width = 8, Height = 1, PixelData = new byte[1] };
    Assert.That(file.SrcDocId, Is.EqualTo("NONE"));
  }

  [Test]
  [Category("Unit")]
  public void CalsFile_DefaultDstDocId_IsNone() {
    var file = new CalsFile { Width = 8, Height = 1, PixelData = new byte[1] };
    Assert.That(file.DstDocId, Is.EqualTo("NONE"));
  }

  [Test]
  [Category("Unit")]
  public void CalsFile_DefaultPixelData_IsEmpty() {
    var file = new CalsFile { Width = 0, Height = 0 };
    Assert.That(file.PixelData, Is.Null);
  }
}
