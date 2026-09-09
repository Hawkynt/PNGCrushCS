using System;
using System.IO;
using System.Linq;
using System.Text;
using FileFormat.Core;
using FileFormat.Dxf;

namespace FileFormat.Dxf.Tests;

[TestFixture]
public sealed class DxfWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_RoundTripsEveryPair() {
    var original = DxfReader.FromBytes(Encoding.ASCII.GetBytes(
      "  0\r\nSECTION\r\n  2\r\nHEADER\r\n  9\r\n$EXTMIN\r\n 10\r\n0\r\n 20\r\n0\r\n" +
      "  9\r\n$EXTMAX\r\n 10\r\n8\r\n 20\r\n4\r\n  0\r\nENDSEC\r\n" +
      "  0\r\nSECTION\r\n  2\r\nENTITIES\r\n  0\r\nLINE\r\n 10\r\n1\r\n 20\r\n1\r\n 11\r\n7\r\n 21\r\n3\r\n" +
      "  0\r\nENDSEC\r\n  0\r\nEOF\r\n"
    ));

    var written = DxfWriter.ToBytes(original);
    var reparsed = DxfReader.FromBytes(written);

    Assert.That(reparsed.Pairs, Is.EqualTo(original.Pairs));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_CoalescesEqualHorizontalPixelsIntoOneSolid() {
    var image = new RawImage {
      Width = 4,
      Height = 1,
      Format = PixelFormat.Rgba32,
      PixelData = [255, 0, 0, 255, 255, 0, 0, 255, 255, 0, 0, 255, 255, 255, 255, 255]
    };

    var file = DxfWriter.FromRawImage(image);
    var solids = file.Pairs.Count(pair => pair.Code == 0 && pair.Value == "SOLID");

    Assert.That(solids, Is.EqualTo(1));
    Assert.That(file.Pairs.Any(pair => pair.Code == 62 && pair.Value == "1"), Is.True);
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_CompositesTransparentPixelsOntoWhitePaper() {
    var image = new RawImage {
      Width = 2,
      Height = 1,
      Format = PixelFormat.Rgba32,
      PixelData = [255, 0, 0, 0, 0, 0, 255, 255]
    };

    var file = DxfWriter.FromRawImage(image);
    var solids = file.Pairs.Count(pair => pair.Code == 0 && pair.Value == "SOLID");

    Assert.That(solids, Is.EqualTo(1));
    Assert.That(file.Pairs.Any(pair => pair.Code == 62 && pair.Value == "5"), Is.True);
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_BoundsLargeSourcesAndPreservesAspectRatio() {
    var pixels = new byte[1024 * 256 * 4];
    Array.Fill(pixels, (byte)255);
    var image = new RawImage {
      Width = 1024,
      Height = 256,
      Format = PixelFormat.Rgba32,
      PixelData = pixels
    };

    var file = DxfWriter.FromRawImage(image);
    var max = file.Pairs
      .Select((pair, index) => (pair, index))
      .First(item => item.pair.Code == 9 && item.pair.Value == "$EXTMAX").index;

    Assert.Multiple(() => {
      Assert.That(file.Pairs[max + 1], Is.EqualTo(new DxfPair(10, "512")));
      Assert.That(file.Pairs[max + 2], Is.EqualTo(new DxfPair(20, "128")));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_WrittenOutputParsesAndRenders() {
    var image = new RawImage {
      Width = 2,
      Height = 2,
      Format = PixelFormat.Rgba32,
      PixelData = [
        255, 0, 0, 255, 0, 0, 255, 255,
        0, 255, 0, 255, 0, 0, 0, 255
      ]
    };

    var written = DxfWriter.ToBytes(DxfWriter.FromRawImage(image));
    var reparsed = DxfReader.FromBytes(written);
    var rendered = DxfFile.ToRawImage(reparsed);

    Assert.Multiple(() => {
      Assert.That(rendered.Width, Is.GreaterThan(0));
      Assert.That(rendered.Height, Is.GreaterThan(0));
      Assert.That(reparsed.Pairs.Count(pair => pair.Code == 0 && pair.Value == "SOLID"), Is.EqualTo(4));
    });
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_RejectsAValueContainingALineBreak() {
    var file = new DxfFile {
      Pairs = [
        new(0, "SECTION"), new(2, "ENTITIES"), new(999, "broken\ncomment"), new(0, "ENDSEC"), new(0, "EOF")
      ]
    };

    Assert.Throws<InvalidDataException>(() => DxfWriter.ToBytes(file));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_RejectsCharactersOutsideTheEightBitTextStream() {
    var file = new DxfFile {
      Pairs = [
        new(0, "SECTION"), new(2, "ENTITIES"), new(999, "snowman \u2603"), new(0, "ENDSEC"), new(0, "EOF")
      ]
    };

    Assert.Throws<InvalidDataException>(() => DxfWriter.ToBytes(file));
  }
}
