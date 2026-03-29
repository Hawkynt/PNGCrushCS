using System;
using System.Text;
using FileFormat.Fits;

namespace FileFormat.Fits.Tests;

[TestFixture]
public sealed class FitsHeaderParserTests {

  [Test]
  [Category("Unit")]
  public void Parse_SimpleKeyword_ParsedCorrectly() {
    var data = _BuildHeaderBlock("SIMPLE  =                    T / conforming");
    var (keywords, _) = FitsHeaderParser.Parse(data);

    Assert.That(keywords, Has.Count.GreaterThanOrEqualTo(1));
    Assert.That(keywords[0].Name, Is.EqualTo("SIMPLE"));
    Assert.That(keywords[0].Value, Is.EqualTo("T"));
  }

  [Test]
  [Category("Unit")]
  public void Parse_BitpixKeyword_ParsedCorrectly() {
    var data = _BuildHeaderBlock(
      "SIMPLE  =                    T / conforming",
      "BITPIX  =                   16 / bits per pixel"
    );
    var (keywords, _) = FitsHeaderParser.Parse(data);

    var bitpixKw = keywords.Find(k => k.Name == "BITPIX");
    Assert.That(bitpixKw, Is.Not.Null);
    Assert.That(bitpixKw!.Value, Is.EqualTo("16"));
  }

  [Test]
  [Category("Unit")]
  public void Parse_NaxisKeywords_ParsedCorrectly() {
    var data = _BuildHeaderBlock(
      "SIMPLE  =                    T",
      "BITPIX  =                    8",
      "NAXIS   =                    2",
      "NAXIS1  =                  100",
      "NAXIS2  =                  200"
    );
    var (keywords, _) = FitsHeaderParser.Parse(data);

    Assert.That(FitsHeaderParser.GetIntValue(keywords, "NAXIS"), Is.EqualTo(2));
    Assert.That(FitsHeaderParser.GetIntValue(keywords, "NAXIS1"), Is.EqualTo(100));
    Assert.That(FitsHeaderParser.GetIntValue(keywords, "NAXIS2"), Is.EqualTo(200));
  }

  [Test]
  [Category("Unit")]
  public void Parse_StringValue_ParsedCorrectly() {
    var data = _BuildHeaderBlock(
      "SIMPLE  =                    T",
      "OBJECT  = 'M31 Galaxy        ' / target name"
    );
    var (keywords, _) = FitsHeaderParser.Parse(data);

    var objectKw = keywords.Find(k => k.Name == "OBJECT");
    Assert.That(objectKw, Is.Not.Null);
    Assert.That(objectKw!.Value, Is.EqualTo("M31 Galaxy"));
  }

  [Test]
  [Category("Unit")]
  public void Parse_Comment_ParsedCorrectly() {
    var data = _BuildHeaderBlock(
      "SIMPLE  =                    T / conforms to FITS standard"
    );
    var (keywords, _) = FitsHeaderParser.Parse(data);

    Assert.That(keywords[0].Comment, Is.EqualTo("conforms to FITS standard"));
  }

  [Test]
  [Category("Unit")]
  public void Parse_NegativeBitpix_ParsedCorrectly() {
    var data = _BuildHeaderBlock(
      "SIMPLE  =                    T",
      "BITPIX  =                  -32 / IEEE float"
    );
    var (keywords, _) = FitsHeaderParser.Parse(data);

    var bitpix = FitsHeaderParser.GetBitpix(keywords);
    Assert.That(bitpix, Is.EqualTo(FitsBitpix.Float32));
  }

  [Test]
  [Category("Unit")]
  public void GetIntValue_MissingKeyword_ThrowsInvalidOperationException() {
    var data = _BuildHeaderBlock("SIMPLE  =                    T");
    var (keywords, _) = FitsHeaderParser.Parse(data);

    Assert.Throws<InvalidOperationException>(() => FitsHeaderParser.GetIntValue(keywords, "NAXIS"));
  }

  private static byte[] _BuildHeaderBlock(params string[] cards) {
    var data = new byte[2880];
    for (var i = 0; i < data.Length; ++i)
      data[i] = (byte)' ';

    var offset = 0;
    foreach (var card in cards) {
      var paddedCard = card.PadRight(80);
      var cardBytes = Encoding.ASCII.GetBytes(paddedCard[..80]);
      Array.Copy(cardBytes, 0, data, offset, 80);
      offset += 80;
    }

    // Write END card
    var endBytes = Encoding.ASCII.GetBytes("END".PadRight(80));
    Array.Copy(endBytes, 0, data, offset, 80);

    return data;
  }
}
