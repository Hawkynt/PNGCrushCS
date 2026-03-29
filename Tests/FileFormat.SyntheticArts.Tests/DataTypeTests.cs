using FileFormat.SyntheticArts;

namespace FileFormat.SyntheticArts.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void FileSize_Is32032() {
    Assert.That(SyntheticArtsFile.FileSize, Is.EqualTo(32032));
  }

  [Test]
  [Category("Unit")]
  public void ImageWidth_Is640() {
    Assert.That(SyntheticArtsFile.ImageWidth, Is.EqualTo(640));
  }

  [Test]
  [Category("Unit")]
  public void ImageHeight_Is200() {
    Assert.That(SyntheticArtsFile.ImageHeight, Is.EqualTo(200));
  }

  [Test]
  [Category("Unit")]
  public void NumPlanes_Is2() {
    Assert.That(SyntheticArtsFile.NumPlanes, Is.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void ColorCount_Is4() {
    Assert.That(SyntheticArtsFile.ColorCount, Is.EqualTo(4));
  }

  [Test]
  [Category("Unit")]
  public void DefaultPalette_Has16Entries() {
    var file = new SyntheticArtsFile();
    Assert.That(file.Palette.Length, Is.EqualTo(16));
  }

  [Test]
  [Category("Unit")]
  public void DefaultPixelData_Is32000Bytes() {
    var file = new SyntheticArtsFile();
    Assert.That(file.PixelData.Length, Is.EqualTo(32000));
  }

  [Test]
  [Category("Unit")]
  public void Extensions_ContainsSrt() {
    var extensions = GetExtensions();
    Assert.That(extensions, Does.Contain(".srt"));
  }

  private static string[] GetExtensions() {
    var prop = typeof(SyntheticArtsFile).GetProperty("FileExtensions", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
    if (prop != null)
      return (string[])prop.GetValue(null)!;

    // Fall back to interface explicit implementation
    var method = typeof(SyntheticArtsFile).GetMethod("FileFormat.Core.IImageFileFormat<FileFormat.SyntheticArts.SyntheticArtsFile>.get_FileExtensions", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
    if (method != null)
      return (string[])method.Invoke(null, null)!;

    return [];
  }
}
