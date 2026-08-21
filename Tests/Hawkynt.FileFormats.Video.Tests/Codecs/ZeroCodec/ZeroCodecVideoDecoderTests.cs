using System;
using System.IO;
using System.IO.Compression;
using FileFormat.Core;

namespace FileFormat.Codecs.ZeroCodec.Tests;

/// <summary>
/// The parts of ZeroCodec whose answers can be written down without a real recording: the header's
/// own refusals, and the one rule the whole format runs on — a decompressed byte of zero keeps the
/// picture already held, anything else replaces it — using real zlib streams built with the same
/// library this decoder reads them with.
/// </summary>
/// <remarks>
/// The decoder as a whole was measured against ffmpeg's own decode of samples.ffmpeg.org's one
/// ZeroCodec recording, on the packed samples themselves rather than through an RGB conversion: 38
/// frames, 1280x720, every one of the 70,041,600 bytes ffmpeg's decode produces reproduced exactly.
/// What these tests add is the merge arithmetic underneath that measurement, small enough to state by
/// hand, and the header's refusals.
/// </remarks>
[TestFixture]
public class ZeroCodecVideoDecoderTests {

  private static readonly CodecTag _Zeco = CodecTag.FromCharacters("ZECO");

  private static MediaStreamInfo _Stream(int width, int height, int bitsPerPixel = 16, CodecTag? codec = null, MediaStreamKind kind = MediaStreamKind.Video) => new() {
    Index = 0,
    Kind = kind,
    Codec = codec ?? _Zeco,
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

  /// <summary>The same BT.601 arithmetic <c>ZeroCodecVideoDecoder</c> converts a packed sample with,
  /// reproduced here so a test can state what pixel a known U/Y/V triple must decode to.</summary>
  private static (byte R, byte G, byte B) ExpectedRgb(byte y, byte u, byte v) {
    var c = y - 16;
    var d = u - 128;
    var e = v - 128;
    byte Clamp(int value) => (byte)(value < 0 ? 0 : value > 255 ? 255 : value);

    return (
      Clamp((298 * c + 409 * e + 128) >> 8),
      Clamp((298 * c - 100 * d - 208 * e + 128) >> 8),
      Clamp((298 * c + 516 * d + 128) >> 8));
  }

  // ============================================================================================
  // Accepts
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void AcceptsTheZecoTag() {
    Assert.That(ZeroCodecVideoDecoder.Accepts(_Stream(16, 16)), Is.True);
  }

  [Test]
  [Category("Unit")]
  public void RefusesAnythingElse() {
    var stream = _Stream(16, 16, codec: CodecTag.FromCharacters("CVID"));
    Assert.That(ZeroCodecVideoDecoder.Accepts(stream), Is.False);
  }

  [Test]
  [Category("Unit")]
  public void RefusesAnAudioStream() {
    var stream = _Stream(16, 16, kind: MediaStreamKind.Audio);
    Assert.That(ZeroCodecVideoDecoder.Accepts(stream), Is.False);
  }

  // ============================================================================================
  // Create
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void RefusesAPictureWithNoPixels() {
    var failure = Assert.Throws<InvalidDataException>(() => ZeroCodecVideoDecoder.Create(_Stream(0, 16)));
    Assert.That(failure!.Message, Does.Contain("0x16"));
  }

  [Test]
  [Category("Unit")]
  public void RefusesAnOddWidth() {
    var failure = Assert.Throws<NotSupportedException>(() => ZeroCodecVideoDecoder.Create(_Stream(15, 16)));
    Assert.That(failure!.Message, Does.Contain("width of 15"));
  }

  [Test]
  [Category("Unit")]
  public void RefusesADepthOtherThanSixteen() {
    var failure = Assert.Throws<NotSupportedException>(() => ZeroCodecVideoDecoder.Create(_Stream(16, 16, bitsPerPixel: 24)));
    Assert.That(failure!.Message, Does.Contain("24 bits"));
  }

  // ============================================================================================
  // A packet whose zlib stream cannot supply the picture's own byte count
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void RefusesAPacketThatRunsOutBeforeItsFrameDoes() {
    var decoder = ZeroCodecVideoDecoder.Create(_Stream(4, 2));

    // A full picture is 4*2*2 = 16 bytes; compress far fewer than that.
    var compressed = Zlib(new byte[4]);
    var packet = new CodedPacket(0, compressed);

    var failure = Assert.Throws<InvalidDataException>(() => decoder.TryDecode(packet, out _));
    Assert.That(failure!.Message, Does.Contain("16 byte(s)"));
  }

  [Test]
  [Category("Unit")]
  public void RefusesAPacketThatIsNotAZlibStreamAtAll() {
    var decoder = ZeroCodecVideoDecoder.Create(_Stream(4, 2));
    var packet = new CodedPacket(0, new byte[] { 0x00, 0x00, 0x00, 0x00 });

    Assert.Throws<InvalidDataException>(() => decoder.TryDecode(packet, out _));
  }

  // ============================================================================================
  // The one rule: zero keeps what is already held, anything else replaces it
  // ============================================================================================

  /// <summary>The very first packet a decoder ever sees comes out identical to a literal copy,
  /// because the picture "already held" before anything has arrived is an all-zero buffer.</summary>
  [Test]
  [Category("Unit")]
  public void TheFirstPacketDecodesAsALiteralPicture() {
    const int _WIDTH = 2;
    const int _HEIGHT = 1;

    var decoder = ZeroCodecVideoDecoder.Create(_Stream(_WIDTH, _HEIGHT));

    // One pair: U, Y0, V, Y1. Y=16, U=V=128 decodes to RGB (0,0,0) under BT.601.
    var picture = new byte[] { 128, 16, 128, 16 };
    var packet = new CodedPacket(0, Zlib(picture));

    Assert.That(decoder.TryDecode(packet, out var frame), Is.True);
    Assert.That(frame.Format, Is.EqualTo(PixelFormat.Rgb24));

    var (r, g, b) = ExpectedRgb(16, 128, 128);
    Assert.That(frame.PixelData, Is.EqualTo(new byte[] { r, g, b, r, g, b }));
  }

  /// <summary>A second packet whose decompressed bytes are all zero leaves the picture exactly as the
  /// first packet left it — nothing changed, nothing decoded fresh.</summary>
  [Test]
  [Category("Unit")]
  public void AllZeroBytesLeaveThePictureUnchanged() {
    const int _WIDTH = 2;
    const int _HEIGHT = 1;

    var decoder = ZeroCodecVideoDecoder.Create(_Stream(_WIDTH, _HEIGHT));

    var first = new byte[] { 90, 200, 60, 210 };
    Assert.That(decoder.TryDecode(new(0, Zlib(first)), out var firstFrame), Is.True);

    Assert.That(decoder.TryDecode(new(0, Zlib(new byte[4])), out var secondFrame), Is.True);
    Assert.That(secondFrame.PixelData, Is.EqualTo(firstFrame.PixelData));
  }

  /// <summary>A nonzero byte replaces the one already held; a zero byte beside it leaves its neighbour
  /// alone — the two halves of the rule exercised in the same packet.</summary>
  [Test]
  [Category("Unit")]
  public void ANonzeroByteReplacesWhatWasHeldAndAZeroByteLeavesItsNeighbourAlone() {
    const int _WIDTH = 4;
    const int _HEIGHT = 1;

    var decoder = ZeroCodecVideoDecoder.Create(_Stream(_WIDTH, _HEIGHT));

    // Two pairs. First pair: U=128,Y0=16,V=128,Y1=16 (black). Second pair: same.
    var first = new byte[] { 128, 16, 128, 16, 128, 16, 128, 16 };
    Assert.That(decoder.TryDecode(new(0, Zlib(first)), out _), Is.True);

    // Second packet: first pair entirely zero (unchanged, stays black); second pair states a new,
    // fully opaque white-ish luma at Y=235 with neutral chroma, all four bytes nonzero.
    var second = new byte[] { 0, 0, 0, 0, 128, 235, 128, 235 };
    Assert.That(decoder.TryDecode(new(0, Zlib(second)), out var frame), Is.True);

    var black = ExpectedRgb(16, 128, 128);
    var bright = ExpectedRgb(235, 128, 128);
    Assert.That(frame.PixelData, Is.EqualTo(new[] {
      black.R, black.G, black.B, black.R, black.G, black.B,
      bright.R, bright.G, bright.B, bright.R, bright.G, bright.B,
    }));
  }

  // ============================================================================================
  // Row order: the picture is coded bottom row first
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void ThePictureIsStoredBottomRowFirst() {
    const int _WIDTH = 2;
    const int _HEIGHT = 2;

    var decoder = ZeroCodecVideoDecoder.Create(_Stream(_WIDTH, _HEIGHT));

    // Coded row 0 (bottom of the picture) is bright; coded row 1 (top) is black.
    var bottomRow = new byte[] { 128, 235, 128, 235 };
    var topRow = new byte[] { 128, 16, 128, 16 };
    var picture = new byte[8];
    bottomRow.CopyTo(picture, 0);
    topRow.CopyTo(picture, 4);

    Assert.That(decoder.TryDecode(new(0, Zlib(picture)), out var frame), Is.True);

    var black = ExpectedRgb(16, 128, 128);
    var bright = ExpectedRgb(235, 128, 128);

    // The composed picture is top-down, so its first row is the coded stream's last (topRow, black)
    // and its second row is the coded stream's first (bottomRow, bright).
    Assert.That(frame.PixelData[..6], Is.EqualTo(new[] { black.R, black.G, black.B, black.R, black.G, black.B }));
    Assert.That(frame.PixelData[6..], Is.EqualTo(new[] { bright.R, bright.G, bright.B, bright.R, bright.G, bright.B }));
  }
}
