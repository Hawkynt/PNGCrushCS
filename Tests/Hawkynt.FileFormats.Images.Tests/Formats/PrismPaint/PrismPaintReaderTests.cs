using System;
using System.IO;
using FileFormat.Core;
using FileFormat.PrismPaint;

namespace FileFormat.PrismPaint.Tests;

/// <summary>
/// Reading a Prism Paint picture.
/// </summary>
/// <remarks>
/// These used to build files in the shape the reader then assumed and no file has: the size in the
/// first four bytes where the signature belongs, a Falcon palette of 256 packed entries, and one
/// byte a pixel. The reader and the writer agreed with each other and with nothing else, which is
/// why a real file came back 20048 by 84.
/// <para/>
/// A real one opens with <c>PNT\0</c>, states its size as two big-endian words with the plane count
/// after them, keeps its palette as three words an entry on the VDI's nought-to-a-thousand scale in
/// the VDI's order, and stores the screen as bitplanes. The sample now matches RECOIL on all 64000
/// of its pixels.
/// </remarks>
[TestFixture]
public sealed class PrismPaintReaderTests {

  /// <summary>Builds a picture the way a real one is laid out.</summary>
  internal static byte[] Build(int width, int height, int planes) {
    var colors = 1 << planes;
    var screen = (width + 15) / 16 * 2 * planes * height;
    var data = new byte[PrismPaintFile.PaletteOffset + colors * PrismPaintFile.PaletteEntryBytes + screen];

    "PNT\0"u8.CopyTo(data);
    data[4] = 1;
    data[PrismPaintFile.WidthOffset] = (byte)(width >> 8);
    data[PrismPaintFile.WidthOffset + 1] = (byte)width;
    data[PrismPaintFile.HeightOffset] = (byte)(height >> 8);
    data[PrismPaintFile.HeightOffset + 1] = (byte)height;
    data[PrismPaintFile.PlanesOffset + 1] = (byte)planes;
    return data;
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => PrismPaintReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_Throws()
    => Assert.Throws<InvalidDataException>(() => PrismPaintReader.FromBytes(new byte[100]));

  [Test]
  [Category("Unit")]
  public void FromBytes_Valid320x200_Parses() {
    var file = PrismPaintReader.FromBytes(Build(320, 200, 4));

    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(320));
      Assert.That(file.Height, Is.EqualTo(200));
      Assert.That(file.PixelData, Has.Length.EqualTo(320 * 200));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Valid640x480_Parses() {
    var file = PrismPaintReader.FromBytes(Build(640, 480, 4));

    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(640));
      Assert.That(file.Height, Is.EqualTo(480));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Valid() {
    using var stream = new MemoryStream(Build(320, 200, 4));
    var file = PrismPaintReader.FromStream(stream);

    Assert.That(file.Width, Is.EqualTo(320));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_WithoutSignature_Throws() {
    var data = Build(320, 200, 4);
    data[0] = 0;

    Assert.Throws<InvalidDataException>(() => PrismPaintReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ShorterThanItsOwnScreen_Throws() {
    var data = Build(320, 200, 4);

    Assert.Throws<InvalidDataException>(() => PrismPaintReader.FromBytes(data[..(data.Length - 64)]));
  }
}
