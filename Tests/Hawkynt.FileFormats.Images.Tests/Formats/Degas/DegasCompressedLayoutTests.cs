using System;
using FileFormat.Core;
using FileFormat.Degas;

namespace FileFormat.Degas.Tests;

/// <summary>
/// How a packed DEGAS arranges its scanlines, and what a monochrome one draws in.
/// </summary>
/// <remarks>
/// An uncompressed DEGAS holds the machine's screen as it stands, four planes interleaved a word at
/// a time. A packed one does not: it stores each scanline as one whole plane row after another, and
/// unpacks to the same number of bytes in a different arrangement. Using it as it came left the
/// picture in roughly the right colours with every group of sixteen pixels drawn from four unrelated
/// places — 19 per cent of pixels matching, which looks like a decode gone slightly wrong rather
/// than a layout read the wrong way round.
/// <para/>
/// High resolution is monochrome and takes no colours from the palette registers, which this was the
/// fifth Atari format found doing.
/// <para/>
/// Both samples now match RECOIL on every pixel, and RECOIL reads a file this writes back as the
/// same picture.
/// </remarks>
[TestFixture]
public sealed class DegasCompressedLayoutTests {

  /// <summary>A screen whose every word says which plane and which word of the row it is.</summary>
  private static byte[] _Screen() {
    var data = new byte[32000];
    for (var row = 0; row < 200; ++row)
    for (var word = 0; word < 20; ++word)
    for (var plane = 0; plane < 4; ++plane) {
      var at = row * 160 + (word * 4 + plane) * 2;
      data[at] = (byte)plane;
      data[at + 1] = (byte)word;
    }

    return data;
  }

  [Test]
  [Category("Integration")]
  public void APackedScreenComesBackAsItWentIn() {
    var screen = _Screen();
    var original = new DegasFile {
      Width = 320, Height = 200, Resolution = DegasResolution.Low,
      IsCompressed = true, Palette = new short[16], PixelData = screen,
    };

    var restored = DegasReader.FromBytes(DegasWriter.ToBytes(original));

    Assert.That(restored.PixelData, Is.EqualTo(screen), "the plane rows must be put back interleaved");
  }

  [Test]
  [Category("Integration")]
  public void APackedAndAnUnpackedScreenDecodeAlike() {
    var screen = _Screen();

    byte[] Decode(bool compressed) => DegasFile.ToRawImage(DegasReader.FromBytes(DegasWriter.ToBytes(new DegasFile {
      Width = 320, Height = 200, Resolution = DegasResolution.Low,
      IsCompressed = compressed, Palette = new short[16], PixelData = screen,
    }))).PixelData;

    Assert.That(Decode(true), Is.EqualTo(Decode(false)), "packing must not change the picture");
  }

  [Test]
  [Category("Unit")]
  public void HighResolutionDrawsInBlackAndWhiteWhateverThePaletteHolds() {
    // The sample leaves 0x0777 in the first register and 0x0006 in the second.
    short[] palette = [0x0777, 0x0006, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
    var image = DegasFile.ToRawImage(new DegasFile {
      Width = 640, Height = 400, Resolution = DegasResolution.High,
      Palette = palette, PixelData = new byte[32000],
    });

    Assert.That(image.Palette, Is.Not.Null);
    Assert.Multiple(() => {
      Assert.That(image.Palette![0], Is.EqualTo(255), "paper is white");
      Assert.That(image.Palette![3], Is.Zero, "ink is black, not what the register happens to hold");
    });
  }
}
