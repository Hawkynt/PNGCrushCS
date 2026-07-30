using System;
using System.IO;
using FileFormat.Core;
using FileFormat.DuoMedium;

namespace FileFormat.DuoMedium.Tests;

[TestFixture]
public sealed class DuoMediumTests {

  [Test]
  public void Reader_AcceptsBothStoredSizes() {
    Assert.Multiple(() => {
      Assert.That(DuoMediumReader.FromBytes(new byte[DuoMediumFile.MinFileSize]).Data, Is.Not.Null);
      Assert.That(DuoMediumReader.FromBytes(new byte[DuoMediumFile.PaddedFileSize]).Data, Is.Not.Null);
      Assert.Throws<InvalidDataException>(() => DuoMediumReader.FromBytes(new byte[DuoMediumFile.MinFileSize - 1]));
    });
  }

  [Test]
  public void MediumResolutionTradesColoursForWidth() {
    var image = DuoMediumFile.ToRawImage(DuoMediumReader.FromBytes(new byte[DuoMediumFile.MinFileSize]));

    Assert.Multiple(() => {
      Assert.That(image.Width, Is.EqualTo(832), "twice the low-resolution width");
      Assert.That(DuoMediumFile.ColorCount, Is.EqualTo(4), "a quarter of the colours");
    });
  }

  [Test]
  public void EveryStoredRowIsDrawnOnTwoScanlines() {
    var data = new byte[DuoMediumFile.MinFileSize];
    data[2] = 0x0F; data[3] = 0xFF;                     // entry 1 white
    data[DuoMediumFile.FirstFrameOffset] = 0x80;        // plane 0, first pixel of row 0
    data[DuoMediumFile.FirstFrameOffset + DuoMediumFile.FrameSize] = 0x80;

    var image = DuoMediumFile.ToRawImage(DuoMediumReader.FromBytes(data));

    Assert.Multiple(() => {
      Assert.That(image.Height, Is.EqualTo(546));
      Assert.That(image.PixelData[832 * 3], Is.EqualTo(image.PixelData[0]), "scanline 1 repeats scanline 0");
    });
  }
}
