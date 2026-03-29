using System;
using FileFormat.Core;
using FileFormat.GraphSaurus;

namespace FileFormat.GraphSaurus.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void GraphSaurusFile_DefaultWidth_Is256() {
    var file = new GraphSaurusFile();
    Assert.That(file.Width, Is.EqualTo(256));
  }

  [Test]
  [Category("Unit")]
  public void GraphSaurusFile_DefaultHeight_Is212() {
    var file = new GraphSaurusFile();
    Assert.That(file.Height, Is.EqualTo(212));
  }

  [Test]
  [Category("Unit")]
  public void GraphSaurusFile_DefaultPixelData_IsEmpty() {
    var file = new GraphSaurusFile();
    Assert.That(file.PixelData, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void GraphSaurusFile_FixedWidth_Is256() {
    Assert.That(GraphSaurusFile.FixedWidth, Is.EqualTo(256));
  }

  [Test]
  [Category("Unit")]
  public void GraphSaurusFile_FixedHeight_Is212() {
    Assert.That(GraphSaurusFile.FixedHeight, Is.EqualTo(212));
  }

  [Test]
  [Category("Unit")]
  public void GraphSaurusFile_ExpectedFileSize_Is54272() {
    Assert.That(GraphSaurusFile.ExpectedFileSize, Is.EqualTo(54272));
  }

  [Test]
  [Category("Unit")]
  public void GraphSaurusFile_InitPixelData_StoresCorrectly() {
    var pixels = new byte[] { 0xAB, 0xCD };
    var file = new GraphSaurusFile { PixelData = pixels };
    Assert.That(file.PixelData, Is.SameAs(pixels));
  }

  [Test]
  [Category("Unit")]
  public void GraphSaurusFile_ToRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => GraphSaurusFile.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void GraphSaurusFile_FromRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => GraphSaurusFile.FromRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void GraphSaurusFile_FromRawImage_WrongDimensions_ThrowsArgumentException() {
    var raw = new RawImage {
      Width = 320,
      Height = 200,
      Format = PixelFormat.Rgb24,
      PixelData = new byte[320 * 200 * 3],
    };
    Assert.Throws<ArgumentException>(() => GraphSaurusFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void GraphSaurusFile_FromRawImage_WrongFormat_ThrowsArgumentException() {
    var raw = new RawImage {
      Width = 256,
      Height = 212,
      Format = PixelFormat.Rgba32,
      PixelData = new byte[256 * 212 * 4],
    };
    Assert.Throws<ArgumentException>(() => GraphSaurusFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void GraphSaurusFile_ToRawImage_ReturnsRgb24Format() {
    var file = new GraphSaurusFile { PixelData = new byte[54272] };
    var raw = GraphSaurusFile.ToRawImage(file);
    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Rgb24));
  }

  [Test]
  [Category("Unit")]
  public void GraphSaurusFile_ToRawImage_HasCorrectDimensions() {
    var file = new GraphSaurusFile { PixelData = new byte[54272] };
    var raw = GraphSaurusFile.ToRawImage(file);
    Assert.That(raw.Width, Is.EqualTo(256));
    Assert.That(raw.Height, Is.EqualTo(212));
  }

  [Test]
  [Category("Unit")]
  public void GraphSaurusFile_ToRawImage_PixelDataSize() {
    var file = new GraphSaurusFile { PixelData = new byte[54272] };
    var raw = GraphSaurusFile.ToRawImage(file);
    Assert.That(raw.PixelData.Length, Is.EqualTo(256 * 212 * 3));
  }
}
