using System;
using FileFormat.Blp;
using FileFormat.Core;

namespace FileFormat.Blp.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void BlpEncoding_HasExpectedValues() {
    Assert.That((int)BlpEncoding.Palette, Is.EqualTo(1));
    Assert.That((int)BlpEncoding.Dxt, Is.EqualTo(2));
    Assert.That((int)BlpEncoding.UncompressedBgra, Is.EqualTo(3));

    var values = Enum.GetValues<BlpEncoding>();
    Assert.That(values, Has.Length.EqualTo(3));
  }

  [Test]
  [Category("Unit")]
  public void BlpAlphaEncoding_HasExpectedValues() {
    Assert.That((int)BlpAlphaEncoding.Dxt1, Is.EqualTo(0));
    Assert.That((int)BlpAlphaEncoding.Dxt3, Is.EqualTo(1));
    Assert.That((int)BlpAlphaEncoding.Dxt5, Is.EqualTo(7));

    var values = Enum.GetValues<BlpAlphaEncoding>();
    Assert.That(values, Has.Length.EqualTo(3));
  }

  [Test]
  [Category("Unit")]
  public void BlpFile_DefaultEncoding_IsPalette() {
    var file = new BlpFile();
    Assert.That(file.Encoding, Is.EqualTo(default(BlpEncoding)));
  }

  [Test]
  [Category("Unit")]
  public void BlpFile_DefaultWidth_IsZero() {
    var file = new BlpFile();
    Assert.That(file.Width, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void BlpFile_DefaultHeight_IsZero() {
    var file = new BlpFile();
    Assert.That(file.Height, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void BlpFile_DefaultAlphaDepth_IsZero() {
    var file = new BlpFile();
    Assert.That(file.AlphaDepth, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void BlpFile_DefaultHasMips_IsFalse() {
    var file = new BlpFile();
    Assert.That(file.HasMips, Is.False);
  }

  [Test]
  [Category("Unit")]
  public void BlpFile_DefaultPalette_IsNull() {
    var file = new BlpFile();
    Assert.That(file.Palette, Is.Null);
  }

  [Test]
  [Category("Unit")]
  public void BlpFile_DefaultMipData_IsEmpty() {
    var file = new BlpFile();
    Assert.That(file.MipData, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void BlpFile_PrimaryExtension_IsBlp() {
    var ext = _GetPrimaryExtension<BlpFile>();
    Assert.That(ext, Is.EqualTo(".blp"));
  }

  [Test]
  [Category("Unit")]
  public void BlpFile_FileExtensions_ContainsBlp() {
    var exts = _GetFileExtensions<BlpFile>();
    Assert.That(exts, Has.Length.EqualTo(1));
    Assert.That(exts[0], Is.EqualTo(".blp"));
  }

  private static string _GetPrimaryExtension<T>() where T : IImageFileFormat<T>
    => T.PrimaryExtension;

  private static string[] _GetFileExtensions<T>() where T : IImageFileFormat<T>
    => T.FileExtensions;
}
