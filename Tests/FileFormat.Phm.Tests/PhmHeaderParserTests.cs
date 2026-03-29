using System;
using System.IO;
using System.Text;
using FileFormat.Phm;

namespace FileFormat.Phm.Tests;

[TestFixture]
public sealed class PhmHeaderParserTests {

  [Test]
  [Category("Unit")]
  public void Parse_RgbMagic_Detected() {
    var data = Encoding.ASCII.GetBytes("PH\n2 2\n-1.0\n");
    var full = new byte[data.Length + 24];
    Array.Copy(data, full, data.Length);
    var result = PhmHeaderParser.Parse(full);
    Assert.That(result.ColorMode, Is.EqualTo(PhmColorMode.Rgb));
  }

  [Test]
  [Category("Unit")]
  public void Parse_GrayscaleMagic_Detected() {
    var data = Encoding.ASCII.GetBytes("Ph\n2 2\n-1.0\n");
    var full = new byte[data.Length + 8];
    Array.Copy(data, full, data.Length);
    var result = PhmHeaderParser.Parse(full);
    Assert.That(result.ColorMode, Is.EqualTo(PhmColorMode.Grayscale));
  }

  [Test]
  [Category("Unit")]
  public void Parse_Dimensions_Correct() {
    var data = Encoding.ASCII.GetBytes("PH\n10 20\n-1.0\n");
    var full = new byte[data.Length + 1200];
    Array.Copy(data, full, data.Length);
    var result = PhmHeaderParser.Parse(full);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(10));
      Assert.That(result.Height, Is.EqualTo(20));
    });
  }

  [Test]
  [Category("Unit")]
  public void Parse_NegativeScale_IsLittleEndian() {
    var data = Encoding.ASCII.GetBytes("Ph\n1 1\n-2.5\n");
    var full = new byte[data.Length + 2];
    Array.Copy(data, full, data.Length);
    var result = PhmHeaderParser.Parse(full);

    Assert.Multiple(() => {
      Assert.That(result.IsLittleEndian, Is.True);
      Assert.That(result.Scale, Is.EqualTo(2.5f));
    });
  }

  [Test]
  [Category("Unit")]
  public void Parse_PositiveScale_IsBigEndian() {
    var data = Encoding.ASCII.GetBytes("Ph\n1 1\n3.0\n");
    var full = new byte[data.Length + 2];
    Array.Copy(data, full, data.Length);
    var result = PhmHeaderParser.Parse(full);

    Assert.Multiple(() => {
      Assert.That(result.IsLittleEndian, Is.False);
      Assert.That(result.Scale, Is.EqualTo(3.0f));
    });
  }

  [Test]
  [Category("Unit")]
  public void Parse_DataOffset_AfterThirdNewline() {
    var header = "Ph\n4 3\n-1.0\n";
    var data = Encoding.ASCII.GetBytes(header);
    var full = new byte[data.Length + 24];
    Array.Copy(data, full, data.Length);
    var result = PhmHeaderParser.Parse(full);
    Assert.That(result.DataOffset, Is.EqualTo(header.Length));
  }

  [Test]
  [Category("Unit")]
  public void Parse_InvalidMagic_Throws() {
    var data = Encoding.ASCII.GetBytes("PF\n1 1\n-1.0\n");
    var full = new byte[data.Length + 4];
    Array.Copy(data, full, data.Length);
    Assert.Throws<InvalidDataException>(() => PhmHeaderParser.Parse(full));
  }

  [Test]
  [Category("Unit")]
  public void Parse_ZeroScale_Throws() {
    var data = Encoding.ASCII.GetBytes("Ph\n1 1\n0.0\n");
    var full = new byte[data.Length + 2];
    Array.Copy(data, full, data.Length);
    Assert.Throws<InvalidDataException>(() => PhmHeaderParser.Parse(full));
  }
}
