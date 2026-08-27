using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;

namespace FileFormat.Codecs.Tests;

[TestFixture]
public sealed class MvhaVideoDecoderTests {

  private const uint _LZYV = (uint)'L' | ((uint)'Z' << 8) | ((uint)'Y' << 16) | ((uint)'V' << 24);
  private const uint _HUFY = (uint)'H' | ((uint)'U' << 8) | ((uint)'F' << 16) | ((uint)'Y' << 24);

  [Test]
  [Category("Unit")]
  public void ZlibPayloadRestoresBottomUpYuv422ResidualPlanes() {
    var decoder = MvhaVideoDecoder.Create(_Stream(2, 1));
    var packet = _Packet(_LZYV, _Zlib(new byte[4]));

    Assert.That(decoder.TryDecode(new(0, packet), out var frame), Is.True);
    Assert.That(frame.Format, Is.EqualTo(PixelFormat.Rgb24));
    Assert.That(frame.PixelData, Is.EqualTo(new byte[] { 0, 135, 0, 0, 135, 0 }));
  }

  [Test]
  [Category("Unit")]
  public void OneSymbolHuffmanPayloadMatchesTheSameResidualPlane() {
    var decoder = MvhaVideoDecoder.Create(_Stream(2, 1));
    var bits = new MsbBitWriter();
    bits.WriteBits(0, 24);
    bits.WriteBits(255, 8); // one-symbol tree adds one with byte wrap => decoded residual zero
    bits.WriteBits(0, 8);   // symbol count minus one
    bits.WriteBit(0);       // short probability
    bits.WriteBits(1, 3);
    for (var i = 0; i < 4; ++i)
      bits.WriteBit(1);     // one-symbol VLC is the explicit code 1

    var packet = _Packet(_HUFY, bits.ToArray());
    Assert.That(decoder.TryDecode(new(0, packet), out var frame), Is.True);
    Assert.That(frame.PixelData, Is.EqualTo(new byte[] { 0, 135, 0, 0, 135, 0 }));
  }

  [Test]
  [Category("Unit")]
  public void UnknownPacketTypeRefuses() {
    var decoder = MvhaVideoDecoder.Create(_Stream(2, 1));
    var packet = _Packet(0x12345678, new byte[] { 1 });

    Assert.Throws<NotSupportedException>(() => decoder.TryDecode(new(0, packet), out _));
  }

  [Test]
  [Category("Unit")]
  public void OddWidthRefusesBecauseChromaIsStoredForPixelPairs()
    => Assert.Throws<NotSupportedException>(() => MvhaVideoDecoder.Create(_Stream(3, 1)));

  [Test]
  [Category("Unit")]
  public void CodecIsRegistered() {
    var stream = _Stream(2, 2);
    Assert.That(VideoFormatRegistry.AllCodecs.Select(codec => codec.CodecName), Does.Contain("MidiVid Archive Codec"));
    Assert.That(VideoFormatRegistry.CanDecode(stream), Is.True);
    Assert.That(VideoFormatRegistry.CreateDecoder(stream), Is.InstanceOf<MvhaVideoDecoder>());
  }

  private static MediaStreamInfo _Stream(int width, int height) => new() {
    Index = 0,
    Kind = MediaStreamKind.Video,
    Codec = CodecTag.FromCharacters("MVHA"),
    Width = width,
    Height = height,
    BitsPerPixel = 16,
  };

  private static byte[] _Packet(uint type, ReadOnlySpan<byte> payload) {
    var packet = new byte[8 + payload.Length];
    BinaryPrimitives.WriteUInt32BigEndian(packet, type);
    BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(4), checked((uint)payload.Length));
    payload.CopyTo(packet.AsSpan(8));
    return packet;
  }

  private static byte[] _Zlib(ReadOnlySpan<byte> data) {
    using var output = new MemoryStream();
    using (var zlib = new ZLibStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
      zlib.Write(data);
    return output.ToArray();
  }

  private sealed class MsbBitWriter {
    private readonly List<byte> _bytes = [];
    private int _position;

    internal void WriteBit(int bit) => this.WriteBits((uint)bit, 1);

    internal void WriteBits(uint value, int count) {
      for (var bit = count - 1; bit >= 0; --bit) {
        var byteIndex = this._position >> 3;
        if (byteIndex == this._bytes.Count)
          this._bytes.Add(0);
        if (((value >> bit) & 1) != 0)
          this._bytes[byteIndex] |= (byte)(1 << (7 - (this._position & 7)));
        ++this._position;
      }
    }

    internal byte[] ToArray() => [.. this._bytes];
  }
}
