using System;
using System.IO;
using System.Linq;
using FileFormat.Codecs;
using FileFormat.Core;
using FileFormat.H265Video;

namespace FileFormat.Codecs.H265.Tests;

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

  /// <summary>
  /// Sixteen-bit samples need the extended precision the range extensions add, which nothing here
  /// implements. Eight, ten and twelve are all decoded, so the refusal has to name the depth rather
  /// than reject everything past eight.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void SamplesDeeperThanTwelveBits_AreRefusedByTheirDepth() {
    var message = _Refusal(new H265TestStream()
      .VideoParameterSet()
      .SequenceParameterSet(bitDepthLuma: 16, bitDepthChroma: 16, profileIdc: 4)
      .ToArray());

    Assert.That(message, Does.Contain("16-bit"));
    Assert.That(message, Does.Contain("twelve bits"));
  }

  [TestCase(8)]
  [TestCase(10)]
  [TestCase(12)]
  [Category("Unit")]
  public void SamplesUpToTwelveBits_AreNotRefusedByTheirDepth(int depth) {
    var failure = _Decode(new H265TestStream()
      .VideoParameterSet()
      .SequenceParameterSet(bitDepthLuma: depth, bitDepthChroma: depth, profileIdc: 2)
      .ToArray());

    // A parameter set on its own yields no picture, so the only thing that can fail here is the
    // depth check itself.
    Assert.That(failure?.Message ?? string.Empty, Does.Not.Contain("chroma samples"));
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
  public void ATiledPictureParameterSet_IsAccepted() {
    var stream = new H265TestStream()
      .VideoParameterSet()
      .SequenceParameterSet()
      .PictureParameterSet(tiles: true)
      .ToArray();

    Assert.That(_Decode(stream), Is.Null,
      "tile syntax is implemented; a packet containing only parameter sets should simply produce no picture");
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

  [TestCase(1)]
  [TestCase(0)]
  [Category("Unit")]
  public void PredictedAndBidirectionalSlicesReachTheirImplementedHeaderPath(int sliceType) {
    var failure = _Decode(new H265TestStream()
      .VideoParameterSet()
      .SequenceParameterSet()
      .PictureParameterSet()
      .InterSliceHeader(sliceType)
      .ToArray());

    Assert.That(failure, Is.Not.Null, "the deliberately truncated inter slice unexpectedly decoded");
    Assert.That(failure, Is.Not.InstanceOf<NotSupportedException>(),
      "P/B slices are implemented and must not be rejected merely because of slice_type");
    Assert.That(failure!.Message, Does.Not.Contain("Intra pictures are decoded exactly"));
  }

  [Test]
  [Category("Unit")]
  public void APredictedSliceInsideARandomAccessPoint_IsRefusedAsMalformedRatherThanUnsupported() {
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
  public void ADependentSliceWithoutAnIndependentPredecessor_IsMalformed() {
    var failure = _Decode(new H265TestStream()
      .VideoParameterSet()
      .SequenceParameterSet()
      .PictureParameterSet(dependentSliceSegments: true)
      .IntraSliceHeader(firstSegment: false, dependent: true)
      .ToArray());

    Assert.That(failure, Is.InstanceOf<InvalidDataException>());
    Assert.That(failure!.Message, Does.Contain("preceding independent"));
  }

  [Test]
  [Category("Unit")]
  public void AnEnhancementLayerUnit_IsRefusedByItsLayer() {
    byte[] stream = [0, 0, 0, 1, 0x42, 0x09, 0x00];
    var message = _Refusal(stream);
    Assert.That(message, Does.Contain("nuh_layer_id"));
  }

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
    Assert.That(H265VideoContainer.MatchesSignature([0, 0, 0, 1, 0x67, 0x42, 0x00, 0x1E]), Is.Null);
    Assert.That(H265VideoContainer.MatchesSignature([0, 0, 0, 1, 0x27, 0x42, 0x00, 0x1E]), Is.Null);
  }

  [Test]
  [Category("Unit")]
  public void AnH265ByteStreamIsRecognisedByItsVideoParameterSet() {
    var stream = new H265TestStream().VideoParameterSet().ToArray();
    Assert.That(H265VideoContainer.MatchesSignature(stream), Is.True);
  }
}
