using System;
using System.IO;
using System.Text;
using FileFormat.SunIcon;

namespace FileFormat.SunIcon.Tests;

[TestFixture]
public sealed class SunIconReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => SunIconReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => SunIconReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".icon"));
    Assert.Throws<FileNotFoundException>(() => SunIconReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => SunIconReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[5];
    Assert.Throws<InvalidDataException>(() => SunIconReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidMagic_ThrowsInvalidDataException() {
    var noMagic = Encoding.ASCII.GetBytes("This is not a Sun Icon file at all, has enough padding.");
    Assert.Throws<InvalidDataException>(() => SunIconReader.FromBytes(noMagic));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Valid16x16_ParsesDimensions() {
    var text = _BuildSunIcon(16, 16, 16);
    var result = SunIconReader.FromBytes(Encoding.ASCII.GetBytes(text));

    Assert.That(result.Width, Is.EqualTo(16));
    Assert.That(result.Height, Is.EqualTo(16));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Valid16x16_ParsesPixelDataLength() {
    var text = _BuildSunIcon(16, 16, 16);
    var result = SunIconReader.FromBytes(Encoding.ASCII.GetBytes(text));

    // 16 pixels wide = 2 bytes per row, 16 rows = 32 bytes
    Assert.That(result.PixelData.Length, Is.EqualTo(32));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Valid32Bit_ParsesCorrectly() {
    var text = _BuildSunIcon(32, 2, 32);
    var result = SunIconReader.FromBytes(Encoding.ASCII.GetBytes(text));

    Assert.That(result.Width, Is.EqualTo(32));
    Assert.That(result.Height, Is.EqualTo(2));
    // 32 pixels wide = 4 bytes per row, 2 rows = 8 bytes
    Assert.That(result.PixelData.Length, Is.EqualTo(8));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_PixelDataIsCorrect() {
    var text =
      "/* Format_version=1, Width=16, Height=1, Depth=1, Valid_bits_per_item=16\n" +
      " */\n" +
      "\t0xFFFF\n";
    var result = SunIconReader.FromBytes(Encoding.ASCII.GetBytes(text));

    Assert.That(result.PixelData[0], Is.EqualTo(0xFF));
    Assert.That(result.PixelData[1], Is.EqualTo(0xFF));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Valid_ParsesCorrectly() {
    var text = _BuildSunIcon(16, 16, 16);
    using var ms = new MemoryStream(Encoding.ASCII.GetBytes(text));
    var result = SunIconReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(16));
    Assert.That(result.Height, Is.EqualTo(16));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_LowercaseHex_ParsesCorrectly() {
    var text =
      "/* Format_version=1, Width=16, Height=1, Depth=1, Valid_bits_per_item=16\n" +
      " */\n" +
      "\t0xffaa\n";
    var result = SunIconReader.FromBytes(Encoding.ASCII.GetBytes(text));

    Assert.That(result.PixelData[0], Is.EqualTo(0xFF));
    Assert.That(result.PixelData[1], Is.EqualTo(0xAA));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_MissingWidth_ThrowsInvalidDataException() {
    var text =
      "/* Format_version=1, Height=16, Depth=1, Valid_bits_per_item=16\n" +
      " */\n" +
      "\t0xFFFF\n";
    Assert.Throws<InvalidDataException>(() => SunIconReader.FromBytes(Encoding.ASCII.GetBytes(text)));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_UnterminatedComment_ThrowsInvalidDataException() {
    var text = "/* Format_version=1, Width=16, Height=16, Depth=1\n";
    Assert.Throws<InvalidDataException>(() => SunIconReader.FromBytes(Encoding.ASCII.GetBytes(text)));
  }

  private static string _BuildSunIcon(int width, int height, int bitsPerItem) {
    var sb = new StringBuilder();
    sb.Append($"/* Format_version=1, Width={width}, Height={height}, Depth=1, Valid_bits_per_item={bitsPerItem}\n");
    sb.AppendLine(" */");

    var bytesPerRow = (width + 7) / 8;
    var totalBytes = bytesPerRow * height;
    var bytesPerItem = bitsPerItem / 8;

    // Pad to item boundary
    var paddedBytes = totalBytes + (totalBytes % bytesPerItem);
    var itemCount = paddedBytes / bytesPerItem;

    for (var i = 0; i < itemCount; ++i) {
      if (i % 8 == 0)
        sb.Append('\t');

      if (bitsPerItem == 16)
        sb.Append($"0x{(ushort)(i * 0x1111 % 0x10000):X4}");
      else
        sb.Append($"0x{(uint)(i * 0x11111111 % uint.MaxValue):X8}");

      if (i < itemCount - 1)
        sb.Append(',');

      if (i % 8 == 7 || i == itemCount - 1)
        sb.AppendLine();
    }

    return sb.ToString();
  }
}
