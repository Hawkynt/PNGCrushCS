using System;
using System.IO;
using FileFormat.AtariSif;
using FileFormat.Core;

namespace FileFormat.AtariSif.Tests;

[TestFixture]
public sealed class AtariSifTests {

  private static AtariSifFile _Build(int width, int height, byte mode) {
    var bpp = mode == 9 ? 1 : 2;
    var rowBytes = (width * bpp + 7) >> 3;
    var data = new byte[rowBytes * height];
    for (var i = 0; i < data.Length; ++i)
      data[i] = (byte)(i * 23 & 0xFF);
    return new AtariSifFile { Width = width, Height = height, AnticMode = mode, PixelData = data };
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => AtariSifReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".sif"));
    Assert.Throws<FileNotFoundException>(() => AtariSifReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => AtariSifReader.FromBytes(new byte[5]));

  [Test]
  [Category("Unit")]
  public void FromBytes_BadMagic_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => AtariSifReader.FromBytes(new byte[20]));

  [Test]
  [Category("Unit")]
  public void FromBytes_UnknownMode_ThrowsInvalidDataException() {
    var data = new byte[20];
    data[0] = 0x53; data[1] = 0x49; data[2] = 0x46; data[3] = 0x00;
    data[5] = 8; data[7] = 8; // 8x8
    data[8] = 7; // unsupported mode
    Assert.Throws<InvalidDataException>(() => AtariSifReader.FromBytes(data));
  }

  [TestCase(160, 96, (byte)8)]
  [TestCase(320, 192, (byte)9)]
  [TestCase(160, 192, (byte)15)]
  [Category("Unit")]
  public void Writer_RoundTrip_ForEachMode(int width, int height, byte mode) {
    var original = _Build(width, height, mode);
    var bytes = AtariSifWriter.ToBytes(original);
    var loaded = AtariSifReader.FromSpan(bytes);
    Assert.That(loaded.Width, Is.EqualTo(width));
    Assert.That(loaded.Height, Is.EqualTo(height));
    Assert.That(loaded.AnticMode, Is.EqualTo(mode));
    Assert.That(loaded.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_AnticMode9_ProducesIndexed1() {
    var f = _Build(320, 192, 9);
    var raw = AtariSifFile.ToRawImage(f);
    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Indexed1));
    Assert.That(raw.PaletteCount, Is.EqualTo(2));
  }
}
