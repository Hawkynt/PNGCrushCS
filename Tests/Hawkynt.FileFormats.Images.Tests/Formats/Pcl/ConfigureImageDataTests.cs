using System.Collections.Generic;
using System.Text;
using FileFormat.Core;
using FileFormat.Pcl;

namespace FileFormat.Pcl.Tests;

/// <summary>
/// A job that states its own palette with ESC*v#W rather than taking a simple-colour one.
/// </summary>
/// <remarks>
/// This is the shape ImageMagick writes, and until the configure-image-data command was read the
/// reader refused every file it produced. Two things arrive with it: indices wider than the one bit
/// a plane carries, and a palette built one primary at a time by ESC*v#a#b#c#I.
/// </remarks>
[TestFixture]
public sealed class ConfigureImageDataTests {

  private static void _Escape(List<byte> job, string command) {
    job.Add(0x1B);
    job.AddRange(Encoding.ASCII.GetBytes(command));
  }

  private static byte[] _Job(int bitsPerIndex, int width, int height, IReadOnlyList<(int R, int G, int B)> palette, byte[] rows) {
    var job = new List<byte> { 0x1B, (byte)'E' };
    _Escape(job, $"*r{width}s{height}T");
    _Escape(job, "*v6W");
    job.AddRange([0, 1, (byte)bitsPerIndex, 8, 8, 8]);
    for (var i = 0; i < palette.Count; ++i)
      _Escape(job, $"*v{palette[i].R}a{palette[i].G}b{palette[i].B}c{i}I");

    _Escape(job, "*r1A");
    var stride = (width * bitsPerIndex + 7) / 8;
    for (var y = 0; y < height; ++y) {
      _Escape(job, $"*b{stride}W");
      job.AddRange(rows[(y * stride)..((y + 1) * stride)]);
    }

    _Escape(job, "*rC");
    return [.. job];
  }

  /// <summary>Eight bits an index is one byte a pixel, which is what a palette of 256 needs.</summary>
  [Test]
  [Category("Unit")]
  public void EightBitIndicesAreOneBytePerPixel() {
    var palette = new (int, int, int)[] { (255, 0, 0), (0, 255, 0), (0, 0, 255), (255, 255, 0) };
    byte[] rows = [0, 1, 2, 3, 3, 2, 1, 0];

    var image = PclFile.ToRawImage(PclReader.FromBytes(_Job(8, 4, 2, palette, rows))).ToRgb24();

    Assert.That(image, Is.EqualTo(new byte[] {
      255, 0, 0,   0, 255, 0,   0, 0, 255,   255, 255, 0,
      255, 255, 0, 0, 0, 255,   0, 255, 0,   255, 0, 0,
    }));
  }

  /// <summary>Four bits an index is two to a byte, the first in the high half.</summary>
  [Test]
  [Category("Unit")]
  public void FourBitIndicesArePackedTwoToAByteHighHalfFirst() {
    var palette = new (int, int, int)[] { (0, 0, 0), (255, 0, 0), (0, 255, 0), (0, 0, 255) };
    byte[] rows = [0x01, 0x23];

    var image = PclFile.ToRawImage(PclReader.FromBytes(_Job(4, 4, 1, palette, rows))).ToRgb24();

    Assert.That(image, Is.EqualTo(new byte[] { 0, 0, 0, 255, 0, 0, 0, 255, 0, 0, 0, 255 }));
  }

  /// <summary>An entry never assigned stays black rather than keeping a previous palette's colour.</summary>
  [Test]
  [Category("Unit")]
  public void ConfiguringImageDataClearsThePaletteToBlack() {
    var palette = new (int, int, int)[] { (255, 255, 255) };
    byte[] rows = [0, 1];

    var image = PclFile.ToRawImage(PclReader.FromBytes(_Job(8, 2, 1, palette, rows))).ToRgb24();

    Assert.That(image, Is.EqualTo(new byte[] { 255, 255, 255, 0, 0, 0 }));
  }
}
