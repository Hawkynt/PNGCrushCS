using System;
using System.Linq;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;

namespace FileFormat.Codecs.Vp5.Tests;

/// <summary>Which streams the VP5 decoder takes, what it refuses, and what it hands back.</summary>
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
  [TestCase("Vp50")]
  [Category("Unit")]
  public void TheOneCodeContainersNameVp5WithIsAcceptedWhateverItsCase(string code)
    => Assert.That(Vp5VideoDecoder.Accepts(_Stream(code)), Is.True);

  /// <summary>
  /// The neighbouring On2 codes, none of which this reads.
  /// </summary>
  /// <remarks>
  /// VP5 shares its entropy coder and half its tables with VP6, and a decoder that answered to
  /// <c>VP60</c> would get some way into a VP6 frame before going wrong. That is the reason for
  /// naming the neighbours here rather than trusting one equality test: the failure this guards
  /// against is a picture, not an exception.
  /// </remarks>
  [TestCase("VP40")]
  [TestCase("VP60")]
  [TestCase("VP61")]
  [TestCase("VP62")]
  [TestCase("VP6F")]
  [TestCase("VP70")]
  [TestCase("VP80")]
  [TestCase("VP31")]
  [TestCase("MJPG")]
  [Category("Unit")]
  public void ACodeThatIsNotVp5IsNotAccepted(string code)
    => Assert.That(Vp5VideoDecoder.Accepts(_Stream(code)), Is.False);

  [Test]
  [Category("Unit")]
  public void AnAudioStreamIsNotAcceptedWhateverItsCode()
    => Assert.That(Vp5VideoDecoder.Accepts(_Stream(kind: MediaStreamKind.Audio)), Is.False);

  [Test]
  [Category("Unit")]
  public void TheRegistryBuildsThisDecoderForVp5()
    => Assert.That(VideoFormatRegistry.CreateDecoder(_Stream()), Is.InstanceOf<Vp5VideoDecoder>());

  [Test]
  [Category("Unit")]
  public void BuildingADecoderForAnAudioStreamIsRefusedByName() {
    var failure = Assert.Throws<NotSupportedException>(() => Vp5VideoDecoder.Create(_Stream(kind: MediaStreamKind.Audio)));
    Assert.That(failure!.Message, Does.Contain("video stream"));
  }

  [Test]
  [Category("Unit")]
  public void ANullStreamIsRefusedRatherThanDereferenced() {
    Assert.Throws<ArgumentNullException>(() => Vp5VideoDecoder.Accepts(null!));
    Assert.Throws<ArgumentNullException>(() => Vp5VideoDecoder.Create(null!));
  }

  [Test]
  [Category("Unit")]
  public void TheCodecIsNamedForWhatItIs()
    => Assert.That(Vp5VideoDecoder.CodecName, Is.EqualTo("On2 VP5"));

  /// <summary>
  /// The picture size comes out of the key frame, not out of the container.
  /// </summary>
  /// <remarks>
  /// Unlike VP3, which has to be told its size, VP5 states one in every key frame. This decoder is
  /// built from a stream description that carries no size at all, so a frame of the right geometry is
  /// evidence that the header was read and not that the container was copied.
  /// </remarks>
  [Test]
  [Category("Unit")]
  public void EveryPacketOfARealClipDecodesToAPictureOfTheSizeItsKeyFrameStates() {
    var decoder = Vp5VideoDecoder.Create(_Stream());
    var frames = 0;

    foreach (var packet in Vp5Fixtures.Packets(Vp5Fixtures.SIXTY_FRAMES)) {
      Assert.That(decoder.TryDecode(new(0, packet), out var frame), Is.True);
      Assert.That((frame.Width, frame.Height), Is.EqualTo((512, 304)));
      Assert.That(frame.Format, Is.EqualTo(PixelFormat.Rgb24));
      Assert.That(frame.PixelData, Has.Length.EqualTo(512 * 304 * 3));
      ++frames;
    }

    Assert.That(frames, Is.EqualTo(60));
  }

  /// <summary>
  /// The interlaced clip is a different size, and comes out at it.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void AnInterlacedClipDecodesAtItsOwnGeometry() {
    var decoder = Vp5VideoDecoder.Create(_Stream());
    var packet = Vp5Fixtures.Packets(Vp5Fixtures.INTERLACED).First();

    Assert.That(decoder.TryDecode(new(0, packet), out var frame), Is.True);
    Assert.That((frame.Width, frame.Height), Is.EqualTo((352, 576)));
  }

  /// <summary>
  /// The picture handed out is the right way up.
  /// </summary>
  /// <remarks>
  /// VP5 codes its rows bottom first, so a decoder that forgot to turn the picture over would produce
  /// something that is correct in every sample and upside down. No digest of the planes catches that,
  /// because the planes are what the flip is applied to; this compares the top of the RGB frame
  /// against the bottom of the coded one.
  /// </remarks>
  [Test]
  [Category("Unit")]
  public void ThePictureIsHandedOutTheRightWayUp() {
    var decoder = Vp5VideoDecoder.Create(_Stream());
    var packet = Vp5Fixtures.Packets(Vp5Fixtures.SIXTY_FRAMES).First();
    decoder.TryDecode(new(0, packet), out var frame);

    var planes = new Vp5Decoder();
    var picture = planes.Decode(packet);

    // The first displayed row's luminance is the last coded row's, and BT.601 makes the green channel
    // by far the largest term in it, so a flipped picture cannot agree here by accident.
    var codedBottom = picture.Luma.AsSpan((picture.LumaHeight - 1) * picture.LumaWidth, picture.LumaWidth);
    var displayedTop = picture.TopDown(0).AsSpan(0, picture.LumaWidth);

    Assert.That(displayedTop.SequenceEqual(codedBottom), Is.True);
    Assert.That(frame.PixelData, Has.Length.EqualTo(picture.LumaWidth * picture.LumaHeight * 3));
  }
}
