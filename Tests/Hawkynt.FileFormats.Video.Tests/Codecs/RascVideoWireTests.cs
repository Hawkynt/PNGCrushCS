using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using FileFormat.Core;

namespace FileFormat.Codecs.Tests;

[TestFixture]
public sealed class RascVideoWireTests {

  [Test]
  [Category("Unit")]
  public void Pal8PaletteWordsStoreBlueInTheLowByte() {
    var decoder = RascVideoDecoder.Create(_Stream(1, 1, 8));
    var format = _Format(1, 1, 8);
    BinaryPrimitives.WriteUInt32LittleEndian(format.AsSpan(72), 30u | (20u << 8) | (10u << 16));

    Assert.That(decoder.TryDecode(_Packet(_Chunk("FINT", format)), out var decoded), Is.True);
    Assert.That(decoded.PixelData, Is.EqualTo(new byte[] { 10, 20, 30 }));
  }

  [Test]
  [Category("Unit")]
  public void Rgb555LittleEndianStoresBlueInTheLowFiveBits() {
    var decoder = RascVideoDecoder.Create(_Stream(1, 1, 16));
    var format = _Format(1, 1, 16);
    var surfaces = new byte[] { 0x00, 0x7c, 0x00, 0x7c };
    var payload = _Concat(format, _Deflate(surfaces));

    Assert.That(decoder.TryDecode(_Packet(_Chunk("KFRM", payload)), out var decoded), Is.True);
    Assert.That(decoded.PixelData, Is.EqualTo(new byte[] { 255, 0, 0 }));
  }

  [Test]
  [Category("Unit")]
  public void EncoderWritesPal8PaletteWordsInBgrOrder() {
    var encoder = RascVideoEncoder.Create(_Stream(1, 1, 8));
    var source = new RawImage {
      Width = 1,
      Height = 1,
      Format = PixelFormat.Indexed8,
      PixelData = [0],
      Palette = [10, 20, 30],
      PaletteCount = 1,
    };

    encoder.TryEncode(source, null, out var packet);

    Assert.That(packet.Data.Span.Slice(84, 4).ToArray(), Is.EqualTo(new byte[] { 30, 20, 10, 0 }));
  }

  [Test]
  [Category("Unit")]
  public void EncoderWritesRedAsTheHighFiveBitsOfRgb555() {
    var encoder = RascVideoEncoder.Create(_Stream(1, 1, 16));
    var source = new RawImage {
      Width = 1,
      Height = 1,
      Format = PixelFormat.Rgb24,
      PixelData = [255, 0, 0],
    };

    encoder.TryEncode(source, null, out var packet);
    var surfaces = _Inflate(packet.Data.Span[84..].ToArray());

    Assert.That(surfaces, Is.EqualTo(new byte[] { 0x00, 0x7c, 0x00, 0x7c }));
  }

  [Test]
  [Category("Unit")]
  public void TruncatedKeyframeZlibRefusesInsteadOfZeroFillingTheMissingSurface() {
    var decoder = RascVideoDecoder.Create(_Stream(1, 1, 32));
    var format = _Format(1, 1, 32);
    var onlyOneSurface = _Deflate([1, 2, 3, 0]);
    var payload = _Concat(format, onlyOneSurface);

    Assert.Throws<InvalidDataException>(() => decoder.TryDecode(_Packet(_Chunk("KFRM", payload)), out _));
  }

  [Test]
  [Category("Unit")]
  public void FourByteDeltaOperationCannotCrossAPal8RowBoundary() {
    var decoder = RascVideoDecoder.Create(_Stream(2, 1, 8));
    var format = _Format(2, 1, 8);
    decoder.TryDecode(_Packet(_Chunk("FINT", format)), out _);

    var delta = new byte[46];
    BinaryPrimitives.WriteUInt32LittleEndian(delta.AsSpan(12), 6);
    BinaryPrimitives.WriteUInt32LittleEndian(delta.AsSpan(16), 0);
    BinaryPrimitives.WriteUInt32LittleEndian(delta.AsSpan(20), 0);
    BinaryPrimitives.WriteUInt32LittleEndian(delta.AsSpan(24), 2);
    BinaryPrimitives.WriteUInt32LittleEndian(delta.AsSpan(28), 1);
    BinaryPrimitives.WriteUInt32LittleEndian(delta.AsSpan(36), 0);
    new byte[] { 13, 1, 1, 2, 3, 4 }.CopyTo(delta, 40);

    Assert.Throws<InvalidDataException>(() => decoder.TryDecode(_Packet(_Chunk("DLTA", delta)), out _));
  }

  private static MediaStreamInfo _Stream(int width, int height, int bitsPerPixel) => new() {
    Index = 0,
    Kind = MediaStreamKind.Video,
    Codec = CodecTag.FromCharacters("RASC"),
    Width = width,
    Height = height,
    BitsPerPixel = bitsPerPixel,
  };

  private static byte[] _Format(int width, int height, int bitsPerPixel) {
    var result = new byte[72 + (bitsPerPixel == 8 ? 256 * 4 : 0)];
    BinaryPrimitives.WriteUInt32LittleEndian(result, 0x65);
    BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(8), checked((uint)width));
    BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(12), checked((uint)height));
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(46), checked((ushort)bitsPerPixel));
    return result;
  }

  private static CodedPacket _Packet(byte[] data) => new(
    StreamIndex: 0,
    Data: data,
    PresentationTimestamp: null,
    DecodeTimestamp: null,
    Duration: 1,
    IsKeyFrame: false);

  private static byte[] _Chunk(string tag, ReadOnlySpan<byte> payload) {
    var result = new byte[checked(8 + payload.Length)];
    for (var i = 0; i < 4; ++i)
      result[i] = checked((byte)tag[i]);
    BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4), checked((uint)payload.Length));
    payload.CopyTo(result.AsSpan(8));
    return result;
  }

  private static byte[] _Concat(ReadOnlySpan<byte> first, ReadOnlySpan<byte> second) {
    var result = new byte[checked(first.Length + second.Length)];
    first.CopyTo(result);
    second.CopyTo(result.AsSpan(first.Length));
    return result;
  }

  private static byte[] _Deflate(ReadOnlySpan<byte> source) {
    using var output = new MemoryStream();
    using (var zlib = new ZLibStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
      zlib.Write(source);
    return output.ToArray();
  }

  private static byte[] _Inflate(byte[] source) {
    using var input = new MemoryStream(source, writable: false);
    using var zlib = new ZLibStream(input, CompressionMode.Decompress);
    using var output = new MemoryStream();
    zlib.CopyTo(output);
    return output.ToArray();
  }
}
