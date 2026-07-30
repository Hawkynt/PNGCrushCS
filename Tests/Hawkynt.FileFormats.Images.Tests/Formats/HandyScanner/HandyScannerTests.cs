using System;
using System.IO;
using FileFormat.Core;
using FileFormat.HandyScanner;

namespace FileFormat.HandyScanner.Tests;

[TestFixture]
public sealed class HandyScannerTests {

  [Test]
  public void HeightComesFromTheFileLength() {
    Assert.Multiple(() => {
      foreach (var rows in new[] { 1, 17, 500 }) {
        var file = HandyScannerReader.FromBytes(new byte[HandyScannerFile.BytesPerRow * rows]);
        Assert.That(file.Height, Is.EqualTo(rows), $"{rows} rows");
      }
    });
  }

  [Test]
  public void Reader_RejectsAPartialRow() {
    Assert.Multiple(() => {
      Assert.Throws<InvalidDataException>(() => HandyScannerReader.FromBytes(new byte[HandyScannerFile.BytesPerRow + 1]));
      Assert.Throws<InvalidDataException>(() => HandyScannerReader.FromBytes([]));
    });
  }

  [Test]
  public void ASetBitIsLight_WhichIsTheOppositeOfAPrintedPage() {
    var data = new byte[HandyScannerFile.BytesPerRow];
    data[0] = 0b1000_0000;

    var image = HandyScannerFile.ToRawImage(HandyScannerReader.FromBytes(data));

    Assert.Multiple(() => {
      Assert.That(image.Palette![..3], Is.EqualTo(new byte[] { 0, 0, 0 }), "background");
      Assert.That(image.Palette![3..6], Is.EqualTo(new byte[] { 255, 255, 255 }), "light");
      Assert.That(image.PixelData[0], Is.EqualTo(1));
    });
  }

  [Test]
  public void Width_IsTheCarriagesFixedSize() {
    var image = HandyScannerFile.ToRawImage(HandyScannerReader.FromBytes(new byte[HandyScannerFile.BytesPerRow * 4]));

    Assert.That(image.Width, Is.EqualTo(840));
  }

  [Test]
  public void RoundTrip_PreservesTheScan() {
    var data = new byte[HandyScannerFile.BytesPerRow * 30];
    for (var i = 0; i < data.Length; ++i)
      data[i] = (byte)(i * 19);

    Assert.That(HandyScannerWriter.ToBytes(HandyScannerReader.FromBytes(data)), Is.EqualTo(data));
  }

  [Test]
  public void EncodingATwoColorScan_ReproducesItExactly() {
    var data = new byte[HandyScannerFile.BytesPerRow * 25];
    for (var i = 0; i < data.Length; ++i)
      data[i] = (byte)(i * 53);

    var source = HandyScannerFile.ToRawImage(HandyScannerReader.FromBytes(data));
    var again = HandyScannerFile.FromRawImage(PixelConverter.Convert(source, PixelFormat.Rgb24));

    Assert.That(again.BitmapData, Is.EqualTo(data));
  }

  [Test]
  public void FromRawImage_RejectsAWrongWidth() {
    var image = new RawImage { Width = 320, Height = 10, Format = PixelFormat.Rgb24, PixelData = new byte[320 * 10 * 3] };

    Assert.Throws<ArgumentException>(() => HandyScannerFile.FromRawImage(image));
  }
}
