extern alias Images;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BitmapInfoHeader = Images::FileFormat.Bmp.BitmapInfoHeader;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;

namespace FileFormat.Codecs.Tests;

[TestFixture]
public sealed class LgplLosslessDecoderTests {

  [Test]
  [Category("Unit")]
  public void LocoRgbRestoresThreeIndependentRicePlanes() {
    var decoder = LocoVideoDecoder.Create(_LocoStream(2, 1, 3));

    // Initial k is 3. Each two-pixel plane still fits exactly in one byte here. Unsigned Rice value
    // 2 restores +1, value 4 restores +2, and value 6 restores +3; repeating each residual makes
    // the left predictor visible as well as the B/G/R plane ordering.
    var packet = new byte[] { 0xAA, 0xCC, 0xEE }; // B, G, R planes
    Assert.That(decoder.TryDecode(new(0, packet), out var frame), Is.True);
    Assert.That(frame.Format, Is.EqualTo(PixelFormat.Rgb24));
    Assert.That(frame.PixelData, Is.EqualTo(new byte[] {
      131, 130, 129,
      134, 132, 130,
    }));
  }

  [Test]
  [Category("Unit")]
  public void LocoZeroResidualUsesItsRunSubcode() {
    var decoder = LocoVideoDecoder.Create(_LocoStream(2, 1, 3));

    // First v=0 at k=3 is 1 000; because save starts non-negative a k=2 zero-run code follows,
    // and it has to state a run of one — 1 01 — so the second pixel is taken from the run and
    // reads no bits at all. A run of zero would leave the second pixel needing a Rice code of its
    // own, which does not fit in the byte this plane gets. Bits are 1000 101, then one of padding.
    Assert.That(decoder.TryDecode(new(0, new byte[] { 0x8A, 0x8A, 0x8A }), out var frame), Is.True);
    Assert.That(frame.PixelData, Is.EqualTo(new byte[] { 128, 128, 128, 128, 128, 128 }));
  }

  [Test]
  [Category("Unit")]
  public void LocoRequiresItsTwelveByteAviTrailer() {
    var stream = _LocoStream(2, 1, 3);
    stream = new() {
      Index = stream.Index,
      Kind = stream.Kind,
      Codec = stream.Codec,
      Width = stream.Width,
      Height = stream.Height,
      CodecPrivateData = new byte[BitmapInfoHeader.StructSize + 11],
    };

    Assert.Throws<InvalidDataException>(() => LocoVideoDecoder.Create(stream));
  }

  [Test]
  [Category("Unit")]
  public void CanopusLosslessReadsCanonicalCodesAfterWordByteSwap() {
    var decoder = CanopusLosslessVideoDecoder.Create(_Stream("CLLC", 1, 1));
    var packet = _CllcRgbOneSymbolFrame(1);

    Assert.That(decoder.TryDecode(new(0, packet), out var frame), Is.True);
    Assert.That(frame.Format, Is.EqualTo(PixelFormat.Rgb24));
    Assert.That(frame.PixelData, Is.EqualTo(new byte[] { 129, 129, 129 }));
  }

  [Test]
  [Category("Unit")]
  public void CanopusLosslessInfoPrefixIsSkippedBeforeEntropyData() {
    var decoder = CanopusLosslessVideoDecoder.Create(_Stream("CLLC", 1, 1));
    var coded = _CllcRgbOneSymbolFrame(0);
    var packet = new byte[12 + coded.Length];
    "INFO"u8.CopyTo(packet);
    BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(4), 4);
    packet[8] = 1;
    packet[9] = 2;
    packet[10] = 3;
    packet[11] = 4;
    coded.CopyTo(packet, 12);

    Assert.That(decoder.TryDecode(new(0, packet), out var frame), Is.True);
    Assert.That(frame.PixelData, Is.EqualTo(new byte[] { 128, 128, 128 }));
  }

  [Test]
  [Category("Unit")]
  public void CanopusLosslessRejectsUnknownCodingType() {
    var packet = _CllcRgbOneSymbolFrame(0);
    packet[1] = 9;
    var decoder = CanopusLosslessVideoDecoder.Create(_Stream("CLLC", 1, 1));

    Assert.Throws<NotSupportedException>(() => decoder.TryDecode(new(0, packet), out _));
  }

  [Test]
  [Category("Unit")]
  public void BothLosslessCodecsAreRegistered() {
    var names = VideoFormatRegistry.AllCodecs.Select(codec => codec.CodecName).ToArray();
    Assert.That(names, Does.Contain("LOCO"));
    Assert.That(names, Does.Contain("Canopus Lossless Codec"));
    Assert.That(VideoFormatRegistry.CanDecode(_LocoStream(2, 2, 3)), Is.True);
    Assert.That(VideoFormatRegistry.CanDecode(_Stream("CLLC", 2, 2)), Is.True);
  }

  private static MediaStreamInfo _LocoStream(int width, int height, int mode) {
    var format = new byte[BitmapInfoHeader.StructSize + 12];
    BinaryPrimitives.WriteInt32LittleEndian(format.AsSpan(BitmapInfoHeader.StructSize), 1);
    BinaryPrimitives.WriteInt32LittleEndian(format.AsSpan(BitmapInfoHeader.StructSize + 4), mode);
    BinaryPrimitives.WriteInt32LittleEndian(format.AsSpan(BitmapInfoHeader.StructSize + 8), 0);
    return new() {
      Index = 0,
      Kind = MediaStreamKind.Video,
      Codec = CodecTag.FromCharacters("LOCO"),
      Width = width,
      Height = height,
      BitsPerPixel = mode == 4 ? 32 : 24,
      CodecPrivateData = format,
    };
  }

  private static MediaStreamInfo _Stream(string codec, int width, int height) => new() {
    Index = 0,
    Kind = MediaStreamKind.Video,
    Codec = CodecTag.FromCharacters(codec),
    Width = width,
    Height = height,
    BitsPerPixel = 24,
  };

  private static byte[] _CllcRgbOneSymbolFrame(byte residual) {
    var writer = new MsbBitWriter();
    writer.WriteBits(1, 8); // after 16-bit byte swap this lands in source byte 1 => coding type RGB
    writer.WriteBits(0, 8);
    for (var table = 0; table < 3; ++table) {
      writer.WriteBits(1, 5); // one code length
      writer.WriteBits(1, 9); // one symbol of length one
      writer.WriteBits(residual, 8);
    }
    writer.WriteBit(0);
    writer.WriteBit(0);
    writer.WriteBit(0);

    var logical = writer.ToEvenByteArray();
    var source = new byte[logical.Length];
    for (var i = 0; i < logical.Length; i += 2) {
      source[i] = logical[i + 1];
      source[i + 1] = logical[i];
    }
    return source;
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

    internal byte[] ToEvenByteArray() {
      if ((this._position & 7) != 0)
        this._position += 8 - (this._position & 7);
      while ((this._bytes.Count & 1) != 0)
        this._bytes.Add(0);
      return [.. this._bytes];
    }
  }
}
