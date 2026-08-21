using System;
using System.IO;
using System.Linq;
using FileFormat.Codecs;
using FileFormat.Core;
using FileFormat.H265Video;

namespace FileFormat.Codecs.H265.Tests;

/// <summary>
/// The H.265 decoder's boundary: which streams it reads, and which it refuses and with what words.
/// </summary>
/// <remarks>
/// The arithmetic — prediction, dequantisation, the two transforms, the interpolator and both loop
/// filters — was checked against a reference decoder over a corpus of encoded streams, plane by plane
/// and frame by frame, and not here. No test that writes its own bitstream can tell whether the
/// decoder and the test agree with the standard or only with each other.
/// <para/>
/// What these check is the half that comparison cannot reach: that a stream this decoder cannot read
/// is refused rather than decoded into a plausible picture, and that the refusal names the syntax
/// element responsible. That is the specific failure this decoder exists to replace — its predecessor
/// reported success while returning a picture that was almost entirely zero.
/// </remarks>
[TestFixture]
public sealed class H265VideoDecoderTests {

  private static readonly MediaStreamInfo _ByteStream = new() {
    Index = 0,
    Kind = MediaStreamKind.Video,
    Codec = CodecTag.FromCharacters("hvc1"),
  };

  private static Exception? _Decode(byte[] stream) {
    try {
      var decoder = H265VideoDecoder.Create(_ByteStream);
      decoder.TryDecode(new(0, stream), out _);
      foreach (var _ in decoder.Flush()) {
        // Drain, so that a refusal raised while a held picture is handed out is seen here too.
      }

      return null;
    } catch (Exception e) {
      return e;
    }
  }

  private static string _Refusal(byte[] stream) {
    var failure = _Decode(stream);
    Assert.That(failure, Is.Not.Null, "the stream was decoded rather than refused");
    Assert.That(failure, Is.InstanceOf<NotSupportedException>().Or.InstanceOf<InvalidDataException>());
    return failure!.Message;
  }

  // ==============================================================================================
  // Which streams reach this decoder at all
  // ==============================================================================================

  [Test]
  [Category("Unit")]
  public void EverySpellingOfHevcIsAccepted() {
    foreach (var code in new[] { "hvc1", "hev1", "hvc2", "hev2", "HEVC", "H265", "h265" })
      Assert.That(
        H265VideoDecoder.Accepts(new() { Index = 0, Kind = MediaStreamKind.Video, Codec = CodecTag.FromCharacters(code) }),
        Is.True, code);

    Assert.That(
      H265VideoDecoder.Accepts(new() { Index = 0, Kind = MediaStreamKind.Video, CodecId = "V_MPEGH/ISO/HEVC" }), Is.True);
  }

  [Test]
  [Category("Unit")]
  public void AnAvcStreamIsNotClaimed() {
    Assert.That(
      H265VideoDecoder.Accepts(new() { Index = 0, Kind = MediaStreamKind.Video, Codec = CodecTag.FromCharacters("avc1") }),
      Is.False);

    Assert.That(
      H265VideoDecoder.Accepts(new() { Index = 0, Kind = MediaStreamKind.Audio, Codec = CodecTag.FromCharacters("hvc1") }),
      Is.False);
  }

  // ==============================================================================================
  // The coded formats that are refused, each by the field that says so
  // ==============================================================================================

  [TestCase(0, "monochrome")]
  [TestCase(2, "4:2:2")]
  [TestCase(3, "4:4:4")]
  [Category("Unit")]
  public void AChromaFormatOtherThan420_IsRefusedByName(int chromaFormatIdc, string expected) {
    var message = _Refusal(new H265TestStream()
      .VideoParameterSet()
      .SequenceParameterSet(chromaFormatIdc: chromaFormatIdc)
      .ToArray());

    Assert.That(message, Does.Contain(expected));
    Assert.That(message, Does.Contain("4:2:0"));
  }

  [Test]
  [Category("Unit")]
  public void TenBitSamples_AreRefusedByTheirDepth() {
    var message = _Refusal(new H265TestStream()
      .VideoParameterSet()
      .SequenceParameterSet(bitDepthLuma: 10, bitDepthChroma: 10, profileIdc: 2)
      .ToArray());

    Assert.That(message, Does.Contain("10-bit"));
    Assert.That(message, Does.Contain("eight bits"));
  }

  [Test]
  [Category("Unit")]
  public void SeparatelyCodedColourPlanes_AreRefusedByTheirFlag() {
    var message = _Refusal(new H265TestStream()
      .VideoParameterSet()
      .SequenceParameterSet(chromaFormatIdc: 3, separateColourPlanes: true)
      .ToArray());

    Assert.That(message, Does.Contain("separate_colour_plane_flag"));
  }

  [Test]
  [Category("Unit")]
  public void TheFormatRangeExtensions_AreRefusedByTheFlagThatIsSet() {
    var message = _Refusal(new H265TestStream()
      .VideoParameterSet()
      .SequenceParameterSet(rangeExtension: true, profileIdc: 4)
      .ToArray());

    Assert.That(message, Does.Contain("transform_skip_rotation_enabled_flag"));
  }

  [Test]
  [Category("Unit")]
  public void TheScreenContentExtensions_AreRefusedByName() {
    var message = _Refusal(new H265TestStream()
      .VideoParameterSet()
      .SequenceParameterSet(screenContentExtension: true, profileIdc: 9)
      .ToArray());

    Assert.That(message, Does.Contain("screen content"));
  }

  [Test]
  [Category("Unit")]
  public void AMultilayerExtension_IsRefusedByName() {
    var message = _Refusal(new H265TestStream()
      .VideoParameterSet()
      .SequenceParameterSet(multilayerExtension: true)
      .ToArray());

    Assert.That(message, Does.Contain("multilayer"));
  }

  [Test]
  [Category("Unit")]
  public void ATiledPicture_IsRefusedWithItsGrid() {
    var message = _Refusal(new H265TestStream()
      .VideoParameterSet()
      .SequenceParameterSet()
      .PictureParameterSet(tiles: true)
      .ToArray());

    Assert.That(message, Does.Contain("tiles"));
    Assert.That(message, Does.Contain("2 by 2"));
  }

  [Test]
  [Category("Unit")]
  public void CrossComponentPrediction_IsRefusedByName() {
    var message = _Refusal(new H265TestStream()
      .VideoParameterSet()
      .SequenceParameterSet()
      .PictureParameterSet(rangeExtension: true, crossComponentPrediction: true)
      .ToArray());

    Assert.That(message, Does.Contain("cross_component_prediction_enabled_flag"));
  }

  [Test]
  [Category("Unit")]
  public void AScaledSampleAdaptiveOffset_IsRefusedByName() {
    var message = _Refusal(new H265TestStream()
      .VideoParameterSet()
      .SequenceParameterSet()
      .PictureParameterSet(rangeExtension: true, saoOffsetScale: 2)
      .ToArray());

    Assert.That(message, Does.Contain("log2_sao_offset_scale"));
  }

  [TestCase(1, "predicted", "an earlier picture")]
  [TestCase(0, "bidirectionally predicted", "an earlier and a later picture")]
  [Category("Unit")]
  public void APicturePredictedFromAnotherPicture_IsRefusedAtItsSliceType(
    int sliceType, string expected, string references) {
    var message = _Refusal(new H265TestStream()
      .VideoParameterSet()
      .SequenceParameterSet()
      .PictureParameterSet()
      .InterSliceHeader(sliceType)
      .ToArray());

    Assert.That(message, Does.Contain("slice_type"));
    Assert.That(message, Does.Contain(expected));
    Assert.That(message, Does.Contain(references));

    // The refusal has to say what is still true, or a caller cannot tell a decoder that reads part of
    // this format from one that reads none of it.
    Assert.That(message, Does.Contain("Intra pictures are decoded exactly"));
  }

  [Test]
  [Category("Unit")]
  public void APredictedSliceInsideARandomAccessPoint_IsRefusedAsMalformedRatherThanUnsupported() {
    // A refresh picture may only carry intra slices, so this stream is wrong rather than merely
    // beyond what is implemented — and the two are worth telling apart.
    var failure = _Decode(new H265TestStream()
      .VideoParameterSet()
      .SequenceParameterSet()
      .PictureParameterSet()
      .IntraSliceHeader(sliceType: 1)
      .ToArray());

    Assert.That(failure, Is.InstanceOf<InvalidDataException>());
    Assert.That(failure!.Message, Does.Contain("7.4.7.1"));
  }

  [Test]
  [Category("Unit")]
  public void ADependentSliceSegment_IsRefusedByName() {
    var message = _Refusal(new H265TestStream()
      .VideoParameterSet()
      .SequenceParameterSet()
      .PictureParameterSet(dependentSliceSegments: true)
      .IntraSliceHeader(firstSegment: false, dependent: true)
      .ToArray());

    Assert.That(message, Does.Contain("dependent_slice_segment_flag"));
  }

  [Test]
  [Category("Unit")]
  public void AnEnhancementLayerUnit_IsRefusedByItsLayer() {
    // A NAL unit header with nuh_layer_id set. Built by hand rather than through the writer, because
    // the writer only ever emits the base layer.
    byte[] stream = [0, 0, 0, 1, 0x42, 0x09, 0x00];

    var message = _Refusal(stream);
    Assert.That(message, Does.Contain("nuh_layer_id"));
  }

  // ==============================================================================================
  // What a stream without a decoder's own parameter sets does
  // ==============================================================================================

  [Test]
  [Category("Unit")]
  public void ASliceNamingAPictureParameterSetThatNeverArrived_IsRefusedWithItsNumber() {
    var message = _Refusal(new H265TestStream()
      .IntraSliceHeader()
      .ToArray());

    Assert.That(message, Does.Contain("picture parameter set 0"));
  }

  [Test]
  [Category("Unit")]
  public void APacketOfParameterSetsAlone_YieldsNoPicture() {
    var decoder = H265VideoDecoder.Create(_ByteStream);
    var stream = new H265TestStream().VideoParameterSet().SequenceParameterSet().PictureParameterSet().ToArray();

    Assert.That(decoder.TryDecode(new(0, stream), out _), Is.False);
    Assert.That(decoder.Flush(), Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void APacketThatIsNeitherAByteStreamNorLengthPrefixed_IsRefusedWithBothForms() {
    var decoder = H265VideoDecoder.Create(_ByteStream);

    var failure = Assert.Throws<InvalidDataException>(
      () => decoder.TryDecode(new(0, new byte[] { 0x40, 0x01, 0x0C, 0x01, 0xFF }), out _));

    Assert.That(failure!.Message, Does.Contain("Annex B"));
    Assert.That(failure.Message, Does.Contain("HEVCDecoderConfigurationRecord"));
  }

  [Test]
  [Category("Unit")]
  public void ANalUnitHeaderWithItsForbiddenBitSet_IsRefusedAsNotBeingOne() {
    var failure = _Decode([0, 0, 0, 1, 0xC2, 0x01, 0x00]);

    Assert.That(failure, Is.InstanceOf<InvalidDataException>());
    Assert.That(failure!.Message, Does.Contain("forbidden_zero_bit"));
  }

  // ==============================================================================================
  // The demuxer beside it
  // ==============================================================================================

  [Test]
  [Category("Unit")]
  public void AByteStreamOfParameterSetsAndOnePicture_IsOneAccessUnit() {
    var stream = new H265TestStream()
      .VideoParameterSet()
      .SequenceParameterSet()
      .PictureParameterSet()
      .IntraSliceHeader()
      .ToArray();

    var packets = H265VideoReader.Split(stream).ToList();

    Assert.That(packets, Has.Count.EqualTo(1));
    Assert.That(packets[0].IsKeyFrame, Is.True, "a picture introduced by a video parameter set may be entered at");
    Assert.That(packets[0].Data.Length, Is.EqualTo(stream.Length));
  }

  [Test]
  [Category("Unit")]
  public void TwoPictures_AreTwoAccessUnitsCutBeforeTheSecondPicturesParameterSets() {
    var stream = new H265TestStream()
      .VideoParameterSet()
      .SequenceParameterSet()
      .PictureParameterSet()
      .IntraSliceHeader()
      .VideoParameterSet()
      .SequenceParameterSet()
      .PictureParameterSet()
      .IntraSliceHeader()
      .ToArray();

    var packets = H265VideoReader.Split(stream).ToList();

    Assert.That(packets, Has.Count.EqualTo(2));
    Assert.That(packets[0].Data.Length + packets[1].Data.Length, Is.EqualTo(stream.Length));
    Assert.That(packets[1].IsKeyFrame, Is.True);
  }

  [Test]
  [Category("Unit")]
  public void ASecondSliceSegmentOfTheSamePicture_DoesNotOpenAnAccessUnit() {
    var stream = new H265TestStream()
      .VideoParameterSet()
      .SequenceParameterSet()
      .PictureParameterSet()
      .IntraSliceHeader()
      .IntraSliceHeader(firstSegment: false)
      .ToArray();

    Assert.That(H265VideoReader.Split(stream).Count(), Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void AnH264ByteStreamIsNotMistakenForAnH265One() {
    // 0x67 is an H.264 sequence parameter set. Read as an H.265 NAL unit header its type is 51,
    // which no stream may be entered at, and the profile byte that follows it reads as a layer this
    // decoder would refuse — so the signature has three separate reasons to say no.
    Assert.That(H265VideoContainer.MatchesSignature([0, 0, 0, 1, 0x67, 0x42, 0x00, 0x1E]), Is.Null);

    // …and an H.264 sequence parameter set at nal_ref_idc 1, whose 0x27 does read as a valid H.265
    // refresh picture, is told apart by the layer its second byte states.
    Assert.That(H265VideoContainer.MatchesSignature([0, 0, 0, 1, 0x27, 0x42, 0x00, 0x1E]), Is.Null);
  }

  [Test]
  [Category("Unit")]
  public void AnH265ByteStreamIsRecognisedByItsVideoParameterSet() {
    var stream = new H265TestStream().VideoParameterSet().ToArray();

    Assert.That(H265VideoContainer.MatchesSignature(stream), Is.True);
  }
}
