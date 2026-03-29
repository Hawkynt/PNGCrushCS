using System;
using System.Text;
using FileFormat.Interfile;

namespace FileFormat.Interfile.Tests;

[TestFixture]
public sealed class InterfileHeaderParserTests {

  [Test]
  [Category("Unit")]
  public void Parse_ExtractsDimensions() {
    var header = "!INTERFILE :=\n!matrix size [1] := 256\n!matrix size [2] := 128\n!number of bytes per pixel := 1\n!number format := unsigned integer\n!END OF INTERFILE :=\n";
    var data = Encoding.ASCII.GetBytes(header);

    var result = InterfileHeaderParser.Parse(data);

    Assert.That(result.Width, Is.EqualTo(256));
    Assert.That(result.Height, Is.EqualTo(128));
  }

  [Test]
  [Category("Unit")]
  public void Parse_ExtractsBytesPerPixel() {
    var header = "!INTERFILE :=\n!matrix size [1] := 64\n!matrix size [2] := 32\n!number of bytes per pixel := 2\n!number format := unsigned integer\n!END OF INTERFILE :=\n";
    var data = Encoding.ASCII.GetBytes(header);

    var result = InterfileHeaderParser.Parse(data);

    Assert.That(result.BytesPerPixel, Is.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void Parse_ExtractsNumberFormat() {
    var header = "!INTERFILE :=\n!matrix size [1] := 10\n!matrix size [2] := 10\n!number of bytes per pixel := 1\n!number format := signed integer\n!END OF INTERFILE :=\n";
    var data = Encoding.ASCII.GetBytes(header);

    var result = InterfileHeaderParser.Parse(data);

    Assert.That(result.NumberFormat, Is.EqualTo("signed integer"));
  }

  [Test]
  [Category("Unit")]
  public void Parse_SetsCorrectDataOffset() {
    var header = "!INTERFILE :=\n!matrix size [1] := 2\n!matrix size [2] := 2\n!number of bytes per pixel := 1\n!number format := unsigned integer\n!END OF INTERFILE :=\n";
    var headerBytes = Encoding.ASCII.GetBytes(header);
    var pixelData = new byte[] { 0xAA, 0xBB, 0xCC, 0xDD };
    var data = new byte[headerBytes.Length + pixelData.Length];
    Array.Copy(headerBytes, data, headerBytes.Length);
    Array.Copy(pixelData, 0, data, headerBytes.Length, pixelData.Length);

    var result = InterfileHeaderParser.Parse(data);

    Assert.That(result.DataOffset, Is.EqualTo(headerBytes.Length));
    Assert.That(data[result.DataOffset], Is.EqualTo(0xAA));
  }

  [Test]
  [Category("Unit")]
  public void Parse_DefaultBytesPerPixel_WhenMissing() {
    var header = "!INTERFILE :=\n!matrix size [1] := 4\n!matrix size [2] := 4\n!END OF INTERFILE :=\n";
    var data = Encoding.ASCII.GetBytes(header);

    var result = InterfileHeaderParser.Parse(data);

    Assert.That(result.BytesPerPixel, Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void Parse_DefaultNumberFormat_WhenMissing() {
    var header = "!INTERFILE :=\n!matrix size [1] := 4\n!matrix size [2] := 4\n!END OF INTERFILE :=\n";
    var data = Encoding.ASCII.GetBytes(header);

    var result = InterfileHeaderParser.Parse(data);

    Assert.That(result.NumberFormat, Is.EqualTo("unsigned integer"));
  }

  [Test]
  [Category("Unit")]
  public void Parse_IgnoresCommentLines() {
    var header = "!INTERFILE :=\n; this is a comment\n!matrix size [1] := 8\n!matrix size [2] := 8\n!number of bytes per pixel := 1\n!END OF INTERFILE :=\n";
    var data = Encoding.ASCII.GetBytes(header);

    var result = InterfileHeaderParser.Parse(data);

    Assert.That(result.Width, Is.EqualTo(8));
    Assert.That(result.Height, Is.EqualTo(8));
  }

  [Test]
  [Category("Unit")]
  public void Parse_HandlesMissingEndMarker() {
    var header = "!INTERFILE :=\n!matrix size [1] := 4\n!matrix size [2] := 4\n!number of bytes per pixel := 1\n";
    var data = Encoding.ASCII.GetBytes(header);

    var result = InterfileHeaderParser.Parse(data);

    Assert.That(result.Width, Is.EqualTo(4));
    Assert.That(result.DataOffset, Is.EqualTo(data.Length));
  }

  [Test]
  [Category("Unit")]
  public void Parse_HandlesKeysWithoutExclamation() {
    var header = "!INTERFILE :=\nmatrix size [1] := 16\nmatrix size [2] := 16\nnumber of bytes per pixel := 1\n!END OF INTERFILE :=\n";
    var data = Encoding.ASCII.GetBytes(header);

    var result = InterfileHeaderParser.Parse(data);

    Assert.That(result.Width, Is.EqualTo(16));
    Assert.That(result.Height, Is.EqualTo(16));
  }

  [Test]
  [Category("Unit")]
  public void Format_ProducesValidHeader() {
    var file = new InterfileFile {
      Width = 64,
      Height = 32,
      BytesPerPixel = 1,
      NumberFormat = "unsigned integer",
      PixelData = new byte[64 * 32]
    };

    var headerBytes = InterfileHeaderParser.Format(file);
    var text = Encoding.ASCII.GetString(headerBytes);

    Assert.That(text, Does.StartWith("!INTERFILE :=\n"));
    Assert.That(text, Does.Contain("!matrix size [1] := 64\n"));
    Assert.That(text, Does.Contain("!matrix size [2] := 32\n"));
    Assert.That(text, Does.Contain("!number of bytes per pixel := 1\n"));
    Assert.That(text, Does.Contain("!number format := unsigned integer\n"));
    Assert.That(text, Does.Contain("!END OF INTERFILE :=\n"));
  }

  [Test]
  [Category("Unit")]
  public void Format_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => InterfileHeaderParser.Format(null!));
  }
}
