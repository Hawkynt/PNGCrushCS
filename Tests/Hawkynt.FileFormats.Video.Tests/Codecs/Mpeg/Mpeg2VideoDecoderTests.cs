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
/// The decoder's arithmetic was checked against ffmpeg over thirty encoded streams, every frame and
/// every sample, and came out identical on all but the MPEG-1 ones. What these tests add is what that
/// comparison cannot reach: the refusals, which by definition no valid stream produces, and the two
/// or three pieces of syntax ffmpeg's encoder never emits — concealment motion vectors, a loaded
/// chrominance quantiser matrix, a picture that states a reserved value.
/// <para/>
/// The expected samples are worked out from the standard rather than recorded from a run. Where a
/// number here disagrees with the decoder, one of the two is wrong and the arithmetic in the comment
/// says which.
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
    // Whatever the precision, a picture whose DC differentials are all zero is mid grey. The
    // predictor resets to 1024 >> precision and the multiplier is 8 >> precision, so the product is
    // 1024 at all four; the transform of a block whose only coefficient is 1024 is 128 everywhere,
    // and a luminance of 128 converts to (298 * (128 - 16) + 128) >> 8 = 130.
    //
    // That the four agree is the point. Reset and multiplier have to move together, and a decoder
    // that changed one without the other would give a picture four times too bright or too dark at
    // precision 2 — and would still give a picture.
    var frame = _Decode(_FlatIntraPicture(16, 16, intraDcPrecision: precision)).Single();

    Assert.That(frame.PixelData.Distinct().ToArray(), Is.EqualTo(new byte[] { 130 }));
  }

  [Test]
  [Category("Unit")]
  public void AFinerIntraDcMovesTheSampleByLessPerStep() {
    // A DC differential of one at eight-bit precision is a coefficient of 8 and so a sample of one;
    // at eleven-bit precision the same differential is a coefficient of 1 and an eighth of a sample,
    // which the transform rounds back to nothing. So the finer precision is the one where a
    // differential of one changes nothing, and that is the right way round.
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
    // The same coefficient written in both tables must decode to the same picture. Run 0 level 1 is
    // '11' in Table B.14 and '10' in Table B.15, and '10' in Table B.14 is End of Block — so a
    // decoder that read a B.15 picture with B.14's table would end the block at its first
    // coefficient and lose exactly this coefficient.
    var withB14 = _Decode(_IntraPictureWithOneCoefficient(intraVlcFormat: false)).Single();
    var withB15 = _Decode(_IntraPictureWithOneCoefficient(intraVlcFormat: true)).Single();

    Assert.That(withB15.PixelData, Is.EqualTo(withB14.PixelData));
    Assert.That(withB15.PixelData.Distinct().Count(), Is.GreaterThan(1), "the coefficient changed nothing");
  }

  [Test]
  [Category("Unit")]
  public void TheAlternateScanPutsACoefficientSomewhereElse() {
    // Scan position 1 is raster position 1 in the zig-zag and raster position 8 in the alternate
    // scan — a horizontal ramp against a vertical one. The two pictures must therefore differ, and
    // each must be the transpose of the other.
    var zigZag = _Decode(_IntraPictureWithOneCoefficient(alternateScan: false)).Single();
    var alternate = _Decode(_IntraPictureWithOneCoefficient(alternateScan: true)).Single();

    Assert.That(alternate.PixelData, Is.Not.EqualTo(zigZag.PixelData));
    for (var y = 0; y < 8; ++y)
      for (var x = 0; x < 8; ++x)
        Assert.That(_Red(alternate, x, y), Is.EqualTo(_Red(zigZag, y, x)), $"({x}, {y})");
  }

  // ============================================================================================
  // Concealment motion vectors, which ffmpeg's encoder never emits
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void ConcealmentMotionVectorsAreReadAndChangeNoSample() {
    // An intra macroblock carrying a concealment vector codes where it would have been predicted
    // from had it been lost. Nothing reconstructs from it while the stream is intact — but it is in
    // the bitstream, and a decoder that did not read it would take the next macroblock's code out of
    // the middle of it. So the test is that the picture is the one the same macroblocks give without
    // the vectors, which can only happen if every bit after them was read from the right place.
    var without = _Decode(_FlatIntraPicture(16, 16)).Single();
    var with = _Decode(_FlatIntraPicture(16, 16, concealmentMotionVectors: true)).Single();

    Assert.That(with.PixelData, Is.EqualTo(without.PixelData));
  }

  // ============================================================================================
  // Quantiser matrices, including the chrominance ones only MPEG-2 has
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void AQuantMatrixExtensionLoadsMatricesTheSequenceHeaderDidNot() {
    // A quant matrix extension with every intra weight at the maximum, which multiplies the
    // alternating current coefficient by far more than the default matrix would and so gives a
    // different picture. The DC does not go through the matrix at all, so a decoder that ignored the
    // extension would give the flat picture instead.
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
  // Refusals
  // ============================================================================================

  [Test]
  [Category("Unit")]
  [TestCase(1, "top", TestName = "a top field picture")]
  [TestCase(2, "bottom", TestName = "a bottom field picture")]
  public void AFieldPictureIsRefusedByName(int structure, string named) {
    var stream = new MpegTestStream()
      .SequenceHeader(16, 32).SequenceExtension(progressiveSequence: false)
      .PictureHeader(1).PictureCodingExtension(pictureStructure: structure);

    var failure = Assert.Throws<NotSupportedException>(() => _Decode(stream.End()));
    Assert.That(failure!.Message, Does.Contain("field picture"));
    Assert.That(failure.Message, Does.Contain(named));
    Assert.That(failure.Message, Does.Contain("not implemented"));
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

  [Test]
  [Category("Unit")]
  public void DualPrimePredictionIsRefusedByName() {
    // A P picture whose first macroblock states frame_motion_type 3. Reaching it needs
    // frame_pred_frame_dct off, since that is what makes the motion type present at all.
    var stream = new MpegTestStream()
      .SequenceHeader(16, 32).SequenceExtension(progressiveSequence: false)
      .PictureHeader(1).PictureCodingExtension().SliceHeader(0, 1);
    _FlatIntraMacroblocks(stream, 1);
    stream.SliceHeader(1, 1);
    _FlatIntraMacroblocks(stream, 1);

    stream
      .PictureHeader(2, forwardFCode: 7).PictureCodingExtension(forwardFCode: 1, framePredFrameDct: false)
      .SliceHeader(0, 1)
      .Code("1")     // macroblock_address_increment = 1
      .Code("001")   // macroblock_type: forward, no pattern (Table B.3)
      .Bits(3, 2);   // frame_motion_type: dual-prime

    var failure = Assert.Throws<NotSupportedException>(() => _Decode(stream.End()));
    Assert.That(failure!.Message, Does.Contain("Dual-prime"));
    Assert.That(failure.Message, Does.Contain("not implemented"));
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
    // f_code 15 is MPEG-2's "this direction carries no vectors", not a range. A macroblock that codes
    // one against it asks for a fourteen-bit motion_residual, which would be read out of the codes
    // that follow and give a vector of some thousands of samples — refused eventually as a vector
    // pointing off the reference, which names the wrong thing.
    var stream = new MpegTestStream()
      .SequenceHeader(16, 16).SequenceExtension()
      .PictureHeader(1).PictureCodingExtension().SliceHeader(0, 1);
    _FlatIntraMacroblocks(stream, 1);

    stream
      .PictureHeader(2, forwardFCode: 7).PictureCodingExtension()   // forward f_code left at 15
      .SliceHeader(0, 1)
      .Code("1").Code("001");                                       // forward motion, no pattern

    var failure = Assert.Throws<InvalidDataException>(() => _Decode(stream.End()));
    Assert.That(failure!.Message, Does.Contain("f_code 15"));
    Assert.That(failure.Message, Does.Contain("forward"));
  }

  [Test]
  [Category("Unit")]
  public void FourFourFourIsRefusedByName() {
    var stream = new MpegTestStream().SequenceHeader(16, 16).SequenceExtension(chromaFormat: 3);

    var failure = Assert.Throws<NotSupportedException>(() => _Decode(stream.End()));
    Assert.That(failure!.Message, Does.Contain("4:4:4"));
    Assert.That(failure.Message, Does.Contain("not implemented"));
  }

  [Test]
  [Category("Unit")]
  public void AReservedChromaFormatIsRefused() {
    var stream = new MpegTestStream().SequenceHeader(16, 16).SequenceExtension(chromaFormat: 0);

    Assert.That(Assert.Throws<InvalidDataException>(() => _Decode(stream.End()))!.Message,
      Does.Contain("chroma_format 0"));
  }

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
    // 13818-2 requires one of every picture. Without it the f_codes, the structure and the scan are
    // whatever the MPEG-1 fields of the picture header happened to say, which for an MPEG-2 stream
    // is nothing at all.
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
    // A stream that declares itself MPEG-1 and then codes itself MPEG-2. Reading on would apply
    // MPEG-1's dequantisation to MPEG-2 coefficients, which is a picture that is very nearly right.
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
    // 13818-2 Table B.16 spends a flat twelve bits on an escaped level and leaves two of the four
    // thousand values out: zero, which would code a coefficient that was not coded, and -2048, which
    // has no positive counterpart.
    var stream = new MpegTestStream()
      .SequenceHeader(16, 16).SequenceExtension()
      .PictureHeader(1).PictureCodingExtension().SliceHeader(0, 1)
      .Code("1").Code("1")
      .Code("100")            // dct_dc_size_luminance: differential of zero
      .Code("0000 01")        // escape
      .Bits(0, 6)             // run
      .Bits(bits, 12);

    Assert.That(Assert.Throws<InvalidDataException>(() => _Decode(stream.End()))!.Message,
      Does.Contain("signed_level"));
  }

  [Test]
  [Category("Unit")]
  public void AnInterlacedSequenceRoundsItsHeightToAWholeNumberOfFieldMacroblockRows() {
    // 13818-2 6.3.3: an interlaced sequence of 48 lines is coded as four macroblock rows and not
    // three, so that each field has two of its own. The fourth row is transmitted and its slice
    // start code is a row a decoder that rounded to three would refuse as past the end.
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
  // Helpers
  // ============================================================================================

  private static List<RawImage> _Decode(byte[] stream) {
    var decoder = new Mpeg2VideoDecoder();
    var frames = new List<RawImage>();
    if (decoder.TryDecode(new(0, stream), out var frame))
      frames.Add(frame);

    frames.AddRange(decoder.Flush());
    return frames;
  }

  /// <summary>An MPEG-2 intra picture whose every macroblock codes nothing but a DC differential.</summary>
  private static byte[] _FlatIntraPicture(
    int width, int height, int intraDcPrecision = 0, bool concealmentMotionVectors = false,
    int luminanceDifferential = 0) {
    // A picture carrying concealment vectors has to state a forward f_code to code them against;
    // 13818-2 6.3.10 forbids the "unused" value of 15 there even in an intra picture.
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

  /// <summary>
  /// A run of intra macroblocks, each coding one DC differential per block and nothing else.
  /// </summary>
  /// <remarks>
  /// Only the first macroblock of the run carries the differential; the rest code zero, so that the
  /// DC predictor carries the value across the row and the picture comes out flat.
  /// </remarks>
  private static void _FlatIntraMacroblocks(
    MpegTestStream stream, int count, bool concealmentMotionVectors = false, int luminanceDifferential = 0) {
    for (var i = 0; i < count; ++i) {
      stream.Code("1"); // macroblock_address_increment = 1
      stream.Code("1"); // macroblock_type: intra (Table B.2)

      if (concealmentMotionVectors) {
        // motion_vectors(0) as a frame vector of zero, then the marker bit.
        stream.Code("1").Code("1").Bits(1, 1);
      }

      var differential = i == 0 ? luminanceDifferential : 0;
      stream.IntraBlock(true, differential).IntraBlock(true, 0).IntraBlock(true, 0).IntraBlock(true, 0);
      stream.IntraBlock(false, 0).IntraBlock(false, 0);
    }
  }

  /// <summary>
  /// A one-macroblock intra picture whose first luminance block carries one alternating current
  /// coefficient at scan position one.
  /// </summary>
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
      .Code("1").Code("1");

    // Run 0, level 1 with a positive sign, then End of Block — both spelled in whichever table the
    // picture said it uses.
    stream
      .Code("100")
      .Code(intraVlcFormat ? "10" : "11").Code("0")
      .Code(endOfBlock);

    stream.IntraBlock(true, endOfBlock, 0).IntraBlock(true, endOfBlock, 0).IntraBlock(true, endOfBlock, 0);
    stream.IntraBlock(false, endOfBlock, 0).IntraBlock(false, endOfBlock, 0);
    return stream.End();
  }

  private static byte _Red(RawImage image, int x, int y) => image.PixelData[(y * image.Width + x) * 3];

  /// <summary>The red — and, with neutral chrominance, also green and blue — a luminance converts to.</summary>
  private static byte _Grey(int luminance) => (byte)Math.Clamp((298 * (luminance - 16) + 128) >> 8, 0, 255);
}
