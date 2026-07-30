using System;
using System.IO;
using FileFormat.XbmColor;

namespace FileFormat.XbmColor.Tests;

[TestFixture]
public sealed class XbmColorTests {

  private static XbmColorFile _Build() => new() {
    Width = 4,
    Height = 2,
    Name = "test",
    ColorCount = 3,
    Palette = [0x00, 0x00, 0x00, 0xFF, 0x00, 0x00, 0x00, 0xFF, 0x00],
    PixelData = [0, 1, 2, 0, 2, 1, 0, 1],
  };

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => XbmColorReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".xbm"));
    Assert.Throws<FileNotFoundException>(() => XbmColorReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => XbmColorReader.FromBytes(new byte[10]));

  [Test]
  [Category("Unit")]
  public void Writer_RoundTrip_PreservesAllFields() {
    var original = _Build();
    var bytes = XbmColorWriter.ToBytes(original);
    var loaded = XbmColorReader.FromSpan(bytes);
    Assert.That(loaded.Width, Is.EqualTo(original.Width));
    Assert.That(loaded.Height, Is.EqualTo(original.Height));
    Assert.That(loaded.ColorCount, Is.EqualTo(original.ColorCount));
    Assert.That(loaded.Name, Is.EqualTo(original.Name));
    Assert.That(loaded.Palette[..(original.ColorCount * 3)], Is.EqualTo(original.Palette));
    Assert.That(loaded.PixelData[..(original.Width * original.Height)], Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void Reader_RejectsMissingColorsMarker()
    => Assert.Throws<InvalidDataException>(() =>
       XbmColorReader.FromBytes(System.Text.Encoding.ASCII.GetBytes(
         "#define img_width 8\n#define img_height 1\nstatic unsigned char img_bits[] = { 0xFF };\n"
         + new string(' ', 32))));
}
