using System;
using System.Text;
using FileFormat.Miff;

namespace FileFormat.Miff.Tests;

[TestFixture]
public sealed class MiffHeaderParserTests {

  [Test]
  [Category("Unit")]
  public void Parse_BasicFields_ExtractsColumnsAndRows() {
    var header = "id=ImageMagick\ncolumns=4\nrows=3\n:\n\x1A";
    var data = Encoding.ASCII.GetBytes(header);

    var fields = MiffHeaderParser.Parse(data, out _);

    Assert.That(fields["columns"], Is.EqualTo("4"));
    Assert.That(fields["rows"], Is.EqualTo("3"));
  }

  [Test]
  [Category("Unit")]
  public void Parse_AllFields_ExtractsEveryKeyValue() {
    var header = "id=ImageMagick\nclass=DirectClass\ncolumns=10\nrows=20\ndepth=16\ntype=TrueColorAlpha\ncolorspace=sRGB\ncompression=RLE\n:\n\x1A";
    var data = Encoding.ASCII.GetBytes(header);

    var fields = MiffHeaderParser.Parse(data, out _);

    Assert.That(fields["id"], Is.EqualTo("ImageMagick"));
    Assert.That(fields["class"], Is.EqualTo("DirectClass"));
    Assert.That(fields["columns"], Is.EqualTo("10"));
    Assert.That(fields["rows"], Is.EqualTo("20"));
    Assert.That(fields["depth"], Is.EqualTo("16"));
    Assert.That(fields["type"], Is.EqualTo("TrueColorAlpha"));
    Assert.That(fields["colorspace"], Is.EqualTo("sRGB"));
    Assert.That(fields["compression"], Is.EqualTo("RLE"));
  }

  [Test]
  [Category("Unit")]
  public void Parse_FindsTerminator_SetsCorrectDataOffset() {
    var header = "id=ImageMagick\ncolumns=2\nrows=2\n:\n\x1A";
    var headerBytes = Encoding.ASCII.GetBytes(header);
    var pixelData = new byte[] { 0xAA, 0xBB, 0xCC };
    var data = new byte[headerBytes.Length + pixelData.Length];
    Array.Copy(headerBytes, data, headerBytes.Length);
    Array.Copy(pixelData, 0, data, headerBytes.Length, pixelData.Length);

    MiffHeaderParser.Parse(data, out var dataOffset);

    Assert.That(dataOffset, Is.EqualTo(headerBytes.Length));
    Assert.That(data[dataOffset], Is.EqualTo(0xAA));
  }
}
