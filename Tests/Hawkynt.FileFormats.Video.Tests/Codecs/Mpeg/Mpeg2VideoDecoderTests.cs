using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FileFormat.Core;

namespace FileFormat.Codecs.Mpeg.Tests;

/// <summary>
/// The MPEG-2 video decoder, on streams built here bit by bit.
/// </summary>
/// <remarks>
/// The expected samples are derived from H.262 syntax and arithmetic rather than recorded from this
/// decoder. The field, dual-prime and 4:4:4 cases deliberately exercise syntax ordinary Main Profile
/// encoders rarely emit, while the external oracle suite covers real High Profile streams.
/// </remarks>
[TestFixture]
public sealed class Mpeg2VideoDecoderTests {

  // ============================================================================================
  // The intra DC, whose precision MPEG-2 made a choice
  // ============================================================================================

  [Test]
  [Category("Unit")]
  [TestCase(0, TestName = "intra_dc_precision 0, eight bits")]
  [TestCase(1, TestName = "intra_dc_precision 1, nine bits")]
  [TestCase(2, TestName = "intra_dc_precision 2, ten bits")]
  [TestCase(3, TestName = "intra_dc_precision 3, eleven bits")]
  public void AFlatIntraPictureIsMidGreyAtEveryDcPrecision(int precision) {
    var frame = _Decode(_FlatIntraPicture(16, 16, intraDcPrecision: precision)).Single();

    Assert.That(frame.PixelData.Distinct().ToArray(), Is.EqualTo(new byte[] { 130 }));
  }

  [Test]
  [Category("Unit")]
  public void AFinerIntraDcMovesTheSampleByLessPerStep() {
    var coarse = _Decode(_FlatIntraPicture(16, 16, intraDcPrecision: 0, luminanceDifferential: 1)).Single();
    var fine = _Decode(_FlatIntraPicture(16, 16, intraDcPrecision: 3, luminanceDifferential: 1)).Single();

    Assert.That(_Red(coarse, 0, 0), Is.EqualTo(_Grey(129)));
    Assert.That(_Red(fine, 0, 0), Is.EqualTo(_Grey(128)));
  }

  // ============================================================================================
  // The second coefficient table and the second scan
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void AnIntraVlcFormatPictureReadsItsCoefficientsFromTableBFifteen() {
    var withB14 = _Decode(_IntraPictureWithOneCoefficient(intraVlcFormat: false)).Single();
    var withB15 = _Decode(_IntraPictureWithOneCoefficient(intraVlcFormat: true)).Single();

    Assert.That(withB15.PixelData, Is.EqualTo(withB14.PixelData));
    Assert.That(withB15.PixelData.Distinct().Count(), Is.GreaterThan(1), "the coefficient changed nothing");
  }

  [Test]
  [Category("Unit")]
  public void TheAlternateScanPutsACoefficientSomewhereElse() {
    var zigZag = _Decode(_IntraPictureWithOneCoefficient(alternateScan: false)).Single();
    var alternate = _Decode(_IntraPictureWithOneCoefficient(alternateScan: true)).Single();

    Assert.That(alternate.PixelData, Is.Not.EqualTo(zigZag.PixelData));
    for (var y = 0; y < 8; ++y)
      for (var x = 0; x < 8; ++x)
        Assert.That(_Red(alternate, x, y), Is.EqualTo(_Red(zigZag, y, x)), $"({x}, {y})");
  }

  // ============================================================================================
  // Concealment motion vectors
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void ConcealmentMotionVectorsAreReadAndChangeNoSample() {
    var without = _Decode(_FlatIntraPicture(16, 16)).Single();
    var with = _Decode(_FlatIntraPicture(16, 16, concealmentMotionVectors: true)).Single();

    Assert.That(with.PixelData, Is.EqualTo(without.PixelData));
  }

  // ============================================================================================
  // Quantiser matrices
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void AQuantMatrixExtensionLoadsMatricesTheSequenceHeaderDidNot() {
    var loud = new byte[64];
    Array.Fill(loud, (byte)255);

    var plain = _Decode(_IntraPictureWithOneCoefficient()).Single();
    var weighted = _Decode(_IntraPictureWithOneCoefficient(intraMatrix: loud)).Single();

    Assert.That(weighted.PixelData, Is.Not.EqualTo(plain.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void AZeroInAMatrixLoadedByTheExtensionIsRefused() {
    var stream = new MpegTestStream()
      .SequenceHeader(16, 16).SequenceExtension()
      .QuantMatrixExtension(intra: new byte[64])
      .PictureHeader(1).PictureCodingExtension().SliceHeader(0, 1);

    Assert.That(Assert.Throws<InvalidDataException>(() => _Decode(stream.End()))!.Message,
      Does.Contain("zero at scan position"));
  }

  // ============================================================================================
  // Field pictures and field references
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void TwoIntraFieldPicturesReconstructOppositeParitiesOfOneFrame() {
    var stream = new MpegTestStream()
      .SequenceHeader(16, 32).SequenceExtension(progressiveSequence: false)
      .PictureHeader(1, temporalReference: 7)
      .PictureCodingExtension(pictureStructure: 1, framePredFrameDct: false, progressiveFrame: false)
      .SliceHeader(0, 1);
    _FlatIntraMacroblocks(stream, 1, luminanceDifferential: 8);

    stream
      .PictureHeader(1, temporalReference: 7)
      .PictureCodingExtension(pictureStructure: 2, framePredFrameDct: false, progressiveFrame: false)
      .SliceHeader(0, 1);
    _FlatIntraMacroblocks(stream, 1);

    var frame = _Decode(stream.End()).Single();

    for (var y = 0; y < frame.Height; ++y)
      Assert.That(_Red(frame, 0, y), Is.EqualTo(_Grey((y & 1) == 0 ? 136 : 128)), $"line {y}");
  }

  [Test]
  [Category("Unit")]
  public void ASecondPFieldCanUseTheFirstIFieldAsItsOnlyReference() {
    // This is the field-pair exception that makes a coded I-frame legal as I/P. There is no complete
    // previous anchor at all; the bottom P field can exist only if the just-decoded top I field is
    // made available immediately as a reference field.
    var stream = new MpegTestStream()
      .SequenceHeader(16, 32).SequenceExtension(progressiveSequence: false)
      .PictureHeader(1)
      .PictureCodingExtension(pictureStructure: 1, framePredFrameDct: false, progressiveFrame: false)
      .SliceHeader(0, 1);
    _FlatIntraMacroblocks(stream, 1, luminanceDifferential: 8);

    stream
      .PictureHeader(2, forwardFCode: 1)
      .PictureCodingExtension(
        forwardFCode: 1, pictureStructure: 2, framePredFrameDct: false, progressiveFrame: false)
      .SliceHeader(0, 1)
      .Code("1")       // macroblock_address_increment
      .Code("001")     // P: forward motion, no residual
      .Bits(1, 2)      // field_motion_type = field
      .Bits(0, 1)      // motion_vertical_field_select = top, the first I field
      .Code("1")       // horizontal motion_code = 0
      .Code("1");      // vertical motion_code = 0

    var frame = _Decode(stream.End()).Single();
    Assert.That(frame.PixelData.Distinct().ToArray(), Is.EqualTo(new[] { _Grey(136) }));
  }

  [Test]
  [Category("Unit")]
  public void TwoFieldsOfTheSameParityAreRefused() {
    var stream = new MpegTestStream()
      .SequenceHeader(16, 32).SequenceExtension(progressiveSequence: false)
      .PictureHeader(1).PictureCodingExtension(
        pictureStructure: 1, framePredFrameDct: false, progressiveFrame: false).SliceHeader(0, 1);
    _FlatIntraMacroblocks(stream, 1);
    stream
      .PictureHeader(1).PictureCodingExtension(
        pictureStructure: 1, framePredFrameDct: false, progressiveFrame: false).SliceHeader(0, 1);
    _FlatIntraMacroblocks(stream, 1);

    Assert.That(Assert.Throws<InvalidDataException>(() => _Decode(stream.End()))!.Message,
      Does.Contain("both state top field"));
  }

  [Test]
  [Category("Unit")]
  public void AReservedPictureStructureIsRefused() {
    var stream = new MpegTestStream()
      .SequenceHeader(16, 16).SequenceExtension()
      .PictureHeader(1).PictureCodingExtension(pictureStructure: 0);

    Assert.That(Assert.Throws<InvalidDataException>(() => _Decode(stream.End()))!.Message,
      Does.Contain("picture_structure 0"));
  }

  // ============================================================================================
  // Dual-prime
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void DualPrimePredictionDecodesWithoutApproximatingItAsOrdinaryFieldMotion() {
    // Four macroblock rows give the derived +/- half-field-line vectors room to stay inside the
    // reference. The two interior rows use dual-prime; the outer rows use ordinary frame prediction.
    // The reference is flat, so every legal prediction must remain bit-exactly flat regardless of
    // which of its two field predictions supplied a sample.
    const int size = 64;
    var stream = new MpegTestStream()
      .SequenceHeader(size, size).SequenceExtension(progressiveSequence: false)
      .PictureHeader(1).PictureCodingExtension(progressiveFrame: false);

    for (var row = 0; row < 4; ++row) {
      stream.SliceHeader(row, 1);
      _FlatIntraMacroblocks(stream, 4);
    }

    stream
      .PictureHeader(2, forwardFCode: 1)
      .PictureCodingExtension(forwardFCode: 1, framePredFrameDct: false, progressiveFrame: false);

    for (var row = 0; row < 4; ++row) {
      stream.SliceHeader(row, 1);
      for (var column = 0; column < 4; ++column) {
        stream.Code("1").Code("001");
        if (row is 1 or 2) {
          stream
            .Bits(3, 2) // frame_motion_type = dual-prime
            .Code("1").Code("1") // coded vector (0,0)
            .Bits(0, 1).Bits(0, 1); // dmvector (0,0)
        } else {
          stream
            .Bits(2, 2) // frame_motion_type = frame
            .Code("1").Code("1");
        }
      }
    }

    var frames = _Decode(stream.End());
    Assert.That(frames, Has.Count.EqualTo(2));
    Assert.That(frames[1].PixelData.Distinct().ToArray(), Is.EqualTo(new byte[] { 130 }));
  }

  [Test]
  [Category("Unit")]
  public void AReservedFrameMotionTypeIsRefused() {
    var stream = new MpegTestStream()
      .SequenceHeader(16, 32).SequenceExtension(progressiveSequence: false)
      .PictureHeader(1).PictureCodingExtension().SliceHeader(0, 1);
    _FlatIntraMacroblocks(stream, 1);
    stream.SliceHeader(1, 1);
    _FlatIntraMacroblocks(stream, 1);

    stream
      .PictureHeader(2, forwardFCode: 7).PictureCodingExtension(forwardFCode: 1, framePredFrameDct: false)
      .SliceHeader(0, 1)
      .Code("1").Code("001").Bits(0, 2);

    Assert.That(Assert.Throws<InvalidDataException>(() => _Decode(stream.End()))!.Message,
      Does.Contain("frame_motion_type 0"));
  }

  [Test]
  [Category("Unit")]
  public void AVectorCodedAgainstAnUnusedFCodeIsRefusedByName() {
    var stream = new MpegTestStream()
      .SequenceHeader(16, 16).SequenceExtension()
      .PictureHeader(1).PictureCodingExtension().SliceHeader(0, 1);
    _FlatIntraMacroblocks(stream, 1);

    stream
      .PictureHeader(2, forwardFCode: 7).PictureCodingExtension()
      .SliceHeader(0, 1)
      .Code("1").Code("001");

    var failure = Assert.Throws<InvalidDataException>(() => _Decode(stream.End()));
    Assert.That(failure!.Message, Does.Contain("f_code 15"));
    Assert.That(failure.Message, Does.Contain("forward"));
  }

  // ============================================================================================
  // 4:4:4 High Profile
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void FourFourFourUsesTwelveBlocksInTheHighProfileLayout() {
    // Cb blocks are 4,6,8,10: TL, BL, TR, BR. Give them four different DC levels while Cr stays
    // neutral. Blue is monotonic in Cb, so the four quadrants expose any 4:2:2-style stacking or
    // index swap without depending on an exact RGB conversion constant.
    var stream = new MpegTestStream()
      .SequenceHeader(16, 16)
      .SequenceExtension(chromaFormat: 3, profileAndLevel: 0x18)
      .PictureHeader(1)
      .PictureCodingExtension()
      .SliceHeader(0, 1)
      .Code("1").Code("1");

    stream
      .IntraBlock(true, 0).IntraBlock(true, 0).IntraBlock(true, 0).IntraBlock(true, 0)
      .IntraBlock(false, 8)   // Cb TL = 136
      .IntraBlock(false, 0)   // Cr TL = 128
      .IntraBlock(false, -16) // Cb BL = 120
      .IntraBlock(false, 0)   // Cr BL = 128
      .IntraBlock(false, 24)  // Cb TR = 144
      .IntraBlock(false, 0)   // Cr TR = 128
      .IntraBlock(false, -16) // Cb BR = 128
      .IntraBlock(false, 0);  // Cr BR = 128

    var frame = _Decode(stream.End()).Single();
    var topLeft = _Blue(frame, 0, 0);
    var bottomLeft = _Blue(frame, 0, 8);
    var topRight = _Blue(frame, 8, 0);
    var bottomRight = _Blue(frame, 8, 8);

    Assert.Multiple(() => {
      Assert.That(topLeft, Is.GreaterThan(bottomLeft));
      Assert.That(topRight, Is.GreaterThan(topLeft));
      Assert.That(bottomRight, Is.GreaterThan(bottomLeft));
      Assert.That(topRight, Is.GreaterThan(bottomRight));
    });
  }

  [Test]
  [Category("Unit")]
  public void AReservedChromaFormatIsRefused() {
    var stream = new MpegTestStream().SequenceHeader(16, 16).SequenceExtension(chromaFormat: 0);

    Assert.That(Assert.Throws<InvalidDataException>(() => _Decode(stream.End()))!.Message,
      Does.Contain("chroma_format 0"));
  }

  // ============================================================================================
  // Refusals
  // ============================================================================================

  [Test]
  [Category("Unit")]
  [TestCase(5, TestName = "the sequence scalable extension")]
  [TestCase(9, TestName = "the picture spatial scalable extension")]
  [TestCase(10, TestName = "the picture temporal scalable extension")]
  public void AScalabilityExtensionIsRefusedByName(int identifier) {
    var stream = new MpegTestStream()
      .SequenceHeader(16, 16).SequenceExtension()
      .Extension(identifier).Bits(0, 32);

    var failure = Assert.Throws<NotSupportedException>(() => _Decode(stream.End()));
    Assert.That(failure!.Message, Does.Contain("scalab"));
    Assert.That(failure.Message, Does.Contain("not implemented"));
  }

  [Test]
  [Category("Unit")]
  public void APictureWithNoCodingExtensionIsRefused() {
    var stream = new MpegTestStream()
      .SequenceHeader(16, 16).SequenceExtension()
      .PictureHeader(1).SliceHeader(0, 1);
    _FlatIntraMacroblocks(stream, 1);

    Assert.That(Assert.Throws<InvalidDataException>(() => _Decode(stream.End()))!.Message,
      Does.Contain("picture coding extension"));
  }

  [Test]
  [Category("Unit")]
  public void APictureCodingExtensionWithoutASequenceExtensionIsRefused() {
    var stream = new MpegTestStream()
      .SequenceHeader(16, 16)
      .PictureHeader(1).PictureCodingExtension();

    Assert.That(Assert.Throws<InvalidDataException>(() => _Decode(stream.End()))!.Message,
      Does.Contain("sequence extension"));
  }

  [Test]
  [Category("Unit")]
  [TestCase(0, TestName = "signed_level zero")]
  [TestCase(2048, TestName = "signed_level -2048")]
  public void AForbiddenEscapedLevelIsRefused(int bits) {
    var stream = new MpegTestStream()
      .SequenceHeader(16, 16).SequenceExtension()
      .PictureHeader(1).PictureCodingExtension().SliceHeader(0, 1)
      .Code("1").Code("1")
      .Code("100")
      .Code("0000 01")
      .Bits(0, 6)
      .Bits(bits, 12);

    Assert.That(Assert.Throws<InvalidDataException>(() => _Decode(stream.End()))!.Message,
      Does.Contain("signed_level"));
  }

  [Test]
  [Category("Unit")]
  public void AnInterlacedSequenceRoundsItsHeightToAWholeNumberOfFieldMacroblockRows() {
    var stream = new MpegTestStream()
      .SequenceHeader(64, 48).SequenceExtension(progressiveSequence: false)
      .PictureHeader(1).PictureCodingExtension();

    for (var row = 0; row < 4; ++row) {
      stream.SliceHeader(row, 1);
      _FlatIntraMacroblocks(stream, 4);
    }

    var frame = _Decode(stream.End()).Single();

    Assert.That(frame.Height, Is.EqualTo(48), "the fourth row is coded but not displayed");
    Assert.That(frame.PixelData.Distinct().ToArray(), Is.EqualTo(new byte[] { 130 }));
  }

  // ============================================================================================
  // Identity
  // ============================================================================================

  [TestCase("MPG2", true)]
  [TestCase("mpg2", true)]
  [TestCase("MPEG", true)]
  [TestCase("mp2v", true)]
  [TestCase("hdv2", true)]
  [TestCase("EM2V", true)]
  [TestCase("MMES", true)]
  [TestCase("MPG1", false, TestName = "MPEG-1, which has a decoder of its own")]
  [TestCase("mp4v", false)]
  [Category("Unit")]
  public void TheCodecTakesTheStreamsItsContainersName(string tag, bool expected)
    => Assert.That(Mpeg2VideoDecoder.Accepts(_Stream(tag)), Is.EqualTo(expected));

  [Test]
  [Category("Unit")]
  public void TheMatroskaNameIsTakenAndAnAudioStreamIsNotWhateverItsTag() {
    Assert.Multiple(() => {
      Assert.That(
        Mpeg2VideoDecoder.Accepts(new() { Index = 0, Kind = MediaStreamKind.Video, CodecId = "V_MPEG2" }),
        Is.True);
      Assert.That(
        Mpeg2VideoDecoder.Accepts(new() { Index = 0, Kind = MediaStreamKind.Audio, Codec = CodecTag.FromCharacters("MPG2") }),
        Is.False);
    });
  }

  // ============================================================================================
  // Helpers
  // ============================================================================================

  private static MediaStreamInfo _Stream(string tag)
    => new() { Index = 0, Kind = MediaStreamKind.Video, Codec = CodecTag.FromCharacters(tag) };

  private static List<RawImage> _Decode(byte[] stream) {
    var decoder = new Mpeg2VideoDecoder();
    var frames = new List<RawImage>();
    if (decoder.TryDecode(new(0, stream), out var frame))
      frames.Add(frame);

    frames.AddRange(decoder.Flush());
    return frames;
  }

  private static byte[] _FlatIntraPicture(
    int width, int height, int intraDcPrecision = 0, bool concealmentMotionVectors = false,
    int luminanceDifferential = 0) {
    var stream = new MpegTestStream()
      .SequenceHeader(width, height).SequenceExtension()
      .PictureHeader(1)
      .PictureCodingExtension(
        forwardFCode: concealmentMotionVectors ? 1 : 15,
        intraDcPrecision: intraDcPrecision, concealmentMotionVectors: concealmentMotionVectors);

    var columns = (width + 15) / 16;
    for (var row = 0; row < (height + 15) / 16; ++row) {
      stream.SliceHeader(row, 1);
      _FlatIntraMacroblocks(stream, columns, concealmentMotionVectors, luminanceDifferential);
    }

    return stream.End();
  }

  private static void _FlatIntraMacroblocks(
    MpegTestStream stream, int count, bool concealmentMotionVectors = false, int luminanceDifferential = 0) {
    for (var i = 0; i < count; ++i) {
      stream.Code("1");
      stream.Code("1");

      if (concealmentMotionVectors)
        stream.Code("1").Code("1").Bits(1, 1);

      var differential = i == 0 ? luminanceDifferential : 0;
      stream.IntraBlock(true, differential).IntraBlock(true, 0).IntraBlock(true, 0).IntraBlock(true, 0);
      stream.IntraBlock(false, 0).IntraBlock(false, 0);
    }
  }

  private static byte[] _IntraPictureWithOneCoefficient(
    bool intraVlcFormat = false, bool alternateScan = false, byte[]? intraMatrix = null) {
    var stream = new MpegTestStream().SequenceHeader(16, 16).SequenceExtension();
    if (intraMatrix != null)
      stream.QuantMatrixExtension(intraMatrix);

    var endOfBlock = intraVlcFormat ? MpegTestStream._END_OF_BLOCK_B15 : MpegTestStream._END_OF_BLOCK_B14;

    stream
      .PictureHeader(1)
      .PictureCodingExtension(intraVlcFormat: intraVlcFormat, alternateScan: alternateScan)
      .SliceHeader(0, 8)
      .Code("1").Code("1")
      .Code("100")
      .Code(intraVlcFormat ? "10" : "11").Code("0")
      .Code(endOfBlock);

    stream.IntraBlock(true, endOfBlock, 0).IntraBlock(true, endOfBlock, 0).IntraBlock(true, endOfBlock, 0);
    stream.IntraBlock(false, endOfBlock, 0).IntraBlock(false, endOfBlock, 0);
    return stream.End();
  }

  private static byte _Red(RawImage image, int x, int y) => image.PixelData[(y * image.Width + x) * 3];
  private static byte _Blue(RawImage image, int x, int y) => image.PixelData[(y * image.Width + x) * 3 + 2];

  private static byte _Grey(int luminance) => (byte)Math.Clamp((298 * (luminance - 16) + 128) >> 8, 0, 255);
}
