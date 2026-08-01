using System;
using System.Text;
using FileFormat.Cals;

namespace FileFormat.Cals.Tests;

[TestFixture]
public sealed class CalsHeaderParserTests {

  [Test]
  [Category("Unit")]
  public void Parse_ExtractsDimensions() {
    var file = new CalsFile {
      Width = 1024,
      Height = 768,
      PixelData = new byte[(1024 + 7) / 8 * 768]
    };

    var header = CalsHeaderParser.Format(file);
    var fields = CalsHeaderParser.ParseAll(header);

    Assert.That(fields.ContainsKey("rpelcnt"), Is.True);
    Assert.That(fields["rpelcnt"], Is.EqualTo("001024,000768"));
  }

  [Test]
  [Category("Unit")]
  public void Parse_ExtractsDpi() {
    var file = new CalsFile {
      Width = 8,
      Height = 1,
      Dpi = 300,
      PixelData = new byte[1]
    };

    var header = CalsHeaderParser.Format(file);
    var fields = CalsHeaderParser.ParseAll(header);

    Assert.That(fields.ContainsKey("rdensty"), Is.True);
    Assert.That(fields["rdensty"], Is.EqualTo("0300"));
  }

  [Test]
  [Category("Unit")]
  public void Format_ProducesTheSpecifiedHeaderSize() {
    var file = new CalsFile {
      Width = 8,
      Height = 1,
      PixelData = new byte[1]
    };

    var header = CalsHeaderParser.Format(file);

    // Sixteen records of 128 bytes, which is what the specification fixes it at.
    Assert.That(header.Length, Is.EqualTo(2048));
  }
}
