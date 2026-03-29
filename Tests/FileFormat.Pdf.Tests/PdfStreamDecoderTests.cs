using System;
using System.Collections.Generic;
using System.Text;
using FileFormat.Pdf;

namespace FileFormat.Pdf.Tests;

[TestFixture]
public sealed class PdfStreamDecoderTests {

  [Test]
  [Category("Unit")]
  public void DecodeFlateDecode_EmptyData_ReturnsInput() {
    var result = PdfStreamDecoder.DecodeFlateDecode([], null);
    Assert.That(result, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void DecodeFlateDecode_SingleByte_ReturnsInput() {
    var data = new byte[] { 0x78 };
    var result = PdfStreamDecoder.DecodeFlateDecode(data, null);
    Assert.That(result, Has.Length.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void DecodeAscii85_EmptyData_ReturnsEmpty() {
    var result = PdfStreamDecoder.DecodeAscii85([]);
    Assert.That(result, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void DecodeAscii85_ZeroGroup_ReturnsFourZeros() {
    var data = Encoding.ASCII.GetBytes("z~>");
    var result = PdfStreamDecoder.DecodeAscii85(data);
    Assert.That(result, Is.EqualTo(new byte[] { 0, 0, 0, 0 }));
  }

  [Test]
  [Category("Unit")]
  public void DecodeAscii85_WithPrefix_Decodes() {
    var data = Encoding.ASCII.GetBytes("<~z~>");
    var result = PdfStreamDecoder.DecodeAscii85(data);
    Assert.That(result, Is.EqualTo(new byte[] { 0, 0, 0, 0 }));
  }

  [Test]
  [Category("Unit")]
  public void DecodeAsciiHex_EmptyData_ReturnsEmpty() {
    var result = PdfStreamDecoder.DecodeAsciiHex([]);
    Assert.That(result, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void DecodeAsciiHex_ValidHex_DecodesCorrectly() {
    var data = Encoding.ASCII.GetBytes("48656C6C6F>");
    var result = PdfStreamDecoder.DecodeAsciiHex(data);
    Assert.That(Encoding.ASCII.GetString(result), Is.EqualTo("Hello"));
  }

  [Test]
  [Category("Unit")]
  public void DecodeAsciiHex_OddNibble_PadsWithZero() {
    var data = Encoding.ASCII.GetBytes("A>");
    var result = PdfStreamDecoder.DecodeAsciiHex(data);
    Assert.That(result, Is.EqualTo(new byte[] { 0xA0 }));
  }

  [Test]
  [Category("Unit")]
  public void DecodeRunLength_EmptyData_ReturnsEmpty() {
    var result = PdfStreamDecoder.DecodeRunLength([]);
    Assert.That(result, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void DecodeRunLength_EodMarker_StopsDecoding() {
    var data = new byte[] { 128 };
    var result = PdfStreamDecoder.DecodeRunLength(data);
    Assert.That(result, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void DecodeRunLength_LiteralRun_CopiesBytes() {
    var data = new byte[] { 2, 0xAA, 0xBB, 0xCC, 128 };
    var result = PdfStreamDecoder.DecodeRunLength(data);
    Assert.That(result, Is.EqualTo(new byte[] { 0xAA, 0xBB, 0xCC }));
  }

  [Test]
  [Category("Unit")]
  public void DecodeRunLength_RepeatRun_RepeatsBytes() {
    var data = new byte[] { 253, 0x42, 128 };
    var result = PdfStreamDecoder.DecodeRunLength(data);
    Assert.That(result, Is.EqualTo(new byte[] { 0x42, 0x42, 0x42, 0x42 }));
  }

  [Test]
  [Category("Unit")]
  public void DecodeLzw_EmptyData_ReturnsEmpty() {
    var result = PdfStreamDecoder.DecodeLzw([], null);
    Assert.That(result, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void Decode_NoFilter_ReturnsRawData() {
    var raw = new byte[] { 1, 2, 3, 4, 5 };
    var dict = new Dictionary<string, object?>();
    var result = PdfStreamDecoder.Decode(raw, dict);
    Assert.That(result, Is.EqualTo(raw));
  }

  [Test]
  [Category("Unit")]
  public void Decode_EmptyRawData_ReturnsEmpty() {
    var dict = new Dictionary<string, object?> {
      ["Filter"] = "FlateDecode",
    };
    var result = PdfStreamDecoder.Decode([], dict);
    Assert.That(result, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void DecodeAsciiHex_WithWhitespace_IgnoresWhitespace() {
    var data = Encoding.ASCII.GetBytes("48 65 6C 6C 6F>");
    var result = PdfStreamDecoder.DecodeAsciiHex(data);
    Assert.That(Encoding.ASCII.GetString(result), Is.EqualTo("Hello"));
  }

  [Test]
  [Category("Unit")]
  public void DecodeAscii85_MultipleZGroups_DecodesAll() {
    var data = Encoding.ASCII.GetBytes("zz~>");
    var result = PdfStreamDecoder.DecodeAscii85(data);
    Assert.That(result, Is.EqualTo(new byte[8]));
  }

  [Test]
  [Category("Unit")]
  public void DecodeRunLength_MixedLiteralAndRepeat() {
    var data = new byte[] { 0, 0xAA, 254, 0xBB, 128 };
    var result = PdfStreamDecoder.DecodeRunLength(data);
    Assert.That(result, Is.EqualTo(new byte[] { 0xAA, 0xBB, 0xBB, 0xBB }));
  }
}
