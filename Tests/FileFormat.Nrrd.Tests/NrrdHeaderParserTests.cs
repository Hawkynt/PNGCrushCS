using System;
using System.IO;
using FileFormat.Nrrd;

namespace FileFormat.Nrrd.Tests;

[TestFixture]
public sealed class NrrdHeaderParserTests {

  [Test]
  [Category("Unit")]
  public void Parse_SimpleFields_ExtractsKeyValuePairs() {
    var header = "NRRD0004\ntype: uint8\nencoding: raw\n\n";

    var fields = NrrdHeaderParser.Parse(header);

    Assert.That(fields["type"], Is.EqualTo("uint8"));
    Assert.That(fields["encoding"], Is.EqualTo("raw"));
  }

  [Test]
  [Category("Unit")]
  public void Parse_Sizes_ExtractsSizesString() {
    var header = "NRRD0004\ntype: uint8\nsizes: 10 20 30\nencoding: raw\n\n";

    var fields = NrrdHeaderParser.Parse(header);

    Assert.That(fields["sizes"], Is.EqualTo("10 20 30"));
  }

  [Test]
  [Category("Unit")]
  public void Parse_EmptyLineTerminates() {
    var header = "NRRD0004\ntype: uint8\n\nencoding: raw\n";

    var fields = NrrdHeaderParser.Parse(header);

    Assert.That(fields.ContainsKey("type"), Is.True);
    Assert.That(fields.ContainsKey("encoding"), Is.False);
  }

  [Test]
  [Category("Unit")]
  public void Parse_SkipsCommentLines() {
    var header = "NRRD0004\n# this is a comment\ntype: float\n\n";

    var fields = NrrdHeaderParser.Parse(header);

    Assert.That(fields["type"], Is.EqualTo("float"));
    Assert.That(fields.Count, Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void ParseType_ValidTypes() {
    Assert.That(NrrdHeaderParser.ParseType("int8"), Is.EqualTo(NrrdType.Int8));
    Assert.That(NrrdHeaderParser.ParseType("uint8"), Is.EqualTo(NrrdType.UInt8));
    Assert.That(NrrdHeaderParser.ParseType("float"), Is.EqualTo(NrrdType.Float));
    Assert.That(NrrdHeaderParser.ParseType("double"), Is.EqualTo(NrrdType.Double));
  }

  [Test]
  [Category("Unit")]
  public void ParseType_UnknownType_ThrowsInvalidDataException() {
    Assert.Throws<InvalidDataException>(() => NrrdHeaderParser.ParseType("complex128"));
  }

  [Test]
  [Category("Unit")]
  public void ParseEncoding_ValidEncodings() {
    Assert.That(NrrdHeaderParser.ParseEncoding("raw"), Is.EqualTo(NrrdEncoding.Raw));
    Assert.That(NrrdHeaderParser.ParseEncoding("gzip"), Is.EqualTo(NrrdEncoding.Gzip));
    Assert.That(NrrdHeaderParser.ParseEncoding("gz"), Is.EqualTo(NrrdEncoding.Gzip));
    Assert.That(NrrdHeaderParser.ParseEncoding("ascii"), Is.EqualTo(NrrdEncoding.Ascii));
    Assert.That(NrrdHeaderParser.ParseEncoding("hex"), Is.EqualTo(NrrdEncoding.Hex));
  }

  [Test]
  [Category("Unit")]
  public void ParseSizes_ValidSizes() {
    var sizes = NrrdHeaderParser.ParseSizes("10 20 30");

    Assert.That(sizes, Is.EqualTo(new[] { 10, 20, 30 }));
  }

  [Test]
  [Category("Unit")]
  public void ParseSpacings_ValidSpacings() {
    var spacings = NrrdHeaderParser.ParseSpacings("1.0 2.5 0.5");

    Assert.That(spacings[0], Is.EqualTo(1.0).Within(0.001));
    Assert.That(spacings[1], Is.EqualTo(2.5).Within(0.001));
    Assert.That(spacings[2], Is.EqualTo(0.5).Within(0.001));
  }
}
