using System;
using FileFormat.Core;

namespace FileFormat.Fits.Tests;

[TestFixture]
public sealed class DocumentTests {

  [Test]
  [Category("Integration")]
  public void TwoImageHdus_RoundTripAndExposeBothImages() {
    var first = new FitsHdu {
      IsPrimary = true,
      Axes = [3, 2],
      Bitpix = FitsBitpix.UInt8,
      Data = [6, 5, 4, 3, 2, 1],
    };
    var second = new FitsHdu {
      IsPrimary = false,
      ExtensionType = "IMAGE",
      Axes = [2, 2],
      Bitpix = FitsBitpix.UInt8,
      Data = [40, 30, 20, 10],
    };

    var bytes = FitsDocumentWriter.ToBytes(new FitsDocumentFile { Hdus = [first, second] });
    var restored = FitsDocumentReader.FromSpan(bytes);

    Assert.That(restored.Hdus, Has.Count.EqualTo(2));
    Assert.That(FitsDocumentFile.ImageCount(restored), Is.EqualTo(2));
    var a = FitsDocumentFile.ToRawImage(restored, 0).EnsureFormat(PixelFormat.Gray8);
    var b = FitsDocumentFile.ToRawImage(restored, 1).EnsureFormat(PixelFormat.Gray8);
    Assert.That(a.PixelData, Is.EqualTo(new byte[] { 1, 2, 3, 4, 5, 6 }));
    Assert.That(b.PixelData, Is.EqualTo(new byte[] { 10, 20, 30, 40 }));
  }

  [Test]
  [Category("Integration")]
  public void FourDimensionalArray_ExposesEveryPlane() {
    // NAXIS1=2, NAXIS2=2, NAXIS3=2, NAXIS4=3 => six grayscale planes.
    var data = new byte[24];
    for (var plane = 0; plane < 6; ++plane)
      for (var i = 0; i < 4; ++i)
        data[plane * 4 + i] = (byte)(plane * 10 + (3 - i)); // bottom-up in each FITS plane

    var bytes = FitsDocumentWriter.ToBytes(new FitsDocumentFile {
      Hdus = [new FitsHdu {
        IsPrimary = true,
        Axes = [2, 2, 2, 3],
        Bitpix = FitsBitpix.UInt8,
        Data = data,
      }],
    });
    var restored = FitsDocumentReader.FromSpan(bytes);

    Assert.That(FitsDocumentFile.ImageCount(restored), Is.EqualTo(6));
    for (var plane = 0; plane < 6; ++plane) {
      var image = FitsDocumentFile.ToRawImage(restored, plane).EnsureFormat(PixelFormat.Gray8);
      Assert.That(image.PixelData, Is.EqualTo(new byte[] {
        (byte)(plane * 10 + 1), (byte)(plane * 10 + 0),
        (byte)(plane * 10 + 3), (byte)(plane * 10 + 2),
      }));
    }
  }

  [Test]
  [Category("Integration")]
  public void HigherDimensionalRgbCube_ExposesEachColourImage() {
    var oneImageBytes = 2 * 1 * 3;
    var data = new byte[oneImageBytes * 2];
    // FITS planar RGB: R plane, G plane, B plane for image 0 then image 1.
    byte[] expected0 = [1, 2, 10, 20, 100, 200];
    byte[] expected1 = [3, 4, 30, 40, 130, 140];
    expected0.CopyTo(data, 0);
    expected1.CopyTo(data, oneImageBytes);

    var doc = new FitsDocumentFile {
      Hdus = [new FitsHdu {
        IsPrimary = true,
        Axes = [2, 1, 3, 2],
        Bitpix = FitsBitpix.UInt8,
        Data = data,
      }],
    };
    var restored = FitsDocumentReader.FromSpan(FitsDocumentWriter.ToBytes(doc));

    Assert.That(FitsDocumentFile.ImageCount(restored), Is.EqualTo(2));
    var first = FitsDocumentFile.ToRawImage(restored, 0);
    var second = FitsDocumentFile.ToRawImage(restored, 1);
    Assert.That(first.Format, Is.EqualTo(PixelFormat.Rgb24));
    Assert.That(second.Format, Is.EqualTo(PixelFormat.Rgb24));
    Assert.That(first.PixelData, Is.EqualTo(new byte[] { 1, 10, 100, 2, 20, 200 }));
    Assert.That(second.PixelData, Is.EqualTo(new byte[] { 3, 30, 130, 4, 40, 140 }));
  }

  [Test]
  [Category("Integration")]
  public void NonImageExtension_IsPreservedButNotCountedAsImage() {
    var table = new FitsHdu {
      IsPrimary = false,
      ExtensionType = "BINTABLE",
      Axes = [8, 2],
      Bitpix = FitsBitpix.UInt8,
      Data = new byte[16],
    };
    var document = new FitsDocumentFile {
      Hdus = [
        new FitsHdu { IsPrimary = true, Axes = [1, 1], Bitpix = FitsBitpix.UInt8, Data = [123] },
        table,
      ],
    };

    var restored = FitsDocumentReader.FromSpan(FitsDocumentWriter.ToBytes(document));
    Assert.That(restored.Hdus, Has.Count.EqualTo(2));
    Assert.That(restored.Hdus[1].ExtensionType.Trim(), Is.EqualTo("BINTABLE"));
    Assert.That(restored.Hdus[1].Data, Is.EqualTo(new byte[16]));
    Assert.That(FitsDocumentFile.ImageCount(restored), Is.EqualTo(1));
  }

  [Test]
  [Category("Integration")]
  public void FromRawImage_CreatesSinglePrimaryHdu() {
    var source = new RawImage {
      Width = 2,
      Height = 2,
      Format = PixelFormat.Rgba32,
      PixelData = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16],
    };
    var doc = FitsDocumentFile.FromRawImage(source);
    var restored = FitsDocumentReader.FromSpan(FitsDocumentWriter.ToBytes(doc));
    var decoded = FitsDocumentFile.ToRawImage(restored);

    Assert.That(restored.Hdus, Has.Count.EqualTo(1));
    Assert.That(restored.Hdus[0].Axes, Is.EqualTo(new long[] { 2, 2, 4 }));
    Assert.That(decoded.Format, Is.EqualTo(PixelFormat.Rgba32));
    Assert.That(decoded.PixelData, Is.EqualTo(source.PixelData));
  }
}
