using System;
using System.IO;
using System.IO.Compression;
using FileFormat.Core;

namespace FileFormat.Codecs.ZeroCodec.Tests;

/// <summary>ZeroCodec's literal I-pictures, previous-picture P-pictures, bottom-up UYVY storage and
/// malformed zlib boundaries.</summary>
[TestFixture]
public class ZeroCodecVideoDecoderTests {

  private static readonly CodecTag _Zeco = CodecTag.FromCharacters("ZECO");

  private static MediaStreamInfo _Stream(
    int width,
    int height,
    int bitsPerPixel = 16,
    CodecTag? codec = null,
    MediaStreamKind kind = MediaStreamKind.Video) => new() {
    Index = 0,
    Kind = kind,
    Codec = codec ?? _Zeco,
    Width = width,
    Height = height,
    BitsPerPixel = bitsPerPixel,
  };

  private static byte[] _Zlib(ReadOnlySpan<byte> raw) {
    using var target = new MemoryStream();
    using (var zlib = new ZLibStream(target, CompressionLevel.Optimal, leaveOpen: true))
      zlib.Write(raw);

    return target.ToArray();
  }

  private static CodedPacket _Packet(ReadOnlySpan<byte> raw, bool keyFrame = false)
    => new(0, _Zlib(raw), IsKeyFrame: keyFrame);

  [Test]
  [Category("Unit")]
  public void AcceptsTheZecoTagAndRefusesOtherKindsAndTags() {
    Assert.Multiple(() => {
      Assert.That(ZeroCodecVideoDecoder.Accepts(_Stream(16, 16)), Is.True);
      Assert.That(ZeroCodecVideoDecoder.Accepts(_Stream(16, 16, codec: CodecTag.FromCharacters("CVID"))), Is.False);
      Assert.That(ZeroCodecVideoDecoder.Accepts(_Stream(16, 16, kind: MediaStreamKind.Audio)), Is.False);
    });
  }

  [Test]
  [Category("Unit")]
  public void CreateRefusesUnsupportedGeometryDepthAndKind() {
    Assert.Multiple(() => {
      Assert.Throws<InvalidDataException>(() => ZeroCodecVideoDecoder.Create(_Stream(0, 16)));
      Assert.Throws<NotSupportedException>(() => ZeroCodecVideoDecoder.Create(_Stream(15, 16)));
      Assert.Throws<NotSupportedException>(() => ZeroCodecVideoDecoder.Create(_Stream(16, 16, bitsPerPixel: 24)));
      Assert.Throws<NotSupportedException>(() => ZeroCodecVideoDecoder.Create(_Stream(16, 16, kind: MediaStreamKind.Audio)));
    });
  }

  [Test]
  [Category("Unit")]
  public void RefusesAnInterPictureBeforeAnyReferenceExists() {
    var decoder = ZeroCodecVideoDecoder.Create(_Stream(2, 1));

    var failure = Assert.Throws<InvalidDataException>(() => decoder.TryDecode(_Packet([0, 0, 0, 0]), out _));
    Assert.That(failure!.Message, Does.Contain("before any reference picture"));
  }

  [Test]
  [Category("Unit")]
  public void RefusesAStreamThatInflatesToLessOrMoreThanOnePicture() {
    var decoder = ZeroCodecVideoDecoder.Create(_Stream(4, 2));

    var shortFailure = Assert.Throws<InvalidDataException>(
      () => decoder.TryDecode(_Packet(new byte[15], keyFrame: true), out _));
    var longFailure = Assert.Throws<InvalidDataException>(
      () => decoder.TryDecode(_Packet(new byte[17], keyFrame: true), out _));

    Assert.Multiple(() => {
      Assert.That(shortFailure!.Message, Does.Contain("exactly the 16 byte(s)"));
      Assert.That(longFailure!.Message, Does.Contain("exactly the 16 byte(s)"));
    });
  }

  [Test]
  [Category("Unit")]
  public void RefusesDataThatIsNotAZlibStream() {
    var decoder = ZeroCodecVideoDecoder.Create(_Stream(2, 1));
    var packet = new CodedPacket(0, new byte[] { 0, 0, 0, 0 }, IsKeyFrame: true);

    Assert.Throws<InvalidDataException>(() => decoder.TryDecode(packet, out _));
  }

  [Test]
  [Category("Unit")]
  public void KeyPicturesAreLiteralAndMayReplaceNonzeroSamplesWithZero() {
    var decoder = ZeroCodecVideoDecoder.Create(_Stream(2, 1));

    Assert.That(decoder.TryDecode(_Packet([128, 16, 128, 16], keyFrame: true), out _), Is.True);
    Assert.That(decoder.TryDecode(_Packet([0, 0, 0, 0], keyFrame: true), out var frame), Is.True);

    Assert.Multiple(() => {
      Assert.That(frame.Format, Is.EqualTo(PixelFormat.Yuv422P8));
      Assert.That(frame.PixelData, Is.EqualTo(new byte[] { 0, 0, 0, 0 }));
    });
  }

  [Test]
  [Category("Unit")]
  public void ZeroBytesInAPictureCopyThePreviousPicture() {
    var decoder = ZeroCodecVideoDecoder.Create(_Stream(2, 1));

    Assert.That(decoder.TryDecode(_Packet([90, 200, 60, 210], keyFrame: true), out var first), Is.True);
    Assert.That(decoder.TryDecode(_Packet([0, 0, 0, 0]), out var second), Is.True);

    Assert.That(second.PixelData, Is.EqualTo(first.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void NonzeroPBytesReplaceTheirPreviousSamples() {
    var decoder = ZeroCodecVideoDecoder.Create(_Stream(4, 1));

    Assert.That(decoder.TryDecode(
      _Packet([128, 16, 128, 16, 128, 16, 128, 16], keyFrame: true), out _), Is.True);
    Assert.That(decoder.TryDecode(
      _Packet([0, 0, 0, 0, 128, 235, 128, 235]), out var frame), Is.True);

    // Y plane, then Cb, then Cr.
    Assert.That(frame.PixelData, Is.EqualTo(new byte[] { 16, 16, 235, 235, 128, 128, 128, 128 }));
  }

  [Test]
  [Category("Unit")]
  public void CodedRowsAreBottomUp() {
    var decoder = ZeroCodecVideoDecoder.Create(_Stream(2, 2));

    // Coded row zero is the displayed bottom row (bright); coded row one is the top row (dark).
    var coded = new byte[] {
      128, 235, 128, 235,
      128, 16, 128, 16,
    };

    Assert.That(decoder.TryDecode(_Packet(coded, keyFrame: true), out var frame), Is.True);

    Assert.That(frame.PixelData, Is.EqualTo(new byte[] {
      16, 16, 235, 235,
      128, 128,
      128, 128,
    }));
  }
}
