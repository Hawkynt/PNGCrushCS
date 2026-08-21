using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using FileFormat.Core;

namespace FileFormat.Codecs.Cscd.Tests;

/// <summary>
/// The parts of CSCD whose answers can be written down without a real capture: the byte-wise delta,
/// the row padding, the LZO1X opcodes underneath the compression the wiki names but does not
/// document, and the header's own refusals — including the one ffmpeg's own decoder agrees with.
/// </summary>
/// <remarks>
/// The decoder as a whole was measured against ffmpeg over four real files and 6,309 frames — 16-bit
/// (555), 24-bit and 32-bit pixel layouts, both the zlib and the LZO compression the header names, a
/// picture whose row is not a whole number of four-byte words so its coded stride differs from its
/// packed one. Every sample of every frame is identical. There is no 8-bit palettised path to measure:
/// ffmpeg's own decoder refuses that depth by name, and this refuses it the same way.
/// </remarks>
[TestFixture]
public class CscdVideoDecoderTests {

  private static readonly CodecTag _Cscd = CodecTag.FromCharacters("CSCD");

  private static MediaStreamInfo _Stream(int width, int height, int bitsPerPixel, CodecTag? codec = null) => new() {
    Index = 0,
    Kind = MediaStreamKind.Video,
    Codec = codec ?? _Cscd,
    Width = width,
    Height = height,
    BitsPerPixel = bitsPerPixel,
  };

  private static byte[] Zlib(byte[] raw) {
    using var ms = new MemoryStream();
    using (var z = new ZLibStream(ms, CompressionLevel.Optimal, leaveOpen: true))
      z.Write(raw);

    return ms.ToArray();
  }

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
  // Accepts / Create
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void AcceptsTheCscdTag() {
    Assert.That(CscdVideoDecoder.Accepts(_Stream(16, 16, 24)), Is.True);
  }

  [Test]
  [Category("Unit")]
  public void RefusesAnythingElse() {
    var stream = _Stream(16, 16, 24, codec: CodecTag.FromCharacters("ZMBV"));
    Assert.That(CscdVideoDecoder.Accepts(stream), Is.False);
  }

  [Test]
  [Category("Unit")]
  public void RefusesEightBitsAPixelByName() {
    // Not because this codec never defined the depth, but because ffmpeg's own decoder refuses a
    // stream stating it, which settles the question this format's own documentation leaves open.
    var failure = Assert.Throws<NotSupportedException>(() => CscdVideoDecoder.Create(_Stream(16, 16, 8)));
    Assert.That(failure!.Message, Does.Contain("no palettised mode"));
  }

  [Test]
  [Category("Unit")]
  public void RefusesADepthTheFormatIsNotReadAt() {
    var failure = Assert.Throws<NotSupportedException>(() => CscdVideoDecoder.Create(_Stream(16, 16, 12)));
    Assert.That(failure!.Message, Does.Contain("12 bits"));
  }

  [Test]
  [Category("Unit")]
  public void RefusesAPictureWithNoPixels() {
    var failure = Assert.Throws<InvalidDataException>(() => CscdVideoDecoder.Create(_Stream(0, 16, 24)));
    Assert.That(failure!.Message, Does.Contain("0x16"));
  }

  [Test]
  [Category("Unit")]
  public void RefusesAPacketTooShortForItsHeader() {
    var decoder = CscdVideoDecoder.Create(_Stream(4, 4, 32));
    var failure = Assert.Throws<InvalidDataException>(() => decoder.TryDecode(new(0, new byte[] { 0x03 }), out _));
    Assert.That(failure!.Message, Does.Contain("header alone is two"));
  }

  // ============================================================================================
  // The delta: unsigned byte addition, wrapped modulo 256 — not XOR
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void AnInterframeAddsItsDeltaOntoThePreviousFrameModulo256() {
    const int _WIDTH = 2;
    const int _HEIGHT = 2;

    // 32 bits a pixel: two pixels a row times four bytes is already a whole word, so no padding
    // complicates this test — that is exercised separately, below.
    var decoder = CscdVideoDecoder.Create(_Stream(_WIDTH, _HEIGHT, 32));

    var keyRaw = new byte[] {
      10, 20, 30, 40, 200, 210, 220, 230, // coded row 0 (bottom)
      1, 2, 3, 4, 5, 6, 7, 8,             // coded row 1 (top)
    };
    var keyHeader = new byte[] { 0x03, 0x00 }; // method=1 (zlib), keyframe
    Assert.That(decoder.TryDecode(new(0, _Concat(keyHeader, Zlib(keyRaw))), out var first), Is.True);
    Assert.That(first.PixelData, Is.EqualTo(keyRaw[8..].Concat(keyRaw[..8]).ToArray()), "the picture is flipped right way up");

    // Add 100 to the coded bottom row's fourth byte only; everything else in the delta is zero.
    var deltaRaw = new byte[] {
      0, 0, 0, 100, 0, 0, 0, 0,
      0, 0, 0, 0, 0, 0, 0, 0,
    };
    var deltaHeader = new byte[] { 0x02, 0x00 }; // method=1 (zlib), interframe
    Assert.That(decoder.TryDecode(new(0, _Concat(deltaHeader, Zlib(deltaRaw))), out var second), Is.True);

    // Coded row 0 becomes 10, 20, 30, 140, 200, 210, 220, 230 (only the fourth byte moved); coded
    // row 1 is untouched. Flipped, coded row 1 is display row 0 and coded row 0 is display row 1.
    var expectedCodedRow0 = new byte[] { 10, 20, 30, 140, 200, 210, 220, 230 };
    var expectedCodedRow1 = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
    Assert.That(second.PixelData, Is.EqualTo(expectedCodedRow1.Concat(expectedCodedRow0).ToArray()));
  }

  [Test]
  [Category("Unit")]
  public void AByteThatWrapsPastTwoHundredFiftyFiveWrapsRatherThanSaturating() {
    const int _WIDTH = 1;
    const int _HEIGHT = 1;

    var decoder = CscdVideoDecoder.Create(_Stream(_WIDTH, _HEIGHT, 32));

    var keyRaw = new byte[] { 230, 0, 0, 0 };
    Assert.That(decoder.TryDecode(new(0, _Concat(new byte[] { 0x03, 0x00 }, Zlib(keyRaw))), out _), Is.True);

    var deltaRaw = new byte[] { 100, 0, 0, 0 };
    Assert.That(decoder.TryDecode(new(0, _Concat(new byte[] { 0x02, 0x00 }, Zlib(deltaRaw))), out var frame), Is.True);

    Assert.That(frame.PixelData[0], Is.EqualTo((byte)((230 + 100) & 0xFF)));
    Assert.That(frame.PixelData[0], Is.EqualTo(74));
  }

  // ============================================================================================
  // Row padding: a coded row is a whole four-byte word, even when the picture is not
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void ARowNotAWholeWordIsPaddedInTheCodedStreamAndUnpaddedInTheFrame() {
    const int _WIDTH = 3;
    const int _HEIGHT = 1;

    // 24 bits a pixel: 3 pixels times 3 bytes is 9 bytes of picture, padded to 12 in the stream.
    var decoder = CscdVideoDecoder.Create(_Stream(_WIDTH, _HEIGHT, 24));

    var coded = new byte[] {
      1, 2, 3, 4, 5, 6, 7, 8, 9, // the picture's 9 real bytes
      0xAA, 0xBB, 0xCC,          // 3 padding bytes a real encoder would also write and never read back
    };
    var header = new byte[] { 0x03, 0x00 };
    Assert.That(decoder.TryDecode(new(0, _Concat(header, Zlib(coded))), out var frame), Is.True);

    Assert.That(frame.PixelData, Is.EqualTo(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 }));
  }

  // ============================================================================================
  // LZO1X, method 0 — see Lzo1x for the format itself
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void AMethodOfZeroDecodesLzo1x() {
    const int _WIDTH = 2;
    const int _HEIGHT = 1;

    // 32 bits a pixel, 8 bytes total: a literal copy of the first 4 bytes (opcode 21 copies exactly
    // four literals with nothing before it), then a match of the next 4 bytes from one byte back —
    // repeating the fourth literal byte four times.
    var lzo = new byte[] {
      21, 0x0A, 0x14, 0x1E, 0x28, // copy 4 literal bytes: 10, 20, 30, 40
      0x60, 0x00,                 // class D, L=1,D=0,S=0: length 4, distance (0<<3)+0+1=1
    };
    var header = new byte[] { 0x01, 0x00 }; // method=0 (LZO), keyframe
    var decoder = CscdVideoDecoder.Create(_Stream(_WIDTH, _HEIGHT, 32));

    Assert.That(decoder.TryDecode(new(0, _Concat(header, lzo)), out var frame), Is.True);
    Assert.That(frame.PixelData, Is.EqualTo(new byte[] { 10, 20, 30, 40, 40, 40, 40, 40 }));
  }
}
