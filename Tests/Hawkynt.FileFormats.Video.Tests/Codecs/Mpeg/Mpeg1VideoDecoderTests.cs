using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FileFormat.Core;
using FileFormat.MpegVideo;

namespace FileFormat.Codecs.Mpeg.Tests;

/// <summary>
/// The MPEG-1 video decoder, on streams built here bit by bit.
/// </summary>
/// <remarks>
/// The decoder's arithmetic was checked against ffmpeg over thirty-one encoded streams, frame by
/// frame and sample by sample; what these tests add is the part that comparison cannot reach. Some of
/// it is syntax ffmpeg's encoder never emits — D pictures, full-pel vectors, macroblock stuffing,
/// intra macroblocks inside B pictures — and some of it is the refusals, which by definition no valid
/// stream produces.
/// <para/>
/// The expected samples are worked out from the standard rather than recorded from a run. Where a
/// number here disagrees with the decoder, one of the two is wrong and the arithmetic in the comment
/// says which.
/// </remarks>
[TestFixture]
public sealed class Mpeg1VideoDecoderTests {

  // ============================================================================================
  // Intra pictures: the DC predictor, the differential coding, dequantisation and the transform
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void AFlatIntraPictureIsMidGrey() {
    // Every DC differential is zero, so every block reconstructs at the predictor's reset value of
    // 1024. The transform of a block whose only coefficient is 1024 is 1024/8 = 128 everywhere, and
    // a luminance of 128 with neutral chrominance is (298 * (128 - 16) + 128) >> 8 = 130.
    var frames = _Decode(_FlatIntraPicture(16, 16, 0));

    Assert.That(frames.Count, Is.EqualTo(1));
    Assert.That(frames[0].Width, Is.EqualTo(16));
    Assert.That(frames[0].Height, Is.EqualTo(16));
    Assert.That(frames[0].Format, Is.EqualTo(PixelFormat.Rgb24));
    Assert.That(frames[0].PixelData.Distinct().ToArray(), Is.EqualTo(new byte[] { 130 }));
  }

  [Test]
  [Category("Unit")]
  public void TheIntraDcPredictorCarriesFromBlockToBlock() {
    // Luminance blocks in order: +8, 0, -8, 0. The predictor starts at 1024 and each differential is
    // eight times the coded value, so the four quadrants reconstruct at 1088, 1088, 1024 and 1024,
    // which are luminances of 136, 136, 128 and 128.
    var stream = new MpegTestStream()
      .SequenceHeader(16, 16).GroupOfPictures().PictureHeader(1).SliceHeader(0, 1)
      .Code("1")  // macroblock_address_increment = 1
      .Code("1")  // macroblock_type: intra (Table B.2)
      .IntraBlock(true, 8).IntraBlock(true, 0).IntraBlock(true, -8).IntraBlock(true, 0)
      .IntraBlock(false, 0).IntraBlock(false, 0)
      .End();

    var frame = _Decode(stream).Single();

    Assert.That(_Red(frame, 0, 0), Is.EqualTo(_Grey(136)));
    Assert.That(_Red(frame, 8, 0), Is.EqualTo(_Grey(136)));
    Assert.That(_Red(frame, 0, 8), Is.EqualTo(_Grey(128)));
    Assert.That(_Red(frame, 8, 8), Is.EqualTo(_Grey(128)));
  }

  [Test]
  [Category("Unit")]
  public void AnAlternatingCurrentCoefficientIsDequantisedAndTransformed() {
    // One coefficient at scan position 1, level 40, quantiser scale 1, default intra matrix.
    // Dequantised: 2 * 40 * 1 * 16 / 16 = 80, which is even, so the oddification takes it to 79.
    // With the DC at 1024 the transform is 128 + 79 * (1/2)cos((2x+1)pi/16) * (1/(2*sqrt 2)),
    // constant down each column, which rounds to the eight luminances below.
    var frame = _Decode(_IntraPictureWithFirstBlockCoefficient(16, 16, "0000 0000 0010 000" + "0")).Single();

    int[] expected = [142, 140, 136, 131, 125, 120, 116, 114];
    for (var x = 0; x < 8; ++x)
      Assert.That(_Red(frame, x, 0), Is.EqualTo(_Grey(expected[x])), $"column {x}");

    // …and the same down the column, since the coefficient carries no vertical frequency.
    for (var y = 0; y < 8; ++y)
      Assert.That(_Red(frame, 0, y), Is.EqualTo(_Grey(142)), $"row {y}");
  }

  [Test]
  [Category("Unit")]
  public void AWiderPictureThanMacroblocksIsCroppedAndNotPadded() {
    // 20x12 is two macroblocks by one, so twelve columns and twenty rows are coded past the picture.
    var frames = _Decode(_FlatIntraPicture(20, 12, 0));

    Assert.That(frames.Single().Width, Is.EqualTo(20));
    Assert.That(frames.Single().Height, Is.EqualTo(12));
    Assert.That(frames.Single().PixelData.Length, Is.EqualTo(20 * 12 * 3));
  }

  // ============================================================================================
  // Motion compensation
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void AWholePixelMotionVectorShiftsThePrediction() {
    // Two half-pixels to the right, so the predicted picture is the reference read one pixel along.
    var frames = _Decode(_ShiftedPicture(motionCode: "0010", fullPel: false));

    Assert.That(frames.Count, Is.EqualTo(2));

    int[] reference = [142, 140, 136, 131, 125, 120, 116, 114];
    for (var x = 0; x < 7; ++x)
      Assert.That(_Red(frames[1], x, 0), Is.EqualTo(_Grey(reference[x + 1])), $"column {x}");

    Assert.That(_Red(frames[1], 7, 0), Is.EqualTo(_Grey(128)));
  }

  [Test]
  [Category("Unit")]
  public void AHalfPixelMotionVectorInterpolatesThePrediction() {
    // One half-pixel to the right: each sample is the mean of the two it sits between, rounded up.
    var frames = _Decode(_ShiftedPicture(motionCode: "010", fullPel: false));

    int[] reference = [142, 140, 136, 131, 125, 120, 116, 114, 128];
    for (var x = 0; x < 8; ++x)
      Assert.That(_Red(frames[1], x, 0), Is.EqualTo(_Grey((reference[x] + reference[x + 1] + 1) >> 1)), $"column {x}");
  }

  [Test]
  [Category("Unit")]
  public void AFullPelVectorCountsWholePixels() {
    // full_pel_forward_vector makes the same motion_code of one mean a whole pixel rather than a
    // half, so this must come out as the whole-pixel shift above and not as the interpolation.
    var frames = _Decode(_ShiftedPicture(motionCode: "010", fullPel: true));

    int[] reference = [142, 140, 136, 131, 125, 120, 116, 114];
    for (var x = 0; x < 7; ++x)
      Assert.That(_Red(frames[1], x, 0), Is.EqualTo(_Grey(reference[x + 1])), $"column {x}");
  }

  [Test]
  [Category("Unit")]
  public void ASkippedMacroblockOfAPredictedPictureCopiesTheReference() {
    // Three macroblocks at three different greys; the predicted picture codes the first and the
    // third and skips the middle one, which must come out as the reference's rather than as black.
    var intra = new MpegTestStream()
      .SequenceHeader(48, 16).GroupOfPictures().PictureHeader(1).SliceHeader(0, 1);
    _IntraMacroblock(intra, "1", 8);    // 1088 -> 136
    _IntraMacroblock(intra, "1", 8);    // 1152 -> 144
    _IntraMacroblock(intra, "1", 8);    // 1216 -> 152

    intra.PictureHeader(2, temporalReference: 1).SliceHeader(0, 1)
      .Code("1").Code("001").Code("1").Code("1")   // macroblock 0: motion forward, vector (0, 0)
      .Code("011").Code("001").Code("1").Code("1");// increment 2: macroblock 1 skipped, 2 coded
    var frames = _Decode(intra.End());

    Assert.That(frames.Count, Is.EqualTo(2));
    foreach (var (x, luminance) in new[] { (0, 136), (16, 144), (32, 152) })
      Assert.That(_Red(frames[1], x, 0), Is.EqualTo(_Grey(luminance)), $"macroblock at {x}");
  }

  [Test]
  [Category("Unit")]
  public void AMotionVectorPointingOffTheReferenceIsRefusedByName() {
    // motion_code -2 at the leftmost macroblock reads one pixel to the left of the picture.
    var stream = new MpegTestStream()
      .SequenceHeader(16, 16).GroupOfPictures().PictureHeader(1).SliceHeader(0, 1);
    _IntraMacroblock(stream, "1", 0);
    stream.PictureHeader(2, temporalReference: 1).SliceHeader(0, 1)
      .Code("1").Code("001").Code("0011").Code("1");

    var failure = Assert.Throws<InvalidDataException>(() => _Decode(stream.End()));
    Assert.That(failure!.Message, Does.Contain("motion vector"));
    Assert.That(failure.Message, Does.Contain("outside the reference"));
  }

  // ============================================================================================
  // Bidirectional prediction and display order
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void ABidirectionalPictureAveragesItsTwoReferencesAndIsShownBetweenThem() {
    var frames = _Decode(_BidirectionalStream("10", backwardIntra: false));

    // Coded order is I, P, B; display order is I, B, P, which is why the middle frame is the average
    // and not the anchor.
    Assert.That(frames.Count, Is.EqualTo(3));
    Assert.That(_Red(frames[0], 0, 0), Is.EqualTo(_Grey(136)));
    Assert.That(_Red(frames[1], 0, 0), Is.EqualTo(_Grey((136 + 200 + 1) >> 1)));
    Assert.That(_Red(frames[2], 0, 0), Is.EqualTo(_Grey(200)));
  }

  [Test]
  [Category("Unit")]
  public void AnIntraMacroblockInsideABidirectionalPictureIsNotPredicted() {
    // Table B.4's intra code, which ffmpeg's encoder never emits, so nothing in the comparison
    // against it exercises this. The macroblock must reconstruct at its own coded value rather than
    // at the average of the two references.
    var frames = _Decode(_BidirectionalStream("0001 1", backwardIntra: true));

    Assert.That(frames.Count, Is.EqualTo(3));
    Assert.That(_Red(frames[1], 0, 0), Is.EqualTo(_Grey(152)));
  }

  // ============================================================================================
  // Syntax a real encoder does not produce
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void APacketHoldingSeveralPicturesDecodesEveryOneOfThem() {
    // The container here cuts a packet per picture, so this only happens to a caller with packets
    // from somewhere else. It is worth having because the failure is silent: a decoder that kept
    // only the last picture of a packet would hand back a shorter film with no error anywhere.
    var stream = new MpegTestStream()
      .SequenceHeader(16, 16).GroupOfPictures().PictureHeader(1).SliceHeader(0, 1);
    _IntraMacroblock(stream, "1", 8);
    stream.PictureHeader(2, temporalReference: 1).SliceHeader(0, 1).Code("1").Code("001").Code("1").Code("1");
    stream.PictureHeader(2, temporalReference: 2).SliceHeader(0, 1).Code("1").Code("001").Code("1").Code("1");

    var frames = _DecodeAsOnePacket(stream.End());

    Assert.That(frames.Count, Is.EqualTo(3));
    Assert.That(frames.All(frame => _Red(frame, 0, 0) == _Grey(136)), Is.True);
  }

  [Test]
  [Category("Unit")]
  public void MacroblockStuffingIsDiscarded() {
    var stream = new MpegTestStream()
      .SequenceHeader(16, 16).GroupOfPictures().PictureHeader(1).SliceHeader(0, 1)
      .Code("0000 0001 111")   // macroblock_stuffing
      .Code("0000 0001 111")   // …twice, since it may repeat
      .Code("1").Code("1")
      .IntraBlock(true, 8).IntraBlock(true, 0).IntraBlock(true, 0).IntraBlock(true, 0)
      .IntraBlock(false, 0).IntraBlock(false, 0)
      .End();

    Assert.That(_Red(_Decode(stream).Single(), 0, 0), Is.EqualTo(_Grey(136)));
  }

  [Test]
  [Category("Unit")]
  public void ALoadedQuantiserMatrixIsUsedInsteadOfTheDefault() {
    // The default intra matrix weighs scan position 1 at 16; this one weighs it at 32, so the same
    // coded level dequantises to twice as much: 2 * 40 * 1 * 32 / 16 = 160, oddified to 159. The
    // transform is then 128 + 159 * (1/2)cos((2x+1)pi/16) * (1/(2*sqrt 2)).
    var matrix = new byte[64];
    Array.Fill(matrix, (byte)16);
    matrix[1] = 32;  // scan position 1, which is where the coefficient below sits

    var frame = _Decode(_IntraPictureWithFirstBlockCoefficient(
      16, 16, "0000 0000 0010 000" + "0", matrix)).Single();

    Assert.That(_Red(frame, 0, 0), Is.EqualTo(_Grey(156)));
    Assert.That(_Red(frame, 7, 0), Is.EqualTo(_Grey(100)));
  }

  // ============================================================================================
  // Refusals
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void ADcPictureIsRefusedByName() {
    var stream = new MpegTestStream()
      .SequenceHeader(16, 16).GroupOfPictures().PictureHeader(4).SliceHeader(0, 1).Code("1").End();

    var failure = Assert.Throws<NotSupportedException>(() => _Decode(stream));
    Assert.That(failure!.Message, Does.Contain("D picture"));
    Assert.That(failure.Message, Does.Contain("not implemented"));
  }

  [Test]
  [Category("Unit")]
  public void AReservedPictureCodingTypeIsRefused() {
    var stream = new MpegTestStream()
      .SequenceHeader(16, 16).GroupOfPictures().PictureHeader(5).SliceHeader(0, 1).Code("1").End();

    Assert.That(Assert.Throws<InvalidDataException>(() => _Decode(stream))!.Message,
      Does.Contain("picture_coding_type 5"));
  }

  [Test]
  [Category("Unit")]
  public void AQuantiserScaleOfZeroIsRefused() {
    var stream = new MpegTestStream()
      .SequenceHeader(16, 16).GroupOfPictures().PictureHeader(1).SliceHeader(0, 0).Code("1").End();

    Assert.That(Assert.Throws<InvalidDataException>(() => _Decode(stream))!.Message,
      Does.Contain("quantiser_scale_code of zero"));
  }

  [Test]
  [Category("Unit")]
  public void AZeroInALoadedQuantiserMatrixIsRefused() {
    var matrix = new byte[64];
    Array.Fill(matrix, (byte)16);
    matrix[40] = 0;

    var stream = new MpegTestStream()
      .SequenceHeader(16, 16, matrix).GroupOfPictures().PictureHeader(1).SliceHeader(0, 1);
    _IntraMacroblock(stream, "1", 0);

    Assert.That(Assert.Throws<InvalidDataException>(() => _Decode(stream.End()))!.Message,
      Does.Contain("scan position 40"));
  }

  [Test]
  [Category("Unit")]
  public void APictureWhoseSlicesLeaveMacroblocksUncodedIsRefused() {
    // A 48-pixel-wide picture is three macroblocks across; this slice codes only the first, which
    // leaves two of them holding whatever the buffer held. That is a picture nobody coded.
    var stream = new MpegTestStream()
      .SequenceHeader(48, 16).GroupOfPictures().PictureHeader(1).SliceHeader(0, 1);
    _IntraMacroblock(stream, "1", 0);

    var failure = Assert.Throws<InvalidDataException>(() => _Decode(stream.End()));
    Assert.That(failure!.Message, Does.Contain("1 of its 3 macroblocks"));
    Assert.That(failure.Message, Does.Contain("cover it completely"));
  }

  [Test]
  [Category("Unit")]
  public void APredictedPictureWithNoReferenceIsRefused() {
    var stream = new MpegTestStream()
      .SequenceHeader(16, 16).GroupOfPictures().PictureHeader(2).SliceHeader(0, 1).Code("1").End();

    Assert.That(Assert.Throws<InvalidDataException>(() => _Decode(stream))!.Message,
      Does.Contain("before any intra picture"));
  }

  [Test]
  [Category("Unit")]
  public void ASliceWithNoPictureHeaderIsRefused() {
    var stream = new MpegTestStream().SequenceHeader(16, 16).SliceHeader(0, 1).Code("1").End();

    // The container hands out packets cut at pictures, so a slice with no picture in front of it
    // reaches the decoder only when a caller assembles packets itself — which is why the decoder
    // checks rather than trusting the demuxer.
    Assert.That(Assert.Throws<InvalidDataException>(() => _DecodeAsOnePacket(stream))!.Message,
      Does.Contain("no picture header before it"));
  }

  [Test]
  [Category("Unit")]
  public void APictureBeforeAnySequenceHeaderIsRefused() {
    var stream = new MpegTestStream().PictureHeader(1).SliceHeader(0, 1).Code("1").ToArray();

    Assert.That(Assert.Throws<InvalidDataException>(() => _DecodeAsOnePacket(stream))!.Message,
      Does.Contain("before any sequence header"));
  }

  [Test]
  [Category("Unit")]
  public void APictureSizeThatChangesMidStreamIsRefusedByName() {
    var stream = new MpegTestStream()
      .SequenceHeader(16, 16).GroupOfPictures().PictureHeader(1).SliceHeader(0, 1);
    _IntraMacroblock(stream, "1", 0);
    stream.SequenceHeader(32, 16).GroupOfPictures().PictureHeader(1).SliceHeader(0, 1);
    _IntraMacroblock(stream, "1", 0);
    _IntraMacroblock(stream, "1", 0);

    var failure = Assert.Throws<NotSupportedException>(() => _Decode(stream.End()));
    Assert.That(failure!.Message, Does.Contain("16x16 to 32x16"));
    Assert.That(failure.Message, Does.Contain("not implemented"));
  }

  [Test]
  [Category("Unit")]
  public void ACodeThatIsNotInATableIsRefusedWithTheTablesName() {
    // Twenty-three zeroes would be a start code, so a shorter invalid prefix is used: eleven zeroes
    // is not a macroblock_address_increment in Table B.1.
    var stream = new MpegTestStream()
      .SequenceHeader(16, 16).GroupOfPictures().PictureHeader(1).SliceHeader(0, 1)
      .Code("0000 0000 001").Code("1")
      .End();

    Assert.That(Assert.Throws<InvalidDataException>(() => _Decode(stream))!.Message,
      Does.Contain("Table B.1"));
  }

  // ============================================================================================
  // Stream builders
  // ============================================================================================

  private static byte[] _FlatIntraPicture(int width, int height, int differential) {
    var stream = new MpegTestStream().SequenceHeader(width, height).GroupOfPictures().PictureHeader(1);

    for (var row = 0; row < (height + 15) / 16; ++row) {
      stream.SliceHeader(row, 1);
      for (var column = 0; column < (width + 15) / 16; ++column)
        _IntraMacroblock(stream, "1", column == 0 ? differential : 0);
    }

    return stream.End();
  }

  /// <summary>A one-macroblock intra picture whose first luminance block carries one coefficient.</summary>
  private static byte[] _IntraPictureWithFirstBlockCoefficient(
    int width, int height, string coefficient, byte[]? intraMatrix = null)
    => new MpegTestStream()
      .SequenceHeader(width, height, intraMatrix).GroupOfPictures().PictureHeader(1).SliceHeader(0, 1)
      .Code("1").Code("1")
      .IntraBlock(true, 0, coefficient).IntraBlock(true, 0).IntraBlock(true, 0).IntraBlock(true, 0)
      .IntraBlock(false, 0).IntraBlock(false, 0)
      .End();

  /// <summary>
  /// An intra picture carrying a horizontal ramp, followed by a predicted picture that displaces its
  /// first macroblock by the given motion code and leaves the second where it is.
  /// </summary>
  private static byte[] _ShiftedPicture(string motionCode, bool fullPel) {
    var stream = new MpegTestStream()
      .SequenceHeader(32, 16).GroupOfPictures().PictureHeader(1).SliceHeader(0, 1)
      .Code("1").Code("1")
      .IntraBlock(true, 0, "0000 0000 0010 000" + "0")
      .IntraBlock(true, 0).IntraBlock(true, 0).IntraBlock(true, 0)
      .IntraBlock(false, 0).IntraBlock(false, 0);
    _IntraMacroblock(stream, "1", 0);

    stream.PictureHeader(2, temporalReference: 1, forwardFullPel: fullPel).SliceHeader(0, 1)
      .Code("1").Code("001").Code(motionCode).Code("1");

    // The second macroblock takes the vector back to zero, since the predictor carries across it.
    var back = motionCode switch { "0010" => "0011", "010" => "011", _ => throw new ArgumentException(null, nameof(motionCode)) };
    stream.Code("1").Code("001").Code(back).Code("1");

    return stream.End();
  }

  /// <summary>
  /// An intra picture at 136, a predicted picture of intra macroblocks at 200, and between them a
  /// bidirectional picture coded with the given macroblock type.
  /// </summary>
  private static byte[] _BidirectionalStream(string macroblockType, bool backwardIntra) {
    var stream = new MpegTestStream()
      .SequenceHeader(16, 16).GroupOfPictures().PictureHeader(1).SliceHeader(0, 1);
    _IntraMacroblock(stream, "1", 8);                                  // 1088 -> 136

    // A P picture may hold intra macroblocks (Table B.3), which is the simplest way to give the
    // backward reference a value of its own without coding a residual.
    stream.PictureHeader(2, temporalReference: 2).SliceHeader(0, 1);
    _IntraMacroblock(stream, "0001 1", 72);                            // 1024 + 576 = 1600 -> 200

    stream.PictureHeader(3, temporalReference: 1).SliceHeader(0, 1).Code("1").Code(macroblockType);
    if (backwardIntra) {
      // Intra in a B picture: no vectors, six blocks, and 1024 + 24 * 8 = 1216 -> 152. Averaged with
      // nothing, so the expectation is that value and not (136 + 200 + 1) / 2.
      stream
        .IntraBlock(true, 24).IntraBlock(true, 0).IntraBlock(true, 0).IntraBlock(true, 0)
        .IntraBlock(false, 0).IntraBlock(false, 0);
    } else {
      stream.Code("1").Code("1")   // forward vector (0, 0)
            .Code("1").Code("1");  // backward vector (0, 0)
    }

    return stream.End();
  }

  /// <summary>One intra macroblock: an address increment of one, a type, and six blocks.</summary>
  private static void _IntraMacroblock(MpegTestStream stream, string type, int luminanceDifferential)
    => stream
      .Code("1").Code(type)
      .IntraBlock(true, luminanceDifferential).IntraBlock(true, 0).IntraBlock(true, 0).IntraBlock(true, 0)
      .IntraBlock(false, 0).IntraBlock(false, 0);

  // ============================================================================================
  // Helpers
  // ============================================================================================

  private static List<RawImage> _Decode(byte[] stream) {
    var container = MpegVideoReader.FromBytes(stream);
    var info = MpegVideoContainer.Streams(container)[0];

    return VideoIO.Decode<Mpeg1VideoDecoder>(MpegVideoContainer.ReadPackets(container, 0), info)
      .Select(frame => frame.Image)
      .ToList();
  }

  /// <summary>Hands the whole stream over as one packet, bypassing the container's cutting.</summary>
  private static List<RawImage> _DecodeAsOnePacket(byte[] stream) {
    var decoder = new Mpeg1VideoDecoder();
    var frames = new List<RawImage>();
    if (decoder.TryDecode(new(0, stream), out var frame))
      frames.Add(frame);

    frames.AddRange(decoder.Flush());
    return frames;
  }

  private static byte _Red(RawImage image, int x, int y) => image.PixelData[(y * image.Width + x) * 3];

  /// <summary>
  /// The red — and, with neutral chrominance, also green and blue — a luminance converts to.
  /// </summary>
  /// <remarks>
  /// ISO/IEC 11172-2's samples are ITU-R BT.601 with studio swing, so a luminance of 16 is black and
  /// 235 is white: <c>(298 * (Y - 16) + 128) &gt;&gt; 8</c>. Stated here so the expectations above can
  /// be written as the luminances the standard's arithmetic produces rather than as a table of
  /// converted numbers whose derivation would be invisible.
  /// </remarks>
  private static byte _Grey(int luminance) => (byte)Math.Clamp((298 * (luminance - 16) + 128) >> 8, 0, 255);
}
