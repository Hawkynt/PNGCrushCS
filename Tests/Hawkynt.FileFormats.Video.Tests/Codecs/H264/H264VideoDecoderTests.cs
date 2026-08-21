using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FileFormat.Core;
using FileFormat.H264Video;

namespace FileFormat.Codecs.H264.Tests;

/// <summary>
/// The H.264 decoder against streams built here, where every bit is known.
/// </summary>
/// <remarks>
/// The arithmetic — prediction, dequantisation, the transform, the interpolator and the loop filter —
/// was checked against ffmpeg over a corpus of encoded streams, sample by sample, and not here: no
/// test that writes its own bitstream can tell whether the decoder and the test agree with the
/// standard or only with each other. What these do check is the two things that comparison cannot
/// reach — the boundary of what is refused, and the handful of syntactic paths a real encoder never
/// emits.
/// </remarks>
[TestFixture]
public sealed class H264VideoDecoderTests {

  private static readonly MediaStreamInfo _AnnexBStream = new() {
    Index = 0,
    Kind = MediaStreamKind.Video,
    Codec = CodecTag.FromCharacters("avc1"),
  };

  /// <summary>Decodes a byte stream the way a container would hand it over: one packet per access unit.</summary>
  private static List<RawImage> _Decode(byte[] stream) {
    var decoder = H264VideoDecoder.Create(_AnnexBStream);
    var frames = new List<RawImage>();

    foreach (var packet in H264VideoReader.Split(stream))
      if (decoder.TryDecode(packet, out var frame))
        frames.Add(frame);

    frames.AddRange(decoder.Flush());
    return frames;
  }

  /// <summary>
  /// Hands the whole stream over as one packet, without the container cutting it into access units.
  /// </summary>
  /// <remarks>
  /// For the units a container would never make a packet of at all. An access unit is a picture, so a
  /// stream of parameter sets and nothing else yields no packets and reaches no decoder — which is
  /// right for the container and useless for testing what the decoder does with such a unit.
  /// </remarks>
  private static void _DecodeWholeStreamAsOnePacket(byte[] stream)
    => H264VideoDecoder.Create(_AnnexBStream).TryDecode(new(0, stream), out _);

  /// <summary>A stream of one intra picture whose single macroblock predicts flat and codes nothing.</summary>
  private static byte[] _OneFlatIntraPicture()
    => new H264TestStream()
      .SequenceParameterSet()
      .PictureParameterSet()
      .BeginIdrSliceHeader()
      .FlatIntra16x16Macroblock()
      .EndNal(5, 3)
      .ToArray();

  // ==============================================================================================
  // What it decodes
  // ==============================================================================================

  [Test]
  public void OneIntraMacroblockDecodesToTheMidGreyItsPredictionSpecifies() {
    var frames = _Decode(_OneFlatIntraPicture());

    Assert.That(frames, Has.Count.EqualTo(1));
    Assert.That(frames[0].Width, Is.EqualTo(16));
    Assert.That(frames[0].Height, Is.EqualTo(16));

    // With no neighbours the Intra_16x16 DC prediction is 1 << (BitDepth − 1) for luma and for both
    // chroma components (clauses 8.3.3 and 8.3.4.1), the coded block pattern is zero so nothing is
    // added, and the deblocking filter finds no step to remove. Y 128 with neutral chrominance is
    // 130 in studio-swing BT.601, and every sample of the picture is that.
    var pixels = frames[0].PixelData;
    Assert.That(pixels, Has.Length.EqualTo(16 * 16 * 3));
    Assert.That(pixels, Is.All.EqualTo(130));
  }

  [Test]
  public void ASkippedPredictedPictureRepeatsItsReference() {
    var stream = new H264TestStream()
      .SequenceParameterSet()
      .PictureParameterSet()
      .BeginIdrSliceHeader()
      .FlatIntra16x16Macroblock()
      .EndNal(5, 3)
      .BeginSliceHeader(frameNum: 1)
      .Unsigned(1) // mb_skip_run: the picture's one macroblock, skipped
      .EndNal(1, 2)
      .ToArray();

    var frames = _Decode(stream);

    Assert.That(frames, Has.Count.EqualTo(2));
    Assert.That(frames[1].PixelData, Is.EqualTo(frames[0].PixelData));
  }

  [Test]
  public void APictureLargerThanOneMacroblockIsDecodedWhole() {
    var builder = new H264TestStream()
      .SequenceParameterSet(widthInMbs: 3, heightInMbs: 2)
      .PictureParameterSet()
      .BeginIdrSliceHeader();

    for (var macroblock = 0; macroblock < 6; ++macroblock)
      builder.FlatIntra16x16Macroblock();

    var frames = _Decode(builder.EndNal(5, 3).ToArray());

    Assert.That(frames, Has.Count.EqualTo(1));
    Assert.That(frames[0].Width, Is.EqualTo(48));
    Assert.That(frames[0].Height, Is.EqualTo(32));
  }

  /// <summary>A macroblock of four quadrants, so a test can tell where a sample landed.</summary>
  private static byte _Quadrants(int x, int y) => (x < 8) == (y < 8) ? (byte)16 : (byte)235;

  [Test]
  public void PcmSamplesReachThePictureVerbatimAndUnfiltered() {
    // I_PCM carries its samples rather than coding them, and its quantisation parameter is zero by
    // definition — which sets the deblocking filter's thresholds to zero and leaves it alone. So the
    // picture is exactly what was written, which also says where each sample landed.
    var frames = _Decode(new H264TestStream()
      .SequenceParameterSet()
      .PictureParameterSet()
      .BeginIdrSliceHeader()
      .PcmMacroblock(25, _Quadrants, chroma: 128)
      .EndNal(5, 3)
      .ToArray());

    Assert.That(frames, Has.Count.EqualTo(1));

    var pixels = frames[0].PixelData;
    for (var y = 0; y < 16; ++y)
      for (var x = 0; x < 16; ++x) {
        // Luma 16 with neutral chrominance is black in studio-swing BT.601, and 235 is white.
        var expected = _Quadrants(x, y) == 16 ? 0 : 255;
        Assert.That(pixels[(y * 16 + x) * 3], Is.EqualTo(expected), $"({x}, {y})");
      }
  }

  [Test]
  public void ReferenceListReorderingDecidesWhichPictureAnIndexNames() {
    // Two references that can be told apart, and a slice that reorders the older one to the front.
    // Without the reordering the list is in descending picture number, so index zero would be the
    // newer picture and the last frame would be white.
    var frames = _Decode(new H264TestStream()
      .SequenceParameterSet(maxRefFrames: 2)
      .PictureParameterSet()
      .BeginIdrSliceHeader().PcmMacroblock(25, static (_, _) => 16, chroma: 128).EndNal(5, 3)
      .BeginSliceHeader(frameNum: 1).SkipRun(0).PcmMacroblock(30, static (_, _) => 235, chroma: 128).EndNal(1, 2)
      .BeginSliceHeader(frameNum: 2, activeRefs: 2, reorderBy: 2).Unsigned(1).EndNal(1, 2)
      .ToArray());

    Assert.That(frames, Has.Count.EqualTo(3));
    Assert.That(frames[0].PixelData, Is.All.EqualTo(0));
    Assert.That(frames[1].PixelData, Is.All.EqualTo(255));

    // The skipped macroblock copies index zero, which the reordering made the first picture.
    Assert.That(frames[2].PixelData, Is.EqualTo(frames[0].PixelData));
  }

  [Test]
  public void MarkingAPictureUnusedTakesItOutOfTheReferenceList() {
    // Three references, the middle one marked unused as the third picture is decoded. The fourth
    // picture then names index one, which is the first picture — where without the marking the list
    // would still hold the second and index one would be white.
    var frames = _Decode(new H264TestStream()
      .SequenceParameterSet(maxRefFrames: 3)
      .PictureParameterSet()
      .BeginIdrSliceHeader().PcmMacroblock(25, static (_, _) => 16, chroma: 128).EndNal(5, 3)
      .BeginSliceHeader(frameNum: 1).SkipRun(0).PcmMacroblock(30, static (_, _) => 235, chroma: 128).EndNal(1, 2)
      .BeginSliceHeader(frameNum: 2, markUnusedAt: 1).Unsigned(1).EndNal(1, 2)
      .BeginSliceHeader(frameNum: 3, activeRefs: 2).SkipRun(0).InterMacroblockCopying(refIdx: 1, activeRefs: 2).EndNal(1, 2)
      .ToArray());

    Assert.That(frames, Has.Count.EqualTo(4));
    Assert.That(frames[2].PixelData, Is.All.EqualTo(255)); // index zero was still the second picture
    Assert.That(frames[3].PixelData, Is.All.EqualTo(0));
  }

  [Test]
  public void AReferenceIndexTheListDoesNotHoldIsRefusedRatherThanGuessed() {
    // One reference in the buffer and a macroblock naming the second entry. The standard leaves that
    // entry undefined, so there is nothing to predict from and nothing to invent.
    var stream = new H264TestStream()
      .SequenceParameterSet(maxRefFrames: 2)
      .PictureParameterSet()
      .BeginIdrSliceHeader().PcmMacroblock(25, static (_, _) => 16, chroma: 128).EndNal(5, 3)
      .BeginSliceHeader(frameNum: 1, activeRefs: 2).SkipRun(0).InterMacroblockCopying(refIdx: 1, activeRefs: 2).EndNal(1, 2)
      .ToArray();

    Assert.That(() => _Decode(stream),
      Throws.TypeOf<InvalidDataException>().With.Message.Contains("reference index 1"));
  }

  [Test]
  public void AReorderingNamingAPictureTheStreamNeverSentIsRefused() {
    var stream = new H264TestStream()
      .SequenceParameterSet(maxRefFrames: 2)
      .PictureParameterSet()
      .BeginIdrSliceHeader().PcmMacroblock(25, static (_, _) => 16, chroma: 128).EndNal(5, 3)
      .BeginSliceHeader(frameNum: 1, activeRefs: 1, reorderBy: 4).Unsigned(1).EndNal(1, 2)
      .ToArray();

    Assert.That(() => _Decode(stream),
      Throws.TypeOf<InvalidDataException>().With.Message.Contains("reorders its reference picture list"));
  }

  [Test]
  public void AGapInTheReferenceFrameNumberingIsRefusedRatherThanDecodedThrough() {
    var stream = new H264TestStream()
      .SequenceParameterSet(maxRefFrames: 2)
      .PictureParameterSet()
      .BeginIdrSliceHeader().PcmMacroblock(25, static (_, _) => 16, chroma: 128).EndNal(5, 3)
      .BeginSliceHeader(frameNum: 3).Unsigned(1).EndNal(1, 2) // 1 was due
      .ToArray();

    Assert.That(() => _Decode(stream),
      Throws.TypeOf<InvalidDataException>().With.Message.Contains("frame_num jumps from 0 to 3"));
  }

  [Test]
  public void TheSamePictureDecodesTheSameFromBothDeliveryForms() {
    var annexB = _OneFlatIntraPicture();

    // The same NAL units with a four-byte length in front of each instead of a start code, and an
    // AVCDecoderConfigurationRecord carrying the parameter sets — which is how MP4, Matroska and FLV
    // carry exactly these bytes.
    var units = _SplitAnnexB(annexB);
    var configuration = _ConfigurationRecord(units[0], units[1]);
    var sample = _LengthPrefixed(units[2]);

    var decoder = H264VideoDecoder.Create(new() {
      Index = 0,
      Kind = MediaStreamKind.Video,
      Codec = CodecTag.FromCharacters("avc1"),
      CodecPrivateData = configuration,
    });

    Assert.That(decoder.TryDecode(new(0, sample), out var lengthPrefixed), Is.True);
    Assert.That(lengthPrefixed.PixelData, Is.EqualTo(_Decode(annexB)[0].PixelData));
  }

  // ==============================================================================================
  // What it refuses, and by what name
  // ==============================================================================================

  [Test]
  public void ArithmeticCodingIsRefusedByName() {
    var stream = new H264TestStream()
      .SequenceParameterSet()
      .PictureParameterSet(cabac: true)
      .BeginIdrSliceHeader()
      .FlatIntra16x16Macroblock()
      .EndNal(5, 3)
      .ToArray();

    Assert.That(() => _Decode(stream), Throws.TypeOf<NotSupportedException>().With.Message.Contains("CABAC"));
  }

  [Test]
  public void BidirectionalSlicesAreRefusedByName() {
    var stream = new H264TestStream()
      .SequenceParameterSet()
      .PictureParameterSet()
      .BeginIdrSliceHeader()
      .FlatIntra16x16Macroblock()
      .EndNal(5, 3)
      .BeginSliceHeader(frameNum: 1, sliceType: 6) // B, all slices of the picture
      .Unsigned(0)
      .EndNal(1, 2)
      .ToArray();

    Assert.That(() => _Decode(stream), Throws.TypeOf<NotSupportedException>().With.Message.Contains("B slice"));
  }

  [Test]
  public void SwitchingSlicesAreRefusedByName() {
    var stream = new H264TestStream()
      .SequenceParameterSet()
      .PictureParameterSet()
      .BeginIdrSliceHeader(sliceType: 9) // SI
      .EndNal(5, 3)
      .ToArray();

    Assert.That(() => _Decode(stream), Throws.TypeOf<NotSupportedException>().With.Message.Contains("switching slice"));
  }

  [Test]
  public void FlexibleMacroblockOrderingIsRefusedByName() {
    var stream = new H264TestStream()
      .SequenceParameterSet()
      .PictureParameterSet(sliceGroups: 2)
      .BeginIdrSliceHeader()
      .FlatIntra16x16Macroblock()
      .EndNal(5, 3)
      .ToArray();

    Assert.That(() => _Decode(stream),
      Throws.TypeOf<NotSupportedException>().With.Message.Contains("slice groups"));
  }

  [Test]
  public void WeightedPredictionIsRefusedByName() {
    var stream = new H264TestStream()
      .SequenceParameterSet()
      .PictureParameterSet(weightedPrediction: true)
      .BeginIdrSliceHeader()
      .FlatIntra16x16Macroblock()
      .EndNal(5, 3)
      .ToArray();

    Assert.That(() => _Decode(stream),
      Throws.TypeOf<NotSupportedException>().With.Message.Contains("weighted_pred_flag"));
  }

  [TestCase(0, "monochrome")]
  [TestCase(2, "4:2:2")]
  [TestCase(3, "4:4:4")]
  public void ChromaFormatsOtherThanFourTwoZeroAreRefusedByName(int chromaFormatIdc, string named) {
    var stream = new H264TestStream()
      .HighSequenceParameterSet(chromaFormatIdc)
      .PictureParameterSet()
      .BeginIdrSliceHeader()
      .FlatIntra16x16Macroblock()
      .EndNal(5, 3)
      .ToArray();

    Assert.That(() => _Decode(stream), Throws.TypeOf<NotSupportedException>().With.Message.Contains(named));
  }

  [Test]
  public void SampleDepthsAboveEightAreRefusedByName() {
    var stream = new H264TestStream()
      .HighSequenceParameterSet(bitDepth: 10)
      .PictureParameterSet()
      .BeginIdrSliceHeader()
      .FlatIntra16x16Macroblock()
      .EndNal(5, 3)
      .ToArray();

    Assert.That(() => _Decode(stream), Throws.TypeOf<NotSupportedException>().With.Message.Contains("10-bit"));
  }

  [Test]
  public void ScalingMatricesAreRefusedByName() {
    var stream = new H264TestStream()
      .HighSequenceParameterSet(scalingMatrices: true)
      .PictureParameterSet()
      .BeginIdrSliceHeader()
      .FlatIntra16x16Macroblock()
      .EndNal(5, 3)
      .ToArray();

    Assert.That(() => _Decode(stream),
      Throws.TypeOf<NotSupportedException>().With.Message.Contains("scaling matrices"));
  }

  [Test]
  public void TransformBypassIsRefusedByName() {
    var stream = new H264TestStream()
      .HighSequenceParameterSet(transformBypass: true)
      .PictureParameterSet()
      .BeginIdrSliceHeader()
      .FlatIntra16x16Macroblock()
      .EndNal(5, 3)
      .ToArray();

    Assert.That(() => _Decode(stream),
      Throws.TypeOf<NotSupportedException>().With.Message.Contains("qpprime_y_zero_transform_bypass_flag"));
  }

  [Test]
  public void InterlacedCodingIsRefusedByName() {
    var stream = new H264TestStream()
      .InterlacedSequenceParameterSet()
      .PictureParameterSet()
      .BeginIdrSliceHeader()
      .FlatIntra16x16Macroblock()
      .EndNal(5, 3)
      .ToArray();

    Assert.That(() => _Decode(stream),
      Throws.TypeOf<NotSupportedException>().With.Message.Contains("frame_mbs_only_flag"));
  }

  [Test]
  public void SliceDataPartitioningIsRefusedByName() {
    var stream = new H264TestStream()
      .SequenceParameterSet()
      .PictureParameterSet()
      .BeginIdrSliceHeader()
      .FlatIntra16x16Macroblock()
      .EndNal(2, 3) // a slice data partition A
      .ToArray();

    Assert.That(() => _Decode(stream),
      Throws.TypeOf<NotSupportedException>().With.Message.Contains("slice data partitioning"));
  }

  [Test]
  public void ScalableAndMultiviewExtensionsAreRefusedByName() {
    var stream = new H264TestStream()
      .SequenceParameterSet()
      .PictureParameterSet()
      .Bits(0, 24) // the three extra header bytes of a prefix NAL unit
      .Bits(0, 8)
      .EndNal(14, 3)
      .ToArray();

    Assert.That(() => _DecodeWholeStreamAsOnePacket(stream),
      Throws.TypeOf<NotSupportedException>().With.Message.Contains("multiview"));
  }

  // ==============================================================================================
  // What it refuses as malformed
  // ==============================================================================================

  [Test]
  public void ASliceNamingAParameterSetTheStreamNeverSentIsRefused() {
    var stream = new H264TestStream()
      .SequenceParameterSet()
      .BeginIdrSliceHeader()
      .FlatIntra16x16Macroblock()
      .EndNal(5, 3)
      .ToArray();

    Assert.That(() => _Decode(stream),
      Throws.TypeOf<InvalidDataException>().With.Message.Contains("picture parameter set"));
  }

  [Test]
  public void APictureWhoseSlicesLeaveMacroblocksUncodedIsRefused() {
    // Two macroblocks declared, one coded: the picture has a hole in it, and a hole is not a decode.
    var stream = new H264TestStream()
      .SequenceParameterSet(widthInMbs: 2)
      .PictureParameterSet()
      .BeginIdrSliceHeader()
      .FlatIntra16x16Macroblock()
      .EndNal(5, 3)
      .ToArray();

    Assert.That(() => _Decode(stream),
      Throws.TypeOf<InvalidDataException>().With.Message.Contains("covered by no slice"));
  }

  [Test]
  public void APredictedSliceWithNoReferenceInTheBufferIsRefused() {
    // A P slice reached without an IDR before it: decoding began in the middle of the stream.
    var stream = new H264TestStream()
      .SequenceParameterSet()
      .PictureParameterSet()
      .BeginSliceHeader(frameNum: 3)
      .Unsigned(1)
      .EndNal(1, 2)
      .ToArray();

    Assert.That(() => _Decode(stream),
      Throws.TypeOf<InvalidDataException>().With.Message.Contains("no reference picture"));
  }

  [Test]
  public void ANalUnitWithItsForbiddenBitSetIsRefused() {
    byte[] stream = [0x00, 0x00, 0x00, 0x01, 0x87, 0x42, 0x00, 0x0A, 0x80];

    Assert.That(() => _DecodeWholeStreamAsOnePacket(stream),
      Throws.TypeOf<InvalidDataException>().With.Message.Contains("forbidden_zero_bit"));
  }

  // ==============================================================================================
  // Identity
  // ==============================================================================================

  [Test]
  public void ItAnswersToTheCodesEveryContainerNamesItWith() {
    foreach (var code in new[] { "avc1", "avc3", "H264", "X264", "DAVC", "VSSH" })
      Assert.That(
        H264VideoDecoder.Accepts(new() { Index = 0, Kind = MediaStreamKind.Video, Codec = CodecTag.FromCharacters(code) }),
        Is.True, code);

    Assert.That(
      H264VideoDecoder.Accepts(new() { Index = 0, Kind = MediaStreamKind.Video, CodecId = "V_MPEG4/ISO/AVC" }),
      Is.True);
  }

  [Test]
  public void ItDoesNotAnswerToAnotherCodecsCode() {
    Assert.That(
      H264VideoDecoder.Accepts(new() { Index = 0, Kind = MediaStreamKind.Video, Codec = CodecTag.FromCharacters("MPG1") }),
      Is.False);

    // Nor to an audio stream, whatever its code says.
    Assert.That(
      H264VideoDecoder.Accepts(new() { Index = 0, Kind = MediaStreamKind.Audio, Codec = CodecTag.FromCharacters("avc1") }),
      Is.False);
  }

  [Test]
  public void ItHoldsNoPictureBackBecauseItRefusesTheSlicesThatWouldReorderOne() {
    var decoder = H264VideoDecoder.Create(_AnnexBStream);
    foreach (var packet in H264VideoReader.Split(_OneFlatIntraPicture()))
      decoder.TryDecode(packet, out _);

    Assert.That(decoder.Flush(), Is.Empty);
  }

  // ==============================================================================================
  // Helpers for the length-prefixed form
  // ==============================================================================================

  private static byte[][] _SplitAnnexB(byte[] stream) {
    var units = new List<byte[]>();
    var starts = new List<int>();
    for (var at = 0; at + 3 < stream.Length; ++at)
      if (stream[at] == 0 && stream[at + 1] == 0 && stream[at + 2] == 1)
        starts.Add(at + 3);

    for (var i = 0; i < starts.Count; ++i) {
      var end = i + 1 < starts.Count ? starts[i + 1] - 3 : stream.Length;
      while (end > starts[i] && stream[end - 1] == 0)
        --end;

      units.Add(stream[starts[i]..end]);
    }

    return [.. units];
  }

  private static byte[] _ConfigurationRecord(byte[] sequenceSet, byte[] pictureSet) {
    var record = new List<byte> {
      1, sequenceSet[1], sequenceSet[2], sequenceSet[3], // version, profile, compatibility, level
      0xFF, // six reserved bits and lengthSizeMinusOne 3, so four-byte lengths
      0xE1, // three reserved bits and one sequence parameter set
      (byte)(sequenceSet.Length >> 8), (byte)sequenceSet.Length,
    };

    record.AddRange(sequenceSet);
    record.Add(1);
    record.Add((byte)(pictureSet.Length >> 8));
    record.Add((byte)pictureSet.Length);
    record.AddRange(pictureSet);
    return [.. record];
  }

  private static byte[] _LengthPrefixed(byte[] unit) {
    var sample = new byte[unit.Length + 4];
    sample[0] = (byte)(unit.Length >> 24);
    sample[1] = (byte)(unit.Length >> 16);
    sample[2] = (byte)(unit.Length >> 8);
    sample[3] = (byte)unit.Length;
    unit.CopyTo(sample, 4);
    return sample;
  }
}
