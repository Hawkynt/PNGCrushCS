using System;
using System.Text;
using FileFormat.Interfile;

namespace FileFormat.Interfile.Tests;

[TestFixture]
public sealed class InterfileWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => InterfileWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_StartsWithInterfileMagic() {
    var file = new InterfileFile {
      Width = 2,
      Height = 2,
      BytesPerPixel = 1,
      PixelData = new byte[4]
    };

    var bytes = InterfileWriter.ToBytes(file);
    var text = Encoding.ASCII.GetString(bytes);

    Assert.That(text, Does.StartWith("!INTERFILE :=\n"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ContainsEndMarker() {
    var file = new InterfileFile {
      Width = 1,
      Height = 1,
      BytesPerPixel = 1,
      PixelData = new byte[1]
    };

    var bytes = InterfileWriter.ToBytes(file);
    var text = Encoding.ASCII.GetString(bytes);

    Assert.That(text, Does.Contain("!END OF INTERFILE :=\n"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ContainsMatrixSizeWidth() {
    var file = new InterfileFile {
      Width = 128,
      Height = 64,
      BytesPerPixel = 1,
      PixelData = new byte[128 * 64]
    };

    var bytes = InterfileWriter.ToBytes(file);
    var text = Encoding.ASCII.GetString(bytes);

    Assert.That(text, Does.Contain("!matrix size [1] := 128\n"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ContainsMatrixSizeHeight() {
    var file = new InterfileFile {
      Width = 128,
      Height = 64,
      BytesPerPixel = 1,
      PixelData = new byte[128 * 64]
    };

    var bytes = InterfileWriter.ToBytes(file);
    var text = Encoding.ASCII.GetString(bytes);

    Assert.That(text, Does.Contain("!matrix size [2] := 64\n"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ContainsNumberFormat() {
    var file = new InterfileFile {
      Width = 2,
      Height = 2,
      BytesPerPixel = 1,
      NumberFormat = "signed integer",
      PixelData = new byte[4]
    };

    var bytes = InterfileWriter.ToBytes(file);
    var text = Encoding.ASCII.GetString(bytes);

    Assert.That(text, Does.Contain("!number format := signed integer\n"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ContainsBytesPerPixel() {
    var file = new InterfileFile {
      Width = 2,
      Height = 2,
      BytesPerPixel = 2,
      PixelData = new byte[8]
    };

    var bytes = InterfileWriter.ToBytes(file);
    var text = Encoding.ASCII.GetString(bytes);

    Assert.That(text, Does.Contain("!number of bytes per pixel := 2\n"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_PixelDataAfterEndMarker() {
    var pixelData = new byte[] { 0xAA, 0xBB, 0xCC };
    var file = new InterfileFile {
      Width = 3,
      Height = 1,
      BytesPerPixel = 1,
      PixelData = pixelData
    };

    var bytes = InterfileWriter.ToBytes(file);
    var text = Encoding.ASCII.GetString(bytes);
    var endIndex = text.IndexOf("!END OF INTERFILE :=\n", StringComparison.Ordinal);
    var dataStart = endIndex + "!END OF INTERFILE :=\n".Length;

    Assert.That(bytes[dataStart], Is.EqualTo(0xAA));
    Assert.That(bytes[dataStart + 1], Is.EqualTo(0xBB));
    Assert.That(bytes[dataStart + 2], Is.EqualTo(0xCC));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_FileSize_IncludesHeaderAndPixels() {
    var pixelData = new byte[6];
    var file = new InterfileFile {
      Width = 3,
      Height = 2,
      BytesPerPixel = 1,
      PixelData = pixelData
    };

    var bytes = InterfileWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.GreaterThan(pixelData.Length));
  }
}
