using System;
using FileFormat.Ktx;

namespace FileFormat.Ktx.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void KtxVersion_HasExpectedValues() {
    Assert.That((int)KtxVersion.Ktx1, Is.EqualTo(1));
    Assert.That((int)KtxVersion.Ktx2, Is.EqualTo(2));

    var values = Enum.GetValues<KtxVersion>();
    Assert.That(values, Has.Length.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void KtxFile_DefaultProperties() {
    var file = new KtxFile();
    Assert.That(file.Width, Is.EqualTo(0));
    Assert.That(file.Height, Is.EqualTo(0));
    Assert.That(file.Depth, Is.EqualTo(0));
    Assert.That(file.MipLevels, Is.Empty);
    Assert.That(file.KeyValues, Is.Null);
  }

  [Test]
  [Category("Unit")]
  public void KtxMipLevel_DefaultProperties() {
    var mip = new KtxMipLevel();
    Assert.That(mip.Width, Is.EqualTo(0));
    Assert.That(mip.Height, Is.EqualTo(0));
    Assert.That(mip.Data, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void KtxKeyValue_DefaultProperties() {
    var kv = new KtxKeyValue();
    Assert.That(kv.Key, Is.EqualTo(string.Empty));
    Assert.That(kv.Value, Is.Empty);
  }
}
