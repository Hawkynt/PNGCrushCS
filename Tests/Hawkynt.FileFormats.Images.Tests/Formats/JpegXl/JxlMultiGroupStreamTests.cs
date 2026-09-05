using System;
using System.IO;
using System.Linq;
using FileFormat.JpegXl.Codec;
using NUnit.Framework;

namespace FileFormat.JpegXl.Tests;

/// <summary>
/// A frame wider than one group states each of its parts at its own offset in
/// the file, and every part is decoded with the histograms the frame stated
/// once at the front. This is about the decoders following the move.
/// </summary>
/// <remarks>
/// The arithmetic decoder reads bits of its own, separately from the
/// hybrid-integer extra bits beside it. It was bound to the reader it was built
/// with — the one the histograms came from — and stayed there while everything
/// around it moved on to the group's own offset. Symbols then came out of the
/// padding at the end of the frame's first part: cheap, plausible, and wrong.
/// A frame coded with prefix codes was unaffected, which is why frames in more
/// than one group passed for as long as they did.
///
/// <para>The check is the one the format itself provides: a stream that was
/// read exactly as it was written leaves the arithmetic decoder in its starting
/// state, and nothing else does.</para>
/// </remarks>
[TestFixture]
internal sealed class JxlMultiGroupStreamTests {

  private static byte[] _Fixture(string name) {
    var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", name);
    Assert.That(File.Exists(path), Is.True, $"Test fixture missing: {path}");
    return File.ReadAllBytes(path);
  }

  /// <summary>
  /// A 260x32 lossy file cjxl 0.12.0 wrote. It is wider than one group, so its
  /// low-frequency part and its first group sit at different offsets, and it is
  /// coded with an arithmetic decoder rather than prefix codes.
  /// </summary>
  [Test]
  public void TheDcOfAFrameInTwoGroupsIsReadFromTheGroupsOwnOffset() {
    var data = _Fixture("cjxl_two_groups_lossy.jxl");

    var reader = new JxlBitReader(data, 2);
    var (width, height) = JxlSizeHeader.Decode(reader);
    var metadata = JxlImageMetadata.Decode(reader);
    JxlCustomTransformData.Decode(reader, metadata.XybEncoded);
    reader.ZeroPadToByte();
    var frame = JxlSpecFrameHeader.Decode(reader, metadata);

    var groupDim = 128 << (int)frame.GroupSizeShift;
    var numGroups = ((width + groupDim - 1) / groupDim) * ((height + groupDim - 1) / groupDim);
    var lfGroupDim = groupDim * 8;
    var numDcGroups = ((width + lfGroupDim - 1) / lfGroupDim) * ((height + lfGroupDim - 1) / lfGroupDim);
    Assert.That(numGroups, Is.EqualTo(2), "the fixture has to be in more than one group to test anything");

    var toc = JxlFrameToc.Decode(reader, numGroups, (int)frame.NumPasses, numDcGroups);
    var frameBody = (int)(reader.BitsRead / 8);

    // The frame's first section: quantization, the block context map, the DC
    // colour correlation, and the histograms every later section shares.
    JxlFrameQuantizer.ReadDcQuantization(reader);
    JxlFrameQuantizer.ReadQuantizerParams(reader);
    JxlBlockContextMap.Decode(reader);
    JxlColorCorrelationMap.DecodeDc(reader);
    var dcWidth = (width + 7) / 8;
    var dcHeight = (height + 7) / 8;
    var (tree, entropy) = JxlModularSpecDecoder.DecodeGlobalInfo(reader, (uint)Math.Max(dcWidth, 3));
    Assert.That(tree, Is.Not.Null, "the fixture has to share one tree across its sections");

    var lfGlobalUsed = (int)(reader.BitsRead / 8) - frameBody;
    Assert.That(lfGlobalUsed, Is.LessThanOrEqualTo(toc.SectionSizes[0]).And.GreaterThan(toc.SectionSizes[0] - 8),
      "the first section is read to its end, give or take its padding");

    // The DC group is the frame's second section, at its own offset, and opens
    // with two bits of extra precision.
    var dcGroupAt = frameBody + toc.SectionOffsets[1];
    var dcReader = new JxlBitReader(data, dcGroupAt);
    dcReader.ReadBits(2);

    // Throws unless the whole stream was read exactly as written.
    var dc = JxlModularSpecDecoder.DecodeGroup(
      dcReader, dcWidth, dcHeight, numChannels: 3,
      bitDepth: (int)metadata.BitDepth.BitsPerSample,
      globalTree: tree, globalEntropy: entropy, streamId: 1);

    var used = (int)(dcReader.BitsRead / 8) - dcGroupAt;
    Assert.Multiple(() => {
      Assert.That(dc.Channels, Has.Length.EqualTo(3));
      Assert.That(used, Is.GreaterThan(0).And.LessThanOrEqualTo(toc.SectionSizes[1]),
        "the DC has to fit inside the section it was seeked to");
      // Reading from the wrong offset used to land in padding, which costs
      // almost nothing per sample and gives back a flat plane.
      Assert.That(dc.Channels[1].Pixels.Distinct().Count(), Is.GreaterThan(1),
        "a picture's DC is not one value repeated");
    });
  }

  /// <summary>The arithmetic decoder takes its bits from wherever it was last
  /// pointed, not from wherever it was built.</summary>
  [Test]
  public void TheArithmeticDecoderReadsFromWhereverItWasLastPointed() {
    var first = new JxlBitReader([0x11, 0x22, 0x33, 0x44], 0);
    var second = new JxlBitReader([0xAA, 0xBB, 0xCC, 0xDD], 0);

    var decoder = new JxlAnsDecoder(first);
    decoder.Rebind(second);
    decoder.Init();

    Assert.That(decoder.State, Is.EqualTo(0xDDCCBBAAu), "the state came from the stream it was pointed at");
  }
}
