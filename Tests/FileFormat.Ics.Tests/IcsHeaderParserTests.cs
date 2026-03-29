using System;
using System.IO;
using System.Text;
using FileFormat.Ics;

namespace FileFormat.Ics.Tests;

[TestFixture]
public sealed class IcsHeaderParserTests {

  [Test]
  [Category("Unit")]
  public void Parse_ExtractsDimensions() {
    var header = "ics_version\t2.0\nlayout\torder\tbits\tx\ty\nlayout\tsizes\t8\t256\t128\nlayout\tsignificant_bits\t8\nrepresentation\tformat\tinteger\nrepresentation\tcompression\tuncompressed\nend\n";
    var data = Encoding.ASCII.GetBytes(header);

    var result = IcsHeaderParser.Parse(data);

    Assert.That(result.Width, Is.EqualTo(256));
    Assert.That(result.Height, Is.EqualTo(128));
  }

  [Test]
  [Category("Unit")]
  public void Parse_ExtractsChannelCount() {
    var header = "ics_version\t2.0\nlayout\torder\tbits\tx\ty\tch\nlayout\tsizes\t8\t64\t32\t3\nlayout\tsignificant_bits\t8\nrepresentation\tformat\tinteger\nrepresentation\tcompression\tuncompressed\nend\n";
    var data = Encoding.ASCII.GetBytes(header);

    var result = IcsHeaderParser.Parse(data);

    Assert.That(result.Channels, Is.EqualTo(3));
  }

  [Test]
  [Category("Unit")]
  public void Parse_ExtractsCompression_Gzip() {
    var header = "ics_version\t2.0\nlayout\torder\tbits\tx\ty\nlayout\tsizes\t8\t10\t10\nlayout\tsignificant_bits\t8\nrepresentation\tformat\tinteger\nrepresentation\tcompression\tgzip\nend\n";
    var data = Encoding.ASCII.GetBytes(header);

    var result = IcsHeaderParser.Parse(data);

    Assert.That(result.Compression, Is.EqualTo(IcsCompression.Gzip));
  }

  [Test]
  [Category("Unit")]
  public void Parse_ExtractsCompression_Uncompressed() {
    var header = "ics_version\t2.0\nlayout\torder\tbits\tx\ty\nlayout\tsizes\t8\t10\t10\nlayout\tsignificant_bits\t8\nrepresentation\tformat\tinteger\nrepresentation\tcompression\tuncompressed\nend\n";
    var data = Encoding.ASCII.GetBytes(header);

    Assert.That(IcsHeaderParser.Parse(data).Compression, Is.EqualTo(IcsCompression.Uncompressed));
  }

  [Test]
  [Category("Unit")]
  public void Parse_ExtractsBitsPerSample() {
    var header = "ics_version\t2.0\nlayout\torder\tbits\tx\ty\nlayout\tsizes\t16\t10\t10\nlayout\tsignificant_bits\t12\nrepresentation\tformat\tinteger\nrepresentation\tcompression\tuncompressed\nend\n";
    var data = Encoding.ASCII.GetBytes(header);

    var result = IcsHeaderParser.Parse(data);

    Assert.That(result.BitsPerSample, Is.EqualTo(12));
  }

  [Test]
  [Category("Unit")]
  public void Parse_ExtractsVersion() {
    var header = "ics_version\t2.0\nlayout\torder\tbits\tx\ty\nlayout\tsizes\t8\t5\t5\nrepresentation\tformat\tinteger\nrepresentation\tcompression\tuncompressed\nend\n";
    var data = Encoding.ASCII.GetBytes(header);

    var result = IcsHeaderParser.Parse(data);

    Assert.That(result.Version, Is.EqualTo("2.0"));
  }

  [Test]
  [Category("Unit")]
  public void Parse_SetsCorrectDataOffset() {
    var header = "ics_version\t2.0\nlayout\torder\tbits\tx\ty\nlayout\tsizes\t8\t2\t2\nrepresentation\tformat\tinteger\nrepresentation\tcompression\tuncompressed\nend\n";
    var headerBytes = Encoding.ASCII.GetBytes(header);
    var pixelData = new byte[] { 0xAA, 0xBB, 0xCC, 0xDD };
    var data = new byte[headerBytes.Length + pixelData.Length];
    Array.Copy(headerBytes, data, headerBytes.Length);
    Array.Copy(pixelData, 0, data, headerBytes.Length, pixelData.Length);

    var result = IcsHeaderParser.Parse(data);

    Assert.That(result.DataOffset, Is.EqualTo(headerBytes.Length));
    Assert.That(data[result.DataOffset], Is.EqualTo(0xAA));
  }

  [Test]
  [Category("Unit")]
  public void Parse_MissingEnd_ThrowsInvalidDataException() {
    var header = "ics_version\t2.0\nlayout\torder\tbits\tx\ty\nlayout\tsizes\t8\t2\t2\n";
    var data = Encoding.ASCII.GetBytes(header);

    Assert.Throws<InvalidDataException>(() => IcsHeaderParser.Parse(data));
  }

  [Test]
  [Category("Unit")]
  public void Parse_MissingLayoutOrder_ThrowsInvalidDataException() {
    var header = "ics_version\t2.0\nlayout\tsizes\t8\t2\t2\nend\n";
    var data = Encoding.ASCII.GetBytes(header);

    Assert.Throws<InvalidDataException>(() => IcsHeaderParser.Parse(data));
  }

  [Test]
  [Category("Unit")]
  public void Parse_MissingLayoutSizes_ThrowsInvalidDataException() {
    var header = "ics_version\t2.0\nlayout\torder\tbits\tx\ty\nend\n";
    var data = Encoding.ASCII.GetBytes(header);

    Assert.Throws<InvalidDataException>(() => IcsHeaderParser.Parse(data));
  }

  [Test]
  [Category("Unit")]
  public void Parse_UnsupportedVersion_ThrowsInvalidDataException() {
    var header = "ics_version\t3.0\nlayout\torder\tbits\tx\ty\nlayout\tsizes\t8\t2\t2\nend\n";
    var data = Encoding.ASCII.GetBytes(header);

    Assert.Throws<InvalidDataException>(() => IcsHeaderParser.Parse(data));
  }

  [Test]
  [Category("Unit")]
  public void Parse_ExtractsRepresentationFormat() {
    var header = "ics_version\t2.0\nlayout\torder\tbits\tx\ty\nlayout\tsizes\t8\t5\t5\nrepresentation\tformat\treal\nrepresentation\tcompression\tuncompressed\nend\n";
    var data = Encoding.ASCII.GetBytes(header);

    var result = IcsHeaderParser.Parse(data);

    Assert.That(result.RepresentationFormat, Is.EqualTo("real"));
  }

  [Test]
  [Category("Unit")]
  public void Parse_ExtractsDimensionOrder() {
    var header = "ics_version\t2.0\nlayout\torder\tbits\tx\ty\tch\nlayout\tsizes\t8\t10\t20\t3\nrepresentation\tformat\tinteger\nrepresentation\tcompression\tuncompressed\nend\n";
    var data = Encoding.ASCII.GetBytes(header);

    var result = IcsHeaderParser.Parse(data);

    Assert.That(result.DimensionOrder, Is.EqualTo(new[] { "bits", "x", "y", "ch" }));
  }
}
