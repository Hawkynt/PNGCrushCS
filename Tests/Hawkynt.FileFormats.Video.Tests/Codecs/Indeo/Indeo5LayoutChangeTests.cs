using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Avi;
using FileFormat.Core;
using Hawkynt.FileFormats.Video.Tests;

namespace FileFormat.Codecs.Indeo.Tests;

/// <summary>
/// A second group of pictures that changes the luminance macroblock geometry and nothing else.
/// </summary>
/// <remarks>
/// The group header reads a macroblock and block size for every band in turn, luminance first and
/// chrominance last, and one flag out of that loop decides whether the tile descriptors are rebuilt.
/// Written as a plain assignment the flag holds only what the last band said, so a luminance band
/// that changed is forgotten the moment a chrominance band that did not is read, the tiles keep the
/// macroblock count they were built with, and the next frame is refused for stating a geometry its
/// own tiles do not have.
/// <para/>
/// Reaching that needs a stream where luminance may change while chrominance may not, and almost no
/// stream can be one. Every tile's macroblock count has to match the first luminance band's, and a
/// chrominance tile is a quarter of a luminance tile in each direction, so the luminance macroblock
/// has to be four times the chrominance one - sixteen against four, the only such pair the format
/// allows. The exception is a picture small enough that every tile is a single macroblock whichever
/// size is chosen, and eight by eight is exactly that: one luminance macroblock at either of its
/// two sizes, one chrominance macroblock at any of its three. So the two groups below differ in the
/// luminance geometry alone, which is the case the flag has to survive.
/// <para/>
/// <see cref="FfmpegReadsEitherGroupOnItsOwn"/> is what keeps this honest. A hand-written bitstream
/// nothing but this package's decoder has ever read is no evidence that a real stream could look
/// like it, so each group is muxed on its own and handed to FFmpeg, which reads both without
/// complaint. The bytes are therefore valid Indeo 5 at either luminance geometry, and the only
/// thing either group asks of a decoder that the other does not is the change between them.
/// <para/>
/// FFmpeg will not read the two in sequence. It refuses the second with "MB sizes mismatch: 8 vs.
/// 16 vs. 8" because its own <c>blk_size_changed</c> is assigned rather than accumulated across the
/// band loop - <c>libavcodec/indeo5.c:143</c>, the same shape of mistake this fixture covers. So
/// there is no external oracle for the sequence itself: the only other implementation of the format
/// carries the defect too. That is stated here rather than worked around, and it is why the
/// sequence is asserted against this decoder alone while the validity of the bytes it is made of is
/// asserted against FFmpeg.
/// </remarks>
[TestFixture]
public sealed class Indeo5LayoutChangeTests {

  private const int _WIDTH = 8;
  private const int _HEIGHT = 8;

  [Test]
  [Category("Unit")]
  public void ANewGopRebuildsTilesWhenOnlyTheLumaMacroblockGeometryChanges() {
    const int width = _WIDTH;
    const int height = _HEIGHT;
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

  [TestCase(16)]
  [TestCase(8)]
  [Category("Conformance")]
  public void FfmpegReadsEitherGroupOnItsOwn(int lumaMacroblockSize) {
    FFmpegOracle.RequireAvailable();

    var stream = Indeo5VideoEncoder
      .Create(new() {
        Index = 0,
        Kind = MediaStreamKind.Video,
        Codec = CodecTag.FromCharacters("IV50"),
        Handler = CodecTag.FromCharacters("IV50"),
        Width = _WIDTH,
        Height = _HEIGHT,
        TimeBase = new(1, 25),
        FrameRate = new(25, 1),
      })
      .DescribeStream();

    var packets = new List<CodedPacket> {
      new(0, _IntraWithEmptyTiles(_WIDTH, _HEIGHT, lumaMacroblockSize), 0, 0, IsKeyFrame: true),
    };

    var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".avi");
    bool decoded;
    string detail;
    try {
      File.WriteAllBytes(path, VideoIO.Mux<AviWriter>([stream], packets));
      var chroma = ((_WIDTH + 3) >> 2) * ((_HEIGHT + 3) >> 2);
      (decoded, detail, _) = FFmpegOracle.TryDecodePicturesAs(
        path, _WIDTH, _HEIGHT, expectedFrames: 1, "yuv410p", _WIDTH * _HEIGHT + 2 * chroma);
    } finally {
      try { File.Delete(path); } catch { /* best effort */ }
    }

    Assert.That(decoded, Is.True, detail);
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
