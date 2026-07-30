using FileFormat.Oric;

namespace FileFormat.Oric.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void OricFile_DefaultWidth_Is240() {
    var file = new OricFile();

    Assert.That(file.Width, Is.EqualTo(240));
  }

  [Test]
  [Category("Unit")]
  public void OricFile_DefaultHeight_Is200() {
    var file = new OricFile();

    Assert.That(file.Height, Is.EqualTo(200));
  }
}
