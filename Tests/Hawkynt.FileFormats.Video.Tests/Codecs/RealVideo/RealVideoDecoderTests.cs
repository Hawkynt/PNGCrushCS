using System;
using System.IO;
using System.Linq;
using FileFormat.Codecs.H263.Tests;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;

namespace FileFormat.Codecs.RealVideo.Tests;

[TestFixture]
public sealed class RealVideoDecoderTests {

  [TestCase("RV10")]
  [TestCase("RV13")]
  [TestCase("rv10")]
  [Category("Unit")]
  public void ItTakesTheCodesRealVideoOneIsNamedBy(string code)
    => Assert.That(RealVideoDecoder.Accepts(RealVideoTestStream.Stream(code)), Is.True);

  [TestCase("RV20")]
  [TestCase("RV30")]
  [TestCase("RV40")]
  [Category("Unit")]
  public void TheLaterGenerations_AreNotTakenAtAll(string code) {
    var stream = RealVideoTestStream.Stream(code);
    Assert.That(RealVideoDecoder.Accepts(stream), Is.False);
    Assert.That(VideoFormatRegistry.CanDecode(stream), Is.False);
  }

  [Test]
  [Category("Unit")]
  public void ASoundStreamIsNotTaken()
    => Assert.That(
      RealVideoDecoder.Accepts(new() { Index = 0, Kind = MediaStreamKind.Audio, Codec = CodecTag.FromCharacters("RV10") }),
      Is.False);

  [TestCase("RV20")]
  [TestCase("RV30")]
  [TestCase("RV40")]
  [Category("Unit")]
  public void ALaterGenerationHandedHereAnyway_IsRefusedByName(string code) {
    var failure = Assert.Throws<NotSupportedException>(() => RealVideoDecoder.Create(RealVideoTestStream.Stream(code)));
    Assert.That(failure!.Message, Does.Contain(code));
  }

  [TestCase(1)]
  [TestCase(3)]
  [Category("Unit")]
  public void ANonzeroMicroVersion_IsRefusedByName(int micro) {
    var stream = RealVideoTestStream.Stream("RV10", codecPrivateData: RealVideoTestStream.Micro(micro));

    var failure = Assert.Throws<NotSupportedException>(() => RealVideoDecoder.Create(stream));
    Assert.Multiple(() => {
      Assert.That(failure!.Message, Does.Contain("minor 0"));
      Assert.That(failure.Message, Does.Contain($"micro {micro}"));
    });
  }

  [Test]
  [Category("Unit")]
  public void AStreamWhoseCodeAndVersionWordDisagree_IsRefused() {
    var stream = RealVideoTestStream.Stream("RV10", codecPrivateData: [0, 0, 0, 8, 0x20, 0, 0, 0]);
    Assert.Throws<NotSupportedException>(() => RealVideoDecoder.Create(stream));
  }

  [Test]
  [Category("Unit")]
  public void AStreamWithNoPictureSize_IsRefusedByName() {
    var stream = RealVideoTestStream.Stream("RV10", width: 0, height: 0);
    var failure = Assert.Throws<NotSupportedException>(() => RealVideoDecoder.Create(stream));
    Assert.That(failure!.Message, Does.Contain("carries no size"));
  }

  [Test]
  [Category("Unit")]
  public void APictureTooLargeForItsRunPositionsToBeStated_IsRefusedByName() {
    var stream = RealVideoTestStream.Stream("RV10", width: 1600, height: 144);
    var failure = Assert.Throws<NotSupportedException>(() => RealVideoDecoder.Create(stream));
    Assert.That(failure!.Message, Does.Contain("sixty-three"));
  }

  [Test]
  [Category("Unit")]
  public void APictureInOneRun_DecodesToItsStatedSize() {
    var picture = RealVideoTestStream.Picture(true, 8, (0, 0, _MACROBLOCKS))
      .FlatIntraMacroblocks(_MACROBLOCKS, 140)
      .ToArray();

    var decoder = RealVideoDecoder.Create(RealVideoTestStream.Stream("RV10", _WIDTH, _HEIGHT));
    Assert.That(decoder.TryDecode(RealVideoTestStream.Packet(picture), out var frame), Is.True);
    Assert.That(frame.Width, Is.EqualTo(_WIDTH));
    Assert.That(frame.Height, Is.EqualTo(_HEIGHT));
    Assert.That(frame.Format, Is.EqualTo(PixelFormat.Rgb24));
  }

  [Test]
  [Category("Unit")]
  public void Rv10sTwelveBitEscapeLevel_IsDecoded() {
    var picture = RealVideoTestStream.Picture(true, 8, (0, 0, 1))
      .Code(H263TestStream.IntraMacroblock)
      .Code(H263TestStream.FirstLuminanceCoded)
      .IntraBlock(100)
      .Code(H263TestStream.CoefficientEscape)
      .Bits(1, 1)
      .Bits(0, 6)
      .Bits(0x80, 8)
      .Bits(200, 12)
      .IntraBlock(100)
      .IntraBlock(100)
      .IntraBlock(100)
      .IntraBlock(255)
      .IntraBlock(255)
      .ToArray();

    var decoder = RealVideoDecoder.Create(RealVideoTestStream.Stream("RV10", 16, 16));
    Assert.That(decoder.TryDecode(RealVideoTestStream.Packet(picture), out _), Is.True);
  }

  [Test]
  [Category("Unit")]
  public void APictureCutIntoRuns_IsDecodedFromTheOffsetsTheContainerReported() {
    var half = _MACROBLOCKS / 2;
    var first = RealVideoTestStream.Picture(true, 8, (0, 0, half)).FlatIntraMacroblocks(half, 100).ToArray();
    var second = RealVideoTestStream.Picture(true, 8, (half % _MACROBLOCK_WIDTH, half / _MACROBLOCK_WIDTH, _MACROBLOCKS - half))
      .FlatIntraMacroblocks(_MACROBLOCKS - half, 200).ToArray();

    var decoder = RealVideoDecoder.Create(RealVideoTestStream.Stream("RV10", _WIDTH, _HEIGHT));
    Assert.That(decoder.TryDecode(RealVideoTestStream.Packet(first, second), out var frame), Is.True);

    var rgb = frame.PixelData;
    var topLeft = rgb[0];
    var bottomRight = rgb[((_HEIGHT - 1) * _WIDTH * 3) + ((_WIDTH - 1) * 3)];
    Assert.That(bottomRight, Is.GreaterThan(topLeft));
  }

  [Test]
  [Category("Unit")]
  public void APictureWhoseRunsLeaveAGap_IsRefused() {
    var first = RealVideoTestStream.Picture(true, 8, (0, 0, 10)).FlatIntraMacroblocks(10, 140).ToArray();
    var second = RealVideoTestStream.Picture(true, 8, (0, 5, _MACROBLOCKS - 55)).FlatIntraMacroblocks(_MACROBLOCKS - 55, 140).ToArray();

    var decoder = RealVideoDecoder.Create(RealVideoTestStream.Stream("RV10", _WIDTH, _HEIGHT));
    Assert.Throws<InvalidDataException>(() => decoder.TryDecode(RealVideoTestStream.Packet(first, second), out _));
  }

  [Test]
  [Category("Unit")]
  public void APictureWhoseRunsStopShort_IsRefusedRatherThanHandedOverPartlyDecoded() {
    var only = RealVideoTestStream.Picture(true, 8, (0, 0, 10)).FlatIntraMacroblocks(10, 140).ToArray();
    var decoder = RealVideoDecoder.Create(RealVideoTestStream.Stream("RV10", _WIDTH, _HEIGHT));
    var failure = Assert.Throws<InvalidDataException>(() => decoder.TryDecode(RealVideoTestStream.Packet(only), out _));
    Assert.That(failure!.Message, Does.Contain("stops after 10"));
  }

  [Test]
  [Category("Unit")]
  public void APredictedPictureBeforeAnyIntraOne_IsRefused() {
    var picture = RealVideoTestStream.Picture(false, 8, (0, 0, _MACROBLOCKS))
      .NotCodedMacroblocks(_MACROBLOCKS)
      .ToArray();

    var decoder = RealVideoDecoder.Create(RealVideoTestStream.Stream("RV10", _WIDTH, _HEIGHT));
    Assert.Throws<InvalidDataException>(() => decoder.TryDecode(RealVideoTestStream.Packet(picture), out _));
  }

  [Test]
  [Category("Unit")]
  public void APredictedPictureAfterAnIntraOne_RepeatsWhatWasNotCoded() {
    var intra = RealVideoTestStream.Picture(true, 8, (0, 0, _MACROBLOCKS)).FlatIntraMacroblocks(_MACROBLOCKS, 200).ToArray();
    var predicted = RealVideoTestStream.Picture(false, 8, (0, 0, _MACROBLOCKS)).NotCodedMacroblocks(_MACROBLOCKS).ToArray();

    var decoder = RealVideoDecoder.Create(RealVideoTestStream.Stream("RV10", _WIDTH, _HEIGHT));
    Assert.That(decoder.TryDecode(RealVideoTestStream.Packet(intra), out var first), Is.True);
    Assert.That(decoder.TryDecode(RealVideoTestStream.Packet(predicted), out var second), Is.True);
    Assert.That(second.PixelData, Is.EqualTo(first.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void APbFrame_IsRefusedByName() {
    var picture = RealVideoTestStream.Picture(true, 8, (0, 0, _MACROBLOCKS), isPbFrame: true).FlatIntraMacroblocks(_MACROBLOCKS, 140).ToArray();
    var decoder = RealVideoDecoder.Create(RealVideoTestStream.Stream("RV10", _WIDTH, _HEIGHT));
    var failure = Assert.Throws<NotSupportedException>(() => decoder.TryDecode(RealVideoTestStream.Packet(picture), out _));
    Assert.That(failure!.Message, Does.Contain("PB-frame"));
  }

  [Test]
  [Category("Unit")]
  public void AQuantiserOfZero_IsRefused() {
    var picture = RealVideoTestStream.Picture(true, 0, (0, 0, _MACROBLOCKS)).FlatIntraMacroblocks(_MACROBLOCKS, 140).ToArray();
    var decoder = RealVideoDecoder.Create(RealVideoTestStream.Stream("RV10", _WIDTH, _HEIGHT));
    Assert.Throws<InvalidDataException>(() => decoder.TryDecode(RealVideoTestStream.Packet(picture), out _));
  }

  [Test]
  [Category("Unit")]
  public void AHeaderWithoutItsMarkerBit_IsRefused() {
    var picture = RealVideoTestStream.Picture(true, 8, (0, 0, _MACROBLOCKS)).FlatIntraMacroblocks(_MACROBLOCKS, 140).ToArray();
    picture[0] &= 0x7F;
    var decoder = RealVideoDecoder.Create(RealVideoTestStream.Stream("RV10", _WIDTH, _HEIGHT));
    Assert.Throws<InvalidDataException>(() => decoder.TryDecode(RealVideoTestStream.Packet(picture), out _));
  }

  [Test]
  [Category("Unit")]
  public void ARunThatWouldRunOffTheEndOfThePicture_IsRefused() {
    var picture = RealVideoTestStream.Picture(true, 8, (0, 0, _MACROBLOCKS + 40))
      .FlatIntraMacroblocks(_MACROBLOCKS, 140).ToArray();
    var decoder = RealVideoDecoder.Create(RealVideoTestStream.Stream("RV10", _WIDTH, _HEIGHT));
    Assert.Throws<InvalidDataException>(() => decoder.TryDecode(RealVideoTestStream.Packet(picture), out _));
  }

  [Test]
  [Category("Unit")]
  public void AFirstRunThatLeavesItsPositionOut_IsRefusedRatherThanGuessedAt() {
    var picture = RealVideoTestStream.Picture(true, 8).FlatIntraMacroblocks(_MACROBLOCKS, 140).ToArray();
    var decoder = RealVideoDecoder.Create(RealVideoTestStream.Stream("RV10", _WIDTH, _HEIGHT));
    var failure = Assert.Throws<NotSupportedException>(() => decoder.TryDecode(RealVideoTestStream.Packet(picture), out _));
    Assert.That(failure!.Message, Does.Contain("leaves the macroblock position out"));
  }

  [Test]
  [Category("Unit")]
  public void AnEmptyPacket_HoldsNoPicture() {
    var decoder = RealVideoDecoder.Create(RealVideoTestStream.Stream("RV10", _WIDTH, _HEIGHT));
    Assert.That(decoder.TryDecode(new(0, ReadOnlyMemory<byte>.Empty), out _), Is.False);
  }

  [Test]
  [Category("Unit")]
  public void NothingIsEverHeldBack()
    => Assert.That(RealVideoDecoder.Create(RealVideoTestStream.Stream("RV10")).Flush(), Is.Empty);

  private const int _WIDTH = 176;
  private const int _HEIGHT = 144;
  private const int _MACROBLOCK_WIDTH = (_WIDTH + 15) / 16;
  private const int _MACROBLOCKS = _MACROBLOCK_WIDTH * ((_HEIGHT + 15) / 16);
}