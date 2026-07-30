using System;
using System.IO;
using FileFormat.Core;
using FileFormat.TrsPix;

namespace FileFormat.TrsPix.Tests;

[TestFixture]
public sealed class TrsPixTests {

  private static TrsPixFile _Build(byte mode) {
    var (w, bpp) = mode switch {
      0 => (320, 1),
      1 => (320, 2),
      2 => (640, 1),
      3 => (640, 2),
      _ => (0, 0),
    };
    var rowBytes = (w * bpp + 7) >> 3;
    var data = new byte[rowBytes * 192];
    for (var i = 0; i < data.Length; ++i)
      data[i] = (byte)(i * 17 & 0xFF);
    return new TrsPixFile { Mode = mode, PixelData = data };
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => TrsPixReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".pix"));
    Assert.Throws<FileNotFoundException>(() => TrsPixReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => TrsPixReader.FromBytes(new byte[3]));

  [Test]
  [Category("Unit")]
  public void FromBytes_BadMagic_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => TrsPixReader.FromBytes([0x00, 0x00, 0x00, 0x00, 0x00]));

  [Test]
  [Category("Unit")]
  public void FromBytes_BadMode_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => TrsPixReader.FromBytes([0x50, 0x49, 0x58, 0x00, 0x07]));

  [TestCase((byte)0)]
  [TestCase((byte)1)]
  [TestCase((byte)2)]
  [TestCase((byte)3)]
  [Category("Unit")]
  public void Writer_RoundTrip_ForEachMode(byte mode) {
    var original = _Build(mode);
    var bytes = TrsPixWriter.ToBytes(original);
    var loaded = TrsPixReader.FromSpan(bytes);
    Assert.That(loaded.Mode, Is.EqualTo(original.Mode));
    Assert.That(loaded.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_Mode0_Produces320x192Indexed1() {
    var f = _Build(0);
    var raw = TrsPixFile.ToRawImage(f);
    Assert.That(raw.Width, Is.EqualTo(320));
    Assert.That(raw.Height, Is.EqualTo(192));
    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Indexed1));
    Assert.That(raw.PaletteCount, Is.EqualTo(2));
  }
}
