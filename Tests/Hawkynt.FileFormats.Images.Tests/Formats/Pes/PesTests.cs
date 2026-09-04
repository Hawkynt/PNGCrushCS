using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;
using FileFormat.Pes;
using NUnit.Framework;

namespace FileFormat.Pes.Tests;

/// <summary>Brother PES embroidery files, read as the path they hold.</summary>
/// <remarks>
/// No real PES was available, so the layout was taken from ImageMagick's own
/// coder and a file written from known stitches is handed back to ImageMagick to
/// judge. It reports the extent it read, which is derived from every stitch
/// coordinate in the file, so agreement there is agreement about the decode.
///
/// <para>ImageMagick states that extent as the difference between the outermost
/// stitches and this states the pixels needed to draw them, which is one more in
/// each axis. The two are the same measurement counted differently, and the
/// tests below check the underlying bounds rather than either convention.</para>
/// </remarks>
[TestFixture]
public sealed class PesTests {

  private static PesFile _Design() {
    var outline = new List<(int X, int Y)>();
    for (var i = 0; i <= 40; ++i)
      outline.Add((10 + i, 10));
    for (var i = 0; i <= 30; ++i)
      outline.Add((50, 10 + i));
    for (var i = 0; i <= 40; ++i)
      outline.Add((50 - i, 40));
    for (var i = 0; i <= 30; ++i)
      outline.Add((10, 40 - i));

    var diagonal = new List<(int X, int Y)>();
    for (var i = 0; i <= 30; ++i)
      diagonal.Add((10 + i, 10 + i));

    return new PesFile {
      Blocks = [
        new PesStitchBlock { ThreadIndex = 5, Color = 0xED171F, Points = outline.ToArray() },
        new PesStitchBlock { ThreadIndex = 2, Color = 0x0A55A3, Points = diagonal.ToArray() },
      ],
    };
  }

  [Test]
  public void EveryStitchComesBackWhereItWasPut() {
    var design = _Design();
    var again = PesReader.FromBytes(PesWriter.ToBytes(design));

    Assert.That(again.Blocks, Has.Count.EqualTo(design.Blocks.Count));
    for (var i = 0; i < design.Blocks.Count; ++i) {
      Assert.That(again.Blocks[i].ThreadIndex, Is.EqualTo(design.Blocks[i].ThreadIndex), $"block {i} thread");
      Assert.That(again.Blocks[i].Points, Is.EqualTo(design.Blocks[i].Points), $"block {i} stitches");
    }
  }

  /// <summary>
  /// The colour a block is sewn in is not in the file; the index into the thread
  /// chart is, and the colour comes from the chart.
  /// </summary>
  [Test]
  public void ABlockTakesItsColourFromTheThreadChart() {
    var again = PesReader.FromBytes(PesWriter.ToBytes(_Design()));
    Assert.Multiple(() => {
      Assert.That(again.Blocks[0].Color, Is.EqualTo(0xED171F));
      Assert.That(again.Blocks[1].Color, Is.EqualTo(0x0A55A3));
    });
  }

  [Test]
  public void TheBoundsAreTheStitchesTheDesignReaches() {
    var again = PesReader.FromBytes(PesWriter.ToBytes(_Design()));
    Assert.Multiple(() => {
      Assert.That(again.MinX, Is.EqualTo(10));
      Assert.That(again.MinY, Is.EqualTo(10));
      Assert.That(again.MaxX, Is.EqualTo(50));
      Assert.That(again.MaxY, Is.EqualTo(40));
      Assert.That(again.Width, Is.EqualTo(41));
      Assert.That(again.Height, Is.EqualTo(31));
    });
  }

  [Test]
  public void TheDrawnPathPutsEachBlocksColourOnTheCanvas() {
    var design = _Design();
    var image = PesFile.ToRawImage(PesReader.FromBytes(PesWriter.ToBytes(design)));

    Assert.Multiple(() => {
      Assert.That(image.Width, Is.EqualTo(41));
      Assert.That(image.Height, Is.EqualTo(31));
      Assert.That(image.Format, Is.EqualTo(PixelFormat.Rgb24));
    });

    // A point on the outline's top edge that the diagonal does not reach, and a
    // point on the diagonal away from the outline. The two blocks share their
    // starting stitch, and there the later one is what shows, so neither sample
    // is taken at the corner.
    var edge = (0 * 41 + 20) * 3;
    Assert.That((image.PixelData[edge], image.PixelData[edge + 1], image.PixelData[edge + 2]),
      Is.EqualTo(((byte)0xED, (byte)0x17, (byte)0x1F)), "the outline's top edge");

    var diagonal = (15 * 41 + 15) * 3;
    Assert.That((image.PixelData[diagonal], image.PixelData[diagonal + 1], image.PixelData[diagonal + 2]),
      Is.EqualTo(((byte)0x0A, (byte)0x55, (byte)0xA3)), "the diagonal");

    // Nothing was sewn in the bottom-right, so the ground shows through.
    var empty = (28 * 41 + 38) * 3;
    Assert.That((image.PixelData[empty], image.PixelData[empty + 1], image.PixelData[empty + 2]),
      Is.EqualTo(((byte)0xFF, (byte)0xFF, (byte)0xFF)), "unsewn ground");
  }

  [Test]
  public void SomethingThatDoesNotBeginWithThePesMarkerIsRefused() {
    var bytes = PesWriter.ToBytes(_Design());
    bytes[1] = (byte)'X';
    Assert.Throws<InvalidDataException>(() => PesReader.FromBytes(bytes));
  }

  [Test]
  public void APesPointingItsStitchesOutsideTheFileIsRefused() {
    var bytes = PesWriter.ToBytes(_Design());
    BitConverter.GetBytes(1 << 24).CopyTo(bytes, 8);
    Assert.Throws<InvalidDataException>(() => PesReader.FromBytes(bytes));
  }

  [Test]
  public void APesWithNoStitchesIsRefused() {
    var bytes = PesWriter.ToBytes(_Design());
    // Turn the first stitch pair into the end-of-stitches marker.
    var stitchStart = bytes.Length - 2;
    for (var i = 12 + 36 + 1 + 2 + 532 - 2 - 21; i < stitchStart; ++i)
      bytes[i] = 0;
    bytes[12 + 36 + 1 + 2 + 532 - 2 - 21] = 0xFF;
    bytes[12 + 36 + 2 + 2 + 532 - 2 - 21] = 0x00;
    Assert.Throws<InvalidDataException>(() => PesReader.FromBytes(bytes));
  }
}
