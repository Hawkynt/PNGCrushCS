using System;
using System.IO;
using FileFormat.Core;
using FileFormat.PaintShop;

namespace FileFormat.PaintShop.Tests;

[TestFixture]
public sealed class PaintShopTests {

  [Test]
  public void Reader_RejectsAnyOtherLength() {
    Assert.Multiple(() => {
      Assert.Throws<InvalidDataException>(() => PaintShopReader.FromBytes(new byte[PaintShopFile.FileSize - 1]));
      Assert.Throws<InvalidDataException>(() => PaintShopReader.FromBytes(new byte[PaintShopFile.FileSize + 1]));
    });
  }

  [Test]
  public void APageIsTwiceAsTallAsTheScreen() {
    var image = PaintShopFile.ToRawImage(PaintShopReader.FromBytes(new byte[PaintShopFile.FileSize]));

    Assert.That((image.Width, image.Height), Is.EqualTo((640, 800)));
  }

  [Test]
  public void ASetBitIsInkOnWhitePaper() {
    var data = new byte[PaintShopFile.FileSize];
    data[0] = 0b1000_0000;

    var image = PaintShopFile.ToRawImage(PaintShopReader.FromBytes(data));

    Assert.Multiple(() => {
      Assert.That(image.Palette![..3], Is.EqualTo(new byte[] { 255, 255, 255 }), "paper");
      Assert.That(image.Palette![3..6], Is.EqualTo(new byte[] { 0, 0, 0 }), "ink");
      Assert.That(image.PixelData[0], Is.EqualTo(1));
      Assert.That(image.PixelData[1], Is.Zero);
    });
  }

  [Test]
  public void RoundTrip_PreservesTheBitmap() {
    var data = new byte[PaintShopFile.FileSize];
    for (var i = 0; i < data.Length; ++i)
      data[i] = (byte)(i * 13);

    Assert.That(PaintShopWriter.ToBytes(PaintShopReader.FromBytes(data)), Is.EqualTo(data));
  }

  [Test]
  public void EncodingATwoColorPage_ReproducesItExactly() {
    var data = new byte[PaintShopFile.FileSize];
    for (var i = 0; i < data.Length; ++i)
      data[i] = (byte)(i * 37);

    var source = PaintShopFile.ToRawImage(PaintShopReader.FromBytes(data));
    var again = PaintShopFile.FromRawImage(PixelConverter.Convert(source, PixelFormat.Rgb24));

    Assert.That(again.BitmapData, Is.EqualTo(data));
  }
}
