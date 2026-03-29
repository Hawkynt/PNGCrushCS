using FileFormat.ZxPaintbrush;
using FileFormat.Core;

namespace FileFormat.ZxPaintbrush.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void ZxPaintbrushFile_DefaultBitmapData_IsEmpty() {
    var file = new ZxPaintbrushFile();
    Assert.That(file.BitmapData, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void ZxPaintbrushFile_DefaultAttributeData_IsEmpty() {
    var file = new ZxPaintbrushFile();
    Assert.That(file.AttributeData, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void ZxPaintbrushFile_DefaultExtraData_IsEmpty() {
    var file = new ZxPaintbrushFile();
    Assert.That(file.ExtraData, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void ZxPaintbrushFile_Width_IsAlways256() {
    var file = new ZxPaintbrushFile();
    Assert.That(file.Width, Is.EqualTo(256));
  }

  [Test]
  [Category("Unit")]
  public void ZxPaintbrushFile_Height_IsAlways192() {
    var file = new ZxPaintbrushFile();
    Assert.That(file.Height, Is.EqualTo(192));
  }

  [Test]
  [Category("Unit")]
  public void ReaderConstants_MinFileSize_Is6912() {
    Assert.That(ZxPaintbrushReader.MinFileSize, Is.EqualTo(6912));
  }

  [Test]
  [Category("Unit")]
  public void ReaderConstants_BitmapSize_Is6144() {
    Assert.That(ZxPaintbrushReader.BitmapSize, Is.EqualTo(6144));
  }

  [Test]
  [Category("Unit")]
  public void ReaderConstants_AttributeSize_Is768() {
    Assert.That(ZxPaintbrushReader.AttributeSize, Is.EqualTo(768));
  }

  [Test]
  [Category("Unit")]
  public void ReaderConstants_BytesPerRow_Is32() {
    Assert.That(ZxPaintbrushReader.BytesPerRow, Is.EqualTo(32));
  }

  [Test]
  [Category("Unit")]
  public void ReaderConstants_RowCount_Is192() {
    Assert.That(ZxPaintbrushReader.RowCount, Is.EqualTo(192));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<System.ArgumentNullException>(() => ZxPaintbrushFile.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<System.ArgumentNullException>(() => ZxPaintbrushFile.FromRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_ThrowsNotSupportedException() {
    var raw = new RawImage {
      Width = 256,
      Height = 192,
      Format = PixelFormat.Rgb24,
      PixelData = new byte[256 * 192 * 3],
    };
    Assert.Throws<System.NotSupportedException>(() => ZxPaintbrushFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_ProducesRgb24() {
    var file = new ZxPaintbrushFile {
      BitmapData = new byte[6144],
      AttributeData = new byte[768],
    };

    var raw = ZxPaintbrushFile.ToRawImage(file);

    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Rgb24));
    Assert.That(raw.Width, Is.EqualTo(256));
    Assert.That(raw.Height, Is.EqualTo(192));
    Assert.That(raw.PixelData.Length, Is.EqualTo(256 * 192 * 3));
  }
}
