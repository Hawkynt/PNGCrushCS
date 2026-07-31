using System;
using System.IO;
using FileFormat.Core;
using FileFormat.PaintMagic;

namespace FileFormat.PaintMagic.Tests;

/// <summary>
/// Paint Magic's layout, and the one thing that sets it apart: pattern 11 shows a single colour
/// across the whole picture rather than one per cell, so there is no colour RAM to read.
/// </summary>
[TestFixture]
public sealed class PaintMagicReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => PaintMagicReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromBytes_WrongSize_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => PaintMagicReader.FromBytes(new byte[10003]));

  /// <summary>The bitmap starts after a preamble, and the two shared registers sit behind it.</summary>
  [Test]
  [Category("Unit")]
  public void FromBytes_EachSection_ReadFromItsOwnOffset() {
    var data = _Build();

    var result = PaintMagicReader.FromBytes(data);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(160));
      Assert.That(result.Height, Is.EqualTo(200));
      Assert.That(result.BitmapData[0], Is.EqualTo(0xAB));
      Assert.That(result.BitmapData[PaintMagicFile.BitmapDataSize - 1], Is.EqualTo(0xCD));
      Assert.That(result.VideoMatrix[0], Is.EqualTo(0x12));
      Assert.That(result.BackgroundColor, Is.EqualTo(0x06));
      Assert.That(result.SharedColor, Is.EqualTo(0x0A));
    });
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_PreservesEverySection() {
    var original = PaintMagicReader.FromBytes(_Build());

    var reread = PaintMagicReader.FromBytes(PaintMagicWriter.ToBytes(original));

    Assert.Multiple(() => {
      Assert.That(reread.BitmapData, Is.EqualTo(original.BitmapData));
      Assert.That(reread.VideoMatrix, Is.EqualTo(original.VideoMatrix));
      Assert.That(reread.BackgroundColor, Is.EqualTo(original.BackgroundColor));
      Assert.That(reread.SharedColor, Is.EqualTo(original.SharedColor));
    });
  }

  /// <summary>The written picture must use one colour for pattern 11 and no more.</summary>
  /// <remarks>
  /// This is the constraint the format imposes and the reason its encoder needed a separate path:
  /// letting each cell pick its own third colour and collapsing the result afterwards would throw
  /// the choice away rather than make it.
  /// </remarks>
  [Test]
  [Category("Unit")]
  public void FromRawImage_UsesOneColorForPatternElevenEverywhere() {
    var rgb = new byte[160 * 200 * 3];
    for (var y = 0; y < 200; ++y)
    for (var x = 0; x < 160; ++x) {
      var color = Commodore64Graphics.HexColors[(x / 5 + y / 7) % 16];
      var at = (y * 160 + x) * 3;
      rgb[at] = (byte)(color >> 16);
      rgb[at + 1] = (byte)(color >> 8);
      rgb[at + 2] = (byte)color;
    }

    var source = new RawImage { Width = 160, Height = 200, Format = PixelFormat.Rgb24, PixelData = rgb };

    var file = PaintMagicFile.FromRawImage(source);
    var written = PaintMagicWriter.ToBytes(file);

    Assert.That(written, Has.Length.EqualTo(PaintMagicFile.ExpectedFileSize));
    Assert.That(PaintMagicReader.FromBytes(written).SharedColor, Is.EqualTo(file.SharedColor));
    Assert.That(file.SharedColor, Is.Not.EqualTo(file.BackgroundColor));
  }

  private static byte[] _Build() {
    var data = new byte[PaintMagicFile.ExpectedFileSize];

    data[PaintMagicFile.BitmapOffset] = 0xAB;
    data[PaintMagicFile.BitmapOffset + PaintMagicFile.BitmapDataSize - 1] = 0xCD;
    data[PaintMagicFile.VideoMatrixOffset] = 0x12;
    data[PaintMagicFile.BackgroundOffset] = 0x06;
    data[PaintMagicFile.SharedColorOffset] = 0x0A;

    return data;
  }
}
