using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using FileFormat.Core;

namespace FileFormat.Codecs.Lcl.Tests;

/// <summary>
/// The parts of LCL ZLIB whose answers can be written down without a real recording: the trailer's
/// own refusals, and the row-padding and row-order arithmetic underneath the format, using real zlib
/// streams built with the same library this decoder reads them with.
/// </summary>
/// <remarks>
/// The decoder as a whole was measured two ways: round-tripped through ffmpeg's own zlib encoder —
/// eight streams, sizes from 2x2 to 320x240 including widths that leave a row unaligned — with every
/// decoded frame identical to the source frame that was encoded, and against seven real recordings
/// from samples.ffmpeg.org, 300 frames from 64x48 to 1246x992, every sample of every frame identical.
/// What these tests add is the row-padding and row-order arithmetic small enough to state by hand, and
/// the trailer's refusals.
/// </remarks>
[TestFixture]
public class LclZlibVideoDecoderTests {

  private static readonly CodecTag _Zlib = CodecTag.FromCharacters("ZLIB");

  /// <summary>A standard 40-byte <c>BITMAPINFOHEADER</c> followed by LCL's own eight-byte trailer.</summary>
  private static byte[] _PrivateData(int width, int height, byte imageType = 2, sbyte compression = -1, byte flags = 0, byte codec = 3) {
    var data = new byte[40 + 8];
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0), 40);
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(4), width);
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(8), height);
    BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(12), 1);
    BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(14), 24);
    "ZLIB"u8.CopyTo(data.AsSpan(16));

    // Bytes 40..43 are the format's own "unknown" field, always [4,0,0,0].
    data[40] = 4;
    data[44] = imageType;
    data[45] = unchecked((byte)compression);
    data[46] = flags;
    data[47] = codec;
    return data;
  }

  private static MediaStreamInfo _Stream(int width, int height, byte[]? privateData = null, CodecTag? codec = null, MediaStreamKind kind = MediaStreamKind.Video) => new() {
    Index = 0,
    Kind = kind,
    Codec = codec ?? _Zlib,
    Width = width,
    Height = height,
    CodecPrivateData = privateData ?? _PrivateData(width, height),
  };

  private static byte[] Zlib(byte[] raw) {
    using var ms = new MemoryStream();
    using (var z = new ZLibStream(ms, CompressionLevel.Optimal, leaveOpen: true))
      z.Write(raw);

    return ms.ToArray();
  }

  // ============================================================================================
  // Accepts
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void AcceptsTheZlibTag() {
    Assert.That(LclZlibVideoDecoder.Accepts(_Stream(16, 16)), Is.True);
  }

  [Test]
  [Category("Unit")]
  public void RefusesAnythingElse() {
    var stream = _Stream(16, 16, codec: CodecTag.FromCharacters("MSZH"));
    Assert.That(LclZlibVideoDecoder.Accepts(stream), Is.False);
  }

  [Test]
  [Category("Unit")]
  public void RefusesAnAudioStream() {
    var stream = _Stream(16, 16, kind: MediaStreamKind.Audio);
    Assert.That(LclZlibVideoDecoder.Accepts(stream), Is.False);
  }

  // ============================================================================================
  // Create
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void RefusesAPictureWithNoPixels() {
    var failure = Assert.Throws<InvalidDataException>(() => LclZlibVideoDecoder.Create(_Stream(0, 16)));
    Assert.That(failure!.Message, Does.Contain("0x16"));
  }

  [Test]
  [Category("Unit")]
  public void RefusesPrivateDataTooShortForTheTrailer() {
    var failure = Assert.Throws<InvalidDataException>(() => LclZlibVideoDecoder.Create(_Stream(16, 16, privateData: new byte[40])));
    Assert.That(failure!.Message, Does.Contain("40 byte(s)"));
  }

  [Test]
  [Category("Unit")]
  public void RefusesAnImageTypeOtherThanRgb24() {
    var stream = _Stream(16, 16, privateData: _PrivateData(16, 16, imageType: 5));
    var failure = Assert.Throws<NotSupportedException>(() => LclZlibVideoDecoder.Create(stream));
    Assert.That(failure!.Message, Does.Contain("image type 5"));
  }

  [Test]
  [Category("Unit")]
  public void RefusesTheMultithreadFlag() {
    var stream = _Stream(16, 16, privateData: _PrivateData(16, 16, flags: 0x01));
    var failure = Assert.Throws<NotSupportedException>(() => LclZlibVideoDecoder.Create(stream));
    Assert.That(failure!.Message, Does.Contain("multithread"));
  }

  [Test]
  [Category("Unit")]
  public void RefusesThePngFilterFlag() {
    var stream = _Stream(16, 16, privateData: _PrivateData(16, 16, flags: 0x08));
    var failure = Assert.Throws<NotSupportedException>(() => LclZlibVideoDecoder.Create(stream));
    Assert.That(failure!.Message, Does.Contain("PNG filter"));
  }

  // ============================================================================================
  // A packet whose zlib stream cannot supply the picture's own padded byte count
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void RefusesAPacketThatRunsOutBeforeItsFrameDoes() {
    var decoder = LclZlibVideoDecoder.Create(_Stream(4, 2));

    // A 4x2 BGR24 frame (width already a multiple of four, so packed and padded agree) is 24 bytes;
    // compress far fewer.
    var packet = new CodedPacket(0, Zlib(new byte[4]));

    var failure = Assert.Throws<InvalidDataException>(() => decoder.TryDecode(packet, out _));
    Assert.That(failure!.Message, Does.Contain("inflates to 4 byte(s)"));
    Assert.That(failure!.Message, Does.Contain("needs either 24"));
  }

  // ============================================================================================
  // Row padding: a coded row is a whole four-byte word, not the packed pixel count
  // ============================================================================================

  /// <summary>Three pixels wide packs to nine bytes, which is not a multiple of four, so each coded
  /// row carries three bytes of padding a real encoder never states anywhere in the header.</summary>
  [Test]
  [Category("Unit")]
  public void APaddedRowIsUnpackedToItsExactPixelCount() {
    const int _WIDTH = 3;
    const int _HEIGHT = 2;

    var decoder = LclZlibVideoDecoder.Create(_Stream(_WIDTH, _HEIGHT));

    // Two coded rows of 12 bytes each: 9 packed bytes (3 BGR pixels) plus 3 bytes of padding, whose
    // value must be ignored rather than read as part of the picture.
    var codedBottomRow = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 0xFF, 0xFF, 0xFF };
    var codedTopRow = new byte[] { 10, 11, 12, 13, 14, 15, 16, 17, 18, 0xAA, 0xAA, 0xAA };
    var padded = new byte[24];
    codedBottomRow.CopyTo(padded, 0);
    codedTopRow.CopyTo(padded, 12);

    var packet = new CodedPacket(0, Zlib(padded));
    Assert.That(decoder.TryDecode(packet, out var frame), Is.True);
    Assert.That(frame.Format, Is.EqualTo(PixelFormat.Bgr24));
    Assert.That(frame.PixelData.Length, Is.EqualTo(_WIDTH * _HEIGHT * 3));

    // Display row 0 is the coded stream's second (top) row; display row 1 is its first (bottom) row.
    Assert.That(frame.PixelData[..9], Is.EqualTo(new byte[] { 10, 11, 12, 13, 14, 15, 16, 17, 18 }));
    Assert.That(frame.PixelData[9..18], Is.EqualTo(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 }));
  }

  /// <summary>An unaligned width whose packet inflates to exactly the packed byte count — what
  /// ffmpeg's own encoder writes — is read as tightly packed rather than refused as short of the
  /// padded figure, since which of the two an encoder wrote is not this format's to assume.</summary>
  [Test]
  [Category("Unit")]
  public void AnUnpaddedRowIsReadAsTightlyPacked() {
    const int _WIDTH = 3;
    const int _HEIGHT = 2;

    var decoder = LclZlibVideoDecoder.Create(_Stream(_WIDTH, _HEIGHT));

    // Two coded rows of exactly 9 bytes each, no padding at all.
    var codedBottomRow = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 };
    var codedTopRow = new byte[] { 10, 11, 12, 13, 14, 15, 16, 17, 18 };
    var packed = new byte[18];
    codedBottomRow.CopyTo(packed, 0);
    codedTopRow.CopyTo(packed, 9);

    var packet = new CodedPacket(0, Zlib(packed));
    Assert.That(decoder.TryDecode(packet, out var frame), Is.True);

    Assert.That(frame.PixelData[..9], Is.EqualTo(codedTopRow));
    Assert.That(frame.PixelData[9..], Is.EqualTo(codedBottomRow));
  }

  /// <summary>A width already a multiple of four pixels leaves no padding to strip at all, and the
  /// decoded picture is exactly the decompressed bytes with the rows reversed.</summary>
  [Test]
  [Category("Unit")]
  public void AnAlignedWidthNeedsNoPadding() {
    const int _WIDTH = 4;
    const int _HEIGHT = 2;

    var decoder = LclZlibVideoDecoder.Create(_Stream(_WIDTH, _HEIGHT));

    var codedBottomRow = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 };
    var codedTopRow = new byte[] { 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32 };
    var picture = new byte[24];
    codedBottomRow.CopyTo(picture, 0);
    codedTopRow.CopyTo(picture, 12);

    var packet = new CodedPacket(0, Zlib(picture));
    Assert.That(decoder.TryDecode(packet, out var frame), Is.True);

    Assert.That(frame.PixelData[..12], Is.EqualTo(codedTopRow));
    Assert.That(frame.PixelData[12..], Is.EqualTo(codedBottomRow));
  }
}
