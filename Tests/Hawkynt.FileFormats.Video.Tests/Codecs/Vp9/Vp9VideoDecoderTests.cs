using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FileFormat.Core;
using static FileFormat.Codecs.Vp9.Vp9Constants;

namespace FileFormat.Codecs.Vp9.Tests;

/// <summary>
/// The VP9 decoder, on frames built here.
/// </summary>
/// <remarks>
/// The decoder's arithmetic was checked by decoding ninety-two streams here, in ffmpeg and in libvpx
/// and comparing the sample planes frame by frame; what these tests add is what that comparison
/// cannot reach. Most of it is the refusals, which by definition no valid stream produces. The rest
/// is the syntax libvpx has but never chooses — intra-only frames, the frame context resets,
/// segmentation stating absolute values, and the per-segment loop filter level — and a handful of
/// frames whose expected samples can be worked out from the standard rather than recorded from a run.
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

    // The same name on an audio track is still not a picture.
    Assert.That(Vp9VideoDecoder.Accepts(_Stream(codecId: "V_VP9", kind: MediaStreamKind.Audio)), Is.False);
  }

  [Test]
  [Category("Unit")]
  public void TheCodecIsNamedForTheProfileItReads()
    => Assert.That(Vp9VideoDecoder.CodecName, Does.Contain("VP9").And.Contain("profile 0"));

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
  public void AKeyFrameOfSkippedBlocksPredictedFlatIsMidGrey() {
    // Nothing above or to the left of the first block, so direct current prediction fills it with
    // 128, and every block after it averages neighbours that are themselves 128. With no residue the
    // luminance is 128 and the chrominance neutral, which is
    // (298 * (128 - 16) + 128) >> 8 = 130 in all three channels.
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
    // VP9 codes whole superblocks whatever the picture size, so a picture that is not a whole number
    // of them is decoded larger and handed back cropped. The samples past the edge are real coded
    // samples that prediction reads, which is why they are kept until then.
    var frame = _Decode(Vp9TestStream.BuildKeyFrame(new() { Width = width, Height = height }));

    Assert.That(frame.Width, Is.EqualTo(width));
    Assert.That(frame.Height, Is.EqualTo(height));
    Assert.That(frame.PixelData.Length, Is.EqualTo(width * height * 3));
  }

  [Test]
  [Category("Unit")]
  public void TheLoopFilterLeavesAFlatPictureAlone() {
    // Every difference the filter measures is zero, so every adjustment it computes is zero. A
    // filter that moved a flat picture would be moving every picture.
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
    // The other half of the test above, and the one that would catch a filter that never runs: a
    // picture whose blocks each predict in a different direction has a discontinuity at every block
    // boundary, and the filter has to change it.
    var filtered = _Decode(
      Vp9TestStream.BuildKeyFrame(
        new() { Width = 64, Height = 48, LoopFilterLevel = 32, LoopFilterSharpness = sharpness }));
    var unfiltered = _Decode(Vp9TestStream.BuildKeyFrame(new() { Width = 64, Height = 48 }));

    Assert.That(filtered.PixelData, Is.Not.EqualTo(unfiltered.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void TheFilterAdjustmentAFrameStatesForIntraBlocksIsApplied() {
    // The adjustment is added to the filter level before it is used and the result is clipped, so an
    // adjustment far enough below zero turns the filter off for those blocks entirely. That is the
    // one outcome that can be asserted without recording samples: the picture has to come out
    // identical to the one from a frame that never asked to be filtered.
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
    // The same magnitude with the two signs has to move the level in opposite directions. Read as
    // two's complement instead, both would come out positive and this would pass for nothing.
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
    // VP9 codes frames that exist only to become references for later ones. Handing one back would
    // put a picture on screen that the film does not contain.
    var decoder = Vp9VideoDecoder.Create(_Stream(codecId: "V_VP9"));
    var hidden = Vp9TestStream.BuildKeyFrame(new() { ShowFrame = false });

    Assert.That(decoder.TryDecode(new(0, hidden), out _), Is.False);
    Assert.That(decoder.Flush(), Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void ASuperframeHandsBackOnlyTheFrameItShows() {
    // A chunk of several coded frames, indexed by its last bytes. Here the first is a reference the
    // stream keeps and does not show and the second is the picture, which is the arrangement an
    // alternate reference frame produces.
    var hidden = Vp9TestStream.BuildKeyFrame(new() { ShowFrame = false, UniformMode = true });
    var shown = Vp9TestStream.BuildKeyFrame(new() { UniformMode = true });
    var packet = _Superframe(hidden, shown);

    var decoder = Vp9VideoDecoder.Create(_Stream(codecId: "V_VP9"));
    Assert.That(decoder.TryDecode(new(0, packet), out var frame), Is.True);
    Assert.That(frame.PixelData.Distinct().ToArray(), Is.EqualTo(new byte[] { 130 }));
    Assert.That(decoder.Flush(), Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void AFrameThatShowsAnEarlierOneRepeatsItExactly() {
    // Two bytes of header and no coded data at all: the frame names one of the eight reference slots
    // and the decoder puts that picture on screen again.
    var pictures = _DecodeAll(
      Vp9TestStream.BuildKeyFrame(new() { Width = 32, Height = 32 }),
      Vp9TestStream.BuildShowExistingFrame(0));

    Assert.That(pictures, Has.Count.EqualTo(2));
    Assert.That(pictures[1].PixelData, Is.EqualTo(pictures[0].PixelData));
  }

  [Test]
  [Category("Unit")]
  public void AnIntraOnlyFrameIsDecodedAndKeptWithoutBeingShown() {
    // An intra-only frame is coded as a frame that is not shown — the flag saying it is intra-only
    // is only present when the frame is not shown — so the way to see one is to ask for the slot it
    // was written into afterwards.
    var key = Vp9TestStream.BuildKeyFrame(new() { Width = 32, Height = 32, UniformMode = true });
    var intraOnly = Vp9TestStream.BuildKeyFrame(new() { Width = 32, Height = 32, IntraOnly = true });
    var alone = Vp9TestStream.BuildKeyFrame(new() { Width = 32, Height = 32 });

    var pictures = _DecodeAll(key, intraOnly, Vp9TestStream.BuildShowExistingFrame(0));

    Assert.That(pictures, Has.Count.EqualTo(2), "the intra-only frame was shown");
    Assert.That(pictures[0].PixelData.Distinct().ToArray(), Is.EqualTo(new byte[] { 130 }));

    // The intra-only frame carries the same blocks as a key frame of the same shape, so what it
    // wrote into the slot has to be the picture that key frame would have produced.
    Assert.That(pictures[1].PixelData, Is.EqualTo(_Decode(alone).PixelData));
  }

  [TestCase(0)]
  [TestCase(1)]
  [TestCase(2)]
  [TestCase(3)]
  [Category("Unit")]
  public void TheFrameContextResetIsReadWithoutChangingThePicture(int reset) {
    // Which of the four saved probability sets a frame overwrites is a two-bit field of the header,
    // and an intra-only frame is the only kind that carries it. It cannot change this frame — an
    // intra frame decodes from the format's defaults whatever it says — but reading it as anything
    // other than two bits would put every field after it in the wrong place.
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
    // Error resilient mode removes three fields from the header — the frame context refresh, the
    // parallel decoding flag and the context reset — because a frame that must stand alone has
    // nothing to say about them. The picture is the same one.
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
    // A segment carrying the skip feature says so once in the frame header instead of once in every
    // block, so the bitstream is shorter — and since every block of this frame is skipped either way,
    // the picture has to come out identical. A decoder that read a skip flag for those blocks anyway
    // would be one bool out of step for the rest of the frame.
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
    // The one segment feature that shows in an intra frame with no coefficients. Half the segments
    // state a filter level of zero outright, so their blocks come out of the frame untouched while
    // the rest are filtered — a picture that is neither the filtered nor the unfiltered one.
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
    // The same numbers mean two different things depending on one flag: a distance from the frame's
    // filter level, or the level itself. Stated outright, a zero turns the filter off; taken as an
    // adjustment, a zero leaves it exactly where the frame put it.
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

  [TestCase(1)]
  [TestCase(2)]
  [TestCase(3)]
  [Category("Unit")]
  public void AProfileOtherThanZeroIsRefusedByName(int profile) {
    var failure = Assert.Throws<NotSupportedException>(
      () => _Decode(Vp9TestStream.BuildKeyFrame(new() { Profile = profile })));

    Assert.That(failure!.Message, Does.Contain($"profile {profile}").And.Contain("profile 0"));
  }

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
    // sRGB is a profile 1 and 3 feature. A profile 0 stream that names it is stating a chrominance
    // arrangement it has no way to carry.
    var failure = Assert.Throws<NotSupportedException>(
      () => _Decode(Vp9TestStream.BuildKeyFrame(new() { ColorSpace = 7 })));

    Assert.That(failure!.Message, Does.Contain("sRGB").And.Contain("7.2.2"));
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

    // Overstate the first frame's size. The marker at both ends of the index still agrees, so the
    // chunk is still recognised as a superframe and the sizes are what has to be checked.
    packet[^5] = 0xFF;

    Assert.That(Assert.Throws<InvalidDataException>(() => _Decode(packet))!.Message, Does.Contain("superframe"));
  }

  [Test]
  [Category("Unit")]
  public void ShowingAReferenceSlotNothingHasWrittenIsRefusedByName() {
    // One byte: the frame marker and profile, show_existing_frame, and which of the eight slots. A
    // stream that begins with one is asking for a picture that has never been decoded.
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

  /// <summary>Decodes one packet through the public codec, as a container would.</summary>
  private static RawImage _Decode(byte[] packet) {
    var decoder = Vp9VideoDecoder.Create(_Stream(codecId: "V_VP9"));
    Assert.That(decoder.TryDecode(new(0, packet), out var picture), Is.True, "no picture was shown");
    return picture;
  }

  /// <summary>Decodes a sequence of packets through one decoder and collects every picture shown.</summary>
  private static List<RawImage> _DecodeAll(params byte[][] packets) {
    var decoder = Vp9VideoDecoder.Create(_Stream(codecId: "V_VP9"));
    var pictures = new List<RawImage>();

    foreach (var packet in packets)
      if (decoder.TryDecode(new(0, packet), out var picture))
        pictures.Add(picture);

    pictures.AddRange(decoder.Flush());
    return pictures;
  }

  /// <summary>Packs frames into one chunk with the index of Annex B.</summary>
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

  /// <summary>
  /// Overwrites the field that states how long a frame's compressed header is.
  /// </summary>
  /// <remarks>
  /// The uncompressed header of a key frame built with the default options is exactly ninety-six bits
  /// long — three bytes of frame tag and sync code apiece, four bits of colour configuration, four
  /// bytes of picture size, and twenty-five bits of everything else — so the sixteen-bit field it
  /// ends with begins at byte twelve. The assertion below is what says so out loud, and what fails
  /// first if a field is ever added to the frames these tests build.
  /// </remarks>
  private static void _SetCompressedHeaderSize(byte[] frame, int size) {
    const int AT = 12;
    Assert.That(frame[AT], Is.Zero, "the compressed header size is not where this test expects it");
    Assert.That(frame[AT + 1], Is.EqualTo(4), "the compressed header size is not where this test expects it");

    frame[AT] = (byte)(size >> 8);
    frame[AT + 1] = (byte)size;
  }
}
