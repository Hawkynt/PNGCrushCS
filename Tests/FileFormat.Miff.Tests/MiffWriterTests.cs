using System;
using System.Text;
using FileFormat.Miff;

namespace FileFormat.Miff.Tests;

[TestFixture]
public sealed class MiffWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => MiffWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_StartsWithMagic() {
    var file = new MiffFile {
      Width = 2,
      Height = 2,
      Depth = 8,
      PixelData = new byte[2 * 2 * 3]
    };

    var bytes = MiffWriter.ToBytes(file);
    var header = Encoding.ASCII.GetString(bytes, 0, 14);

    Assert.That(header, Is.EqualTo("id=ImageMagick"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_HasTerminator() {
    var file = new MiffFile {
      Width = 1,
      Height = 1,
      Depth = 8,
      PixelData = new byte[3]
    };

    var bytes = MiffWriter.ToBytes(file);
    var text = Encoding.ASCII.GetString(bytes);

    Assert.That(text, Does.Contain(":\n"));

    // Find 0x1A after the terminator
    var terminatorIdx = text.IndexOf(":\n", StringComparison.Ordinal);
    var byteAfterTerminator = bytes[terminatorIdx + 2];
    Assert.That(byteAfterTerminator, Is.EqualTo(0x1A));
  }
}
