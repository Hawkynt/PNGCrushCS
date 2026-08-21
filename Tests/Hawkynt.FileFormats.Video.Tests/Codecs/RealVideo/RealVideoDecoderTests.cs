using System;
using System.IO;
using System.Linq;
using FileFormat.Codecs.H263.Tests;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;

namespace FileFormat.Codecs.RealVideo.Tests;

/// <summary>
/// The RealVideo 1 decoder's behaviour.
/// </summary>
/// <remarks>
/// The arithmetic was checked against ffmpeg over twenty-seven encoded streams, frame by frame and
/// plane by plane: sizes from 96x64 to 352x288, quantisers from 2 to 31, intra-only streams and groups
/// of pictures up to fifty. Against ffmpeg's floating-point inverse transform, two hundred and
/// thirty-five of two hundred and thirty-eight frames are identical sample for sample and the other
/// three differ in five samples between them, always by one level. Those numbers are not asserted here
/// — they need ffmpeg and a corpus that is not in the tree — and what is asserted instead is the
/// syntax ffmpeg's own encoder cannot emit: a picture cut into runs, which it refuses to produce at
/// all, and every refusal, which by definition no valid stream produces.
/// </remarks>
[TestFixture]
public sealed class RealVideoDecoderTests {

  // ------------------------------------------------------------------------------------------
  // Which streams it takes
  // ------------------------------------------------------------------------------------------

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
    // Not taken rather than taken and then refused, so that a caller asking whether anything reads an
    // RV40 stream is told no once instead of being handed a decoder that fails on its first packet.
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
  public void ARevisionWhoseMacroblockLayerIsNotImplemented_IsRefusedByName(int minor) {
    // The recordings in the wild state these. Their macroblock layer is not the H.263 one this shares:
    // no offset into one of their pictures decodes even three macroblocks with the H.263 tables. A
    // decoder that read one as though it were revision 0 would produce noise that looks like a picture.
    var stream = RealVideoTestStream.Stream("RV10", codecPrivateData: RealVideoTestStream.Revision(minor));

    var failure = Assert.Throws<NotSupportedException>(() => RealVideoDecoder.Create(stream));
    Assert.That(failure!.Message, Does.Contain($"revision {minor}"));
  }

  [Test]
  [Category("Unit")]
  public void AStreamWhoseCodeAndVersionWordDisagree_IsRefused() {
    // A file that says RV10 in the container and states an RV20 bitstream in its private data is one
    // thing claiming to be another, and picking either would be this decoder deciding which to believe.
    var stream = RealVideoTestStream.Stream("RV10", codecPrivateData: [0, 0, 0, 8, 0x20, 0, 0, 0]);

    Assert.Throws<NotSupportedException>(() => RealVideoDecoder.Create(stream));
  }

  [Test]
  [Category("Unit")]
  public void AStreamWithNoPictureSize_IsRefusedByName() {
    // RealVideo carries no size in its bitstream, so the container's is the only one there is.
    var stream = RealVideoTestStream.Stream("RV10", width: 0, height: 0);

    var failure = Assert.Throws<NotSupportedException>(() => RealVideoDecoder.Create(stream));
    Assert.That(failure!.Message, Does.Contain("carries no size"));
  }

  [Test]
  [Category("Unit")]
  public void APictureTooLargeForItsRunPositionsToBeStated_IsRefusedByName() {
    // The position fields are six bits each way, so a picture past sixty-three macroblocks cannot say
    // where its runs begin.
    var stream = RealVideoTestStream.Stream("RV10", width: 1600, height: 144);

    var failure = Assert.Throws<NotSupportedException>(() => RealVideoDecoder.Create(stream));
    Assert.That(failure!.Message, Does.Contain("sixty-three"));
  }

  // ------------------------------------------------------------------------------------------
  // Decoding
  // ------------------------------------------------------------------------------------------

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
  public void APictureCutIntoRuns_IsDecodedFromTheOffsetsTheContainerReported() {
    // The shape every recording in the wild has, and the one ffmpeg's encoder refuses to produce. The
    // runs carry no start code and the padding between them is not fixed, so the offsets on the packet
    // are the only record of where each begins.
    var half = _MACROBLOCKS / 2;
    var first = RealVideoTestStream.Picture(true, 8, (0, 0, half)).FlatIntraMacroblocks(half, 100).ToArray();
    var second = RealVideoTestStream.Picture(true, 8, (half % _MACROBLOCK_WIDTH, half / _MACROBLOCK_WIDTH, _MACROBLOCKS - half))
      .FlatIntraMacroblocks(_MACROBLOCKS - half, 200).ToArray();

    var decoder = RealVideoDecoder.Create(RealVideoTestStream.Stream("RV10", _WIDTH, _HEIGHT));
    Assert.That(decoder.TryDecode(RealVideoTestStream.Packet(first, second), out var frame), Is.True);

    // The two runs were coded at different levels, so the picture is light where the second one is and
    // darker where the first is — which is only true if both were decoded and placed correctly.
    var rgb = frame.PixelData;
    var topLeft = rgb[0];
    var bottomRight = rgb[((_HEIGHT - 1) * _WIDTH * 3) + ((_WIDTH - 1) * 3)];
    Assert.That(bottomRight, Is.GreaterThan(topLeft));
  }

  [Test]
  [Category("Unit")]
  public void APictureWhoseRunsLeaveAGap_IsRefused() {
    // A picture made of whichever runs arrived is worse than none: it looks like a picture, so nobody
    // checks it.
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

  // ------------------------------------------------------------------------------------------
  // Refusals in the picture header
  // ------------------------------------------------------------------------------------------

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
    // Every stream measured states the position even to say nought, so where the bits after it sit
    // when it is absent has never been checked against a reference decoder. Reading it wrongly would
    // produce noise shaped like a picture, which is worse than a refusal.
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
