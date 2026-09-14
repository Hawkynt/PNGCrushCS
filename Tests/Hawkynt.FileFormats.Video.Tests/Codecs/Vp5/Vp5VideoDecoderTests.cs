using System.IO;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;

namespace FileFormat.Codecs.Vp5.Tests;

[TestFixture]
public sealed class Vp5VideoDecoderTests {
  private static MediaStreamInfo _Stream(string code = "VP50", MediaStreamKind kind = MediaStreamKind.Video) => new() {
    Index = 0,
    Kind = kind,
    Codec = CodecTag.FromCharacters(code),
    Handler = CodecTag.FromCharacters(code),
  };

  [TestCase("VP50")]
  [TestCase("vp50")]
  [Category("Unit")]
  public void TheVp5FourCcIsAcceptedCaseInsensitively(string code)
    => Assert.That(Vp5VideoDecoder.Accepts(_Stream(code)), Is.True);

  [TestCase("VP40")]
  [TestCase("VP60")]
  [TestCase("VP70")]
  [TestCase("VP80")]
  [Category("Unit")]
  public void OtherOn2FourCcsAreNotClaimed(string code)
    => Assert.That(Vp5VideoDecoder.Accepts(_Stream(code)), Is.False);

  [Test]
  [Category("Unit")]
  public void AnAudioTrackIsNotClaimed()
    => Assert.That(Vp5VideoDecoder.Accepts(_Stream(kind: MediaStreamKind.Audio)), Is.False);

  [Test]
  [Category("Unit")]
  public void TheRegistryBuildsVp5()
    => Assert.That(VideoFormatRegistry.CreateDecoder(_Stream()), Is.InstanceOf<Vp5VideoDecoder>());

  [Test]
  [Category("Unit")]
  public void EmptyFrameIsRejected() {
    var decoder = Vp5VideoDecoder.Create(_Stream());
    var failure = Assert.Throws<InvalidDataException>(() => decoder.TryDecode(new(0, []), out _));
    Assert.That(failure!.Message, Does.Contain("VP5"));
  }

  [Test]
  [Category("Unit")]
  public void StreamCannotBeginWithInterPicture() {
    var decoder = Vp5VideoDecoder.Create(_Stream());

    // With the range coder at its initial 255 range, 0x8000 lies exactly in the upper half for the
    // even-odds frame-mode bit, so the first decoded bit is one: an inter picture.
    var failure = Assert.Throws<InvalidDataException>(
      () => decoder.TryDecode(new(0, new byte[] { 0x80, 0x00, 0x00, 0x00 }), out _));

    Assert.That(failure!.Message, Does.Contain("begins with an inter frame"));
  }

  [Test]
  [Category("Unit")]
  public void KeyPictureWithZeroDimensionsIsRejected() {
    var decoder = Vp5VideoDecoder.Create(_Stream());
    var failure = Assert.Throws<InvalidDataException>(
      () => decoder.TryDecode(new(0, new byte[16]), out _));

    Assert.That(failure!.Message, Does.Contain("invalid coded size"));
  }

  [Test]
  [Category("Unit")]
  public void CodecNameNamesVp5()
    => Assert.That(Vp5VideoDecoder.CodecName, Does.Contain("VP5"));
}
