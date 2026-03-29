using System;
using System.IO;
using System.Text;
using FileFormat.Netpbm;

namespace FileFormat.Netpbm.Tests;

[TestFixture]
public sealed class NetpbmHeaderParserTests {

  [Test]
  [Category("Unit")]
  public void Parse_SkipsCommentLines() {
    var header = "P6\n# this is a comment\n3 2\n255\n";
    var headerBytes = Encoding.ASCII.GetBytes(header);
    var data = new byte[headerBytes.Length + 3 * 2 * 3];
    Array.Copy(headerBytes, data, headerBytes.Length);

    var result = NetpbmHeaderParser.Parse(data);

    Assert.That(result.Width, Is.EqualTo(3));
    Assert.That(result.Height, Is.EqualTo(2));
    Assert.That(result.MaxValue, Is.EqualTo(255));
  }

  [Test]
  [Category("Unit")]
  public void Parse_SkipsInlineCommentBetweenDimensions() {
    var header = "P5\n4\n# comment between width and height\n3\n255\n";
    var headerBytes = Encoding.ASCII.GetBytes(header);
    var data = new byte[headerBytes.Length + 4 * 3];
    Array.Copy(headerBytes, data, headerBytes.Length);

    var result = NetpbmHeaderParser.Parse(data);

    Assert.That(result.Width, Is.EqualTo(4));
    Assert.That(result.Height, Is.EqualTo(3));
    Assert.That(result.MaxValue, Is.EqualTo(255));
  }

  [Test]
  [Category("Unit")]
  public void Parse_HandlesMultipleWhitespaceSeparators() {
    var header = "P6\n  3   2  \n  255  \n";
    var headerBytes = Encoding.ASCII.GetBytes(header);
    var data = new byte[headerBytes.Length + 3 * 2 * 3];
    Array.Copy(headerBytes, data, headerBytes.Length);

    var result = NetpbmHeaderParser.Parse(data);

    Assert.That(result.Width, Is.EqualTo(3));
    Assert.That(result.Height, Is.EqualTo(2));
    Assert.That(result.MaxValue, Is.EqualTo(255));
  }

  [Test]
  [Category("Unit")]
  public void Parse_PbmFormat_NoMaxValue() {
    var header = "P4\n8 4\n";
    var headerBytes = Encoding.ASCII.GetBytes(header);
    var bytesPerRow = (8 + 7) / 8;
    var data = new byte[headerBytes.Length + bytesPerRow * 4];
    Array.Copy(headerBytes, data, headerBytes.Length);

    var result = NetpbmHeaderParser.Parse(data);

    Assert.That(result.Width, Is.EqualTo(8));
    Assert.That(result.Height, Is.EqualTo(4));
    Assert.That(result.MaxValue, Is.EqualTo(1));
    Assert.That(result.Channels, Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void Parse_P7_KeywordParsing() {
    var header = "P7\nWIDTH 5\nHEIGHT 3\nDEPTH 4\nMAXVAL 65535\nTUPLTYPE RGB_ALPHA\nENDHDR\n";
    var headerBytes = Encoding.ASCII.GetBytes(header);
    var data = new byte[headerBytes.Length + 5 * 3 * 4 * 2]; // 16-bit samples
    Array.Copy(headerBytes, data, headerBytes.Length);

    var result = NetpbmHeaderParser.Parse(data);

    Assert.That(result.Format, Is.EqualTo(NetpbmFormat.Pam));
    Assert.That(result.Width, Is.EqualTo(5));
    Assert.That(result.Height, Is.EqualTo(3));
    Assert.That(result.MaxValue, Is.EqualTo(65535));
    Assert.That(result.Channels, Is.EqualTo(4));
    Assert.That(result.TupleType, Is.EqualTo("RGB_ALPHA"));
  }

  [Test]
  [Category("Unit")]
  public void Parse_P7_WithoutTupleType() {
    var header = "P7\nWIDTH 2\nHEIGHT 2\nDEPTH 1\nMAXVAL 255\nENDHDR\n";
    var headerBytes = Encoding.ASCII.GetBytes(header);
    var data = new byte[headerBytes.Length + 2 * 2];
    Array.Copy(headerBytes, data, headerBytes.Length);

    var result = NetpbmHeaderParser.Parse(data);

    Assert.That(result.TupleType, Is.Null);
    Assert.That(result.Channels, Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void Parse_P7_CommentsInPamHeader() {
    var header = "P7\n# comment\nWIDTH 2\n# another comment\nHEIGHT 2\nDEPTH 3\nMAXVAL 255\nTUPLTYPE RGB\nENDHDR\n";
    var headerBytes = Encoding.ASCII.GetBytes(header);
    var data = new byte[headerBytes.Length + 2 * 2 * 3];
    Array.Copy(headerBytes, data, headerBytes.Length);

    var result = NetpbmHeaderParser.Parse(data);

    Assert.That(result.Width, Is.EqualTo(2));
    Assert.That(result.Height, Is.EqualTo(2));
    Assert.That(result.TupleType, Is.EqualTo("RGB"));
  }

  [Test]
  [Category("Unit")]
  public void Parse_DataOffset_PointsAfterHeader() {
    var header = "P6\n3 2\n255\n";
    var headerBytes = Encoding.ASCII.GetBytes(header);
    var pixelData = new byte[3 * 2 * 3];
    var data = new byte[headerBytes.Length + pixelData.Length];
    Array.Copy(headerBytes, data, headerBytes.Length);

    var result = NetpbmHeaderParser.Parse(data);

    Assert.That(result.DataOffset, Is.EqualTo(headerBytes.Length));
  }

  [Test]
  [Category("Unit")]
  public void Parse_TabSeparators_ParsesCorrectly() {
    var header = "P5\n2\t3\n255\n";
    var headerBytes = Encoding.ASCII.GetBytes(header);
    var data = new byte[headerBytes.Length + 2 * 3];
    Array.Copy(headerBytes, data, headerBytes.Length);

    var result = NetpbmHeaderParser.Parse(data);

    Assert.That(result.Width, Is.EqualTo(2));
    Assert.That(result.Height, Is.EqualTo(3));
  }
}
