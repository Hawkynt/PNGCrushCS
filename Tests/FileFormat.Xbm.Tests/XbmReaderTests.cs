using System;
using System.IO;
using System.Text;
using FileFormat.Xbm;

namespace FileFormat.Xbm.Tests;

[TestFixture]
public sealed class XbmReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => XbmReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => XbmReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".xbm"));
    Assert.Throws<FileNotFoundException>(() => XbmReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => XbmReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[10];
    Assert.Throws<InvalidDataException>(() => XbmReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidFormat_ThrowsInvalidDataException() {
    var noDefine = Encoding.ASCII.GetBytes("This is not an XBM file at all, just random text padding.");
    Assert.Throws<InvalidDataException>(() => XbmReader.FromBytes(noDefine));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidXbm_ParsesCorrectly() {
    var xbm = _BuildMinimalXbm(8, 2, "test");
    var result = XbmReader.FromBytes(xbm);

    Assert.That(result.Width, Is.EqualTo(8));
    Assert.That(result.Height, Is.EqualTo(2));
    Assert.That(result.Name, Is.EqualTo("test"));
    Assert.That(result.PixelData, Has.Length.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidXbm_ParsesCorrectly() {
    var xbm = _BuildMinimalXbm(8, 1, "icon");
    using var ms = new MemoryStream(xbm);
    var result = XbmReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(8));
    Assert.That(result.Height, Is.EqualTo(1));
    Assert.That(result.Name, Is.EqualTo("icon"));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_WithHotspot_ParsesHotspotCoordinates() {
    var text =
      "#define cursor_width 8\n" +
      "#define cursor_height 8\n" +
      "#define cursor_x_hot 3\n" +
      "#define cursor_y_hot 5\n" +
      "static unsigned char cursor_bits[] = {\n" +
      "   0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF\n" +
      "};\n";
    var bytes = Encoding.ASCII.GetBytes(text);
    var result = XbmReader.FromBytes(bytes);

    Assert.That(result.HotspotX, Is.EqualTo(3));
    Assert.That(result.HotspotY, Is.EqualTo(5));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_WithoutHotspot_HotspotIsNull() {
    var xbm = _BuildMinimalXbm(8, 1, "img");
    var result = XbmReader.FromBytes(xbm);

    Assert.That(result.HotspotX, Is.Null);
    Assert.That(result.HotspotY, Is.Null);
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_LowercaseHex_ParsesCorrectly() {
    var text =
      "#define img_width 8\n" +
      "#define img_height 1\n" +
      "static unsigned char img_bits[] = {\n" +
      "   0xff\n" +
      "};\n";
    var bytes = Encoding.ASCII.GetBytes(text);
    var result = XbmReader.FromBytes(bytes);

    Assert.That(result.PixelData[0], Is.EqualTo(0xFF));
  }

  private static byte[] _BuildMinimalXbm(int width, int height, string name) {
    var bytesPerRow = (width + 7) / 8;
    var totalBytes = bytesPerRow * height;
    var sb = new StringBuilder();
    sb.AppendLine($"#define {name}_width {width}");
    sb.AppendLine($"#define {name}_height {height}");
    sb.AppendLine($"static unsigned char {name}_bits[] = {{");
    for (var i = 0; i < totalBytes; ++i) {
      if (i > 0)
        sb.Append(", ");
      sb.Append($"0x{(byte)(i * 17 % 256):X2}");
    }
    sb.AppendLine("\n};");
    return Encoding.ASCII.GetBytes(sb.ToString());
  }
}
