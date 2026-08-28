using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FileFormat.Core;
using static FileFormat.Codecs.Vp9.Vp9Constants;

namespace FileFormat.Codecs.Vp9.Tests;

/// <summary>The VP9 decoder, on frames built here.</summary>
/// <remarks>
/// Codec reconstruction is native YUV. Display-oriented assertions explicitly convert through
/// <see cref="RawImageConverter"/>, keeping colour conversion out of the decoder contract.
/// </remarks>
[TestFixture]
public sealed class Vp9VideoDecoderTests {

  // ============================================================================================
  // Identity
  // ============================================================================================

  [TestCase("V_VP9")]
  [TestCase("v_vp9")]
  [Category("Unit")]
  public void TheCodecTakesTheNameMatroskaGivesIt(string codecId)
    => Assert.That(Vp9VideoDecoder.Accepts(_Stream(codecId: codecId)), Is.True);

  [TestCase("VP90")]
  [TestCase("vp09")]
  [Category("Unit")]
  public void TheCodecTakesTheCodesContainersWithACodeFieldGiveIt(string code)
    => Assert.That(Vp9VideoDecoder.Accepts(_Stream(code: code)), Is.True);

  [Test]
  [Category("Unit")]
  public void TheCodecLeavesOtherStreamsAlone() {
    Assert.That(Vp9VideoDecoder.Accepts(_Stream(codecId: "V_VP8")), Is.False);
    Assert.That(Vp9VideoDecoder.Accepts(_Stream(codecId: "V_AV1")), Is.False);
    Assert.That(Vp9VideoDecoder.Accepts(_Stream(code: "VP80")), Is.False);
    Assert.That(Vp9VideoDecoder.Accepts(_Stream(codecId: "A_VORBIS", kind: MediaStreamKind.Audio)), Is.False);
    Assert.That(Vp9VideoDecoder.Accepts(_Stream(codecId: "V_VP9", kind: MediaStreamKind.Audio)), Is.False);
  }

  [Test]
  [Category("Unit")]
  public void TheCodecIsNamedForTheProfilesItReads()
    => Assert.That(Vp9VideoDecoder.CodecName, Does.Contain("VP9").And.Contain("profiles 0-3"));

  [Test]
  [Category("Unit")]
  public void CreatingADecoderForNothingIsRefused() {
    Assert.Throws<ArgumentNullException>(() => Vp9VideoDecoder.Create(null!));
    Assert.Throws<ArgumentNullException>(() => Vp9VideoDecoder.Accepts(null!));
  }

  // ============================================================================================
  // Reconstruction
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void ThePublicDecoderReturnsTheExactNativePlanes() {
    var frame = _DecodeNative(Vp9TestStream.BuildKeyFrame(new() { UniformMode = true }));

    Assert.That(frame.Format, Is.EqualTo(PixelFormat.Yuv420P8));
    Assert.That(frame.GetPlaneData(0).ToArray(), Is.All.EqualTo(128));
    Assert.That(frame.GetPlaneData(1).ToArray(), Is.All.EqualTo(128));
    Assert.That(frame.GetPlaneData(2).ToArray(), Is.All.EqualTo(128));
    Assert.That(frame.ColorInfo!.Range, Is.EqualTo(RawColorRange.Limited));
    Assert.That(frame.ColorInfo.Matrix, Is.EqualTo(RawMatrixCoefficients.Bt601));
  }

  [Test]
  [Category("Unit")]
  public void AKeyFrameOfSkippedBlocksPredictedFlatIsMidGrey() {
    var frame = _Decode(Vp9TestStream.BuildKeyFrame(new() { UniformMode = true }));

    Assert.That(frame.Width, Is.EqualTo(8));
    Assert.That(frame.Height, Is.EqualTo(8));
    Assert.That(frame.Format, Is.EqualTo(PixelFormat.Rgb24));
    Assert.That(frame.PixelData.Length, Is.EqualTo(8 * 8 * 3));
    Assert.That(frame.PixelData.Distinct().ToArray(), Is.EqualTo(new byte[] { 130 }));
  }

  [TestCase(8, 8)]
  [TestCase(64, 48)]
  [TestCase(65, 33)]
  [TestCase(130, 98)]
  [TestCase(17, 11)]
  [Category("Unit")]
  public void ThePictureComesOutTheSizeTheKeyFrameStates(int width, int height) {
    var frame = _Decode(Vp9TestStream.BuildKeyFrame(new() { Width = width, Height = height }));

    Assert.That(frame.Width, Is.EqualTo(width));
    Assert.That(frame.Height, Is.EqualTo(height));
    Assert.That(frame.PixelData.Length, Is.EqualTo(width * height * 3));
  }

  [Test]
  [Category("Unit")]
  public void TheLoopFilterLeavesAFlatPictureAlone() {
    var filtered = _Decode(
      Vp9TestStream.BuildKeyFrame(
        new() { Width = 64, Height = 48, UniformMode = true, LoopFilterLevel = 63, LoopFilterSharpness = 7 }));
    var unfiltered = _Decode(Vp9TestStream.BuildKeyFrame(new() { Width = 64, Height = 48, UniformMode = true }));

    Assert.That(filtered.PixelData, Is.EqualTo(unfiltered.PixelData));
  }

  [TestCase(0)]
  [TestCase(1)]
  [TestCase(4)]
  [TestCase(7)]
  [Category("Unit")]
  public void TheLoopFilterMovesAPictureThatHasEdgesInIt(int sharpness) {
    var filtered = _Decode(
      Vp9TestStream.BuildKeyFrame(
        new() { Width = 64, Height = 48, LoopFilterLevel = 32, LoopFilterSharpness = sharpness }));
    var unfiltered = _Decode(Vp9TestStream.BuildKeyFrame(new() { Width = 64, Height = 48 }));

    Assert.That(filtered.PixelData, Is.Not.EqualTo(unfiltered.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void TheFilterAdjustmentAFrameStatesForIntraBlocksIsApplied() {
    var unfiltered = _Decode(Vp9TestStream.BuildKeyFrame(new() { Width = 64, Height = 48 }));
    var cancelled = _Decode(
      Vp9TestStream.BuildKeyFrame(
        new() {
          Width = 64, Height = 48, LoopFilterLevel = 32, LoopFilterDeltas = true,
          ReferenceDeltas = [-32, 0, 0, 0],
        }));

    Assert.That(cancelled.PixelData, Is.EqualTo(unfiltered.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void TheSignOfAFilterAdjustmentIsAFlagAndNotTwosComplement() {
    var raised = _Decode(
      Vp9TestStream.BuildKeyFrame(
        new() {
          Width = 64, Height = 48, LoopFilterLevel = 32, LoopFilterDeltas = true, ReferenceDeltas = [32, 0, 0, 0],
        }));
    var lowered = _Decode(
      Vp9TestStream.BuildKeyFrame(
        new() {
          Width = 64, Height = 48, LoopFilterLevel = 32, LoopFilterDeltas = true, ReferenceDeltas = [-32, 0, 0, 0],
        }));

    Assert.That(raised.PixelData, Is.Not.EqualTo(lowered.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void AFrameTheStreamAsksNotToShowIsNotHandedBack() {
    var decoder = Vp9VideoDecoder.Create(_Stream(codecId: "V_VP9"));
    var hidden = Vp9TestStream.BuildKeyFrame(new() { ShowFrame = false });

    Assert.That(decoder.TryDecode(new(0, hidden), out _), Is.False);
    Assert.That(decoder.Flush(), Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void ASuperframeHandsBackOnlyTheFrameItShows() {
    var hidden = Vp9TestStream.BuildKeyFrame(new() { ShowFrame = false, UniformMode = true });
    var shown = Vp9TestStream.BuildKeyFrame(new() { UniformMode = true });
    var packet = _Superframe(hidden, shown);

    var decoder = Vp9VideoDecoder.Create(_Stream(codecId: "V_VP9"));
    Assert.That(decoder.TryDecode(new(0, packet), out var native), Is.True);
    var frame = RawImageConverter.Convert(native, PixelFormat.Rgb24);
    Assert.That(frame.PixelData.Distinct().ToArray(), Is.EqualTo(new byte[] { 130 }));
    Assert.That(decoder.Flush(), Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void AFrameThatShowsAnEarlierOneRepeatsItExactly() {
    var pictures = _DecodeAll(
      Vp9TestStream.BuildKeyFrame(new() { Width = 32, Height = 32 }),
      Vp9TestStream.BuildShowExistingFrame(0));

    Assert.That(pictures, Has.Count.EqualTo(2));
    Assert.That(pictures[1].PixelData, Is.EqualTo(pictures[0].PixelData));
  }

  [Test]
  [Category("Unit")]
  public void AnIntraOnlyFrameIsDecodedAndKeptWithoutBeingShown() {
    var key = Vp9TestStream.BuildKeyFrame(new() { Width = 32, Height = 32, UniformMode = true });
    var intraOnly = Vp9TestStream.BuildKeyFrame(new() { Width = 32, Height = 32, IntraOnly = true });
    var alone = Vp9TestStream.BuildKeyFrame(new() { Width = 32, Height = 32 });

    var pictures = _DecodeAll(key, intraOnly, Vp9TestStream.BuildShowExistingFrame(0));

    Assert.That(pictures, Has.Count.EqualTo(2), "the intra-only frame was shown");
    Assert.That(pictures[0].PixelData.Distinct().ToArray(), Is.EqualTo(new byte[] { 130 }));
    Assert.That(pictures[1].PixelData, Is.EqualTo(_Decode(alone).PixelData));
  }

  [TestCase(0)]
  [TestCase(1)]
  [TestCase(2)]
  [TestCase(3)]
  [Category("Unit")]
  public void TheFrameContextResetIsReadWithoutChangingThePicture(int reset) {
    var key = Vp9TestStream.BuildKeyFrame(new() { Width = 32, Height = 32, UniformMode = true });
    var intraOnly = Vp9TestStream.BuildKeyFrame(
      new() { Width = 32, Height = 32, IntraOnly = true, ResetFrameContext = reset });
    var alone = Vp9TestStream.BuildKeyFrame(new() { Width = 32, Height = 32 });

    var pictures = _DecodeAll(key, intraOnly, Vp9TestStream.BuildShowExistingFrame(0));

    Assert.That(pictures, Has.Count.EqualTo(2));
    Assert.That(pictures[1].PixelData, Is.EqualTo(_Decode(alone).PixelData));
  }

  [Test]
  [Category("Unit")]
  public void AnErrorResilientFrameStatesFewerFieldsAndDecodesTheSame() {
    var resilient = _Decode(Vp9TestStream.BuildKeyFrame(new() { Width = 32, Height = 32, ErrorResilient = true }));
    var plain = _Decode(Vp9TestStream.BuildKeyFrame(new() { Width = 32, Height = 32 }));

    Assert.That(resilient.PixelData, Is.EqualTo(plain.PixelData));
  }

  // ============================================================================================
  // Segmentation
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void ASegmentThatIsAlwaysSkippedCostsNoSkipFlagAndDecodesTheSame() {
    var segments = new Vp9TestStream.Segmentation();
    for (var segment = 0; segment < MAX_SEGMENTS; segment += 2)
      segments.Set(segment, SEG_LVL_SKIP, 0);

    var segmented = Vp9TestStream.BuildKeyFrame(new() { Width = 64, Height = 48, Segments = segments });
    var plain = Vp9TestStream.BuildKeyFrame(new() { Width = 64, Height = 48 });

    Assert.That(segmented, Is.Not.EqualTo(plain), "the two frames are the same bitstream");
    Assert.That(_Decode(segmented).PixelData, Is.EqualTo(_Decode(plain).PixelData));
  }

  [Test]
  [Category("Unit")]
  public void ASegmentThatStatesAFilterLevelOfZeroIsNotFiltered() {
    var segments = new Vp9TestStream.Segmentation { AbsoluteValues = true };
    for (var segment = 0; segment < MAX_SEGMENTS; segment += 2)
      segments.Set(segment, SEG_LVL_ALT_L, 0);

    var segmented = _Decode(
      Vp9TestStream.BuildKeyFrame(new() { Width = 64, Height = 48, LoopFilterLevel = 32, Segments = segments }));
    var filtered = _Decode(Vp9TestStream.BuildKeyFrame(new() { Width = 64, Height = 48, LoopFilterLevel = 32 }));
    var unfiltered = _Decode(Vp9TestStream.BuildKeyFrame(new() { Width = 64, Height = 48 }));

    Assert.That(segmented.PixelData, Is.Not.EqualTo(filtered.PixelData));
    Assert.That(segmented.PixelData, Is.Not.EqualTo(unfiltered.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void SegmentValuesStatedOutrightAreNotAddedToTheFramesOwn() {
    var absolute = new Vp9TestStream.Segmentation { AbsoluteValues = true };
    var relative = new Vp9TestStream.Segmentation();
    for (var segment = 0; segment < MAX_SEGMENTS; ++segment) {
      absolute.Set(segment, SEG_LVL_ALT_L, 0);
      relative.Set(segment, SEG_LVL_ALT_L, 0);
    }

    var withValues = _Decode(
      Vp9TestStream.BuildKeyFrame(new() { Width = 64, Height = 48, LoopFilterLevel = 32, Segments = absolute }));
    var withDeltas = _Decode(
      Vp9TestStream.BuildKeyFrame(new() { Width = 64, Height = 48, LoopFilterLevel = 32, Segments = relative }));
    var unfiltered = _Decode(Vp9TestStream.BuildKeyFrame(new() { Width = 64, Height = 48 }));

    Assert.That(withValues.PixelData, Is.EqualTo(unfiltered.PixelData));
    Assert.That(withDeltas.PixelData, Is.Not.EqualTo(unfiltered.PixelData));
  }

  // ============================================================================================
  // Refusals
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void AMissingFrameMarkerIsRefusedByName() {
    var failure = Assert.Throws<InvalidDataException>(
      () => _Decode(Vp9TestStream.BuildKeyFrame(new() { FrameMarker = 1 })));

    Assert.That(failure!.Message, Does.Contain("frame marker"));
  }

  [Test]
  [Category("Unit")]
  public void AMissingSyncCodeIsRefusedByName() {
    var failure = Assert.Throws<InvalidDataException>(
      () => _Decode(Vp9TestStream.BuildKeyFrame(new() { SyncCode = [0x49, 0x83, 0x43] })));

    Assert.That(failure!.Message, Does.Contain("49 83 42"));
  }

  [Test]
  [Category("Unit")]
  public void TheColourSpaceProfileZeroCannotCarryIsRefusedByName() {
    var failure = Assert.Throws<InvalidDataException>(
      () => _Decode(Vp9TestStream.BuildKeyFrame(new() { ColorSpace = 7 })));

    Assert.That(failure!.Message, Does.Contain("sRGB").And.Contain("profile-0"));
  }

  [Test]
  [Category("Unit")]
  public void APacketTooShortForItsHeaderIsRefusedByName() {
    var frame = Vp9TestStream.BuildKeyFrame(new());
    var failure = Assert.Throws<InvalidDataException>(() => _Decode(frame[..4]));

    Assert.That(failure!.Message, Does.Contain("uncompressed header"));
  }

  [Test]
  [Category("Unit")]
  public void ACompressedHeaderLargerThanThePacketIsRefusedByName() {
    var frame = Vp9TestStream.BuildKeyFrame(new());
    _SetCompressedHeaderSize(frame, 0x7FFF);

    Assert.That(Assert.Throws<InvalidDataException>(() => _Decode(frame))!.Message,
      Does.Contain("compressed header").And.Contain("truncated"));
  }

  [Test]
  [Category("Unit")]
  public void ACompressedHeaderOfNoBytesIsRefusedByName() {
    var frame = Vp9TestStream.BuildKeyFrame(new());
    _SetCompressedHeaderSize(frame, 0);

    Assert.That(Assert.Throws<InvalidDataException>(() => _Decode(frame))!.Message, Does.Contain("zero bytes"));
  }

  [Test]
  [Category("Unit")]
  public void AnEmptyPacketIsRefusedByName()
    => Assert.That(Assert.Throws<InvalidDataException>(() => _Decode([]))!.Message, Does.Contain("empty"));

  [Test]
  [Category("Unit")]
  public void ASuperframeStatingMoreThanItHoldsIsRefusedByName() {
    var frame = Vp9TestStream.BuildKeyFrame(new());
    var packet = _Superframe(frame, frame);
    packet[^5] = 0xFF;

    Assert.That(Assert.Throws<InvalidDataException>(() => _Decode(packet))!.Message, Does.Contain("superframe"));
  }

  [Test]
  [Category("Unit")]
  public void ShowingAReferenceSlotNothingHasWrittenIsRefusedByName() {
    Assert.That(Assert.Throws<InvalidDataException>(() => _Decode(Vp9TestStream.BuildShowExistingFrame(3)))!.Message,
      Does.Contain("reference slot").And.Contain("shown"));
  }

  // ============================================================================================
  // Helpers
  // ============================================================================================

  private static MediaStreamInfo _Stream(
    string? codecId = null, string? code = null, MediaStreamKind kind = MediaStreamKind.Video)
    => new() {
      Index = 0,
      Kind = kind,
      CodecId = codecId,
      Codec = code == null ? CodecTag.None : CodecTag.FromCharacters(code),
    };

  private static RawImage _DecodeNative(byte[] packet) {
    var decoder = Vp9VideoDecoder.Create(_Stream(codecId: "V_VP9"));
    Assert.That(decoder.TryDecode(new(0, packet), out var picture), Is.True, "no picture was shown");
    return picture;
  }

  /// <summary>Display-oriented compatibility helper. The decoder itself remains native YUV.</summary>
  private static RawImage _Decode(byte[] packet)
    => RawImageConverter.Convert(_DecodeNative(packet), PixelFormat.Rgb24);

  private static List<RawImage> _DecodeAll(params byte[][] packets) {
    var decoder = Vp9VideoDecoder.Create(_Stream(codecId: "V_VP9"));
    var pictures = new List<RawImage>();

    foreach (var packet in packets)
      if (decoder.TryDecode(new(0, packet), out var picture))
        pictures.Add(RawImageConverter.Convert(picture, PixelFormat.Rgb24));

    foreach (var picture in decoder.Flush())
      pictures.Add(RawImageConverter.Convert(picture, PixelFormat.Rgb24));

    return pictures;
  }

  private static byte[] _Superframe(params byte[][] frames) {
    var marker = (byte)(0xC0 | (3 << 3) | (frames.Length - 1));
    var index = new byte[2 + frames.Length * 4];
    index[0] = marker;
    index[^1] = marker;

    for (var i = 0; i < frames.Length; ++i) {
      var size = frames[i].Length;
      for (var b = 0; b < 4; ++b)
        index[1 + i * 4 + b] = (byte)(size >> (8 * b));
    }

    var packet = new byte[frames.Sum(frame => frame.Length) + index.Length];
    var at = 0;
    foreach (var frame in frames) {
      frame.CopyTo(packet, at);
      at += frame.Length;
    }

    index.CopyTo(packet, at);
    return packet;
  }

  private static void _SetCompressedHeaderSize(byte[] frame, int size) {
    const int AT = 12;
    Assert.That(frame[AT], Is.Zero, "the compressed header size is not where this test expects it");
    Assert.That(frame[AT + 1], Is.EqualTo(4), "the compressed header size is not where this test expects it");

    frame[AT] = (byte)(size >> 8);
    frame[AT + 1] = (byte)size;
  }
}
