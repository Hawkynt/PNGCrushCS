using System;
using System.IO;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;

namespace FileFormat.Codecs.Vp3.Tests;

/// <summary>Which streams the VP3 decoder takes, and what it refuses.</summary>
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
    => Assert.That(Vp3VideoDecoder.Accepts(_Stream(code)), Is.True);

  [TestCase("VP40")]
  [TestCase("VP80")]
  [TestCase("VP60")]
  [TestCase("MJPG")]
  [Category("Unit")]
  public void ACodeThatIsNotVp3IsNotAccepted(string code)
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

  [TestCase("VP30")]
  [TestCase("VP31")]
  [TestCase("VP32")]
  [Category("Unit")]
  public void TheRegistryBuildsThisDecoderForEveryVp3Revision(string code)
    => Assert.That(VideoFormatRegistry.CreateDecoder(_Stream(code)), Is.InstanceOf<Vp3VideoDecoder>());

  [TestCase(0, 64)]
  [TestCase(64, 0)]
  [TestCase(-16, 16)]
  [Category("Unit")]
  public void AStreamWithNoPictureSizeIsRefusedByName(int width, int height) {
    var failure = Assert.Throws<NotSupportedException>(
      () => Vp3VideoDecoder.Create(_Stream("VP31", width, height)));
    Assert.That(failure!.Message, Does.Contain("carries no picture size of its own"));
  }

  [Test]
  [Category("Unit")]
  public void AStreamThatBeginsAtAnInterFrameIsRefused() {
    var decoder = Vp3VideoDecoder.Create(_Stream("VP31"));
    var failure = Assert.Throws<InvalidDataException>(
      () => decoder.TryDecode(_Packet(0x80, 0x00, 0x00, 0x00), out _));

    Assert.That(failure!.Message, Does.Contain("begins with an inter frame"));
  }

  [Test]
  [Category("Unit")]
  public void AStreamThatBeginsWithAnEmptyPacketIsRefused() {
    var decoder = Vp3VideoDecoder.Create(_Stream("VP31"));
    var failure = Assert.Throws<InvalidDataException>(() => decoder.TryDecode(_Packet(), out _));

    Assert.That(failure!.Message, Does.Contain("begins with an empty packet"));
  }

  [Test]
  [Category("Unit")]
  public void ATruncatedIntraFrameIsRefusedRatherThanFilledInWithZeroes() {
    var decoder = Vp3VideoDecoder.Create(_Stream("VP31"));
    var failure = Assert.Throws<InvalidDataException>(
      () => decoder.TryDecode(_Packet(0x00, 0x00, 0x08), out _));

    Assert.That(failure!.Message, Does.Contain("ran off the end"));
  }

  [Test]
  [Category("Unit")]
  public void AFrameWhoseTokensDoNotAccountForEveryCoefficientIsRefused() {
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
