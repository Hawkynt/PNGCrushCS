using System;
using System.Collections.Generic;
using FileFormat.Pds;

namespace FileFormat.Pds.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void PdsBandStorage_HasExpectedValues() {
    Assert.That((int)PdsBandStorage.BandSequential, Is.EqualTo(0));
    Assert.That((int)PdsBandStorage.LineInterleaved, Is.EqualTo(1));
    Assert.That((int)PdsBandStorage.SampleInterleaved, Is.EqualTo(2));

    var values = Enum.GetValues<PdsBandStorage>();
    Assert.That(values, Has.Length.EqualTo(3));
  }

  [Test]
  [Category("Unit")]
  public void PdsSampleType_HasExpectedValues() {
    Assert.That((int)PdsSampleType.UnsignedByte, Is.EqualTo(0));
    Assert.That((int)PdsSampleType.MsbUnsigned16, Is.EqualTo(1));
    Assert.That((int)PdsSampleType.LsbUnsigned16, Is.EqualTo(2));

    var values = Enum.GetValues<PdsSampleType>();
    Assert.That(values, Has.Length.EqualTo(3));
  }

  [Test]
  [Category("Unit")]
  public void PdsFile_DefaultWidth_IsZero() {
    var file = new PdsFile();

    Assert.That(file.Width, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void PdsFile_DefaultHeight_IsZero() {
    var file = new PdsFile();

    Assert.That(file.Height, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void PdsFile_DefaultSampleBits_IsZero() {
    var file = new PdsFile();

    Assert.That(file.SampleBits, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void PdsFile_DefaultBands_IsZero() {
    var file = new PdsFile();

    Assert.That(file.Bands, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void PdsFile_DefaultPixelData_IsNull() {
    var file = new PdsFile();

    Assert.That(file.PixelData, Is.Null);
  }

  [Test]
  [Category("Unit")]
  public void PdsFile_DefaultLabels_IsNull() {
    var file = new PdsFile();

    Assert.That(file.Labels, Is.Null);
  }

  [Test]
  [Category("Unit")]
  public void PdsFile_DefaultBandStorage_IsBandSequential() {
    var file = new PdsFile();

    Assert.That(file.BandStorage, Is.EqualTo(PdsBandStorage.BandSequential));
  }

  [Test]
  [Category("Unit")]
  public void PdsFile_DefaultSampleType_IsUnsignedByte() {
    var file = new PdsFile();

    Assert.That(file.SampleType, Is.EqualTo(PdsSampleType.UnsignedByte));
  }

  [Test]
  [Category("Unit")]
  public void PdsFile_PrimaryExtension_IsPds() {
    var ext = GetPrimaryExtension();

    Assert.That(ext, Is.EqualTo(".pds"));
  }

  [Test]
  [Category("Unit")]
  public void PdsFile_FileExtensions_IncludesBothExtensions() {
    var exts = GetFileExtensions();

    Assert.That(exts, Does.Contain(".pds"));
    Assert.That(exts, Does.Contain(".lbl"));
    Assert.That(exts, Has.Length.EqualTo(2));
  }

  private static string GetPrimaryExtension() {
    // Access via interface to test static abstract member
    return GetPrimary<PdsFile>();
  }

  private static string GetPrimary<T>() where T : FileFormat.Core.IImageFormatMetadata<T>
    => T.PrimaryExtension;

  private static string[] GetFileExtensions() => GetExts<PdsFile>();

  private static string[] GetExts<T>() where T : FileFormat.Core.IImageFormatMetadata<T>
    => T.FileExtensions;
}
