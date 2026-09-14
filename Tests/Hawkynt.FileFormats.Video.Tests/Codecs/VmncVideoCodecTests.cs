using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;

namespace FileFormat.Codecs.Tests;

[TestFixture]
public sealed class VmncVideoCodecTests {

  [Test]
  [Category("Unit")]
  public void EncoderWritesKeyFrameThenFramebufferDelta() {
    var requested = _Stream(3, 2, 32);
    var encoder = VmncVideoEncoder.Create(requested);
    var described = encoder.DescribeStream();
    var decoder = VmncVideoDecoder.Create(described);

    var first = _Image(3, 2,
      [255, 0, 0], [0, 255, 0], [0, 0, 255],
      [12, 34, 56], [78, 90, 123], [210, 211, 212]);

    Assert.That(encoder.TryEncode(first, 0, out var keyPacket), Is.True);
    Assert.That(keyPacket.IsKeyFrame, Is.True);
    Assert.That(described.BitsPerPixel, Is.EqualTo(32));
    Assert.That(BinaryPrimitives.ReadUInt16BigEndian(keyPacket.Data.Span.Slice(2, 2)), Is.EqualTo(2));
    Assert.That(BinaryPrimitives.ReadUInt32BigEndian(keyPacket.Data.Span.Slice(12, 4)), Is.EqualTo(0x574D5669u));
    Assert.That(decoder.TryDecode(keyPacket, out var firstDecoded), Is.True);
    Assert.That(firstDecoded.PixelData, Is.EqualTo(first.PixelData));

    var secondData = (byte[])first.PixelData.Clone();
    secondData[(1 * 3 + 1) * 3] = 1;
    secondData[(1 * 3 + 1) * 3 + 1] = 2;
    secondData[(1 * 3 + 1) * 3 + 2] = 3;
    var second = new RawImage { Width = 3, Height = 2, Format = PixelFormat.Rgb24, PixelData = secondData };

    Assert.That(encoder.TryEncode(second, 1, out var deltaPacket), Is.True);
    Assert.That(deltaPacket.IsKeyFrame, Is.False);
    Assert.That(BinaryPrimitives.ReadUInt16BigEndian(deltaPacket.Data.Span.Slice(2, 2)), Is.EqualTo(1));
    Assert.That(decoder.TryDecode(deltaPacket, out var secondDecoded), Is.True);
    Assert.That(secondDecoded.PixelData, Is.EqualTo(second.PixelData));

    Assert.That(encoder.TryEncode(second, 2, out var repeatPacket), Is.True);
    Assert.That(repeatPacket.IsKeyFrame, Is.False);
    Assert.That(BinaryPrimitives.ReadUInt16BigEndian(repeatPacket.Data.Span.Slice(2, 2)), Is.Zero);
    Assert.That(decoder.TryDecode(repeatPacket, out var repeated), Is.True);
    Assert.That(repeated.PixelData, Is.EqualTo(second.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void CopyRectReferencesThePreviousFramebuffer() {
    var decoder = VmncVideoDecoder.Create(_Stream(3, 1, 16));
    var red = _Rgb555(31, 0, 0);
    var green = _Rgb555(0, 31, 0);
    var blue = _Rgb555(0, 0, 31);
    var white = _Rgb555(31, 31, 31);

    Assert.That(decoder.TryDecode(new(0, _Packet(
      _Chunk(0, 0, 3, 1, 0, [.. red, .. green, .. blue]))), out _), Is.True);

    var copyPayload = new byte[4];
    BinaryPrimitives.WriteUInt16BigEndian(copyPayload, 0);
    BinaryPrimitives.WriteUInt16BigEndian(copyPayload.AsSpan(2), 0);
    var update = _Packet(
      _Chunk(0, 0, 1, 1, 0, white),
      _Chunk(1, 0, 1, 1, 1, copyPayload));

    Assert.That(decoder.TryDecode(new(0, update), out var frame), Is.True);
    Assert.That(frame.PixelData, Is.EqualTo(new byte[] {
      255, 255, 255,
      255, 0, 0,
      0, 0, 255,
    }));
  }

  [Test]
  [Category("Unit")]
  public void RreAndCoRrePaintTheirSubrectangles() {
    var blue = _Rgb555(0, 0, 31);
    var red = _Rgb555(31, 0, 0);

    var rre = new MemoryStream();
    _WriteUInt32BigEndian(rre, 1);
    rre.Write(blue);
    rre.Write(red);
    _WriteUInt16BigEndian(rre, 1);
    _WriteUInt16BigEndian(rre, 0);
    _WriteUInt16BigEndian(rre, 1);
    _WriteUInt16BigEndian(rre, 1);

    var rreDecoder = VmncVideoDecoder.Create(_Stream(3, 1, 16));
    Assert.That(rreDecoder.TryDecode(new(0, _Packet(_Chunk(0, 0, 3, 1, 2, rre.ToArray()))), out var rreFrame), Is.True);
    Assert.That(rreFrame.PixelData, Is.EqualTo(new byte[] {
      0, 0, 255, 255, 0, 0, 0, 0, 255,
    }));

    var corre = new MemoryStream();
    _WriteUInt32BigEndian(corre, 1);
    corre.Write(blue);
    corre.Write(red);
    corre.WriteByte(1);
    corre.WriteByte(0);
    corre.WriteByte(1);
    corre.WriteByte(1);

    var correDecoder = VmncVideoDecoder.Create(_Stream(3, 1, 16));
    Assert.That(correDecoder.TryDecode(new(0, _Packet(_Chunk(0, 0, 3, 1, 4, corre.ToArray()))), out var correFrame), Is.True);
    Assert.That(correFrame.PixelData, Is.EqualTo(rreFrame.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void CursorVisibilityAndAlphaCursorAreHonoured() {
    var decoder = VmncVideoDecoder.Create(_Stream(1, 1, 32));
    var blue = new byte[] { 255, 0, 0, 0 };
    var alphaCursor = new byte[] {
      1, 0,             // alpha cursor type, padding
      255, 0, 0, 128,   // half-transparent red RGBA
    };

    var visible = _Packet(
      _Chunk(0, 0, 1, 1, 0, blue),
      _Chunk(0, 0, 1, 1, 0x574D5664, alphaCursor),
      _Chunk(0, 0, 0, 0, 0x574D5666, []));
    Assert.That(decoder.TryDecode(new(0, visible), out var withCursor), Is.True);
    Assert.That(withCursor.PixelData, Is.EqualTo(new byte[] { 128, 0, 127 }));

    var hidden = _Packet(_Chunk(0, 0, 0, 0, 0x574D5665, [0, 0]));
    Assert.That(decoder.TryDecode(new(0, hidden), out var withoutCursor), Is.True);
    Assert.That(withoutCursor.PixelData, Is.EqualTo(new byte[] { 0, 0, 255 }));
  }

  [Test]
  [Category("Unit")]
  public void DisplayModeRecordCanResizeTheFramebuffer() {
    var decoder = VmncVideoDecoder.Create(_Stream(1, 1, 32));
    var descriptor = new byte[] {
      32, 24, 0, 1,
      0, 255, 0, 255, 0, 255,
      16, 8, 0,
      0, 0, 0,
    };
    var pixels = new byte[] {
      0, 0, 255, 0,
      0, 255, 0, 0,
    };

    var packet = _Packet(
      _Chunk(0, 0, 2, 1, 0x574D5669, descriptor),
      _Chunk(0, 0, 2, 1, 0, pixels));

    Assert.That(decoder.TryDecode(new(0, packet), out var frame), Is.True);
    Assert.That(frame.Width, Is.EqualTo(2));
    Assert.That(frame.Height, Is.EqualTo(1));
    Assert.That(frame.PixelData, Is.EqualTo(new byte[] { 255, 0, 0, 0, 255, 0 }));
  }

  [Test]
  [Category("Unit")]
  public void EncoderAndDecoderAreBothRegistered() {
    Assert.That(VideoFormatRegistry.AllCodecs.Select(static codec => codec.CodecName),
      Does.Contain("VMware Screen Codec / VMware Video"));
    Assert.That(VideoFormatRegistry.AllEncoders.Select(static codec => codec.CodecName),
      Does.Contain("VMware Screen Codec / VMware Video"));
  }

  private static MediaStreamInfo _Stream(int width, int height, short bitsPerPixel) => new() {
    Index = 0,
    Kind = MediaStreamKind.Video,
    Codec = CodecTag.FromCharacters("VMnc"),
    Handler = CodecTag.FromCharacters("VMnc"),
    Width = width,
    Height = height,
    BitsPerPixel = bitsPerPixel,
    TimeBase = new Rational(1, 25),
    FrameRate = new Rational(25, 1),
  };

  private static RawImage _Image(int width, int height, params byte[][] pixels) {
    var data = pixels.SelectMany(static pixel => pixel).ToArray();
    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = data };
  }

  private static byte[] _Rgb555(int red, int green, int blue) {
    var result = new byte[2];
    BinaryPrimitives.WriteUInt16LittleEndian(result, checked((ushort)((red << 10) | (green << 5) | blue)));
    return result;
  }

  private static byte[] _Packet(params byte[][] chunks) {
    using var output = new MemoryStream();
    output.WriteByte(0);
    output.WriteByte(0);
    _WriteUInt16BigEndian(output, checked((ushort)chunks.Length));
    foreach (var chunk in chunks)
      output.Write(chunk);
    return output.ToArray();
  }

  private static byte[] _Chunk(ushort x, ushort y, ushort width, ushort height, uint encoding, byte[] payload) {
    using var output = new MemoryStream();
    _WriteUInt16BigEndian(output, x);
    _WriteUInt16BigEndian(output, y);
    _WriteUInt16BigEndian(output, width);
    _WriteUInt16BigEndian(output, height);
    _WriteUInt32BigEndian(output, encoding);
    output.Write(payload);
    return output.ToArray();
  }

  private static void _WriteUInt16BigEndian(Stream output, ushort value) {
    Span<byte> bytes = stackalloc byte[2];
    BinaryPrimitives.WriteUInt16BigEndian(bytes, value);
    output.Write(bytes);
  }

  private static void _WriteUInt32BigEndian(Stream output, uint value) {
    Span<byte> bytes = stackalloc byte[4];
    BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
    output.Write(bytes);
  }
}
