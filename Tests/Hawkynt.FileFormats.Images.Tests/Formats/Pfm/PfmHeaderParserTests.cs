using System;
using System.IO;
using System.Text;
using FileFormat.Pfm;

namespace FileFormat.Pfm.Tests;

[TestFixture]
public sealed class PfmHeaderParserTests {

  [Test]
  [Category("Unit")]
  public void Parse_RgbHeader_DetectsRgbColorMode() {
    var header = Encoding.ASCII.GetBytes("PF\n4 3\n-1.0\n");
    var data = new byte[header.Length + 4 * 3 * 3 * 4];
    Array.Copy(header, data, header.Length);

    var result = PfmHeaderParser.Parse(data);

    Assert.That(result.ColorMode, Is.EqualTo(PfmColorMode.Rgb));
    Assert.That(result.Width, Is.EqualTo(4));
    Assert.That(result.Height, Is.EqualTo(3));
  }

  [Test]
  [Category("Unit")]
  public void Parse_GrayscaleHeader_DetectsGrayscaleColorMode() {
    var header = Encoding.ASCII.GetBytes("Pf\n2 5\n-1.0\n");
    var data = new byte[header.Length + 2 * 5 * 4];
    Array.Copy(header, data, header.Length);

    var result = PfmHeaderParser.Parse(data);

    Assert.That(result.ColorMode, Is.EqualTo(PfmColorMode.Grayscale));
    Assert.That(result.Width, Is.EqualTo(2));
    Assert.That(result.Height, Is.EqualTo(5));
  }

  [Test]
  [Category("Unit")]
  public void Parse_NegativeScale_IsLittleEndian() {
    var header = Encoding.ASCII.GetBytes("PF\n1 1\n-2.5\n");
    var data = new byte[header.Length + 1 * 1 * 3 * 4];
    Array.Copy(header, data, header.Length);

    var result = PfmHeaderParser.Parse(data);

    Assert.That(result.IsLittleEndian, Is.True);
    Assert.That(result.Scale, Is.EqualTo(2.5f));
  }

  [Test]
  [Category("Unit")]
  public void Parse_PositiveScale_IsBigEndian() {
    var header = Encoding.ASCII.GetBytes("PF\n1 1\n3.0\n");
    var data = new byte[header.Length + 1 * 1 * 3 * 4];
    Array.Copy(header, data, header.Length);

    var result = PfmHeaderParser.Parse(data);

    Assert.That(result.IsLittleEndian, Is.False);
    Assert.That(result.Scale, Is.EqualTo(3.0f));
  }

  [Test]
  [Category("Unit")]
  public void Parse_DataOffset_PointsAfterHeader() {
    var header = Encoding.ASCII.GetBytes("PF\n10 20\n-1.0\n");
    var data = new byte[header.Length + 10 * 20 * 3 * 4];
    Array.Copy(header, data, header.Length);

    var result = PfmHeaderParser.Parse(data);

    Assert.That(result.DataOffset, Is.EqualTo(header.Length));
  }

  [Test]
  [Category("Unit")]
  public void Parse_ZeroScale_ThrowsInvalidDataException() {
    var header = Encoding.ASCII.GetBytes("PF\n1 1\n0.0\n");
    var data = new byte[header.Length + 1 * 1 * 3 * 4];
    Array.Copy(header, data, header.Length);

    Assert.Throws<InvalidDataException>(() => PfmHeaderParser.Parse(data));
  }

  [Test]
  [Category("Unit")]
  public void Parse_InvalidDimensions_ThrowsInvalidDataException() {
    var header = Encoding.ASCII.GetBytes("PF\nabc def\n-1.0\n");
    var data = new byte[header.Length + 100];
    Array.Copy(header, data, header.Length);

    Assert.Throws<InvalidDataException>(() => PfmHeaderParser.Parse(data));
  }
}
