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
    Assert.That(fields["rpelcnt"], Is.EqualTo("1024,768"));
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
    Assert.That(fields["rdensty"], Is.EqualTo("300"));
  }

  [Test]
  [Category("Unit")]
  public void Format_Produces768Bytes() {
    var file = new CalsFile {
      Width = 8,
      Height = 1,
      PixelData = new byte[1]
    };

    var header = CalsHeaderParser.Format(file);

    Assert.That(header.Length, Is.EqualTo(768));
  }
}
