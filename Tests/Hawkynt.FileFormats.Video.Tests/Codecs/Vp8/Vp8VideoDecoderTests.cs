using System;
using System.IO;
using System.Linq;
using FileFormat.Core;

namespace FileFormat.Codecs.Vp8.Tests;

/// <summary>
/// The VP8 decoder, on frames built here.
/// </summary>
/// <remarks>
/// The decoder's arithmetic was checked by decoding fifty-nine streams here and in ffmpeg and
/// comparing the sample planes frame by frame; what these tests add is what that comparison
/// cannot reach. Most of it is the refusals, which by definition no valid stream produces. The rest
/// is a handful of frames whose expected samples can be worked out from the standard rather than
/// recorded from a run — so that where a number here disagrees with the decoder, the arithmetic in
/// the comment says which of the two is wrong.
/// </remarks>
[TestFixture]
public sealed class Vp8VideoDecoderTests {

  // ============================================================================================
  // Identity
  // ============================================================================================

  [TestCase("V_VP8")]
  [TestCase("v_vp8")]
  [Category("Unit")]
  public void TheCodecTakesTheNameMatroskaGivesIt(string codecId)
    => Assert.That(Vp8VideoDecoder.Accepts(_Stream(codecId: codecId)), Is.True);

  [TestCase("VP80")]
  [TestCase("vp08")]
  [Category("Unit")]
  public void TheCodecTakesTheCodesContainersWithACodeFieldGiveIt(string code)
    => Assert.That(Vp8VideoDecoder.Accepts(_Stream(code: code)), Is.True);

  [Test]
  [Category("Unit")]
  public void TheCodecLeavesOtherStreamsAlone() {
    Assert.That(Vp8VideoDecoder.Accepts(_Stream(codecId: "V_VP9")), Is.False);
    Assert.That(Vp8VideoDecoder.Accepts(_Stream(codecId: "V_MJPEG")), Is.False);
    Assert.That(Vp8VideoDecoder.Accepts(_Stream(code: "MJPG")), Is.False);
    Assert.That(Vp8VideoDecoder.Accepts(_Stream(codecId: "A_VORBIS", kind: MediaStreamKind.Audio)), Is.False);

    // The same name on an audio track is still not a picture.
    Assert.That(Vp8VideoDecoder.Accepts(_Stream(codecId: "V_VP8", kind: MediaStreamKind.Audio)), Is.False);
  }

  [Test]
  [Category("Unit")]
  public void TheCodecIsNamedForTheStandardItReads()
    => Assert.That(Vp8VideoDecoder.CodecName, Does.Contain("VP8").And.Contain("6386"));

  // ============================================================================================
  // Reconstruction
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void AKeyFrameOfEmptyMacroblocksIsMidGrey() {
    // Nothing above or to the left of the first macroblock, so direct current prediction fills it
    // with 128, and every macroblock after it averages neighbours that are themselves 128. With no
    // residue the luminance is 128 and the chrominance neutral, which is
    // (298 * (128 - 16) + 128) >> 8 = 130 in all three channels.
    var frame = _Decode(Vp8TestStream.BuildKeyFrame(new()));

    Assert.That(frame.Width, Is.EqualTo(16));
    Assert.That(frame.Height, Is.EqualTo(16));
    Assert.That(frame.Format, Is.EqualTo(PixelFormat.Rgb24));
    Assert.That(frame.PixelData.Length, Is.EqualTo(16 * 16 * 3));
    Assert.That(frame.PixelData.Distinct().ToArray(), Is.EqualTo(new byte[] { 130 }));
  }

  [Test]
  [Category("Unit")]
  public void ACoefficientInTheY2BlockReachesEveryLuminanceSubblock() {
    // Quantiser index 0: the direct current factor is 4, and the Y2 block's is twice that, so the
    // token for 4 dequantises to 32. The inverse Walsh-Hadamard transform of a block whose only
    // value is 32 is (32 + 3) >> 3 = 4 everywhere, which becomes the first coefficient of each of
    // the sixteen luminance subblocks; the inverse transform of each of those is (4 + 4) >> 3 = 1.
    // So the luminance is 129 and the chrominance untouched: (298 * (129 - 16) + 128) >> 8 = 132.
    var frame = _Decode(Vp8TestStream.BuildKeyFrame(new() { Y2DirectCurrentToken = "111011" }));

    Assert.That(frame.PixelData.Distinct().ToArray(), Is.EqualTo(new byte[] { 132 }));
  }

  [Test]
  [Category("Unit")]
  public void TheQuantiserIndexScalesTheResidue() {
    // Quantiser index 20: the direct current factor is 21 and the Y2 block's is 42, so the token for
    // 4 dequantises to 168. The Walsh-Hadamard inversion gives (168 + 3) >> 3 = 21, and the inverse
    // transform of that gives (21 + 4) >> 3 = 3. The luminance is 131, which is
    // (298 * (131 - 16) + 128) >> 8 = 134.
    var frame = _Decode(Vp8TestStream.BuildKeyFrame(new() { QuantiserIndex = 20, Y2DirectCurrentToken = "111011" }));

    Assert.That(frame.PixelData.Distinct().ToArray(), Is.EqualTo(new byte[] { 134 }));
  }

  [Test]
  [Category("Unit")]
  public void TheLoopFilterLeavesAFlatPictureAlone() {
    // Every difference the filter measures is zero, so every adjustment it computes is zero. A
    // filter that moved a flat picture would be moving every picture.
    var filtered = _Decode(Vp8TestStream.BuildKeyFrame(new() { LoopFilterLevel = 63, Width = 48, Height = 32 }));
    var unfiltered = _Decode(Vp8TestStream.BuildKeyFrame(new() { Width = 48, Height = 32 }));

    Assert.That(filtered.PixelData, Is.EqualTo(unfiltered.PixelData));
  }

  [TestCase(16, 16)]
  [TestCase(48, 32)]
  [TestCase(20, 12)]
  [TestCase(129, 65)]
  [Category("Unit")]
  public void ThePictureComesOutTheSizeTheKeyFrameStates(int width, int height) {
    // VP8 codes whole macroblocks whatever the picture size, so a picture that is not a whole number
    // of macroblocks across is decoded larger and handed back cropped. The samples past the edge are
    // real coded samples and later frames predict from them, which is why they are kept until then.
    var frame = _Decode(Vp8TestStream.BuildKeyFrame(new() { Width = width, Height = height }));

    Assert.That(frame.Width, Is.EqualTo(width));
    Assert.That(frame.Height, Is.EqualTo(height));
    Assert.That(frame.PixelData.Length, Is.EqualTo(width * height * 3));
  }

  // ============================================================================================
  // Refusals
  // ============================================================================================

  [TestCase(4)]
  [TestCase(5)]
  [TestCase(7)]
  [Category("Unit")]
  public void AReservedBitstreamVersionIsRefusedByName(int version) {
    var failure = Assert.Throws<NotSupportedException>(
      () => _Decode(Vp8TestStream.BuildKeyFrame(new() { Version = version })));

    Assert.That(failure!.Message, Does.Contain($"version {version}").And.Contain("9.1"));
  }

  [Test]
  [Category("Unit")]
  public void AMissingStartCodeIsRefusedByName() {
    var frame = Vp8TestStream.BuildKeyFrame(new());
    frame[4] = 0x02;

    Assert.That(Assert.Throws<InvalidDataException>(() => _Decode(frame))!.Message,
      Does.Contain("9D 01 2A"));
  }

  [Test]
  [Category("Unit")]
  public void AStreamThatBeginsWithAnInterframeIsRefusedByName() {
    // The lowest bit of the frame tag says which kind of frame this is. An interframe is a difference
    // from frames that do not exist yet, and inventing them is how a decoder produces a plausible
    // wrong picture rather than an error.
    var frame = Vp8TestStream.BuildKeyFrame(new());
    frame[0] |= 1;

    Assert.That(Assert.Throws<InvalidDataException>(() => _Decode(frame))!.Message,
      Does.Contain("interframe").And.Contain("key frame"));
  }

  [Test]
  [Category("Unit")]
  public void APacketTooShortForItsFrameTagIsRefusedByName()
    => Assert.That(Assert.Throws<InvalidDataException>(() => _Decode([0x50, 0x00]))!.Message,
      Does.Contain("frame tag"));

  [Test]
  [Category("Unit")]
  public void AKeyFrameTooShortForItsPictureSizeIsRefusedByName()
    => Assert.That(Assert.Throws<InvalidDataException>(() => _Decode([0x50, 0x00, 0x00, 0x9D, 0x01]))!.Message,
      Does.Contain("start code and picture size"));

  [Test]
  [Category("Unit")]
  public void AFirstPartitionLargerThanThePacketIsRefusedByName() {
    // The size of the first partition is in the frame tag, so a packet that ends inside it can be
    // told from a whole one. A packet that ends inside the last token partition cannot, because that
    // partition's size is stated nowhere and is simply whatever is left.
    var frame = Vp8TestStream.BuildKeyFrame(new());
    var declared = (frame[0] | (frame[1] << 8) | (frame[2] << 16)) >> 5;
    var truncated = frame[..(10 + declared - 1)];

    Assert.That(Assert.Throws<InvalidDataException>(() => _Decode(truncated))!.Message,
      Does.Contain("first partition").And.Contain("truncated"));
  }

  [Test]
  [Category("Unit")]
  public void AFirstPartitionTooSmallToHoldAHeaderIsRefusedByName() {
    // The first partition carries the whole frame header, so unlike a token partition it cannot
    // credibly be one byte — and a decoder that started reading one would read zeroes and call the
    // result a frame.
    var tag = 1 << 4 | (1 << 5);
    byte[] frame = [(byte)tag, (byte)(tag >> 8), (byte)(tag >> 16), 0x9D, 0x01, 0x2A, 16, 0, 16, 0, 0, 0, 0];

    Assert.That(Assert.Throws<InvalidDataException>(() => _Decode(frame))!.Message,
      Does.Contain("frame header").And.Contain("cannot fit"));
  }

  [Test]
  [Category("Unit")]
  public void APictureWithNoSamplesIsRefusedByName() {
    var frame = Vp8TestStream.BuildKeyFrame(new());
    frame[6] = 0;
    frame[7] = 0;

    Assert.That(Assert.Throws<InvalidDataException>(() => _Decode(frame))!.Message,
      Does.Contain("0x16").Or.Contain("no samples"));
  }

  [Test]
  [Category("Unit")]
  public void AReservedColourSpaceIsRefusedByName() {
    // The two bits after the key frame header are the colour space and the clamping type, and RFC
    // 6386 section 9.2 gives no meaning to either being set.
    var header = new Vp8TestStream();
    header.Literal(2, 1);
    var partition = header.Finish();
    var tag = (1 << 4) | (partition.Length << 5);

    var frame = new byte[10 + partition.Length];
    frame[0] = (byte)tag;
    frame[1] = (byte)(tag >> 8);
    frame[2] = (byte)(tag >> 16);
    frame[3] = 0x9D;
    frame[4] = 0x01;
    frame[5] = 0x2A;
    frame[6] = 16;
    frame[8] = 16;
    partition.CopyTo(frame, 10);

    Assert.That(Assert.Throws<NotSupportedException>(() => _Decode(frame))!.Message,
      Does.Contain("colour space").And.Contain("9.2"));
  }

  [Test]
  [Category("Unit")]
  public void CreatingADecoderForNothingIsRefused() {
    Assert.Throws<ArgumentNullException>(() => Vp8VideoDecoder.Create(null!));
    Assert.Throws<ArgumentNullException>(() => Vp8VideoDecoder.Accepts(null!));
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
  private static RawImage _Decode(byte[] frame) {
    var decoder = Vp8VideoDecoder.Create(_Stream(codecId: "V_VP8"));
    Assert.That(decoder.TryDecode(new(0, frame), out var picture), Is.True, "the frame was not shown");
    return picture;
  }
}
