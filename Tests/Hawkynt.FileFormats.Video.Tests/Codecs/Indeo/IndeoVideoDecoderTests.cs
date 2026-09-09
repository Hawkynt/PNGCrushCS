using System;
using System.IO;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;

namespace FileFormat.Codecs.Indeo.Tests;

/// <summary>
/// Which streams the two Indeo decoders take, and what they refuse.
/// </summary>
/// <remarks>
/// Every refusal here names the field that was wrong. That matters for Indeo because a frame carrying
/// nothing is a legitimate and common thing for both formats to send — an empty frame, an empty tile,
/// an uncoded macroblock — so a decoder that handed back a repeat of the previous picture when it
/// could not read a frame would produce something indistinguishable from correct output. There is no
/// <c>catch</c> anywhere in either decoder that does that.
/// </remarks>
[TestFixture]
public sealed class IndeoVideoDecoderTests {

  private static MediaStreamInfo _Stream(string code, int width = 320, int height = 240) => new() {
    Index = 0,
    Kind = MediaStreamKind.Video,
    Codec = CodecTag.FromCharacters(code),
    Handler = CodecTag.FromCharacters(code),
    Width = width,
    Height = height,
  };

  [TestCase("IV41")]
  [TestCase("iv41")]
  [Category("Unit")]
  public void EveryCodeAContainerNamesIndeo4WithIsAccepted(string code)
    => Assert.That(Indeo4VideoDecoder.Accepts(_Stream(code)), Is.True);

  [TestCase("IV50")]
  [TestCase("iv50")]
  [Category("Unit")]
  public void EveryCodeAContainerNamesIndeo5WithIsAccepted(string code)
    => Assert.That(Indeo5VideoDecoder.Accepts(_Stream(code)), Is.True);

  [TestCase("IV50")]
  [TestCase("IV32")]
  [TestCase("IV31")]
  [TestCase("MJPG")]
  [Category("Unit")]
  public void ACodeThatIsNotIndeo4IsNotAcceptedByIndeo4(string code)
    // Indeo 3 is a different format entirely and shares nothing below the four-character code.
    => Assert.That(Indeo4VideoDecoder.Accepts(_Stream(code)), Is.False);

  [TestCase("IV41")]
  [TestCase("IV32")]
  [TestCase("MJPG")]
  [Category("Unit")]
  public void ACodeThatIsNotIndeo5IsNotAcceptedByIndeo5(string code)
    => Assert.That(Indeo5VideoDecoder.Accepts(_Stream(code)), Is.False);

  [Test]
  [Category("Unit")]
  public void AnAudioStreamIsNotAcceptedWhateverItsCode() {
    var stream = new MediaStreamInfo {
      Index = 0,
      Kind = MediaStreamKind.Audio,
      Codec = CodecTag.FromCharacters("IV50"),
    };

    Assert.That(Indeo4VideoDecoder.Accepts(stream), Is.False);
    Assert.That(Indeo5VideoDecoder.Accepts(stream), Is.False);
  }

  [Test]
  [Category("Unit")]
  public void TheRegistryBuildsTheseDecodersForTheirStreams() {
    Assert.That(VideoFormatRegistry.CanDecode(_Stream("IV41")), Is.True);
    Assert.That(VideoFormatRegistry.CanDecode(_Stream("IV50")), Is.True);
    Assert.That(VideoFormatRegistry.CreateDecoder(_Stream("IV41")), Is.InstanceOf<Indeo4VideoDecoder>());
    Assert.That(VideoFormatRegistry.CreateDecoder(_Stream("IV50")), Is.InstanceOf<Indeo5VideoDecoder>());
  }

  [TestCase(0, 240)]
  [TestCase(320, 0)]
  [TestCase(-16, 16)]
  [Category("Unit")]
  public void AnIndeo5StreamWithNoPictureSizeIsRefusedByName(int width, int height) {
    // Indeo 5 states the picture size in its first key frame, but the buffers a frame decodes into are
    // laid out before any frame is read, so the container has to state something to start from.
    var failure = Assert.Throws<NotSupportedException>(
      () => Indeo5VideoDecoder.Create(_Stream("IV50", width, height)));
    Assert.That(failure!.Message, Does.Contain("no area"));
  }

  [Test]
  [Category("Unit")]
  public void AnIndeo4StreamNeedsNoSizeFromItsContainer()
    // Indeo 4 states its picture size, tiling and band subdivision in every picture header.
    => Assert.That(Indeo4VideoDecoder.Create(_Stream("IV41", 0, 0)), Is.Not.Null);

  [Test]
  [Category("Unit")]
  public void APacketThatDoesNotStartWithIndeo5sStartCodeIsRefusedByName() {
    var decoder = Indeo5VideoDecoder.Create(_Stream("IV50"));

    var failure = Assert.Throws<InvalidDataException>(
      () => decoder.TryDecode(new(0, new byte[] { 0x00, 0x00, 0x00, 0x00 }), out _));
    Assert.That(failure!.Message, Does.Contain("picture start code"));
  }

  [Test]
  [Category("Unit")]
  public void APacketThatDoesNotStartWithIndeo4sStartCodeIsRefusedByName() {
    var decoder = Indeo4VideoDecoder.Create(_Stream("IV41"));

    var failure = Assert.Throws<InvalidDataException>(
      () => decoder.TryDecode(new(0, new byte[] { 0x00, 0x00, 0x00, 0x00 }), out _));
    Assert.That(failure!.Message, Does.Contain("picture start code"));
  }

  [Test]
  [Category("Unit")]
  public void AnEmptyPacketIsRefusedRatherThanTakenForAFrameThatChangedNothing() {
    var decoder = Indeo5VideoDecoder.Create(_Stream("IV50"));

    Assert.Throws<InvalidDataException>(() => decoder.TryDecode(new(0, Array.Empty<byte>()), out _));
  }

  [Test]
  [Category("Unit")]
  public void AnIndeo5FrameTypeTheFormatDoesNotDefineIsRefusedByName() {
    // Five bits of start code and then a three-bit frame type of six, which is two past the last one
    // the format defines.
    var decoder = Indeo5VideoDecoder.Create(_Stream("IV50"));

    var failure = Assert.Throws<InvalidDataException>(
      () => decoder.TryDecode(new(0, new byte[] { 0b1101_1111, 0x00, 0x00, 0x00 }), out _));
    Assert.That(failure!.Message, Does.Contain("frame type"));
  }
}
