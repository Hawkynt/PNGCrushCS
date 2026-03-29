using FileFormat.FullscreenKit;

namespace FileFormat.FullscreenKit.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void PrimaryFileSize_Is57024() {
    Assert.That(FullscreenKitFile.PrimaryFileSize, Is.EqualTo(57024));
  }

  [Test]
  [Category("Unit")]
  public void AlternateFileSize_Is60960() {
    Assert.That(FullscreenKitFile.AlternateFileSize, Is.EqualTo(60960));
  }

  [Test]
  [Category("Unit")]
  public void PrimaryWidth_Is416() {
    Assert.That(FullscreenKitFile.PrimaryWidth, Is.EqualTo(416));
  }

  [Test]
  [Category("Unit")]
  public void PrimaryHeight_Is274() {
    Assert.That(FullscreenKitFile.PrimaryHeight, Is.EqualTo(274));
  }

  [Test]
  [Category("Unit")]
  public void AlternateWidth_Is448() {
    Assert.That(FullscreenKitFile.AlternateWidth, Is.EqualTo(448));
  }

  [Test]
  [Category("Unit")]
  public void AlternateHeight_Is272() {
    Assert.That(FullscreenKitFile.AlternateHeight, Is.EqualTo(272));
  }

  [Test]
  [Category("Unit")]
  public void NumPlanes_Is4() {
    Assert.That(FullscreenKitFile.NumPlanes, Is.EqualTo(4));
  }

  [Test]
  [Category("Unit")]
  public void ColorCount_Is16() {
    Assert.That(FullscreenKitFile.ColorCount, Is.EqualTo(16));
  }

  [Test]
  [Category("Unit")]
  public void DefaultWidth_Is416() {
    var file = new FullscreenKitFile();
    Assert.That(file.Width, Is.EqualTo(416));
  }

  [Test]
  [Category("Unit")]
  public void DefaultHeight_Is274() {
    var file = new FullscreenKitFile();
    Assert.That(file.Height, Is.EqualTo(274));
  }

  [Test]
  [Category("Unit")]
  public void DefaultPalette_Has16Entries() {
    var file = new FullscreenKitFile();
    Assert.That(file.Palette.Length, Is.EqualTo(16));
  }

  [Test]
  [Category("Unit")]
  public void PrimaryPixelDataSize_MatchesCalculation() {
    // (416/16) * 4 * 2 * 274 = 26 * 8 * 274 = 56992
    Assert.That(FullscreenKitFile.PrimaryPixelDataSize, Is.EqualTo(56992));
  }

  [Test]
  [Category("Unit")]
  public void AlternatePixelDataSize_MatchesCalculation() {
    // (448/16) * 4 * 2 * 272 = 28 * 8 * 272 = 60928
    Assert.That(FullscreenKitFile.AlternatePixelDataSize, Is.EqualTo(60928));
  }
}
