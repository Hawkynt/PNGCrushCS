using System;
using System.IO;
using System.Text;
using FileFormat.Hdr;

namespace FileFormat.Hdr.Tests;

[TestFixture]
public sealed class HdrHeaderParserTests {

  [Test]
  [Category("Unit")]
  public void Parse_ValidHeader_ExtractsWidthAndHeight() {
    var header = "#?RADIANCE\nFORMAT=32-bit_rle_rgbe\n\n-Y 3 +X 4\n";
    var data = Encoding.ASCII.GetBytes(header);

    var (width, height, _, _) = HdrHeaderParser.Parse(data);

    Assert.That(width, Is.EqualTo(4));
    Assert.That(height, Is.EqualTo(3));
  }

  [Test]
  [Category("Unit")]
  public void Parse_WithExposure_ExtractsExposure() {
    var header = "#?RADIANCE\nFORMAT=32-bit_rle_rgbe\nEXPOSURE=2.5\n\n-Y 2 +X 2\n";
    var data = Encoding.ASCII.GetBytes(header);

    var (_, _, exposure, _) = HdrHeaderParser.Parse(data);

    Assert.That(exposure, Is.EqualTo(2.5f).Within(0.01f));
  }

  [Test]
  [Category("Unit")]
  public void Parse_NoExposure_DefaultsToOne() {
    var header = "#?RADIANCE\nFORMAT=32-bit_rle_rgbe\n\n-Y 2 +X 2\n";
    var data = Encoding.ASCII.GetBytes(header);

    var (_, _, exposure, _) = HdrHeaderParser.Parse(data);

    Assert.That(exposure, Is.EqualTo(1.0f));
  }

  [Test]
  [Category("Unit")]
  public void Parse_DataOffset_PointsPastHeader() {
    var header = "#?RADIANCE\nFORMAT=32-bit_rle_rgbe\n\n-Y 2 +X 2\n";
    var data = Encoding.ASCII.GetBytes(header);

    var (_, _, _, dataOffset) = HdrHeaderParser.Parse(data);

    Assert.That(dataOffset, Is.EqualTo(header.Length));
  }

  [Test]
  [Category("Unit")]
  public void Parse_InvalidMagic_ThrowsInvalidDataException() {
    var header = "XYZINVALID\n\n-Y 2 +X 2\n";
    var data = Encoding.ASCII.GetBytes(header);

    Assert.Throws<InvalidDataException>(() => HdrHeaderParser.Parse(data));
  }

  [Test]
  [Category("Unit")]
  public void Parse_InvalidResolution_ThrowsInvalidDataException() {
    var header = "#?RADIANCE\n\n+X 2 -Y 2\n";
    var data = Encoding.ASCII.GetBytes(header);

    Assert.Throws<InvalidDataException>(() => HdrHeaderParser.Parse(data));
  }

  [Test]
  [Category("Unit")]
  public void Parse_MultipleExposures_Multiplied() {
    var header = "#?RADIANCE\nEXPOSURE=2.0\nEXPOSURE=3.0\n\n-Y 2 +X 2\n";
    var data = Encoding.ASCII.GetBytes(header);

    var (_, _, exposure, _) = HdrHeaderParser.Parse(data);

    Assert.That(exposure, Is.EqualTo(6.0f).Within(0.01f));
  }

  [Test]
  [Category("Unit")]
  public void Parse_ShortMagic_Accepted() {
    var header = "#?CUSTOM\nFORMAT=32-bit_rle_rgbe\n\n-Y 1 +X 1\n";
    var data = Encoding.ASCII.GetBytes(header);

    var (width, height, _, _) = HdrHeaderParser.Parse(data);

    Assert.That(width, Is.EqualTo(1));
    Assert.That(height, Is.EqualTo(1));
  }
}
