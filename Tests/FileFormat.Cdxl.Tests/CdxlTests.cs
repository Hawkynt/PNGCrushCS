using System;
using System.IO;
using FileFormat.Cdxl;
using FileFormat.Core;

namespace FileFormat.Cdxl.Tests;

[TestFixture]
public sealed class CdxlTests {

  private static CdxlFile _BuildFrame(int width, int height, int planes) {
    var paletteCount = 1 << planes;
    var palette = new byte[paletteCount * 2];
    for (var i = 0; i < paletteCount; ++i) {
      palette[i * 2] = (byte)(i & 0x0F);
      palette[i * 2 + 1] = (byte)(((i & 0x0F) << 4) | (i & 0x0F));
    }
    var rowBytes = (width + 7) >> 3;
    var bitmap = new byte[planes * rowBytes * height];
    for (var i = 0; i < bitmap.Length; ++i)
      bitmap[i] = (byte)(i * 31);
    return new CdxlFile { Width = width, Height = height, BitPlanes = planes, Palette = palette, PixelData = bitmap };
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => CdxlReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".cdxl"));
    Assert.Throws<FileNotFoundException>(() => CdxlReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => CdxlReader.FromBytes(new byte[10]));

  [Test]
  [Category("Unit")]
  public void Writer_RoundTrip_PreservesGeometryAndData() {
    var original = _BuildFrame(8, 4, 2);
    var bytes = CdxlWriter.ToBytes(original);
    var loaded = CdxlReader.FromSpan(bytes);
    Assert.That(loaded.Width, Is.EqualTo(original.Width));
    Assert.That(loaded.Height, Is.EqualTo(original.Height));
    Assert.That(loaded.BitPlanes, Is.EqualTo(original.BitPlanes));
    Assert.That(loaded.Palette, Is.EqualTo(original.Palette));
    Assert.That(loaded.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_FromRawImage_RoundTripsIndices() {
    var original = _BuildFrame(16, 8, 4);
    var raw = CdxlFile.ToRawImage(original);
    Assert.That(raw.Width, Is.EqualTo(16));
    Assert.That(raw.Height, Is.EqualTo(8));
    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Indexed8));
    Assert.That(raw.PaletteCount, Is.EqualTo(16));
    var rebuilt = CdxlFile.FromRawImage(raw);
    Assert.That(rebuilt.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void Reader_RejectsImplausibleGeometry() {
    var bad = new byte[64];
    // width=0, height=0 → invalid
    Assert.Throws<InvalidDataException>(() => CdxlReader.FromBytes(bad));
  }
}
