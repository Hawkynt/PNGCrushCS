using System;
using FileFormat.Fsh;

namespace FileFormat.Fsh.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void FshRecordCode_Dxt1_HasExpectedValue() {
    Assert.That((byte)FshRecordCode.Dxt1, Is.EqualTo(0x60));
  }

  [Test]
  [Category("Unit")]
  public void FshRecordCode_Dxt3_HasExpectedValue() {
    Assert.That((byte)FshRecordCode.Dxt3, Is.EqualTo(0x61));
  }

  [Test]
  [Category("Unit")]
  public void FshRecordCode_Argb4444_HasExpectedValue() {
    Assert.That((byte)FshRecordCode.Argb4444, Is.EqualTo(0x6D));
  }

  [Test]
  [Category("Unit")]
  public void FshRecordCode_Argb8888_78_HasExpectedValue() {
    Assert.That((byte)FshRecordCode.Argb8888_78, Is.EqualTo(0x78));
  }

  [Test]
  [Category("Unit")]
  public void FshRecordCode_Indexed8_HasExpectedValue() {
    Assert.That((byte)FshRecordCode.Indexed8, Is.EqualTo(0x7B));
  }

  [Test]
  [Category("Unit")]
  public void FshRecordCode_Argb8888_HasExpectedValue() {
    Assert.That((byte)FshRecordCode.Argb8888, Is.EqualTo(0x7D));
  }

  [Test]
  [Category("Unit")]
  public void FshRecordCode_Argb1555_HasExpectedValue() {
    Assert.That((byte)FshRecordCode.Argb1555, Is.EqualTo(0x7E));
  }

  [Test]
  [Category("Unit")]
  public void FshRecordCode_Rgb888_HasExpectedValue() {
    Assert.That((byte)FshRecordCode.Rgb888, Is.EqualTo(0x7F));
  }

  [Test]
  [Category("Unit")]
  public void FshRecordCode_Rgb565_HasExpectedValue() {
    Assert.That((byte)FshRecordCode.Rgb565, Is.EqualTo(0x80));
  }

  [Test]
  [Category("Unit")]
  public void FshRecordCode_EnumCount_Is9() {
    var values = Enum.GetValues<FshRecordCode>();
    Assert.That(values.Length, Is.EqualTo(9));
  }

  [Test]
  [Category("Unit")]
  public void FshEntry_DefaultPixelData_IsEmptyArray() {
    var entry = new FshEntry();
    Assert.That(entry.PixelData, Is.Not.Null);
    Assert.That(entry.PixelData, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void FshEntry_DefaultPalette_IsNull() {
    var entry = new FshEntry();
    Assert.That(entry.Palette, Is.Null);
  }

  [Test]
  [Category("Unit")]
  public void FshEntry_DefaultTag_IsFourNulls() {
    var entry = new FshEntry();
    Assert.That(entry.Tag, Is.EqualTo("\0\0\0\0"));
  }

  [Test]
  [Category("Unit")]
  public void FshEntry_DefaultDimensions_AreZero() {
    var entry = new FshEntry();
    Assert.That(entry.Width, Is.EqualTo(0));
    Assert.That(entry.Height, Is.EqualTo(0));
    Assert.That(entry.CenterX, Is.EqualTo(0));
    Assert.That(entry.CenterY, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void FshEntry_InitProperties_RoundTrip() {
    var pixels = new byte[] { 0x01, 0x02, 0x03, 0x04 };
    var entry = new FshEntry {
      Tag = "test",
      RecordCode = FshRecordCode.Argb8888,
      Width = 1,
      Height = 1,
      PixelData = pixels,
      CenterX = 5,
      CenterY = 10,
    };

    Assert.That(entry.Tag, Is.EqualTo("test"));
    Assert.That(entry.RecordCode, Is.EqualTo(FshRecordCode.Argb8888));
    Assert.That(entry.Width, Is.EqualTo(1));
    Assert.That(entry.Height, Is.EqualTo(1));
    Assert.That(entry.PixelData, Is.SameAs(pixels));
    Assert.That(entry.CenterX, Is.EqualTo(5));
    Assert.That(entry.CenterY, Is.EqualTo(10));
  }

  [Test]
  [Category("Unit")]
  public void FshFile_DefaultDirectoryId_IsGimx() {
    var file = new FshFile();
    Assert.That(file.DirectoryId, Is.EqualTo("GIMX"));
  }

  [Test]
  [Category("Unit")]
  public void FshFile_DefaultEntries_IsEmptyList() {
    var file = new FshFile();
    Assert.That(file.Entries, Is.Not.Null);
    Assert.That(file.Entries, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void FshFile_ToRawImage_Null_Throws() {
    Assert.Throws<ArgumentNullException>(() => FshFile.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FshFile_ToRawImage_EmptyEntries_Throws() {
    var file = new FshFile { Entries = [] };
    Assert.Throws<ArgumentException>(() => FshFile.ToRawImage(file));
  }

  [Test]
  [Category("Unit")]
  public void FshFile_FromRawImage_Null_Throws() {
    Assert.Throws<ArgumentNullException>(() => FshFile.FromRawImage(null!));
  }
}
