using System;
using System.Collections.Generic;
using System.Linq;
using FileFormat.Core;

namespace FileFormat.Codecs.Mpeg.Tests;

[TestFixture]
public sealed class Mpeg1VideoEncoderTests {

  [Test]
  [Category("Unit")]
  public void ThreeFramesBecomeThreeIndependentlyDecodablePictures() {
    var stream = _Stream(64, 48);
    var encoder = Mpeg1VideoEncoder.Create(stream);
    var packets = new List<CodedPacket>();

    for (var frame = 0; frame < 3; ++frame)
      if (encoder.TryEncode(_Gradient(64, 48, frame), frame, out var packet))
        packets.Add(packet);

    packets.AddRange(encoder.Flush());

    Assert.That(packets.Count, Is.EqualTo(3));
    Assert.That(packets.All(static packet => packet.IsKeyFrame), Is.True);
    Assert.That(packets.Select(static packet => packet.PresentationTimestamp).ToArray(),
      Is.EqualTo(new long?[] { 0, 1, 2 }));

    Assert.That(packets[0].Data.Span[..4].ToArray(),
      Is.EqualTo(new byte[] { 0x00, 0x00, 0x01, MpegStartCode.SequenceHeader }));
    Assert.That(packets[1].Data.Span[..4].ToArray(),
      Is.EqualTo(new byte[] { 0x00, 0x00, 0x01, MpegStartCode.Picture }));
    Assert.That(packets[^1].Data.Span[^4..].ToArray(),
      Is.EqualTo(new byte[] { 0x00, 0x00, 0x01, MpegStartCode.SequenceEnd }));

    var decoder = Mpeg1VideoDecoder.Create(stream);
    var decoded = new List<RawImage>();
    foreach (var packet in packets)
      if (decoder.TryDecode(packet, out var frame))
        decoded.Add(frame);

    decoded.AddRange(decoder.Flush());

    Assert.That(decoded.Count, Is.EqualTo(3));
    Assert.That(decoded.All(static frame => frame.Width == 64 && frame.Height == 48), Is.True);
    Assert.That(decoded.All(static frame => frame.Format == PixelFormat.Rgb24), Is.True);
  }

  [Test]
  [Category("Unit")]
  public void OddDimensionsAreCodedAtTheirDeclaredSizeRatherThanRoundedUp() {
    var stream = _Stream(17, 13);
    var encoder = Mpeg1VideoEncoder.Create(stream);

    Assert.That(encoder.TryEncode(_Gradient(17, 13, 0), 0, out _), Is.False);
    var packet = encoder.Flush().Single();

    var decoder = Mpeg1VideoDecoder.Create(stream);
    Assert.That(decoder.TryDecode(packet, out _), Is.False);
    var frame = decoder.Flush().Single();

    Assert.That(frame.Width, Is.EqualTo(17));
    Assert.That(frame.Height, Is.EqualTo(13));
    Assert.That(frame.PixelData.Length, Is.EqualTo(17 * 13 * 3));
  }

  [Test]
  [Category("Unit")]
  public void DescribeStreamNamesTheElementaryMpeg1PayloadWithoutPrivateContainerBytes() {
    var requested = _Stream(64, 48);
    var described = Mpeg1VideoEncoder.Create(requested).DescribeStream();

    Assert.That(described.Codec, Is.EqualTo(CodecTag.FromCharacters("MPG1")));
    Assert.That(described.Handler, Is.EqualTo(CodecTag.FromCharacters("MPG1")));
    Assert.That(described.CodecId, Is.EqualTo("V_MPEG1"));
    Assert.That(described.CodecPrivateData.IsEmpty, Is.True);
    Assert.That(described.Width, Is.EqualTo(64));
    Assert.That(described.Height, Is.EqualTo(48));
    Assert.That(described.FrameRate, Is.EqualTo(new Rational(25, 1)));
  }

  [Test]
  [Category("Unit")]
  public void AFrameRateTheFourBitFieldCannotNameIsRefused() {
    var stream = new MediaStreamInfo {
      Index = 0,
      Kind = MediaStreamKind.Video,
      Codec = CodecTag.FromCharacters("MPG1"),
      Width = 64,
      Height = 48,
      TimeBase = new Rational(1, 27),
      FrameRate = new Rational(27, 1),
    };

    var failure = Assert.Throws<NotSupportedException>(() => Mpeg1VideoEncoder.Create(stream));
    Assert.That(failure!.Message, Does.Contain("27/1"));
  }

  private static MediaStreamInfo _Stream(int width, int height)
    => new() {
      Index = 0,
      Kind = MediaStreamKind.Video,
      Codec = CodecTag.FromCharacters("MPG1"),
      Width = width,
      Height = height,
      TimeBase = new Rational(1, 25),
      FrameRate = new Rational(25, 1),
    };

  private static RawImage _Gradient(int width, int height, int phase) {
    var data = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var at = (y * width + x) * 3;
      data[at] = (byte)((x * 255 / Math.Max(1, width - 1) + phase * 17) & 0xFF);
      data[at + 1] = (byte)((y * 255 / Math.Max(1, height - 1) + phase * 29) & 0xFF);
      data[at + 2] = (byte)(((x / 5 + y / 3 + phase) & 1) == 0 ? 240 : 24);
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = data };
  }
}
