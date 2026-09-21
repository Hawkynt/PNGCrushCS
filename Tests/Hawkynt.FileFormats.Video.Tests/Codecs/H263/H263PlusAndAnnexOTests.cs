using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;

namespace FileFormat.Codecs.H263.Tests;

/// <summary>H.263+ custom-picture and Annex O temporal-scalability coverage.</summary>
[TestFixture]
public sealed class H263PlusAndAnnexOTests {

  private const int _PICTURE_START_CODE = 1 << 5;

  // ============================================================================================
  // H.263+ custom picture formats
  // ============================================================================================

  [Test]
  [Category("RoundTrip")]
  public void CustomPictureThatIsNotMacroblockAlignedRoundTripsAndIsCroppedForDisplay() {
    const int width = 180;
    const int height = 100;
    var stream = _Stream(width, height);
    var encoder = H263VideoEncoder.Create(stream);

    Assert.That(encoder.TryEncode(_Flat(width, height, 96), 0, out var packet), Is.True);

    var header = _ParseHeader(packet.Data.Span);
    Assert.Multiple(() => {
      Assert.That(header.Width, Is.EqualTo(width));
      Assert.That(header.Height, Is.EqualTo(height));
      Assert.That(header.MacroblockWidth, Is.EqualTo(12));
      Assert.That(header.MacroblockHeight, Is.EqualTo(7));
      Assert.That(header.PictureKind, Is.EqualTo(H263PictureKind.Intra));
    });

    var decoder = H263VideoDecoder.Create(stream);
    Assert.That(decoder.TryDecode(packet, out var decoded), Is.True);
    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(width));
      Assert.That(decoded.Height, Is.EqualTo(height));
      Assert.That(decoded.PixelData.Length, Is.EqualTo(width * height * 3));
      Assert.That(decoded.PixelData.Distinct().Count(), Is.EqualTo(1),
        "the coded padding must be cropped rather than leaking into the displayed picture");
    });
  }

  [Test]
  [Category("Unit")]
  public void CustomPictureHeightIndicationAboveTheNormativeMaximumIsRejected() {
    var data = _CustomPlusHeader(heightIndication: 289);

    var failure = Assert.Throws<InvalidDataException>(() => _ParseHeaderAfterStart(data));
    Assert.That(failure!.Message, Does.Contain("PHI"));
    Assert.That(failure.Message, Does.Contain("288"));
  }

  [Test]
  [Category("Unit")]
  public void ExtendedPixelAspectRatioMustBeRelativelyPrime() {
    var data = _CustomPlusHeader(pixelAspectRatio: 15, parWidth: 4, parHeight: 2);

    var failure = Assert.Throws<InvalidDataException>(() => _ParseHeaderAfterStart(data));
    Assert.That(failure!.Message, Does.Contain("relatively prime"));
  }

  // ============================================================================================
  // Annex O sequence ordering
  // ============================================================================================

  [Test]
  [Category("RoundTrip")]
  public void AnnexOSequenceIsWrittenInCodingOrderAndReturnedInDisplayOrder() {
    const int width = 128;
    const int height = 96;
    var stream = _Stream(width, height);
    var encoder = H263VideoEncoder.Create(stream);
    encoder.BidirectionalPicturesBetweenReferences = 2;

    byte[] luminances = [48, 80, 112, 144];
    var packets = new List<CodedPacket>();
    for (var index = 0; index < luminances.Length; ++index)
      if (encoder.TryEncode(_Flat(width, height, luminances[index]), index, out var packet))
        packets.Add(packet);
    packets.AddRange(encoder.Flush());

    Assert.That(packets, Has.Count.EqualTo(4));
    Assert.Multiple(() => {
      Assert.That(packets.Select(static packet => packet.PresentationTimestamp).ToArray(),
        Is.EqualTo(new long?[] { 0, 3, 1, 2 }));
      Assert.That(packets.Select(static packet => packet.DecodeTimestamp).ToArray(),
        Is.EqualTo(new long?[] { 0, 1, 2, 3 }));
      Assert.That(packets.Select(static packet => packet.IsKeyFrame).ToArray(),
        Is.EqualTo(new[] { true, false, false, false }));
    });

    var kinds = packets.Select(static packet => _ParseHeader(packet.Data.Span).PictureKind).ToArray();
    Assert.That(kinds, Is.EqualTo(new[] {
      H263PictureKind.Intra,
      H263PictureKind.Predicted,
      H263PictureKind.Bidirectional,
      H263PictureKind.Bidirectional,
    }));

    var decoder = H263VideoDecoder.Create(stream);
    var decoded = new List<RawImage>();
    foreach (var packet in packets)
      if (decoder.TryDecode(packet, out var frame))
        decoded.Add(frame);
    decoded.AddRange(decoder.Flush());

    Assert.That(decoded, Has.Count.EqualTo(4));
    var means = decoded.Select(static frame => frame.PixelData.Average(static value => (double)value)).ToArray();
    Assert.That(means[0], Is.LessThan(means[1]));
    Assert.That(means[1], Is.LessThan(means[2]));
    Assert.That(means[2], Is.LessThan(means[3]),
      "the future P anchor must be displayed after the B-pictures that precede it");
  }

  [Test]
  [Category("Unit")]
  public void BidirectionalPictureWithoutDecodedAnchorsIsRejected() {
    var stream = _Stream(128, 96);
    var decoder = H263VideoDecoder.Create(stream);
    var data = _CustomPlusHeader(
      sourceFormat: 1,
      pictureKind: H263PictureKind.Bidirectional,
      temporalReference: 1);
    var packet = new CodedPacket(0, data);

    var failure = Assert.Throws<InvalidDataException>(() => decoder.TryDecode(packet, out _));
    Assert.That(failure!.Message, Does.Contain("previous reference"));
  }

  // ============================================================================================
  // Annex O macroblock prediction modes and motion-vector rules
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void DirectMacroblockScalesTheFutureVectorAcrossTemporalReferenceWraparound() {
    var past = _GradientReference(16, temporalReference: 254, offset: 10);
    var future = _GradientReference(16, temporalReference: 0, offset: 100);
    future.HasMotion[0] = true;
    future.MotionX[0] = 4; // two pixels from past to future

    var bits = new H263TestStream()
      .Coded()
      .Code("11") // Table O.1: Direct
      .Code("0")  // Table O.4: CBPC 00
      .Code("11") // Table 12 value 15 -> complemented INTER pattern 0000
      .ToArray();

    var target = _DecodeBMacroblocks(bits, _BHeader(16, 16, temporalReference: 255), past, future);

    // TRD=(0-254)&255=2 and TRB=(255-254)&255=1. The +4-half-pixel future vector therefore becomes
    // +2 half pixels forward and -2 backward: one pixel right in the past and one left in the future.
    var expected = (_Luma(past, 5, 4) + _Luma(future, 3, 4)) >> 1;
    Assert.That(_Luma(target, 4, 4), Is.EqualTo(expected));
  }

  [Test]
  [Category("Unit")]
  public void ForwardMacroblockUsesItsForwardMotionVector() {
    var past = _GradientReference(16, temporalReference: 10, offset: 10);
    var future = _GradientReference(16, temporalReference: 12, offset: 100);
    var bits = new H263TestStream()
      .Coded()
      .Code("100")  // Table O.1: Forward, no texture
      .Code("0010") // MVDFW x = +2 half pixels
      .Code("1")    // MVDFW y = 0
      .ToArray();

    var target = _DecodeBMacroblocks(bits, _BHeader(16, 16, temporalReference: 11), past, future);
    Assert.That(_Luma(target, 4, 4), Is.EqualTo(_Luma(past, 5, 4)));
  }

  [Test]
  [Category("Unit")]
  public void BackwardMacroblockUsesItsBackwardMotionVector() {
    var past = _GradientReference(16, temporalReference: 10, offset: 10);
    var future = _GradientReference(16, temporalReference: 12, offset: 100);
    var bits = new H263TestStream()
      .Coded()
      .Code("010")  // Table O.1: Backward, no texture
      .Code("0011") // MVDBW x = -2 half pixels
      .Code("1")    // MVDBW y = 0
      .ToArray();

    var target = _DecodeBMacroblocks(bits, _BHeader(16, 16, temporalReference: 11), past, future);
    Assert.That(_Luma(target, 4, 4), Is.EqualTo(_Luma(future, 3, 4)));
  }

  /// <summary>
  /// ITU-T H.263 O.4: "the average is calculated by dividing the sum of the two predictions by two
  /// (division by truncation)".
  /// </summary>
  /// <remarks>
  /// The two references are chosen so that the predictions sum to an odd number, which is the only
  /// case in which truncating and rounding differ at all — and the assertion below checks that they
  /// do, because with an even sum this test would pass against either rule and would be measuring
  /// nothing. FFmpeg rounds here, so a reader who trusts it rather than the Recommendation will
  /// change this and find out from this test rather than from a stream.
  /// </remarks>
  [Test]
  [Category("Unit")]
  public void BidirectionalMacroblockAveragesForwardAndBackwardPredictionsByTruncation() {
    var past = _GradientReference(16, temporalReference: 10, offset: 10);
    var future = _GradientReference(16, temporalReference: 12, offset: 101);
    var bits = new H263TestStream()
      .Coded()
      .Code("00100") // Table O.1: Bi-dir, no texture
      .Code("0010")  // MVDFW x = +2
      .Code("1")     // MVDFW y = 0
      .Code("0011")  // MVDBW x = -2
      .Code("1")     // MVDBW y = 0
      .ToArray();

    var forward = _Luma(past, 5, 4);
    var backward = _Luma(future, 3, 4);
    Assert.That((forward + backward) % 2, Is.EqualTo(1),
      "the two predictions must sum to an odd number or truncation and rounding agree and this test proves nothing");

    var target = _DecodeBMacroblocks(bits, _BHeader(16, 16, temporalReference: 11), past, future);
    Assert.That(_Luma(target, 4, 4), Is.EqualTo((forward + backward) >> 1));
    Assert.That(_Luma(target, 4, 4), Is.Not.EqualTo((forward + backward + 1) >> 1),
      "a rounded average is FFmpeg's behaviour, not the Recommendation's");
  }

  /// <summary>
  /// A decoded reference picture keeps the macroblock vectors an Annex O direct-mode B-picture
  /// scales (O.5.2).
  /// </summary>
  /// <remarks>
  /// The direct-mode tests above hand the decoder a motion field they wrote themselves, so nothing
  /// among them notices if a real predicted picture leaves that field empty — and the writer never
  /// selects direct mode, so no round trip notices either. Every macroblock of a real P-picture is
  /// checked here instead: an intra macroblock carries no vector, and a skipped one carries a real
  /// zero vector rather than no vector, because O.5.2 scales the former to nothing and the latter to
  /// a zero prediction and the two are not the same picture.
  /// </remarks>
  [Test]
  [Category("Unit")]
  public void APredictedPictureKeepsTheMotionFieldDirectModeScales() {
    const int width = 176;
    const int height = 144;
    var stream = _Stream(width, height);
    var encoder = H263VideoEncoder.Create(stream);

    var packets = new List<CodedPacket>();
    for (var index = 0; index < 3; ++index)
      if (encoder.TryEncode(_Shifted(width, height, index), index, out var packet))
        packets.Add(packet);

    packets.AddRange(encoder.Flush());
    Assert.That(packets, Has.Count.EqualTo(3));

    H263Frame? reference = null;
    H263Frame? predicted = null;
    foreach (var packet in packets) {
      var reader = new H263BitReader(packet.Data.Span);
      Assert.That(reader.ReadBits(22), Is.EqualTo(_PICTURE_START_CODE));
      var header = H263PictureHeader.Parse(ref reader);
      var target = new H263Frame(header.MacroblockWidth, header.MacroblockHeight);
      var picture = H263PictureDecoder.BeginPicture(header, target, reference);
      picture.DecodePicture(ref reader);
      reference = target;
      if (!header.IsIntra)
        predicted = target;
    }

    Assert.That(predicted, Is.Not.Null);
    Assert.That(predicted!.HasMotion.Length, Is.EqualTo(11 * 9));
    Assert.That(predicted.HasMotion, Has.Some.True,
      "a predicted picture must leave a motion field behind for a direct-mode B-picture to scale");
    Assert.That(predicted.MotionX.Zip(predicted.MotionY, static (x, y) => x != 0 || y != 0).Any(static moved => moved),
      Is.True,
      "the recorded field must hold the vectors the picture was predicted with, not zero everywhere");

    for (var address = 0; address < predicted.HasMotion.Length; ++address)
      if (!predicted.HasMotion[address])
        Assert.That((predicted.MotionX[address], predicted.MotionY[address]), Is.EqualTo(((short)0, (short)0)),
          $"macroblock {address} carries no vector and so must record none");
  }

  [Test]
  [Category("Unit")]
  public void SameDirectionVectorPredictorUsesTheNormalTopEdgeSubstitution() {
    var past = _GradientReference(32, temporalReference: 10, offset: 10);
    var future = _GradientReference(32, temporalReference: 12, offset: 100);
    var bits = new H263TestStream()
      .Coded().Code("100").Code("0010").Code("1") // MB 0: forward vector (+2, 0)
      .Coded().Code("100").Code("1").Code("1")    // MB 1: zero MVD, so predictor must keep +2
      .ToArray();

    var target = _DecodeBMacroblocks(bits, _BHeader(32, 16, temporalReference: 11), past, future);
    Assert.That(_Luma(target, 16, 4), Is.EqualTo(_Luma(past, 17, 4)),
      "at the top edge the unavailable above candidates are substituted from the left before taking the median");
  }

  [Test]
  [Category("Unit")]
  public void BidirectionalTemporalReferenceMustLieStrictlyBetweenItsAnchors() {
    var past = _GradientReference(16, temporalReference: 254, offset: 10);
    var future = _GradientReference(16, temporalReference: 0, offset: 100);
    var target = new H263Frame(1, 1);

    var failure = Assert.Throws<InvalidDataException>(() =>
      _ = new H263BidirectionalPictureDecoder(_BHeader(16, 16, temporalReference: 0), target, past, future));

    Assert.That(failure!.Message, Does.Contain("strictly between"));
  }

  [Test]
  [Category("Unit")]
  public void BidirectionalReferencesMustMatchTheCodedGeometry() {
    var past = _GradientReference(16, temporalReference: 10, offset: 10);
    var future = _GradientReference(32, temporalReference: 12, offset: 100);
    var target = new H263Frame(1, 1);

    var failure = Assert.Throws<InvalidDataException>(() =>
      _ = new H263BidirectionalPictureDecoder(_BHeader(16, 16, temporalReference: 11), target, past, future));

    Assert.That(failure!.Message, Does.Contain("same coded geometry"));
  }

  // ============================================================================================
  // Syntax tables and malformed VLCs
  // ============================================================================================

  [TestCase("11", 0)]
  [TestCase("0001", 1)]
  [TestCase("100", 2)]
  [TestCase("101", 3)]
  [TestCase("00110", 4)]
  [TestCase("010", 5)]
  [TestCase("011", 6)]
  [TestCase("00111", 7)]
  [TestCase("00100", 8)]
  [TestCase("00101", 9)]
  [TestCase("00001", 10)]
  [TestCase("000001", 11)]
  [TestCase("0000001", 12)]
  [TestCase("000000001", 13)]
  [Category("Unit")]
  public void AnnexOBidirectionalMacroblockTypeCodesMatchTableO1(string code, int expected) {
    var reader = new H263BitReader(new H263TestStream().Code(code).ToArray());
    Assert.That(H263AnnexOVlc.BidirectionalMacroblockType.Read(ref reader), Is.EqualTo(expected));
  }

  [Test]
  [Category("Unit")]
  public void TruncatedVlcCannotTurnPeekPaddingIntoARealCodeword() {
    var table = new H263VlcTable("test table", ("100000000", 7));
    var data = new byte[] { 0x80 }; // only eight bits; the ninth zero exists only in NextBits padding

    var failure = Assert.Throws<InvalidDataException>(() => _ReadVlc(table, data));
    Assert.That(failure!.Message, Does.Contain("short"));
  }

  private static H263PictureHeader _ParseHeader(ReadOnlySpan<byte> data) {
    var reader = new H263BitReader(data);
    Assert.That(reader.ReadBits(22), Is.EqualTo(_PICTURE_START_CODE));
    return H263PictureHeader.Parse(ref reader);
  }

  private static H263PictureHeader _ParseHeaderAfterStart(byte[] data) {
    var reader = new H263BitReader(data);
    reader.Skip(22);
    return H263PictureHeader.Parse(ref reader);
  }

  private static int _ReadVlc(H263VlcTable table, byte[] data) {
    var reader = new H263BitReader(data);
    return table.Read(ref reader);
  }

  private static H263Frame _DecodeBMacroblocks(
    byte[] data,
    H263PictureHeader header,
    H263Frame past,
    H263Frame future) {
    var target = new H263Frame(header.MacroblockWidth, header.MacroblockHeight);
    var reader = new H263BitReader(data);
    var decoder = new H263BidirectionalPictureDecoder(header, target, past, future);
    decoder.DecodePicture(ref reader);
    return target;
  }

  private static H263PictureHeader _BHeader(int width, int height, int temporalReference) => new() {
    Width = width,
    Height = height,
    MacroblockRowsPerGroup = 1,
    IsIntra = false,
    IsReference = false,
    Quantiser = 1,
    HasWideEscapeLevel = false,
    HasGroupLayer = true,
    AllowsVectorsOutsidePicture = true,
    TemporalReference = temporalReference,
    PictureKind = H263PictureKind.Bidirectional,
    RoundingType = 0,
    EnhancementLayerNumber = 2,
    ReferenceLayerNumber = 1,
  };

  /// <summary>A picture whose content moves by whole pixels, so the encoder has vectors worth coding.</summary>
  private static RawImage _Shifted(int width, int height, int phase) {
    var pixels = new byte[width * height * 3];
    var left = 24 + phase * 6;
    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var at = (y * width + x) * 3;
        var inBox = x >= left && x < left + 48 && y >= 32 && y < 96;
        pixels[at] = (byte)(inBox ? 210 : 60);
        pixels[at + 1] = (byte)(inBox ? 80 : 120);
        pixels[at + 2] = (byte)(inBox ? 150 : 170);
      }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = pixels };
  }

  private static H263Frame _GradientReference(int width, int temporalReference, int offset) {
    var frame = new H263Frame(width / 16, 1) { TemporalReference = temporalReference };
    for (var y = 0; y < frame.LumaHeight; ++y)
      for (var x = 0; x < frame.LumaWidth; ++x)
        frame.Luma[y * frame.LumaWidth + x] = (byte)(offset + x + 2 * y);
    frame.Cb.AsSpan().Fill(128);
    frame.Cr.AsSpan().Fill(128);
    return frame;
  }

  private static int _Luma(H263Frame frame, int x, int y) => frame.Luma[y * frame.LumaWidth + x];

  private static MediaStreamInfo _Stream(int width, int height) => new() {
    Index = 0,
    Kind = MediaStreamKind.Video,
    Codec = CodecTag.FromCharacters("H263"),
    Width = width,
    Height = height,
  };

  private static RawImage _Flat(int width, int height, byte luminance) {
    var planes = new byte[width * height * 3 / 2];
    planes.AsSpan(0, width * height).Fill(luminance);
    planes.AsSpan(width * height).Fill(128);
    return new() { Width = width, Height = height, Format = PixelFormat.Yuv420P8, PixelData = planes };
  }

  /// <summary>Builds the full-UFEP PLUSPTYPE subset used by these tests, including deliberately malformed CPFMT fields.</summary>
  private static byte[] _CustomPlusHeader(
    int sourceFormat = 6,
    int width = 16,
    int heightIndication = 4,
    int pixelAspectRatio = 1,
    int parWidth = 1,
    int parHeight = 1,
    H263PictureKind pictureKind = H263PictureKind.Intra,
    int temporalReference = 0,
    int quantiser = 1) {
    var writer = new H263BitWriter();
    writer.Write(_PICTURE_START_CODE, 22);
    writer.Write(temporalReference & 0xFF, 8);

    writer.WriteBit(1); // H.263 discriminator
    writer.WriteBit(0);
    writer.Write(0, 3); // display flags
    writer.Write(7, 3); // extended PTYPE

    writer.Write(1, 3); // UFEP=001
    writer.Write(sourceFormat, 3);
    writer.WriteBit(0); // custom PCF
    writer.Write(0, 10); // optional coding modes
    writer.WriteBit(1); // OPPTYPE marker
    writer.Write(0, 3); // reserved

    writer.Write((int)pictureKind, 3);
    writer.Write(0, 2); // RPR/RRU
    writer.WriteBit(0); // RTYPE
    writer.Write(0, 2); // reserved
    writer.WriteBit(1); // MPPTYPE marker
    writer.WriteBit(0); // CPM

    if (sourceFormat == 6) {
      writer.Write(pixelAspectRatio, 4);
      writer.Write(width / 4 - 1, 9);
      writer.WriteBit(1); // CPFMT anti-emulation marker
      writer.Write(heightIndication, 9);
      if (pixelAspectRatio == 15) {
        writer.Write(parWidth, 8);
        writer.Write(parHeight, 8);
      }
    }

    if (pictureKind == H263PictureKind.Bidirectional) {
      writer.Write(2, 4); // ELNUM
      writer.Write(1, 4); // RLNUM
    }

    writer.Write(quantiser, 5);
    writer.WriteBit(0); // PEI
    return writer.ToArray();
  }
}