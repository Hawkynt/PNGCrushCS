using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;

namespace FileFormat.Codecs.Tests;

/// <summary>TrueMotion 2 packet syntax, all seven block types, previous-frame semantics and writing.</summary>
[TestFixture]
public sealed class TrueMotion2VideoCodecTests {

  [Test]
  [Category("Unit")]
  public void DecoderAndEncoderAreRegisteredForTm20() {
    var stream = _Stream(8, 8);
    Assert.Multiple(() => {
      Assert.That(TrueMotion2VideoDecoder.Accepts(stream), Is.True);
      Assert.That(VideoFormatRegistry.CanDecode(stream), Is.True);
      Assert.That(VideoFormatRegistry.CreateDecoder(stream), Is.InstanceOf<TrueMotion2VideoDecoder>());
      Assert.That(VideoFormatRegistry.CanEncode(stream), Is.True);
      Assert.That(VideoFormatRegistry.CreateEncoder(stream), Is.InstanceOf<TrueMotion2VideoEncoder>());
    });
  }

  [Test]
  [Category("Unit")]
  public void GeometryMustBeDivisibleByFour() {
    Assert.Multiple(() => {
      Assert.Throws<NotSupportedException>(() => TrueMotion2VideoDecoder.Create(_Stream(6, 8)));
      Assert.Throws<NotSupportedException>(() => TrueMotion2VideoEncoder.Create(_Stream(8, 6)));
    });
  }

  [Test]
  [Category("Unit")]
  public void EncoderDescribesVfwTm20() {
    var encoder = TrueMotion2VideoEncoder.Create(_Stream(16, 12, index: 3));
    var stream = encoder.DescribeStream();
    var format = stream.CodecPrivateData.ToArray();

    Assert.Multiple(() => {
      Assert.That(stream.Index, Is.EqualTo(3));
      Assert.That(stream.Codec, Is.EqualTo(CodecTag.FromCharacters("TM20")));
      Assert.That(stream.Handler, Is.EqualTo(CodecTag.FromCharacters("TM20")));
      Assert.That(stream.CodecId, Is.EqualTo("V_MS/VFW/FOURCC"));
      Assert.That(stream.Width, Is.EqualTo(16));
      Assert.That(stream.Height, Is.EqualTo(12));
      Assert.That(stream.BitsPerPixel, Is.EqualTo(24));
      Assert.That(BinaryPrimitives.ReadInt32LittleEndian(format), Is.EqualTo(40));
      Assert.That(format.AsSpan(16, 4).ToArray(), Is.EqualTo("TM20"u8.ToArray()));
    });
  }

  [Test]
  [Category("Unit")]
  public void AKeyFrameRoundTripsASolidColourExactly() {
    var picture = _Solid(8, 8, blue: 180, green: 70, red: 120);
    var encoder = TrueMotion2VideoEncoder.Create(_Stream(8, 8));

    Assert.That(encoder.TryEncode(picture, 9, out var packet), Is.True);
    Assert.Multiple(() => {
      Assert.That(packet.IsKeyFrame, Is.True);
      Assert.That(packet.PresentationTimestamp, Is.EqualTo(9));
      Assert.That(packet.DecodeTimestamp, Is.EqualTo(9));
      Assert.That(packet.Data.Span[..4].ToArray(), Is.EqualTo(new byte[] { 0, 0, 1, 1 }));
    });

    var decoder = TrueMotion2VideoDecoder.Create(encoder.DescribeStream());
    Assert.That(decoder.TryDecode(packet, out var decoded), Is.True);
    Assert.That(decoded.Format, Is.EqualTo(PixelFormat.Bgr24));
    Assert.That(decoded.PixelData, Is.EqualTo(picture.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void AnIdenticalSecondPictureUsesPreviousFrameCodingAndStaysExact() {
    var picture = _Solid(8, 8, blue: 33, green: 91, red: 147);
    var encoder = TrueMotion2VideoEncoder.Create(_Stream(8, 8));
    Assert.That(encoder.TryEncode(picture, 0, out var first), Is.True);
    Assert.That(encoder.TryEncode(picture, 1, out var second), Is.True);
    Assert.That(second.IsKeyFrame, Is.False);

    var decoder = TrueMotion2VideoDecoder.Create(encoder.DescribeStream());
    Assert.That(decoder.TryDecode(first, out _), Is.True);
    Assert.That(decoder.TryDecode(second, out var decoded), Is.True);
    Assert.That(decoded.PixelData, Is.EqualTo(picture.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void AnUpdateFrameChangesThePreviousPicture() {
    var firstPicture = _Solid(8, 8, blue: 30, green: 60, red: 90);
    var secondPicture = _Solid(8, 8, blue: 38, green: 64, red: 98);
    var encoder = TrueMotion2VideoEncoder.Create(_Stream(8, 8));
    Assert.That(encoder.TryEncode(firstPicture, 0, out var first), Is.True);
    Assert.That(encoder.TryEncode(secondPicture, 1, out var second), Is.True);
    Assert.That(second.IsKeyFrame, Is.False);

    var decoder = TrueMotion2VideoDecoder.Create(encoder.DescribeStream());
    decoder.TryDecode(first, out _);
    Assert.That(decoder.TryDecode(second, out var decoded), Is.True);
    Assert.That(decoded.PixelData, Is.EqualTo(secondPicture.PixelData));
  }

  [TestCase(0, 8, 0, 16, 0)]
  [TestCase(1, 0, 2, 16, 0)]
  [TestCase(2, 0, 2, 0, 4)]
  [TestCase(3, 0, 0, 0, 0)]
  [Category("Unit")]
  public void SpatialBlockTypesDecodeWithoutAReference(
    int blockType,
    int chromaHighTokens,
    int chromaLowTokens,
    int lumaHighTokens,
    int lumaLowTokens) {
    var packet = _Packet(
      magic: 0x00000101,
      chromaHigh: _DeltaStream(chromaHighTokens, 0),
      chromaLow: _DeltaStream(chromaLowTokens, 0),
      lumaHigh: _DeltaStream(lumaHighTokens, 0),
      lumaLow: _DeltaStream(lumaLowTokens, 0),
      update: _EmptyStream(),
      motion: _EmptyStream(),
      type: _ConstantStream(1, blockType, null));

    var decoder = TrueMotion2VideoDecoder.Create(_Stream(4, 4));
    Assert.That(decoder.TryDecode(new(0, packet), out var frame), Is.True);
    Assert.That(frame.PixelData, Is.EqualTo(new byte[4 * 4 * 3]));
  }

  [TestCase(4, 24)]
  [TestCase(5, 0)]
  [TestCase(6, 0)]
  [Category("Unit")]
  public void ReferenceBlockTypesUseOnlyThePreviousPicture(int blockType, int updateTokens) {
    var decoder = TrueMotion2VideoDecoder.Create(_Stream(4, 4));
    var key = _Packet(
      0x00000100,
      _DeltaStream(8, 0), _EmptyStream(), _DeltaStream(16, 0), _EmptyStream(),
      _EmptyStream(), _EmptyStream(), _ConstantStream(1, 0, null));
    Assert.That(decoder.TryDecode(new(0, key), out var first), Is.True);

    var predicted = _Packet(
      0x00000101,
      _EmptyStream(), _EmptyStream(), _EmptyStream(), _EmptyStream(),
      updateTokens == 0 ? _EmptyStream() : _DeltaStream(updateTokens, 0),
      blockType == 6 ? _DeltaStream(2, 0) : _EmptyStream(),
      _ConstantStream(1, blockType, null));
    Assert.That(decoder.TryDecode(new(0, predicted), out var second), Is.True);
    Assert.That(second.PixelData, Is.EqualTo(first.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void AReferenceBlockInTheFirstPictureIsRejected() {
    var packet = _Packet(
      0x00000101,
      _EmptyStream(), _EmptyStream(), _EmptyStream(), _EmptyStream(),
      _EmptyStream(), _EmptyStream(), _ConstantStream(1, 5, null));
    var decoder = TrueMotion2VideoDecoder.Create(_Stream(4, 4));

    Assert.Throws<InvalidDataException>(() => decoder.TryDecode(new(0, packet), out _));
  }

  [Test]
  [Category("Unit")]
  public void AnOutOfPictureMotionVectorIsRejected() {
    var decoder = TrueMotion2VideoDecoder.Create(_Stream(4, 4));
    var key = _Packet(
      0x00000101,
      _DeltaStream(8, 0), _EmptyStream(), _DeltaStream(16, 0), _EmptyStream(),
      _EmptyStream(), _EmptyStream(), _ConstantStream(1, 0, null));
    decoder.TryDecode(new(0, key), out _);

    var badMotion = _Packet(
      0x00000101,
      _EmptyStream(), _EmptyStream(), _EmptyStream(), _EmptyStream(), _EmptyStream(),
      _DeltaStream(2, -8),
      _ConstantStream(1, 6, null));

    Assert.Throws<InvalidDataException>(() => decoder.TryDecode(new(0, badMotion), out _));
  }

  [Test]
  [Category("Unit")]
  public void TruncatedStreamIsRejected() {
    var packet = _Packet(
      0x00000101,
      _EmptyStream(), _EmptyStream(), _EmptyStream(), _EmptyStream(),
      _EmptyStream(), _EmptyStream(), _ConstantStream(1, 3, null));
    Array.Resize(ref packet, packet.Length - 4);
    var decoder = TrueMotion2VideoDecoder.Create(_Stream(4, 4));

    Assert.Throws<InvalidDataException>(() => decoder.TryDecode(new(0, packet), out _));
  }

  private static MediaStreamInfo _Stream(int width, int height, int index = 0) => new() {
    Index = index,
    Kind = MediaStreamKind.Video,
    Codec = CodecTag.FromCharacters("TM20"),
    Handler = CodecTag.FromCharacters("TM20"),
    Width = width,
    Height = height,
    BitsPerPixel = 24,
    TimeBase = new Rational(1, 25),
    FrameRate = new Rational(25, 1),
  };

  private static RawImage _Solid(int width, int height, byte blue, byte green, byte red) {
    var data = new byte[width * height * 3];
    for (var i = 0; i < width * height; ++i) {
      data[i * 3] = blue;
      data[i * 3 + 1] = green;
      data[i * 3 + 2] = red;
    }
    return new() { Width = width, Height = height, Format = PixelFormat.Bgr24, PixelData = data };
  }

  private static byte[] _Packet(
    uint magic,
    byte[] chromaHigh,
    byte[] chromaLow,
    byte[] lumaHigh,
    byte[] lumaLow,
    byte[] update,
    byte[] motion,
    byte[] type) {
    var streams = new[] { chromaHigh, chromaLow, lumaHigh, lumaLow, update, motion, type };
    var size = 40 + streams.Sum(static stream => stream.Length);
    var packet = new byte[size];
    BinaryPrimitives.WriteUInt32BigEndian(packet, magic);
    var offset = 40;
    foreach (var stream in streams) {
      stream.CopyTo(packet, offset);
      offset += stream.Length;
    }
    return packet;
  }

  private static byte[] _DeltaStream(int tokenCount, int delta)
    => _ConstantStream(tokenCount, 0, delta);

  private static byte[] _EmptyStream() => _ConstantStream(0, 0, null);

  private static byte[] _ConstantStream(int tokenCount, int symbol, int? delta) {
    using var output = new MemoryStream();
    _WriteUInt32(output, 0);
    _WriteUInt32(output, checked((uint)tokenCount << 1) | (delta.HasValue ? 1u : 0u));
    if (delta.HasValue) {
      _WriteUInt32(output, 1);
      var width = _SignedWidth(delta.Value);
      var bits = new TestBitWriter();
      bits.Write(1, 9);
      bits.Write((uint)width, 5);
      bits.Write(_TwosComplement(delta.Value, width), width);
      output.Write(bits.ToArray());
    }

    var symbolBits = Math.Max(1, _UnsignedWidth(symbol));
    var tree = new TestBitWriter();
    tree.Write((uint)symbolBits, 5);
    tree.Write(0, 5);
    tree.Write(0, 5);
    tree.Write(1, 17);
    tree.Write(0, 1);
    tree.Write((uint)symbol, symbolBits);
    var treeBytes = tree.ToArray();
    _WriteUInt32(output, (uint)(treeBytes.Length / 4));
    _WriteUInt32(output, 0);
    output.Write(treeBytes);
    _WriteUInt32(output, 0);

    var result = output.ToArray();
    BinaryPrimitives.WriteUInt32LittleEndian(result, (uint)((result.Length - 4) / 4));
    return result;
  }

  private static int _SignedWidth(int value) {
    for (var bits = 1; bits <= 31; ++bits)
      if (value >= -(1L << (bits - 1)) && value <= (1L << (bits - 1)) - 1)
        return bits;
    throw new ArgumentOutOfRangeException(nameof(value));
  }

  private static int _UnsignedWidth(int value) {
    var bits = 0;
    do {
      ++bits;
      value >>= 1;
    } while (value != 0);
    return bits;
  }

  private static uint _TwosComplement(int value, int bits)
    => unchecked((uint)value) & ((1u << bits) - 1);

  private static void _WriteUInt32(Stream stream, uint value) {
    Span<byte> bytes = stackalloc byte[4];
    BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
    stream.Write(bytes);
  }

  private sealed class TestBitWriter {
    private readonly List<uint> _words = [];
    private uint _word;
    private int _count;

    internal void Write(uint value, int bits) {
      for (var bit = bits - 1; bit >= 0; --bit) {
        this._word |= ((value >> bit) & 1) << (31 - this._count);
        if (++this._count != 32)
          continue;
        this._words.Add(this._word);
        this._word = 0;
        this._count = 0;
      }
    }

    internal byte[] ToArray() {
      var result = new byte[(this._words.Count + (this._count == 0 ? 0 : 1)) * 4];
      var offset = 0;
      foreach (var word in this._words) {
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(offset), word);
        offset += 4;
      }
      if (this._count != 0)
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(offset), this._word);
      return result;
    }
  }
}
