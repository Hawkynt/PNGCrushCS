using System;
using System.IO;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;

namespace FileFormat.Codecs.Vp3.Tests;

/// <summary>
/// Which streams the VP3 decoder takes, and what it refuses.
/// </summary>
/// <remarks>
/// Every refusal here names the field that was wrong. That matters more for VP3 than for most codecs:
/// a frame in which nothing changed is a legitimate and common thing for a VP3 stream to contain, so
/// a decoder that handed back a repeat of the previous picture when it could not read a frame would
/// be producing something indistinguishable from correct output. There is no <c>catch</c> anywhere in
/// this decoder that does that, and these tests are what says so.
/// <para/>
/// Nothing here needs a sample file. What a real stream decodes to was measured against a reference
/// decoder instead, which is a measurement no unit test can make.
/// </remarks>
[TestFixture]
public sealed class Vp3VideoDecoderTests {

  private static MediaStreamInfo _Stream(string code, int width = 64, int height = 64) => new() {
    Index = 0,
    Kind = MediaStreamKind.Video,
    Codec = CodecTag.FromCharacters(code),
    Handler = CodecTag.FromCharacters(code),
    Width = width,
    Height = height,
  };

  private static CodedPacket _Packet(params byte[] data) => new(0, data);

  [TestCase("VP30")]
  [TestCase("VP31")]
  [TestCase("VP32")]
  [TestCase("vp31")]
  [Category("Unit")]
  public void EveryCodeAContainerNamesVp3WithIsAccepted(string code)
    // VP30 is accepted so that a file holding it is refused for being VP3.0 rather than for being
    // nothing anybody recognises.
    => Assert.That(Vp3VideoDecoder.Accepts(_Stream(code)), Is.True);

  [TestCase("VP40")]
  [TestCase("VP80")]
  [TestCase("VP60")]
  [TestCase("MJPG")]
  [Category("Unit")]
  public void ACodeThatIsNotVp3IsNotAccepted(string code)
    // VP4 and VP6 are later On2 formats with different bitstreams, not later revisions of this one.
    => Assert.That(Vp3VideoDecoder.Accepts(_Stream(code)), Is.False);

  [Test]
  [Category("Unit")]
  public void AnAudioStreamIsNotAcceptedWhateverItsCode() {
    var stream = new MediaStreamInfo {
      Index = 0,
      Kind = MediaStreamKind.Audio,
      Codec = CodecTag.FromCharacters("VP31"),
    };

    Assert.That(Vp3VideoDecoder.Accepts(stream), Is.False);
  }

  [Test]
  [Category("Unit")]
  public void TheRegistryBuildsThisDecoderForAVp31Stream()
    => Assert.That(VideoFormatRegistry.CreateDecoder(_Stream("VP31")), Is.InstanceOf<Vp3VideoDecoder>());

  [Test]
  [Category("Unit")]
  public void AVp30StreamIsRefusedByName() {
    // VP3.0 is a different bitstream, not merely a different frame header: none of its key frames can
    // be read with VP3.1's rules at any bit offset.
    var failure = Assert.Throws<NotSupportedException>(() => Vp3VideoDecoder.Create(_Stream("VP30")));
    Assert.That(failure!.Message, Does.Contain("VP30"));
    Assert.That(failure.Message, Does.Contain("VP3.0"));
  }

  [TestCase(0, 64)]
  [TestCase(64, 0)]
  [TestCase(-16, 16)]
  [Category("Unit")]
  public void AStreamWithNoPictureSizeIsRefusedByName(int width, int height) {
    // VP3 carries no picture size of its own, so a container that states none leaves the decoder with
    // nothing to allocate. Refusing says that rather than decoding into a frame of no area.
    var failure = Assert.Throws<NotSupportedException>(
      () => Vp3VideoDecoder.Create(_Stream("VP31", width, height)));
    Assert.That(failure!.Message, Does.Contain("carries no picture size of its own"));
  }

  [Test]
  [Category("Unit")]
  public void AStreamThatBeginsAtAnInterFrameIsRefused() {
    // The first bit of a VP3 frame is its type. An inter frame is coded as differences from a frame
    // that was never sent, so there is nothing to difference against.
    var decoder = Vp3VideoDecoder.Create(_Stream("VP31"));
    var failure = Assert.Throws<InvalidDataException>(
      () => decoder.TryDecode(_Packet(0x80, 0x00, 0x00, 0x00), out _));

    Assert.That(failure!.Message, Does.Contain("begins with an inter frame"));
  }

  [Test]
  [Category("Unit")]
  public void AStreamThatBeginsWithAnEmptyPacketIsRefused() {
    // An empty packet means nothing changed since the previous frame, which is a real and common
    // thing for VP3 to say — but not as the first thing it says.
    var decoder = Vp3VideoDecoder.Create(_Stream("VP31"));
    var failure = Assert.Throws<InvalidDataException>(() => decoder.TryDecode(_Packet(), out _));

    Assert.That(failure!.Message, Does.Contain("begins with an empty packet"));
  }

  [Test]
  [Category("Unit")]
  public void ATruncatedIntraFrameIsRefusedRatherThanFilledInWithZeroes() {
    // Three bytes is exactly the intra frame header and not one bit more, so the first DCT token
    // reads off the end. Carrying on with zeroes would produce a grey picture that looks decoded.
    var decoder = Vp3VideoDecoder.Create(_Stream("VP31"));
    var failure = Assert.Throws<InvalidDataException>(
      () => decoder.TryDecode(_Packet(0x00, 0x00, 0x08), out _));

    Assert.That(failure!.Message, Does.Contain("ran off the end"));
  }

  [Test]
  [Category("Unit")]
  public void AFrameWhoseTokensDoNotAccountForEveryCoefficientIsRefused() {
    // An intra frame marks every block coded, so every block has to reach coefficient sixty-four. A
    // packet of zero bits after the header states, over and over, the shortest code of codebook zero
    // — which is a token that writes one coefficient — so the blocks advance one position at a time
    // and the packet runs out first. Either refusal is the right one; what must not happen is a
    // picture.
    var decoder = Vp3VideoDecoder.Create(_Stream("VP31"));
    var packet = new byte[4096];
    packet[2] = 0x08;

    var failure = Assert.Throws<InvalidDataException>(() => decoder.TryDecode(new(0, packet), out _));
    Assert.That(failure!.Message, Does.Contain("VP3"));
  }

  [Test]
  [Category("Unit")]
  public void TheCodecNamesItselfAfterTheFormatAndNotAfterAFourCharacterCode()
    => Assert.That(Vp3VideoDecoder.CodecName, Does.Contain("VP3"));
}
