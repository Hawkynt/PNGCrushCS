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
/// Codec reconstruction is tested in its native YUV domain. Tests that intentionally describe
/// display RGB use <see cref="RawImageConverter"/> at the same boundary a writer/viewer now uses.
/// </remarks>
[TestFixture]
public sealed class H264VideoDecoderTests {

  private static readonly MediaStreamInfo _AnnexBStream = new() {
    Index = 0,
    Kind = MediaStreamKind.Video,
    Codec = CodecTag.FromCharacters("avc1"),
  };

  private static List<RawImage> _DecodeNative(byte[] stream) {
    var decoder = H264VideoDecoder.Create(_AnnexBStream);
    var frames = new List<RawImage>();

    foreach (var packet in H264VideoReader.Split(stream))
      if (decoder.TryDecode(packet, out var frame))
        frames.Add(frame);

    frames.AddRange(decoder.Flush());
    return frames;
  }

  /// <summary>
  /// Existing display-oriented assertions deliberately convert here rather than forcing that
  /// conversion back into the decoder.
  /// </summary>
  private static List<RawImage> _Decode(byte[] stream)
    => _DecodeNative(stream)
      .Select(static frame => RawImageConverter.Convert(frame, PixelFormat.Rgb24))
      .ToList();

  private static void _DecodeWholeStreamAsOnePacket(byte[] stream)
    => H264VideoDecoder.Create(_AnnexBStream).TryDecode(new(0, stream), out _);

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
  public void DecoderReturnsTheReconstructedPlanesWithoutAnRgbRoundTrip() {
    var frames = _DecodeNative(_OneFlatIntraPicture());

    Assert.That(frames, Has.Count.EqualTo(1));
    var frame = frames[0];
    Assert.That(frame.Format, Is.EqualTo(PixelFormat.Yuv420P8));
    Assert.That(frame.PlaneCount, Is.EqualTo(3));
    Assert.That(frame.GetPlaneData(0).ToArray(), Is.All.EqualTo(128));
    Assert.That(frame.GetPlaneData(1).ToArray(), Is.All.EqualTo(128));
    Assert.That(frame.GetPlaneData(2).ToArray(), Is.All.EqualTo(128));
    Assert.That(frame.ColorInfo!.Range, Is.EqualTo(RawColorRange.Limited));
    Assert.That(frame.ColorInfo.Matrix, Is.EqualTo(RawMatrixCoefficients.Bt601));
  }

  [Test]
  public void OneIntraMacroblockDecodesToTheMidGreyItsPredictionSpecifies() {
    var frames = _Decode(_OneFlatIntraPicture());

    Assert.That(frames, Has.Count.EqualTo(1));
    Assert.That(frames[0].Width, Is.EqualTo(16));
    Assert.That(frames[0].Height, Is.EqualTo(16));

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
      .Unsigned(1)
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

  private static byte _Quadrants(int x, int y) => (x < 8) == (y < 8) ? (byte)16 : (byte)235;

  [Test]
  public void PcmSamplesReachThePictureVerbatimAndUnfiltered() {
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
        var expected = _Quadrants(x, y) == 16 ? 0 : 255;
        Assert.That(pixels[(y * 16 + x) * 3], Is.EqualTo(expected), $"({x}, {y})");
      }
  }

  [Test]
  public void ReferenceListReorderingDecidesWhichPictureAnIndexNames() {
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
    Assert.That(frames[2].PixelData, Is.EqualTo(frames[0].PixelData));
  }

  [Test]
  public void MarkingAPictureUnusedTakesItOutOfTheReferenceList() {
    var frames = _Decode(new H264TestStream()
      .SequenceParameterSet(maxRefFrames: 3)
      .PictureParameterSet()
      .BeginIdrSliceHeader().PcmMacroblock(25, static (_, _) => 16, chroma: 128).EndNal(5, 3)
      .BeginSliceHeader(frameNum: 1).SkipRun(0).PcmMacroblock(30, static (_, _) => 235, chroma: 128).EndNal(1, 2)
      .BeginSliceHeader(frameNum: 2, markUnusedAt: 1).Unsigned(1).EndNal(1, 2)
      .BeginSliceHeader(frameNum: 3, activeRefs: 2).SkipRun(0).InterMacroblockCopying(refIdx: 1, activeRefs: 2).EndNal(1, 2)
      .ToArray());

    Assert.That(frames, Has.Count.EqualTo(4));
    Assert.That(frames[2].PixelData, Is.All.EqualTo(255));
    Assert.That(frames[3].PixelData, Is.All.EqualTo(0));
  }

  [Test]
  public void AReferenceIndexTheListDoesNotHoldIsRefusedRatherThanGuessed() {
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
      .BeginSliceHeader(frameNum: 3).Unsigned(1).EndNal(1, 2)
      .ToArray();

    Assert.That(() => _Decode(stream),
      Throws.TypeOf<InvalidDataException>().With.Message.Contains("frame_num jumps from 0 to 3"));
  }

  [Test]
  public void TheSamePictureDecodesTheSameFromBothDeliveryForms() {
    var annexB = _OneFlatIntraPicture();
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
    var rgb = RawImageConverter.Convert(lengthPrefixed, PixelFormat.Rgb24);
    Assert.That(rgb.PixelData, Is.EqualTo(_Decode(annexB)[0].PixelData));
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
      .BeginSliceHeader(frameNum: 1, sliceType: 6)
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
      .BeginIdrSliceHeader(sliceType: 9)
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

  /// <summary>
  /// Scaling matrices used to be refused by name; they are parsed and applied now, so a stream
  /// carrying them decodes rather than throwing.
  /// </summary>
  [Test]
  public void ScalingMatricesAreDecodedRatherThanRefused() {
    var stream = new H264TestStream()
      .HighSequenceParameterSet(scalingMatrices: true)
      .PictureParameterSet()
      .BeginIdrSliceHeader()
      .FlatIntra16x16Macroblock()
      .EndNal(5, 3)
      .ToArray();

    Assert.That(() => _Decode(stream), Throws.Nothing);
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
      .EndNal(2, 3)
      .ToArray();

    Assert.That(() => _Decode(stream),
      Throws.TypeOf<NotSupportedException>().With.Message.Contains("slice data partitioning"));
  }

  [Test]
  public void ScalableAndMultiviewExtensionsAreRefusedByName() {
    var stream = new H264TestStream()
      .SequenceParameterSet()
      .PictureParameterSet()
      .Bits(0, 24)
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
      1, sequenceSet[1], sequenceSet[2], sequenceSet[3],
      0xFF,
      0xE1,
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