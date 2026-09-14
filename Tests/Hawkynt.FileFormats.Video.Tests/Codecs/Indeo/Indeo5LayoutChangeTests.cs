using System;
using System.Collections.Generic;

namespace FileFormat.Codecs.Indeo.Tests;

[TestFixture]
public sealed class Indeo5LayoutChangeTests {

  [Test]
  [Category("Unit")]
  public void ANewGopRebuildsTilesWhenOnlyTheLumaMacroblockGeometryChanges() {
    const int width = 32;
    const int height = 24;
    var decoder = new Indeo5Decoder(width, height);

    var first = decoder.Decode(_IntraWithEmptyTiles(width, height, lumaMacroblockSize: 16));
    var second = decoder.Decode(_IntraWithEmptyTiles(width, height, lumaMacroblockSize: 8));

    Assert.Multiple(() => {
      Assert.That(first, Is.Not.Null);
      Assert.That(second, Is.Not.Null);
      Assert.That(second!.Width, Is.EqualTo(width));
      Assert.That(second.Height, Is.EqualTo(height));
    });
  }

  private static byte[] _IntraWithEmptyTiles(int width, int height, int lumaMacroblockSize) {
    if (lumaMacroblockSize is not (8 or 16))
      throw new ArgumentOutOfRangeException(nameof(lumaMacroblockSize));

    var writer = new _BitWriter();
    writer.Write(0x1F, 5);
    writer.Write(Indeo5Decoder.FrameTypeIntra, 3);
    writer.Write(0, 8);

    writer.Write(0, 8); // GOP flags: one whole-picture tile, YVU9, no protection/transparency.
    writer.Write(0, 2); // One luminance band.
    writer.Write(0, 1); // One chrominance band.
    writer.Write(15, 4); // Explicit dimensions.
    writer.Write(height, 13);
    writer.Write(width, 13);

    writer.Write(0, 1); // Luma: whole-pel vectors.
    writer.Write(lumaMacroblockSize == 8 ? 1 : 0, 1); // MB is one or two 8x8 blocks wide.
    writer.Write(0, 1); // 8x8 blocks.
    writer.Write(0, 1); // No extended transform information.
    writer.Write(0, 2);

    writer.Write(0, 1); // Chroma: whole-pel vectors.
    writer.Write(1, 1); // One 4x4 block per macroblock.
    writer.Write(1, 1); // 4x4 blocks.
    writer.Write(0, 1); // No extended transform information.
    writer.Write(0, 2);

    writer.Align();
    writer.Write(0, 23);
    writer.Write(0, 1); // No GOP extension.
    writer.Align();

    writer.Write(0, 8); // Picture flags; use the default macroblock codebook.
    writer.Write(0, 3);
    writer.Align();

    for (var band = 0; band < 3; ++band) {
      writer.Write(0, 8); // Band flags; use default run/value map and block codebook.
      writer.Write(0, 1); // No checksum.
      writer.Write(0, 5); // Quantiser 0.
      writer.Align();
      writer.Write(1, 1); // The one whole-picture tile repeats its reference rectangle.
      writer.Align();
    }

    return writer.ToArray();
  }

  private sealed class _BitWriter {
    private readonly List<byte> _bytes = [];
    private int _bit;

    internal void Write(int value, int count) {
      for (var bit = 0; bit < count; ++bit) {
        if (this._bit == 0)
          this._bytes.Add(0);

        if (((value >> bit) & 1) != 0)
          this._bytes[^1] |= (byte)(1 << this._bit);

        this._bit = (this._bit + 1) & 7;
      }
    }

    internal void Align() => this._bit = 0;

    internal byte[] ToArray() => [.. this._bytes];
  }
}
