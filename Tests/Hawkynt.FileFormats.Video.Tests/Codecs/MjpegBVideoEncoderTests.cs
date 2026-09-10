using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using FileFormat.Codecs;
using FileFormat.Core;

namespace Hawkynt.FileFormats.Video.Tests.Codecs;

[TestFixture]
public sealed class MjpegBVideoEncoderTests {

  private static readonly CodecTag _Mjpb = CodecTag.FromCharacters("mjpb");

  [Test]
  [Category("Unit")]
  public void EncodeWritesAlignedFormatBFieldAndRoundTripsThroughDecoder() {
    var stream = new MediaStreamInfo {
      Index = 2,
      Kind = MediaStreamKind.Video,
      Codec = _Mjpb,
      Width = 16,
      Height = 16,
      TimeBase = new Rational(1, 25),
      FrameRate = new Rational(25, 1),
      DeclaredFrameCount = 1,
    };
    var pixels = Enumerable.Range(0, 16 * 16)
      .SelectMany(i => new[] { (byte)(32 + i % 32), (byte)(96 + i % 16), (byte)(160 + i % 48) })
      .ToArray();
    var image = new RawImage {
      Width = 16,
      Height = 16,
      Format = PixelFormat.Rgb24,
      PixelData = pixels,
    };

    var encoder = MjpegBVideoEncoder.Create(stream);
    var described = encoder.DescribeStream();
    Assert.Multiple(() => {
      Assert.That(described.Codec, Is.EqualTo(_Mjpb));
      Assert.That(described.Handler, Is.EqualTo(_Mjpb));
      Assert.That(described.Width, Is.EqualTo(16));
      Assert.That(described.Height, Is.EqualTo(16));
      Assert.That(described.TimeBase, Is.EqualTo(new Rational(1, 25)));
    });

    Assert.That(encoder.TryEncode(image, 7, out var packet), Is.True);
    var data = packet.Data.Span;
    var fieldSize = BinaryPrimitives.ReadUInt32BigEndian(data[8..12]);
    var paddedSize = BinaryPrimitives.ReadUInt32BigEndian(data[12..16]);
    var nextField = BinaryPrimitives.ReadUInt32BigEndian(data[16..20]);
    var quantOffset = BinaryPrimitives.ReadUInt32BigEndian(data[20..24]);
    var huffmanOffset = BinaryPrimitives.ReadUInt32BigEndian(data[24..28]);
    var frameOffset = BinaryPrimitives.ReadUInt32BigEndian(data[28..32]);
    var scanOffset = BinaryPrimitives.ReadUInt32BigEndian(data[32..36]);
    var entropyOffset = BinaryPrimitives.ReadUInt32BigEndian(data[36..40]);

    Assert.Multiple(() => {
      Assert.That(data[4..8].SequenceEqual("mjpg"u8), Is.True);
      Assert.That(fieldSize, Is.GreaterThan(48));
      Assert.That(paddedSize, Is.EqualTo((uint)data.Length));
      Assert.That(paddedSize, Is.GreaterThanOrEqualTo(fieldSize));
      Assert.That(nextField, Is.Zero);
      Assert.That(quantOffset % 16, Is.Zero);
      Assert.That(huffmanOffset % 16, Is.Zero);
      Assert.That(entropyOffset % 16, Is.Zero);
      Assert.That(quantOffset, Is.LessThan(huffmanOffset));
      Assert.That(huffmanOffset, Is.LessThan(frameOffset));
      Assert.That(frameOffset, Is.LessThan(scanOffset));
      Assert.That(scanOffset, Is.LessThan(entropyOffset));
      Assert.That(packet.StreamIndex, Is.EqualTo(2));
      Assert.That(packet.PresentationTimestamp, Is.EqualTo(7));
      Assert.That(packet.DecodeTimestamp, Is.EqualTo(7));
      Assert.That(packet.IsKeyFrame, Is.True);
    });

    var decoder = MjpegBVideoDecoder.Create(described);
    Assert.That(decoder.TryDecode(packet, out var decoded), Is.True);
    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(16));
      Assert.That(decoded.Height, Is.EqualTo(16));
      Assert.That(decoded.PixelData, Is.Not.Empty);
    });
  }

  [Test]
  [Category("Unit")]
  public void DecodeIgnoresTrailingHardwarePaddingBeyondFieldSize() {
    var (stream, packet) = _EncodeGray8();
    var original = packet.Data.ToArray();
    var padded = new byte[original.Length + 16];
    original.CopyTo(padded, 0);
    padded.AsSpan(original.Length).Fill(0xFF);
    BinaryPrimitives.WriteUInt32BigEndian(padded.AsSpan(12, 4), (uint)padded.Length);

    var decoder = MjpegBVideoDecoder.Create(stream);
    var paddedPacket = packet with { Data = padded };
    Assert.That(decoder.TryDecode(paddedPacket, out var decoded), Is.True);
    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(8));
      Assert.That(decoded.Height, Is.EqualTo(8));
    });
  }

  [Test]
  [Category("Unit")]
  public void DecodeRejectsPaddedSizeSmallerThanFieldSize() {
    var (stream, packet) = _EncodeGray8();
    var broken = packet.Data.ToArray();
    var fieldSize = BinaryPrimitives.ReadUInt32BigEndian(broken.AsSpan(8, 4));
    BinaryPrimitives.WriteUInt32BigEndian(broken.AsSpan(12, 4), fieldSize - 1);

    var decoder = MjpegBVideoDecoder.Create(stream);
    var brokenPacket = packet with { Data = broken };
    Assert.Throws<InvalidDataException>(() => decoder.TryDecode(brokenPacket, out _));
  }

  [Test]
  [Category("Unit")]
  public void DecodeNamesImageDescriptionDefaultsItCannotImport() {
    var (stream, packet) = _EncodeGray8();
    var broken = packet.Data.ToArray();
    BinaryPrimitives.WriteUInt32BigEndian(broken.AsSpan(20, 4), 0);

    var decoder = MjpegBVideoDecoder.Create(stream);
    var brokenPacket = packet with { Data = broken };
    var error = Assert.Throws<NotSupportedException>(() => decoder.TryDecode(brokenPacket, out _));
    Assert.That(error!.Message, Does.Contain("mjqt"));
  }

  [Test]
  [Category("Unit")]
  public void EncodeRejectsMidStreamGeometryChange() {
    var encoder = MjpegBVideoEncoder.Create(new() {
      Index = 0,
      Kind = MediaStreamKind.Video,
      Codec = _Mjpb,
      Width = 16,
      Height = 16,
    });
    var wrongSize = new RawImage {
      Width = 8,
      Height = 8,
      Format = PixelFormat.Gray8,
      PixelData = new byte[64],
    };

    Assert.Throws<InvalidDataException>(() => encoder.TryEncode(wrongSize, 0, out _));
  }

  private static (MediaStreamInfo Stream, CodedPacket Packet) _EncodeGray8() {
    var stream = new MediaStreamInfo {
      Index = 0,
      Kind = MediaStreamKind.Video,
      Codec = _Mjpb,
      Width = 8,
      Height = 8,
    };
    var image = new RawImage {
      Width = 8,
      Height = 8,
      Format = PixelFormat.Gray8,
      PixelData = Enumerable.Repeat((byte)112, 64).ToArray(),
    };
    var encoder = MjpegBVideoEncoder.Create(stream);
    Assert.That(encoder.TryEncode(image, 0, out var packet), Is.True);
    return (encoder.DescribeStream(), packet);
  }
}
