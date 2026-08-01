using System;
using System.IO;
using FileFormat.AtariGfb;
using FileFormat.Core;

namespace FileFormat.AtariGfb.Tests;

/// <summary>
/// DeskPic, which states its own shape rather than having one assumed for it.
/// </summary>
/// <remarks>
/// These build their fixtures through the writer. A file of this format is a header, then
/// interleaved bitplanes, then a palette of 256 entries however few the picture uses — none of
/// which can be assembled by hand without repeating the writer's own arithmetic and agreeing with
/// whatever it got wrong.
/// </remarks>
[TestFixture]
public sealed class AtariGfbReaderTests {

  [Test]
  [Category("Unit")]
  public void FromFile_NullFile_Throws()
    => Assert.Throws<ArgumentNullException>(() => AtariGfbReader.FromFile(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_MissingFile_Throws()
    => Assert.Throws<FileNotFoundException>(() => AtariGfbReader.FromFile(new FileInfo("nonexistent.gfb")));

  [Test]
  [Category("Unit")]
  public void FromBytes_NullData_Throws()
    => Assert.Throws<ArgumentNullException>(() => AtariGfbReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromBytes_WrongSignature_Throws()
    => Assert.Throws<InvalidDataException>(() => AtariGfbReader.FromBytes(new byte[64]));

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_Throws()
    => Assert.Throws<InvalidDataException>(() => AtariGfbReader.FromBytes(new byte[8]));

  [TestCase(2)]
  [TestCase(4)]
  [TestCase(16)]
  [TestCase(256)]
  [Category("Unit")]
  public void RoundTrip_KeepsShapeAndPixels(int colors) {
    var written = AtariGfbWriter.ToBytes(AtariGfbFile.FromRawImage(_Sample(64, 20, colors)));

    var restored = AtariGfbReader.FromBytes(written);

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(64));
      Assert.That(restored.Height, Is.EqualTo(20));
      Assert.That(1 << restored.Bitplanes, Is.LessThanOrEqualTo(colors));
      Assert.That(restored.PixelData, Has.Length.EqualTo(64 * 20));
    });
  }

  /// <summary>A row is padded out to whole words, which a width of 17 makes visible.</summary>
  [Test]
  [Category("Unit")]
  public void Stride_PadsRowsToWholeWords() {
    Assert.Multiple(() => {
      Assert.That(AtariGfbFile.Stride(16, 1), Is.EqualTo(2));
      Assert.That(AtariGfbFile.Stride(17, 1), Is.EqualTo(4));
      Assert.That(AtariGfbFile.Stride(320, 4), Is.EqualTo(160));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromStream_NullStream_Throws()
    => Assert.Throws<ArgumentNullException>(() => AtariGfbReader.FromStream(null!));

  private static RawImage _Sample(int width, int height, int colors) {
    var rgb = new byte[width * height * 3];
    for (var i = 0; i < width * height; ++i) {
      var shade = (byte)(i % colors * 255 / Math.Max(1, colors - 1));
      rgb[i * 3] = rgb[i * 3 + 1] = rgb[i * 3 + 2] = shade;
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = rgb };
  }
}
