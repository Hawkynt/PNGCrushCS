using System;
using System.Text;
using FileFormat.Mtv;

namespace FileFormat.Mtv.Tests;

[TestFixture]
public sealed class MtvWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_StartsWithAsciiHeader() {
    var file = new MtvFile {
      Width = 4,
      Height = 2,
      PixelData = new byte[4 * 2 * 3]
    };

    var bytes = MtvWriter.ToBytes(file);
    var headerEnd = Array.IndexOf(bytes, (byte)'\n');

    Assert.That(headerEnd, Is.GreaterThan(0));
    var headerText = Encoding.ASCII.GetString(bytes, 0, headerEnd);
    Assert.That(headerText, Does.Match(@"^\d+ \d+$"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_HeaderContainsDimensions() {
    var file = new MtvFile {
      Width = 320,
      Height = 240,
      PixelData = new byte[320 * 240 * 3]
    };

    var bytes = MtvWriter.ToBytes(file);
    var headerEnd = Array.IndexOf(bytes, (byte)'\n');
    var headerText = Encoding.ASCII.GetString(bytes, 0, headerEnd);

    Assert.That(headerText, Is.EqualTo("320 240"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_PixelDataAfterNewline() {
    var pixels = new byte[] { 0xAA, 0xBB, 0xCC };
    var file = new MtvFile {
      Width = 1,
      Height = 1,
      PixelData = pixels
    };

    var bytes = MtvWriter.ToBytes(file);
    var headerEnd = Array.IndexOf(bytes, (byte)'\n');
    var pixelStart = headerEnd + 1;

    Assert.That(bytes[pixelStart], Is.EqualTo(0xAA));
    Assert.That(bytes[pixelStart + 1], Is.EqualTo(0xBB));
    Assert.That(bytes[pixelStart + 2], Is.EqualTo(0xCC));
  }
}
