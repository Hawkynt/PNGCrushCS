using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;

namespace FileFormat.Codecs.Tests;

[TestFixture]
public sealed class VbleVideoDecoderTests {

  [Test]
  [Category("Unit")]
  public void TheVbleFourccIsTaken()
    => Assert.That(VbleVideoDecoder.Accepts(_Stream(2, 2)), Is.True);

  [Test]
  [Category("Unit")]
  public void TheCodecIsRegistered() {
    var stream = _Stream(2, 2);
    Assert.That(VideoFormatRegistry.AllCodecs.Select(codec => codec.CodecName), Does.Contain("VBLE Lossless Codec"));
    Assert.That(VideoFormatRegistry.CanDecode(stream), Is.True);
    Assert.That(VideoFormatRegistry.CreateDecoder(stream), Is.InstanceOf<VbleVideoDecoder>());
  }

  [Test]
  [Category("Unit")]
  public void ReverseUnaryResidualsAndMedianPredictionReconstructAConstantPicture() {
    var decoder = VbleVideoDecoder.Create(_Stream(2, 2));
    var packet = _ConstantNeutralFrame(82);

    Assert.That(decoder.TryDecode(new(0, packet), out var frame), Is.True);
    Assert.That(frame.Width, Is.EqualTo(2));
    Assert.That(frame.Height, Is.EqualTo(2));
    Assert.That(frame.Format, Is.EqualTo(PixelFormat.Rgb24));
    Assert.That(frame.PixelData, Is.EqualTo(new byte[] {
      77, 77, 77,  77, 77, 77,
      77, 77, 77,  77, 77, 77,
    }));
  }

  [Test]
  [Category("Unit")]
  public void StudioBlackWithNeutralChromaDecodesToBlack() {
    var decoder = VbleVideoDecoder.Create(_Stream(2, 2));
    Assert.That(decoder.TryDecode(new(0, _ConstantNeutralFrame(16)), out var frame), Is.True);
    Assert.That(frame.PixelData, Is.EqualTo(new byte[12]));
  }

  [Test]
  [Category("Unit")]
  public void AVersionOtherThanOneRefuses() {
    var packet = _ConstantNeutralFrame(16);
    BinaryPrimitives.WriteUInt32LittleEndian(packet, 2);
    var decoder = VbleVideoDecoder.Create(_Stream(2, 2));
    Assert.Throws<NotSupportedException>(() => decoder.TryDecode(new(0, packet), out _));
  }

  [Test]
  [Category("Unit")]
  public void AnUnterminatedReverseUnaryLengthRefuses() {
    var packet = new byte[5];
    BinaryPrimitives.WriteUInt32LittleEndian(packet, 1);
    // The fifth byte is zero: eight zero unary bits with no terminating ninth one.
    var decoder = VbleVideoDecoder.Create(_Stream(2, 2));
    Assert.Throws<InvalidDataException>(() => decoder.TryDecode(new(0, packet), out _));
  }

  [Test]
  [Category("Unit")]
  public void OddYuv420DimensionsRefuse()
    => Assert.Throws<NotSupportedException>(() => VbleVideoDecoder.Create(_Stream(3, 2)));

  private static byte[] _ConstantNeutralFrame(byte luma) {
    // A 2x2 YUV420 picture has lengths for four Y samples, one U and one V. For a constant luma
    // picture the first sample of each row carries the value and the second has zero residual;
    // chroma's one-sample planes carry 128 each.
    var lumaEncoded = _EncodeSigned(luma);
    var chromaEncoded = _EncodeSigned(128);

    var writer = new LittleEndianBitWriter();
    writer.WriteReverseUnary(lumaEncoded.Length);
    writer.WriteReverseUnary(0);
    writer.WriteReverseUnary(lumaEncoded.Length);
    writer.WriteReverseUnary(0);
    writer.WriteReverseUnary(chromaEncoded.Length);
    writer.WriteReverseUnary(chromaEncoded.Length);

    writer.WriteBits(lumaEncoded.Payload, lumaEncoded.Length);
    writer.WriteBits(lumaEncoded.Payload, lumaEncoded.Length);
    writer.WriteBits(chromaEncoded.Payload, chromaEncoded.Length);
    writer.WriteBits(chromaEncoded.Payload, chromaEncoded.Length);

    var bits = writer.ToArray();
    var packet = new byte[4 + bits.Length];
    BinaryPrimitives.WriteUInt32LittleEndian(packet, 1);
    bits.CopyTo(packet, 4);
    return packet;
  }

  private static (int Length, uint Payload) _EncodeSigned(byte wrappedDifference) {
    var signed = wrappedDifference <= 127 ? wrappedDifference : wrappedDifference - 256;
    if (signed == 0)
      return (0, 0);

    var encoded = signed < 0 ? (-signed * 2 - 1) : signed * 2;
    var length = 0;
    while (((1 << (length + 1)) - 1) <= encoded && length < 8)
      ++length;
    var payload = (uint)(encoded - (1 << length) + 1);
    return (length, payload);
  }

  private static MediaStreamInfo _Stream(int width, int height) => new() {
    Index = 0,
    Kind = MediaStreamKind.Video,
    Codec = CodecTag.FromCharacters("VBLE"),
    Width = width,
    Height = height,
  };

  private sealed class LittleEndianBitWriter {
    private readonly List<byte> _bytes = [];
    private int _position;

    internal void WriteReverseUnary(int length) {
      for (var i = 0; i < length; ++i)
        this.WriteBit(0);
      this.WriteBit(1);
    }

    internal void WriteBit(int bit) => this.WriteBits((uint)bit, 1);

    internal void WriteBits(uint value, int count) {
      for (var i = 0; i < count; ++i) {
        var byteIndex = this._position >> 3;
        var bitIndex = this._position & 7;
        if (byteIndex == this._bytes.Count)
          this._bytes.Add(0);
        if (((value >> i) & 1) != 0)
          this._bytes[byteIndex] |= (byte)(1 << bitIndex);
        ++this._position;
      }
    }

    internal byte[] ToArray() => [.. this._bytes];
  }
}
