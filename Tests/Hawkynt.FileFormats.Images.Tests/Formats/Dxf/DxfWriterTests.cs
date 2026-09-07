using System;
using System.Linq;
using System.Text;
using FileFormat.Core;

namespace FileFormat.Dxf.Tests;

[TestFixture]
public sealed class DxfWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_WritesCanonicalAsciiPairs() {
    var file = new DxfFile {
      Pairs = [
        new(0, "SECTION"),
        new(2, "ENTITIES"),
        new(0, "ENDSEC"),
        new(0, "EOF")
      ]
    };

    var text = Encoding.Latin1.GetString(DxfWriter.ToBytes(file));

    Assert.That(text, Is.EqualTo(
      "  0\r\nSECTION\r\n"
      + "  2\r\nENTITIES\r\n"
      + "  0\r\nENDSEC\r\n"
      + "  0\r\nEOF\r\n"
    ));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ParsedDrawingRoundTripsEveryPair() {
    const string source =
      "  0\r\nSECTION\r\n"
      + "  2\r\nENTITIES\r\n"
      + "  0\r\nLINE\r\n"
      + " 10\r\n1.25\r\n"
      + " 20\r\n2.5\r\n"
      + " 11\r\n8.75\r\n"
      + " 21\r\n9.5\r\n"
      + "  0\r\nENDSEC\r\n"
      + "  0\r\nEOF\r\n";

    var original = DxfReader.FromBytes(Encoding.Latin1.GetBytes(source));
    var roundTrip = DxfReader.FromBytes(DxfWriter.ToBytes(original));

    Assert.That(roundTrip.Pairs, Is.EqualTo(original.Pairs));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ValueContainingLineBreakIsRefused() {
    var file = new DxfFile {
      Pairs = [
        new(0, "SECTION"),
        new(2, "ENTITIES"),
        new(999, "two\nlines"),
        new(0, "ENDSEC"),
        new(0, "EOF")
      ]
    };

    Assert.Throws<ArgumentException>(() => DxfWriter.ToBytes(file));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_FlatColourCoalescesIntoOneTrueColourSolid() {
    var pixels = Enumerable.Repeat(new byte[] { 255, 0, 0, 255 }, 12).SelectMany(static pixel => pixel).ToArray();
    var image = new RawImage { Width = 4, Height = 3, Format = PixelFormat.Rgba32, PixelData = pixels };

    var file = DxfFile.FromRawImage(image);
    var drawing = DxfDrawing.From(file);

    Assert.That(drawing.Entities, Has.Count.EqualTo(1));
    var solid = drawing.Entities[0];
    Assert.Multiple(() => {
      Assert.That(solid.Type, Is.EqualTo("SOLID"));
      Assert.That(solid.Integer(62, -1), Is.EqualTo(1));
      Assert.That(solid.Integer(420, -1), Is.EqualTo(0x00ff0000));
      Assert.That(solid.Number(10, -1), Is.EqualTo(0));
      Assert.That(solid.Number(20, -1), Is.EqualTo(0));
      Assert.That(solid.Number(11, -1), Is.EqualTo(4));
      Assert.That(solid.Number(21, -1), Is.EqualTo(0));
      Assert.That(solid.Number(12, -1), Is.EqualTo(0));
      Assert.That(solid.Number(22, -1), Is.EqualTo(3));
      Assert.That(solid.Number(13, -1), Is.EqualTo(4));
      Assert.That(solid.Number(23, -1), Is.EqualTo(3));
      Assert.That(file.Pairs[^1], Is.EqualTo(new DxfPair(0, "EOF")));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_AlphaIsCompositedOntoWhiteAndWhiteIsOmitted() {
    var image = new RawImage {
      Width = 2,
      Height = 1,
      Format = PixelFormat.Rgba32,
      PixelData = [
        0, 0, 0, 0,
        255, 0, 0, 128
      ]
    };

    var drawing = DxfDrawing.From(DxfFile.FromRawImage(image));

    Assert.That(drawing.Entities, Has.Count.EqualTo(1));
    var solid = drawing.Entities[0];
    Assert.Multiple(() => {
      Assert.That(solid.Integer(420, -1), Is.EqualTo(0x00ff7f7f));
      Assert.That(solid.Number(10, -1), Is.EqualTo(1));
      Assert.That(solid.Number(11, -1), Is.EqualTo(2));
    });
  }

  [Test]
  [Category("Unit")]
  public void FormatIoEncode_WritesAReadableDxf() {
    var image = new RawImage {
      Width = 2,
      Height = 2,
      Format = PixelFormat.Rgb24,
      PixelData = [
        255, 0, 0, 255, 0, 0,
        0, 0, 255, 0, 0, 255
      ]
    };

    var bytes = FormatIO.Encode<DxfFile>(image);
    var parsed = DxfReader.FromBytes(bytes);
    var drawing = DxfDrawing.From(parsed);

    Assert.Multiple(() => {
      Assert.That(drawing.Entities, Has.Count.EqualTo(2));
      Assert.That(drawing.Entities.All(static entity => entity.Type == "SOLID"));
      Assert.That(parsed.Variable("$ACADVER"), Is.Null);
      Assert.That(parsed.Pairs.Any(static pair => pair is { Code: 1, Value: "AC1018" }));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_TruncatedPixelBufferIsRefused() {
    var image = new RawImage {
      Width = 2,
      Height = 2,
      Format = PixelFormat.Rgba32,
      PixelData = [255, 0, 0, 255]
    };

    Assert.Throws<ArgumentException>(() => DxfFile.FromRawImage(image));
  }
}
