using System;
using System.Text;
using FileFormat.Envi;

namespace FileFormat.Envi.Tests;

[TestFixture]
public sealed class EnviHeaderParserTests {

  [Test]
  [Category("Unit")]
  public void Parse_ExtractsDimensions() {
    var header = _BuildMinimalHeader(10, 20, 1, 1, "bsq");
    var (fields, _) = EnviHeaderParser.Parse(header);

    Assert.That(EnviHeaderParser.GetInt(fields, "samples"), Is.EqualTo(10));
    Assert.That(EnviHeaderParser.GetInt(fields, "lines"), Is.EqualTo(20));
  }

  [Test]
  [Category("Unit")]
  public void Parse_ExtractsBands() {
    var header = _BuildMinimalHeader(4, 2, 3, 1, "bip");
    var (fields, _) = EnviHeaderParser.Parse(header);

    Assert.That(EnviHeaderParser.GetInt(fields, "bands"), Is.EqualTo(3));
  }

  [Test]
  [Category("Unit")]
  public void Parse_ExtractsInterleave() {
    var header = _BuildMinimalHeader(4, 2, 3, 1, "bil");
    var (fields, _) = EnviHeaderParser.Parse(header);

    Assert.That(EnviHeaderParser.GetString(fields, "interleave"), Is.EqualTo("bil"));
  }

  [Test]
  [Category("Unit")]
  public void Parse_ExtractsDataType() {
    var header = _BuildMinimalHeader(4, 2, 1, 12, "bsq");
    var (fields, _) = EnviHeaderParser.Parse(header);

    Assert.That(EnviHeaderParser.GetInt(fields, "data type"), Is.EqualTo(12));
  }

  [Test]
  [Category("Unit")]
  public void Parse_ExtractsByteOrder() {
    var sb = new StringBuilder();
    sb.Append("ENVI\n");
    sb.Append("samples = 2\n");
    sb.Append("lines = 1\n");
    sb.Append("bands = 1\n");
    sb.Append("data type = 1\n");
    sb.Append("interleave = bsq\n");
    sb.Append("byte order = 1\n");
    var data = Encoding.ASCII.GetBytes(sb.ToString());

    var (fields, _) = EnviHeaderParser.Parse(data);

    Assert.That(EnviHeaderParser.GetInt(fields, "byte order"), Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void Parse_HandlesMultilineValues() {
    var sb = new StringBuilder();
    sb.Append("ENVI\n");
    sb.Append("samples = 2\n");
    sb.Append("lines = 1\n");
    sb.Append("bands = 3\n");
    sb.Append("data type = 1\n");
    sb.Append("interleave = bsq\n");
    sb.Append("byte order = 0\n");
    sb.Append("band names = {\n");
    sb.Append("Red, Green, Blue}\n");
    var data = Encoding.ASCII.GetBytes(sb.ToString());

    var (fields, _) = EnviHeaderParser.Parse(data);

    Assert.That(fields.ContainsKey("band names"), Is.True);
    Assert.That(fields["band names"], Does.Contain("Red"));
    Assert.That(fields["band names"], Does.Contain("Blue"));
  }

  [Test]
  [Category("Unit")]
  public void Parse_HandlesSingleLineBraceValues() {
    var sb = new StringBuilder();
    sb.Append("ENVI\n");
    sb.Append("samples = 2\n");
    sb.Append("lines = 1\n");
    sb.Append("bands = 1\n");
    sb.Append("data type = 1\n");
    sb.Append("interleave = bsq\n");
    sb.Append("description = {test image}\n");
    var data = Encoding.ASCII.GetBytes(sb.ToString());

    var (fields, _) = EnviHeaderParser.Parse(data);

    Assert.That(fields["description"], Is.EqualTo("test image"));
  }

  [Test]
  [Category("Unit")]
  public void Parse_ReturnsDataOffset() {
    var header = "ENVI\nsamples = 2\nlines = 1\nbands = 1\ndata type = 1\ninterleave = bsq\n";
    var headerBytes = Encoding.ASCII.GetBytes(header);

    var (_, dataOffset) = EnviHeaderParser.Parse(headerBytes);

    Assert.That(dataOffset, Is.EqualTo(headerBytes.Length));
  }

  [Test]
  [Category("Unit")]
  public void ParseInterleave_Bsq() {
    Assert.That(EnviHeaderParser.ParseInterleave("bsq"), Is.EqualTo(EnviInterleave.Bsq));
  }

  [Test]
  [Category("Unit")]
  public void ParseInterleave_Bip() {
    Assert.That(EnviHeaderParser.ParseInterleave("bip"), Is.EqualTo(EnviInterleave.Bip));
  }

  [Test]
  [Category("Unit")]
  public void ParseInterleave_Bil() {
    Assert.That(EnviHeaderParser.ParseInterleave("bil"), Is.EqualTo(EnviInterleave.Bil));
  }

  [Test]
  [Category("Unit")]
  public void ParseInterleave_CaseInsensitive() {
    Assert.That(EnviHeaderParser.ParseInterleave("BSQ"), Is.EqualTo(EnviInterleave.Bsq));
    Assert.That(EnviHeaderParser.ParseInterleave("BIP"), Is.EqualTo(EnviInterleave.Bip));
    Assert.That(EnviHeaderParser.ParseInterleave("BIL"), Is.EqualTo(EnviInterleave.Bil));
  }

  [Test]
  [Category("Unit")]
  public void ParseInterleave_UnknownDefaultsToBsq() {
    Assert.That(EnviHeaderParser.ParseInterleave("unknown"), Is.EqualTo(EnviInterleave.Bsq));
  }

  [Test]
  [Category("Unit")]
  public void Format_ProducesValidHeader() {
    var headerBytes = EnviHeaderParser.Format(4, 2, 1, 1, EnviInterleave.Bsq, 0);
    var headerText = Encoding.ASCII.GetString(headerBytes);

    Assert.That(headerText, Does.StartWith("ENVI\n"));
    Assert.That(headerText, Does.Contain("samples = 4"));
    Assert.That(headerText, Does.Contain("lines = 2"));
    Assert.That(headerText, Does.Contain("bands = 1"));
    Assert.That(headerText, Does.Contain("data type = 1"));
    Assert.That(headerText, Does.Contain("interleave = bsq"));
    Assert.That(headerText, Does.Contain("byte order = 0"));
  }

  [Test]
  [Category("Unit")]
  public void Format_ContainsHeaderOffsetField() {
    var headerBytes = EnviHeaderParser.Format(2, 1, 1, 1, EnviInterleave.Bsq, 0);
    var headerText = Encoding.ASCII.GetString(headerBytes);

    Assert.That(headerText, Does.Contain("header offset = 0"));
  }

  [Test]
  [Category("Unit")]
  public void GetInt_MissingKey_ReturnsDefault() {
    var fields = new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    Assert.That(EnviHeaderParser.GetInt(fields, "nonexistent", 42), Is.EqualTo(42));
  }

  [Test]
  [Category("Unit")]
  public void GetString_MissingKey_ReturnsDefault() {
    var fields = new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    Assert.That(EnviHeaderParser.GetString(fields, "nonexistent", "fallback"), Is.EqualTo("fallback"));
  }

  private static byte[] _BuildMinimalHeader(int width, int height, int bands, int dataType, string interleave) {
    var sb = new StringBuilder();
    sb.Append("ENVI\n");
    sb.Append($"samples = {width}\n");
    sb.Append($"lines = {height}\n");
    sb.Append($"bands = {bands}\n");
    sb.Append($"data type = {dataType}\n");
    sb.Append($"interleave = {interleave}\n");
    sb.Append("byte order = 0\n");
    return Encoding.ASCII.GetBytes(sb.ToString());
  }
}
