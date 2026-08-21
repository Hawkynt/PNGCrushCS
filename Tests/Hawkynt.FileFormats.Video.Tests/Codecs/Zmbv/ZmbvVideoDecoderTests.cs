using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Codecs.Zmbv.Tests;

/// <summary>
/// The parts of ZMBV whose answers can be written down without a real capture: the block motion
/// vector's sign, the copy-then-XOR order, the header's own refusals, and a palette read through
/// uncompressed end to end.
/// </summary>
/// <remarks>
/// The decoder as a whole was measured against ffmpeg's own encoder over six streams and 460 frames —
/// 8-bit palettised, 15-, 16- and 32-bit pixel layouts, a picture that is not a whole number of blocks
/// in either direction, a stream carrying more than one intraframe, and a 150-frame run long enough
/// that a zlib dictionary carried wrongly across one packet would have shown up in the frame after it.
/// A palette-change interframe, which no encoder here can be driven to write, was checked against a
/// hand-built stream ffmpeg decoded the same way. What these tests add is the arithmetic underneath
/// it, using the format's uncompressed compression type so the block logic can be checked without any
/// zlib involved at all.
/// </remarks>
[TestFixture]
public class ZmbvVideoDecoderTests {

  private static readonly CodecTag _Zmbv = CodecTag.FromCharacters("ZMBV");

  private static MediaStreamInfo _Stream(int width, int height, CodecTag? codec = null, MediaStreamKind kind = MediaStreamKind.Video) => new() {
    Index = 0,
    Kind = kind,
    Codec = codec ?? _Zmbv,
    Width = width,
    Height = height,
  };

  private static byte[] _Concat(params byte[][] parts) {
    var length = 0;
    foreach (var part in parts)
      length += part.Length;

    var result = new byte[length];
    var at = 0;
    foreach (var part in parts) {
      part.CopyTo(result, at);
      at += part.Length;
    }

    return result;
  }

  // ============================================================================================
  // Accepts
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void AcceptsTheZmbvTag() {
    Assert.That(ZmbvVideoDecoder.Accepts(_Stream(16, 16)), Is.True);
  }

  [Test]
  [Category("Unit")]
  public void RefusesAnythingElse() {
    var stream = _Stream(16, 16, codec: CodecTag.FromCharacters("CVID"));
    Assert.That(ZmbvVideoDecoder.Accepts(stream), Is.False);
  }

  [Test]
  [Category("Unit")]
  public void RefusesAnAudioStream() {
    var stream = _Stream(16, 16, kind: MediaStreamKind.Audio);
    Assert.That(ZmbvVideoDecoder.Accepts(stream), Is.False);
  }

  // ============================================================================================
  // Create
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void RefusesAPictureWithNoPixels() {
    var failure = Assert.Throws<InvalidDataException>(() => ZmbvVideoDecoder.Create(_Stream(0, 16)));
    Assert.That(failure!.Message, Does.Contain("0x16"));
  }

  // ============================================================================================
  // The stream has to open on an intraframe
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void RefusesOpeningOnAnInterframe() {
    var decoder = ZmbvVideoDecoder.Create(_Stream(8, 8));
    var packet = new CodedPacket(0, new byte[] { 0x00, 0x00, 0x00, 0x00 });

    var failure = Assert.Throws<NotSupportedException>(() => decoder.TryDecode(packet, out _));
    Assert.That(failure!.Message, Does.Contain("interframe"));
  }

  // ============================================================================================
  // The intraframe header
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void RefusesAnIntraframeTooShortForItsHeader() {
    var decoder = ZmbvVideoDecoder.Create(_Stream(8, 8));
    var packet = new CodedPacket(0, new byte[] { 0x01, 0x00, 0x01 });

    var failure = Assert.Throws<InvalidDataException>(() => decoder.TryDecode(packet, out _));
    Assert.That(failure!.Message, Does.Contain("header alone is seven"));
  }

  [Test]
  [Category("Unit")]
  public void RefusesAVersionOtherThanZeroDotOne() {
    var decoder = ZmbvVideoDecoder.Create(_Stream(8, 8));
    var packet = new CodedPacket(0, new byte[] { 0x01, 0x01, 0x00, 0x00, 0x06, 0x04, 0x04 });

    var failure = Assert.Throws<NotSupportedException>(() => decoder.TryDecode(packet, out _));
    Assert.That(failure!.Message, Does.Contain("version 1.0"));
  }

  [Test]
  [Category("Unit")]
  public void RefusesACompressionTypeOtherThanZeroOrOne() {
    var decoder = ZmbvVideoDecoder.Create(_Stream(8, 8));
    var packet = new CodedPacket(0, new byte[] { 0x01, 0x00, 0x01, 0x02, 0x06, 0x04, 0x04 });

    var failure = Assert.Throws<NotSupportedException>(() => decoder.TryDecode(packet, out _));
    Assert.That(failure!.Message, Does.Contain("compression type 2"));
  }

  [Test]
  [Category("Unit")]
  public void RefusesAZeroBlockWidth() {
    var decoder = ZmbvVideoDecoder.Create(_Stream(8, 8));
    var packet = new CodedPacket(0, new byte[] { 0x01, 0x00, 0x01, 0x00, 0x06, 0x00, 0x04 });

    var failure = Assert.Throws<InvalidDataException>(() => decoder.TryDecode(packet, out _));
    Assert.That(failure!.Message, Does.Contain("0x4"));
  }

  [Test]
  [Category("Unit")]
  public void RefusesAVideoFormatNoEncoderWrites() {
    var decoder = ZmbvVideoDecoder.Create(_Stream(8, 8));
    // Format 7 is 24 bits a pixel, defined by the format and written by nothing.
    var packet = new CodedPacket(0, new byte[] { 0x01, 0x00, 0x01, 0x00, 0x07, 0x04, 0x04 });

    var failure = Assert.Throws<NotSupportedException>(() => decoder.TryDecode(packet, out _));
    Assert.That(failure!.Message, Does.Contain("no encoder"));
  }

  [Test]
  [Category("Unit")]
  public void RefusesAVideoFormatTheSpecificationDoesNotDefine() {
    var decoder = ZmbvVideoDecoder.Create(_Stream(8, 8));
    var packet = new CodedPacket(0, new byte[] { 0x01, 0x00, 0x01, 0x00, 0x09, 0x04, 0x04 });

    var failure = Assert.Throws<NotSupportedException>(() => decoder.TryDecode(packet, out _));
    Assert.That(failure!.Message, Does.Contain("does not define"));
  }

  // ============================================================================================
  // The block grid, read uncompressed so the copy-then-XOR arithmetic stands on its own
  // ============================================================================================

  /// <summary>
  /// Two blocks, one left unchanged and one whose motion vector reaches across into the other and
  /// is then corrected — everything an interframe does, with no zlib in the way of seeing it.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void CopiesAndXorsEachBlockAccordingToItsOwnMotionVector() {
    const int _WIDTH = 8;
    const int _HEIGHT = 4;
    const int _BLOCK = 4;

    var decoder = ZmbvVideoDecoder.Create(_Stream(_WIDTH, _HEIGHT));

    // Intraframe: 16-bit pixel layout (format 6), uncompressed, one 4x4 block wide by one tall.
    // Every sample is row*8+col, so a block's origin is recognisable in whatever picture it ends
    // up part of.
    var intraPixels = new byte[_WIDTH * _HEIGHT * 2];
    for (var row = 0; row < _HEIGHT; ++row)
      for (var col = 0; col < _WIDTH; ++col) {
        var value = (ushort)(row * 8 + col);
        var at = (row * _WIDTH + col) * 2;
        intraPixels[at] = (byte)value;
        intraPixels[at + 1] = (byte)(value >> 8);
      }

    var intraHeader = new byte[] { 0x01, 0x00, 0x01, 0x00, 0x06, _BLOCK, _BLOCK };
    var intraPacket = new CodedPacket(0, _Concat(intraHeader, intraPixels));

    Assert.That(decoder.TryDecode(intraPacket, out var first), Is.True);
    Assert.That(first.Format, Is.EqualTo(PixelFormat.Rgb565));
    Assert.That(first.PixelData, Is.EqualTo(intraPixels));

    // Interframe: block (0,0) unchanged (dx=0, dy=0, no correction); block (1,0) pulled four
    // columns to its left — which is block (0,0)'s own original data — and then inverted.
    var blockInfo = new byte[] { 0x00, 0x00, unchecked((byte)-7), 0x00 };

    var xor = new byte[_BLOCK * _BLOCK * 2];
    Array.Fill(xor, (byte)0xFF);

    var interPacket = new CodedPacket(0, _Concat([0x00], blockInfo, xor));

    Assert.That(decoder.TryDecode(interPacket, out var second), Is.True);

    for (var row = 0; row < _HEIGHT; ++row) {
      for (var col = 0; col < _BLOCK; ++col) {
        var at = (row * _WIDTH + col) * 2;
        var value = (ushort)(second.PixelData[at] | (second.PixelData[at + 1] << 8));
        Assert.That(value, Is.EqualTo((ushort)(row * 8 + col)), $"block (0,0) at row {row}, col {col} should be unchanged");
      }

      for (var col = _BLOCK; col < _WIDTH; ++col) {
        var at = (row * _WIDTH + col) * 2;
        var value = (ushort)(second.PixelData[at] | (second.PixelData[at + 1] << 8));
        var expected = (ushort)((row * 8 + (col - _BLOCK)) ^ 0xFFFF);
        Assert.That(value, Is.EqualTo(expected), $"block (1,0) at row {row}, col {col} should be block (0,0)'s data, inverted");
      }
    }
  }

  /// <summary>A motion vector reaching off the picture zero-fills the part of the block it reaches
  /// past, rather than reading whatever memory sits beyond the frame buffer.</summary>
  [Test]
  [Category("Unit")]
  public void AMotionVectorReachingOffThePictureIsZeroFilled() {
    const int _SIZE = 4;

    var decoder = ZmbvVideoDecoder.Create(_Stream(_SIZE, _SIZE));

    var intraPixels = new byte[_SIZE * _SIZE * 2];
    Array.Fill(intraPixels, (byte)0xAB);

    var intraHeader = new byte[] { 0x01, 0x00, 0x01, 0x00, 0x06, _SIZE, _SIZE };
    Assert.That(decoder.TryDecode(new(0, _Concat(intraHeader, intraPixels)), out _), Is.True);

    // The one block's vector points eight pixels up and to the left of a four-pixel picture, so
    // every source sample it names is out of bounds. The single block's two-byte entry is padded
    // out to four, as the format pads its whole block info array.
    var blockInfo = new byte[] { unchecked((byte)-16), unchecked((byte)-16), 0x00, 0x00 };
    Assert.That(decoder.TryDecode(new(0, _Concat([0x00], blockInfo)), out var second), Is.True);

    Assert.That(second.PixelData, Is.EqualTo(new byte[_SIZE * _SIZE * 2]));
  }

  // ============================================================================================
  // The palette
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void AnIntraframeReadsItsOwnPaletteRatherThanTakingOneFromTheContainer() {
    const int _SIZE = 2;

    var decoder = ZmbvVideoDecoder.Create(_Stream(_SIZE, _SIZE));

    var palette = new byte[768];
    for (var i = 0; i < 256; ++i) {
      palette[i * 3] = (byte)i;
      palette[i * 3 + 1] = (byte)(255 - i);
      palette[i * 3 + 2] = 7;
    }

    var pixels = new byte[] { 1, 2, 3, 4 };
    var header = new byte[] { 0x01, 0x00, 0x01, 0x00, 0x04, _SIZE, _SIZE };

    Assert.That(decoder.TryDecode(new(0, _Concat(header, palette, pixels)), out var frame), Is.True);

    Assert.That(frame.Format, Is.EqualTo(PixelFormat.Indexed8));
    Assert.That(frame.PixelData, Is.EqualTo(pixels));
    Assert.That(frame.Palette, Is.EqualTo(palette));
    Assert.That(frame.PaletteCount, Is.EqualTo(256));
  }
}
