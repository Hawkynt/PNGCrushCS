using FileFormat.GeoPaint;

namespace FileFormat.GeoPaint.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void FixedWidth_Is640() {
    Assert.That(GeoPaintFile.FixedWidth, Is.EqualTo(640));
  }

  [Test]
  [Category("Unit")]
  public void MaxHeight_Is720() {
    Assert.That(GeoPaintFile.MaxHeight, Is.EqualTo(720));
  }

  [Test]
  [Category("Unit")]
  public void BytesPerRow_Is80() {
    Assert.That(GeoPaintFile.BytesPerRow, Is.EqualTo(80));
  }

  [Test]
  [Category("Unit")]
  public void Default_Width_Is640() {
    var file = new GeoPaintFile { Height = 1, PixelData = new byte[80] };
    Assert.That(file.Width, Is.EqualTo(640));
  }

  [Test]
  [Category("Unit")]
  public void Default_PixelData_IsEmpty() {
    var file = new GeoPaintFile { Height = 0 };
    Assert.That(file.PixelData, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void Height_SetViaInit() {
    var file = new GeoPaintFile { Height = 42 };
    Assert.That(file.Height, Is.EqualTo(42));
  }
}
