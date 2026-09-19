using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Codecs.Ffv1.Tests;

/// <summary>Malformed FFV1 boundaries which must fail rather than borrow bytes from a footer or invent coder state.</summary>
[TestFixture]
public class Ffv1MalformedInputTests {

  [Test]
  [Category("Unit")]
  public void TruncatedVersion3FooterIsRefused() {
    var (stream, packet) = _Version3Packet(sliceCrc: false);
    var truncated = packet.Data[..^1].ToArray();
    var decoder = Ffv1Decoder.Create(stream);

    Assert.Throws<InvalidDataException>(() => decoder.TryDecode(packet with { Data = truncated }, out _));
  }

  [Test]
  [Category("Unit")]
  public void Version3SliceLengthCannotPointBeforeTheFrame() {
    var (stream, packet) = _Version3Packet(sliceCrc: false);
    var damaged = packet.Data.ToArray();
    damaged[^3] = 0xFF;
    damaged[^2] = 0xFF;
    damaged[^1] = 0xFF;
    var decoder = Ffv1Decoder.Create(stream);

    var failure = Assert.Throws<InvalidDataException>(() => decoder.TryDecode(packet with { Data = damaged }, out _));
    Assert.That(failure!.Message, Does.Contain("remain").Or.Contain("footer chain"));
  }

  [Test]
  [Category("Unit")]
  public void Version3CrcDamageIsRefusedBeforeEntropyDecode() {
    var (stream, packet) = _Version3Packet(sliceCrc: true);
    var damaged = packet.Data.ToArray();
    damaged[2] ^= 0x20;
    var decoder = Ffv1Decoder.Create(stream);

    var failure = Assert.Throws<InvalidDataException>(() => decoder.TryDecode(packet with { Data = damaged }, out _));
    Assert.That(failure!.Message, Does.Contain("checksum"));
  }

  [Test]
  [Category("Unit")]
  public void LegacyGolombFrameTruncatedInsideSamplesIsRefused() {
    const int width = 31;
    const int height = 17;
    var source = _Grey(width, height, 73);
    var encoder = Ffv1Encoder.Create(_Stream(width, height), PixelFormat.Gray8, new() {
      Version = 1,
      EntropyCoder = Ffv1EntropyCoder.GolombRice,
    });
    encoder.TryEncode(source, 0, out var packet);
    var shortened = packet.Data[..(packet.Data.Length / 2)].ToArray();
    var decoder = Ffv1Decoder.Create(encoder.DescribeStream());

    Assert.Throws<InvalidDataException>(() => decoder.TryDecode(packet with { Data = shortened }, out _));
  }

  [Test]
  [Category("Unit")]
  public void Version3GolombCannotConsumeItsFooterAsSampleBits() {
    const int width = 29;
    const int height = 13;
    var source = _Grey(width, height, 91);
    var encoder = Ffv1Encoder.Create(_Stream(width, height), PixelFormat.Gray8, new() {
      Version = 3,
      EntropyCoder = Ffv1EntropyCoder.GolombRice,
      SliceCrc = false,
      HorizontalSlices = 1,
      VerticalSlices = 1,
    });
    encoder.TryEncode(source, 0, out var packet);

    // Remove a sizeable part of the actual slice body and write a self-consistent shorter footer.
    // A decoder that accidentally includes the footer in its Golomb bit reader can otherwise turn
    // those three length bytes into plausible sample bits instead of reporting truncation.
    var oldBodyLength = (packet.Data.Span[^3] << 16) | (packet.Data.Span[^2] << 8) | packet.Data.Span[^1];
    var removed = Math.Max(4, oldBodyLength / 3);
    var newBodyLength = oldBodyLength - removed;
    var shortened = new byte[newBodyLength + 3];
    packet.Data.Span[..newBodyLength].CopyTo(shortened);
    shortened[^3] = (byte)(newBodyLength >> 16);
    shortened[^2] = (byte)(newBodyLength >> 8);
    shortened[^1] = (byte)newBodyLength;

    var decoder = Ffv1Decoder.Create(encoder.DescribeStream());
    Assert.Throws<InvalidDataException>(() => decoder.TryDecode(packet with { Data = shortened }, out _));
  }

  [Test]
  [Category("Unit")]
  public void OneSliceMayCoverSeveralConfiguredRasterCells() {
    const int width = 8;
    const int height = 6;
    var source = _Grey(width, height, 117);
    var descriptor = Ffv1Encoder.Create(_Stream(width, height), PixelFormat.Gray8, new() {
      HorizontalSlices = 2,
      VerticalSlices = 2,
      SliceCrc = false,
    }).DescribeStream();
    var parameters = _Parameters(descriptor);

    var (zero, one) = Ffv1StateTransition.Build([]);
    var coder = new Ffv1RangeEncoder(zero, one);
    var keyState = _States();
    coder.Put(keyState, 0, 1);

    var header = _States();
    coder.Symbol(header, 0, false); // x
    coder.Symbol(header, 0, false); // y
    coder.Symbol(header, 1, false); // two raster cells wide, stored minus one
    coder.Symbol(header, 1, false); // two high
    for (var i = 0; i < parameters.QuantTableSetIndexCount; ++i)
      coder.Symbol(header, 0, false);
    coder.Symbol(header, 3, false);
    coder.Symbol(header, 0, false);
    coder.Symbol(header, 0, false);

    var plane = new Ffv1Plane(width, height);
    for (var i = 0; i < source.PixelData.Length; ++i)
      plane.Samples[i] = source.PixelData[i];

    var contexts = new byte[parameters.ContextCount[0]][];
    for (var i = 0; i < contexts.Length; ++i)
      contexts[i] = _States();
    new Ffv1SliceEncoder(parameters).EncodePlane(coder, plane, contexts, 0);

    var body = coder.Terminate(true);
    var frame = new byte[body.Length + 3];
    body.CopyTo(frame, 0);
    frame[^3] = (byte)(body.Length >> 16);
    frame[^2] = (byte)(body.Length >> 8);
    frame[^1] = (byte)body.Length;

    var decoder = Ffv1Decoder.Create(descriptor);
    Assert.That(decoder.TryDecode(new(0, frame), out var decoded), Is.True);
    Assert.That(decoded.PixelData, Is.EqualTo(source.PixelData));
  }

  private static (MediaStreamInfo Stream, CodedPacket Packet) _Version3Packet(bool sliceCrc) {
    var source = _Grey(17, 9, 51);
    var encoder = Ffv1Encoder.Create(_Stream(17, 9), PixelFormat.Gray8, new() {
      SliceCrc = sliceCrc,
      HorizontalSlices = 1,
      VerticalSlices = 1,
    });
    encoder.TryEncode(source, 0, out var packet);
    return (encoder.DescribeStream(), packet);
  }

  private static Ffv1Parameters _Parameters(MediaStreamInfo stream) {
    var (zero, one) = Ffv1StateTransition.Build([]);
    return Ffv1Parameters.Read(new Ffv1RangeCoder(stream.CodecPrivateData[..^4], zero, one), _States(), true);
  }

  private static byte[] _States() {
    var states = new byte[Ffv1RangeCoder.CONTEXT_SIZE];
    Array.Fill(states, (byte)128);
    return states;
  }

  private static RawImage _Grey(int width, int height, int seed) {
    var pixels = new byte[width * height];
    new Random(seed).NextBytes(pixels);
    return new() { Width = width, Height = height, Format = PixelFormat.Gray8, PixelData = pixels };
  }

  private static MediaStreamInfo _Stream(int width, int height) => new() {
    Index = 11,
    Kind = MediaStreamKind.Video,
    Width = width,
    Height = height,
    BitsPerPixel = 8,
    TimeBase = new Rational(1, 25),
    FrameRate = new Rational(25, 1),
  };
}