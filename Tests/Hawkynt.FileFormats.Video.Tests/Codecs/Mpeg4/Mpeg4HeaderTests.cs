using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Codecs.Mpeg4.Tests;

/// <summary>
/// The video object layer and video object plane headers, and the refusals that live in them.
/// </summary>
/// <remarks>
/// Almost every coding tool MPEG-4 Part 2 has is announced in the layer header and nowhere else, so
/// almost every refusal this decoder makes is made there. That is not a stylistic choice: a tool that
/// is signalled and not implemented has to be refused where it is signalled, because by the time its
/// bits appear the decoder has already read the ones before them as something else and is no longer
/// anywhere it can describe.
/// <para/>
/// These streams stop at the header. None of them carries a macroblock, because none of them needs
/// one: what is being checked is that the header was read correctly and that the refusal names the
/// clause. A stream that got past the header would be testing the block layer instead.
/// </remarks>
[TestFixture]
public sealed class Mpeg4HeaderTests {

  // ============================================================================================
  // Tools that are announced and refused
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void AnInterlacedLayerIsRefusedByClause() {
    var failure = _Refusal<NotSupportedException>(new Mpeg4TestStream().VisualObjectSequence()
      .VideoObjectLayer(interlaced: true).ToArray());

    Assert.That(failure.Message, Does.Contain("interlaced"));
    Assert.That(failure.Message, Does.Contain("6.3.3"));
  }

  [Test]
  [Category("Unit")]
  public void OverlappedBlockMotionCompensationIsRefusedByClause() {
    var failure = _Refusal<NotSupportedException>(new Mpeg4TestStream().VisualObjectSequence()
      .VideoObjectLayer(overlappedMotionCompensation: true).ToArray());

    Assert.That(failure.Message, Does.Contain("obmc_disable"));
    Assert.That(failure.Message, Does.Contain("7.6.4"));
  }

  [TestCase(1, TestName = "A static sprite is refused")]
  [TestCase(2, TestName = "Global motion compensation is refused")]
  [Category("Unit")]
  public void ASpriteIsRefusedByClause(int spriteEnable) {
    var failure = _Refusal<NotSupportedException>(new Mpeg4TestStream().VisualObjectSequence()
      .VideoObjectLayer(verid: 5, spriteEnable: spriteEnable).ToArray());

    Assert.That(failure.Message, Does.Contain("sprite_enable"));
    Assert.That(failure.Message, Does.Contain("7.8"));
  }

  [Test]
  [Category("Unit")]
  public void ADataPartitionedLayerIsRefusedByClause() {
    var failure = _Refusal<NotSupportedException>(new Mpeg4TestStream().VisualObjectSequence()
      .VideoObjectLayer(dataPartitioned: true).ToArray());

    Assert.That(failure.Message, Does.Contain("data partitioned"));
  }

  [Test]
  [Category("Unit")]
  public void AScalableLayerIsRefusedByClause() {
    var failure = _Refusal<NotSupportedException>(new Mpeg4TestStream().VisualObjectSequence()
      .VideoObjectLayer(scalable: true).ToArray());

    Assert.That(failure.Message, Does.Contain("scalable"));
  }

  [TestCase(1)]
  [TestCase(2)]
  [TestCase(3)]
  [Category("Unit")]
  public void AShapedObjectIsRefusedByClause(int shape) {
    var failure = _Refusal<NotSupportedException>(new Mpeg4TestStream().VisualObjectSequence()
      .VideoObjectLayer(shape: shape).ToArray());

    Assert.That(failure.Message, Does.Contain("video_object_layer_shape"));
  }

  [TestCase(0)]
  [TestCase(2)]
  [TestCase(3)]
  [Category("Unit")]
  public void AChromaFormatOtherThanFourTwoZeroIsRefused(int chromaFormat) {
    var failure = _Refusal<NotSupportedException>(new Mpeg4TestStream().VisualObjectSequence()
      .VideoObjectLayer(chromaFormat: chromaFormat).ToArray());

    Assert.That(failure.Message, Does.Contain("chroma_format"));
  }

  [Test]
  [Category("Unit")]
  public void SamplesOfAnyDepthButEightBitsAreRefused() {
    var failure = _Refusal<NotSupportedException>(new Mpeg4TestStream().VisualObjectSequence()
      .VideoObjectLayer(notEightBit: true, quantiserPrecision: 5, bitsPerPixel: 10).ToArray());

    Assert.That(failure.Message, Does.Contain("10-bit samples"));
  }

  [Test]
  [Category("Unit")]
  public void TheComplexityEstimationHeaderIsRefusedByClause() {
    var failure = _Refusal<NotSupportedException>(new Mpeg4TestStream().VisualObjectSequence()
      .VideoObjectLayer(complexityEstimation: true).ToArray());

    Assert.That(failure.Message, Does.Contain("complexity_estimation_disable"));
  }

  [Test]
  [Category("Unit")]
  public void NewpredIsRefusedByClause() {
    var failure = _Refusal<NotSupportedException>(new Mpeg4TestStream().VisualObjectSequence()
      .VideoObjectLayer(verid: 5, newPredictive: true).ToArray());

    Assert.That(failure.Message, Does.Contain("newpred"));
  }

  [Test]
  [Category("Unit")]
  public void ReducedResolutionPicturesAreRefusedByClause() {
    var failure = _Refusal<NotSupportedException>(new Mpeg4TestStream().VisualObjectSequence()
      .VideoObjectLayer(verid: 5, reducedResolution: true).ToArray());

    Assert.That(failure.Message, Does.Contain("reduced resolution"));
  }

  [Test]
  [Category("Unit")]
  public void ASpriteCodedPictureIsRefusedByClause() {
    var stream = new Mpeg4TestStream().VisualObjectSequence().VideoObjectLayer()
      .VideoObjectPlane(codingType: 3).ToArray();

    var failure = _Refusal<NotSupportedException>(stream);
    Assert.That(failure.Message, Does.Contain("sprite coded"));
  }

  // ============================================================================================
  // Fields the standard fixes, and values it forbids
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void AMarkerBitThatIsZeroIsRefused() {
    var failure = _Refusal<InvalidDataException>(new Mpeg4TestStream().VisualObjectSequence()
      .VideoObjectLayer(firstMarker: 0).ToArray());

    Assert.That(failure.Message, Does.Contain("marker bit"));
  }

  [Test]
  [Category("Unit")]
  public void AMarkerBitInTheMiddleOfThePictureSizeIsRefused() {
    var failure = _Refusal<InvalidDataException>(new Mpeg4TestStream().VisualObjectSequence()
      .VideoObjectLayer(widthMarker: 0).ToArray());

    Assert.That(failure.Message, Does.Contain("marker bit"));
  }

  [Test]
  [Category("Unit")]
  public void AVideoObjectTypeOfZeroIsRefused() {
    var failure = _Refusal<InvalidDataException>(new Mpeg4TestStream().VisualObjectSequence()
      .VideoObjectLayer(objectType: 0).ToArray());

    Assert.That(failure.Message, Does.Contain("video_object_type_indication"));
  }

  [Test]
  [Category("Unit")]
  public void APictureSizeOfZeroIsRefused() {
    var failure = _Refusal<InvalidDataException>(new Mpeg4TestStream().VisualObjectSequence()
      .VideoObjectLayer(width: 0).ToArray());

    Assert.That(failure.Message, Does.Contain("0x16"));
  }

  [Test]
  [Category("Unit")]
  public void ATimeIncrementResolutionOfZeroIsRefused() {
    var failure = _Refusal<InvalidDataException>(new Mpeg4TestStream().VisualObjectSequence()
      .VideoObjectLayer(timeIncrementResolution: 0).ToArray());

    Assert.That(failure.Message, Does.Contain("vop_time_increment_resolution"));
  }

  [Test]
  [Category("Unit")]
  public void AQuantiserOfZeroIsRefused() {
    var stream = new Mpeg4TestStream().VisualObjectSequence().VideoObjectLayer()
      .VideoObjectPlane(quantiser: 0).ToArray();

    var failure = _Refusal<InvalidDataException>(stream);
    Assert.That(failure.Message, Does.Contain("vop_quant 0"));
  }

  [Test]
  [Category("Unit")]
  public void AForwardMotionCodeOfZeroIsRefused() {
    var stream = new Mpeg4TestStream().VisualObjectSequence().VideoObjectLayer()
      .VideoObjectPlane(codingType: 1, forwardFCode: 0).ToArray();

    var failure = _Refusal<InvalidDataException>(stream);
    Assert.That(failure.Message, Does.Contain("vop_fcode_forward 0"));
  }

  [Test]
  [Category("Unit")]
  public void APictureBeforeAnyLayerHeaderIsRefused() {
    var stream = new Mpeg4TestStream().VideoObjectPlane().ToArray();

    var failure = _Refusal<InvalidDataException>(stream);
    Assert.That(failure.Message, Does.Contain("video object layer header"));
  }

  // ============================================================================================
  // Identity, and the headers a container carried out of band
  // ============================================================================================

  [TestCase("mp4v", true)]
  [TestCase("XVID", true)]
  [TestCase("DIVX", true)]
  [TestCase("DX50", true)]
  [TestCase("FMP4", true)]
  [TestCase("DIV3", false)]
  [TestCase("MPG1", false)]
  [TestCase("H263", false)]
  [Category("Unit")]
  public void TheCodecTakesTheStreamsItsContainersName(string tag, bool expected)
    => Assert.That(Mpeg4VideoDecoder.Accepts(_Stream(tag)), Is.EqualTo(expected));

  [TestCase("V_MPEG4/ISO/ASP", true)]
  [TestCase("V_MPEG4/ISO/SP", true)]
  [TestCase("V_MPEG4/MS/V3", false)]
  [Category("Unit")]
  public void TheCodecTakesTheNamesMatroskaGivesIt(string codecId, bool expected)
    => Assert.That(
      Mpeg4VideoDecoder.Accepts(new() { Index = 0, Kind = MediaStreamKind.Video, CodecId = codecId }),
      Is.EqualTo(expected));

  [Test]
  [Category("Unit")]
  public void AnAudioStreamIsNotTakenWhateverItsTag()
    => Assert.That(
      Mpeg4VideoDecoder.Accepts(new() { Index = 0, Kind = MediaStreamKind.Audio, Codec = CodecTag.FromCharacters("mp4v") }),
      Is.False);

  [Test]
  [Category("Unit")]
  public void HeadersThatBeginWithAStartCodeArePassedThroughUnchanged() {
    var headers = new byte[] { 0, 0, 1, 0xB0, 0xF1 };

    Assert.That(Mpeg4DecoderConfiguration.HeadersIn(headers).ToArray(), Is.EqualTo(headers));
  }

  [Test]
  [Category("Unit")]
  public void HeadersInsideAVisualSampleEntryAreFoundInsideItsElementaryStreamDescriptor() {
    var headers = new byte[] { 0, 0, 1, 0xB0, 0xF1, 0, 0, 1, 0xB5, 0x89 };

    Assert.That(Mpeg4DecoderConfiguration.HeadersIn(_SampleEntry(headers)).ToArray(), Is.EqualTo(headers));
  }

  [Test]
  [Category("Unit")]
  public void ASampleEntryWithNoDescriptorYieldsNoHeaders()
    => Assert.That(Mpeg4DecoderConfiguration.HeadersIn(_SampleEntry(null)).Length, Is.Zero);

  [Test]
  [Category("Unit")]
  public void PrivateDataThatIsNeitherIsNotMistakenForHeaders()
    => Assert.That(Mpeg4DecoderConfiguration.HeadersIn(new byte[] { 1, 2, 3, 4, 5 }).Length, Is.Zero);

  // ============================================================================================
  // Helpers
  // ============================================================================================

  /// <summary>
  /// A visual sample entry as an ISO base media file writes one, with the headers inside its
  /// <c>esds</c> box.
  /// </summary>
  /// <remarks>
  /// Built here rather than checked in because what is being tested is the walk over its fixed
  /// eighty-six-byte header and the descriptor nesting inside it — and the six reserved zero bytes at
  /// its start are exactly what makes a naive search for a start code find one four bytes early.
  /// </remarks>
  private static byte[] _SampleEntry(byte[]? headers) {
    var boxes = new List<byte>();
    if (headers != null) {
      var specific = _Descriptor(0x05, headers);
      var configuration = _Descriptor(0x04, [.. new byte[] { 0x20, 0x11, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, .. specific]);
      var elementaryStream = _Descriptor(0x03, [.. new byte[] { 0, 1, 0 }, .. configuration]);

      var body = new List<byte> { 0, 0, 0, 0 };  // version and flags
      body.AddRange(elementaryStream);
      boxes.AddRange([(byte)0, 0, 0, (byte)(8 + body.Count), (byte)'e', (byte)'s', (byte)'d', (byte)'s']);
      boxes.AddRange(body);
    }

    var entry = new List<byte>();
    var size = 86 + boxes.Count;
    entry.AddRange([(byte)(size >> 24), (byte)(size >> 16), (byte)(size >> 8), (byte)size]);
    entry.AddRange([(byte)'m', (byte)'p', (byte)'4', (byte)'v']);
    entry.AddRange(new byte[6]);          // reserved
    entry.AddRange([0, 1]);               // data_reference_index
    entry.AddRange(new byte[16]);         // pre_defined and reserved
    entry.AddRange([0, 16, 0, 16]);       // width and height
    entry.AddRange(new byte[8]);          // resolutions
    entry.AddRange(new byte[4]);          // reserved
    entry.AddRange([0, 1]);               // frame_count
    entry.AddRange(new byte[32]);         // compressorname
    entry.AddRange([0, 0x18]);            // depth
    entry.AddRange([0xFF, 0xFF]);         // pre_defined
    entry.AddRange(boxes);
    return entry.ToArray();
  }

  private static byte[] _Descriptor(byte tag, byte[] body) {
    var result = new List<byte> { tag };

    // The four-byte form of the length, which is what a real writer uses and what a decoder that
    // assumed one byte would read as a body four bytes too short.
    result.AddRange([
      (byte)(0x80 | ((body.Length >> 21) & 0x7F)),
      (byte)(0x80 | ((body.Length >> 14) & 0x7F)),
      (byte)(0x80 | ((body.Length >> 7) & 0x7F)),
      (byte)(body.Length & 0x7F),
    ]);
    result.AddRange(body);
    return result.ToArray();
  }

  private static MediaStreamInfo _Stream(string tag)
    => new() { Index = 0, Kind = MediaStreamKind.Video, Codec = CodecTag.FromCharacters(tag) };

  private static TException _Refusal<TException>(byte[] stream) where TException : Exception {
    var decoder = Mpeg4VideoDecoder.Create(_Stream("mp4v"));

    return Assert.Throws<TException>(() => decoder.TryDecode(new(0, stream), out _))!;
  }
}
