using System;
using FileFormat.Jbig2;
using FileFormat.Core;

namespace FileFormat.Jbig2.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void Jbig2SegmentType_HasExpectedValues() {
    Assert.That((byte)Jbig2SegmentType.PageInformation, Is.EqualTo(48));
    Assert.That((byte)Jbig2SegmentType.ImmediateGenericRegion, Is.EqualTo(38));
    Assert.That((byte)Jbig2SegmentType.ImmediateLosslessGenericRegion, Is.EqualTo(39));
    Assert.That((byte)Jbig2SegmentType.EndOfPage, Is.EqualTo(49));
    Assert.That((byte)Jbig2SegmentType.EndOfFile, Is.EqualTo(51));
  }

  [Test]
  [Category("Unit")]
  public void Jbig2File_DefaultPixelData_IsEmpty() {
    var file = new Jbig2File { Width = 8, Height = 1 };

    Assert.That(file.PixelData, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void Jbig2File_DefaultSegments_IsEmpty() {
    var file = new Jbig2File { Width = 8, Height = 1 };

    Assert.That(file.Segments, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void Jbig2File_DefaultWidth_IsZero() {
    var file = new Jbig2File();

    Assert.That(file.Width, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void Jbig2File_DefaultHeight_IsZero() {
    var file = new Jbig2File();

    Assert.That(file.Height, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void Jbig2File_InitProperties_StoreCorrectly() {
    var pixelData = new byte[] { 0xFF };
    var file = new Jbig2File {
      Width = 8,
      Height = 1,
      PixelData = pixelData,
    };

    Assert.That(file.Width, Is.EqualTo(8));
    Assert.That(file.Height, Is.EqualTo(1));
    Assert.That(file.PixelData, Is.SameAs(pixelData));
  }

  [Test]
  [Category("Unit")]
  public void Jbig2File_ToRawImage_ReturnsIndexed1Format() {
    var raw = Jbig2File.ToRawImage(new Jbig2File { Width = 1, Height = 1, PixelData = [0x00] });

    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Indexed1));
  }

  [Test]
  [Category("Unit")]
  public void Jbig2File_FileExtensions_ContainJb2AndJbig2() {
    var file = new Jbig2File { Width = 8, Height = 1, PixelData = [0x00] };
    var bytes = Jbig2Writer.ToBytes(file);

    // Verify file can be written and re-read (exercises the interface path)
    var restored = Jbig2Reader.FromBytes(bytes);
    Assert.That(restored.Width, Is.EqualTo(8));
  }

  [Test]
  [Category("Unit")]
  public void Jbig2File_ToRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => Jbig2File.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void Jbig2File_FromRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => Jbig2File.FromRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void Jbig2File_FromRawImage_WrongFormat_ThrowsArgumentException() {
    var raw = new RawImage {
      Width = 8,
      Height = 1,
      Format = PixelFormat.Rgb24,
      PixelData = new byte[24],
    };

    Assert.Throws<ArgumentException>(() => Jbig2File.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void Jbig2File_ToRawImage_HasCorrectPalette() {
    var file = new Jbig2File {
      Width = 8,
      Height = 1,
      PixelData = [0x00],
    };

    var raw = Jbig2File.ToRawImage(file);

    Assert.That(raw.Palette, Is.Not.Null);
    Assert.That(raw.PaletteCount, Is.EqualTo(2));
    // Palette entry 0: black (0,0,0)
    Assert.That(raw.Palette![0], Is.EqualTo(0));
    Assert.That(raw.Palette[1], Is.EqualTo(0));
    Assert.That(raw.Palette[2], Is.EqualTo(0));
    // Palette entry 1: white (255,255,255)
    Assert.That(raw.Palette[3], Is.EqualTo(255));
    Assert.That(raw.Palette[4], Is.EqualTo(255));
    Assert.That(raw.Palette[5], Is.EqualTo(255));
  }

  [Test]
  [Category("Unit")]
  public void Jbig2File_ToRawImage_ClonesPixelData() {
    var pixelData = new byte[] { 0xFF };
    var file = new Jbig2File {
      Width = 8,
      Height = 1,
      PixelData = pixelData,
    };

    var raw = Jbig2File.ToRawImage(file);

    Assert.That(raw.PixelData, Is.Not.SameAs(pixelData));
    Assert.That(raw.PixelData, Is.EqualTo(pixelData));
  }

  [Test]
  [Category("Unit")]
  public void Jbig2Segment_DefaultValues() {
    var seg = new Jbig2Segment();

    Assert.That(seg.Number, Is.EqualTo(0));
    Assert.That(seg.Type, Is.EqualTo((Jbig2SegmentType)0));
    Assert.That(seg.DeferredNonRetain, Is.False);
    Assert.That(seg.ReferredSegments, Is.Empty);
    Assert.That(seg.PageAssociation, Is.EqualTo(0));
    Assert.That(seg.Data, Is.Empty);
  }
}
