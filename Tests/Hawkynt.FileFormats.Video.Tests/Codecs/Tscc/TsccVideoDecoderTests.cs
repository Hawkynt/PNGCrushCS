using System;
using System.IO;
using System.IO.Compression;
using FileFormat.Core;

namespace FileFormat.Codecs.Tscc.Tests;

/// <summary>
/// The parts of TSCC whose answers can be written down without a real capture: the run-length
/// opcodes, the skip-frame convention, and the header's own refusals.
/// </summary>
/// <remarks>
/// The decoder as a whole was measured against ffmpeg over four real files and 2,240 frames — 16-bit
/// (555), 24-bit and 32-bit pixel layouts, plane for plane and frame for frame, with no differing
/// samples anywhere. What these tests add is the 8-bit palettised path, which none of the four
/// available samples happens to use, checked instead against a hand-built stream ffmpeg decodes the
/// same way — and the run-length arithmetic underneath all four, using real zlib streams built with
/// the same library this decoder reads them with.
/// </remarks>
[TestFixture]
public class TsccVideoDecoderTests {

  private static readonly CodecTag _Tscc = CodecTag.FromCharacters("tscc");

  private static MediaStreamInfo _Stream(int width, int height, int bitsPerPixel, byte[]? privateData = null, CodecTag? codec = null) => new() {
    Index = 0,
    Kind = MediaStreamKind.Video,
    Codec = codec ?? _Tscc,
    Width = width,
    Height = height,
    BitsPerPixel = bitsPerPixel,
    CodecPrivateData = privateData ?? [],
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
  public void AcceptsTheTsccTag() {
    Assert.That(TsccVideoDecoder.Accepts(_Stream(16, 16, 24)), Is.True);
  }

  [Test]
  [Category("Unit")]
  public void RefusesAnythingElse() {
    var stream = _Stream(16, 16, 24, codec: CodecTag.FromCharacters("CVID"));
    Assert.That(TsccVideoDecoder.Accepts(stream), Is.False);
  }

  // ============================================================================================
  // Create
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void RefusesAPictureWithNoPixels() {
    var failure = Assert.Throws<InvalidDataException>(() => TsccVideoDecoder.Create(_Stream(0, 16, 24)));
    Assert.That(failure!.Message, Does.Contain("0x16"));
  }

  [Test]
  [Category("Unit")]
  public void RefusesADepthTheFormatDoesNotDefine() {
    var failure = Assert.Throws<NotSupportedException>(() => TsccVideoDecoder.Create(_Stream(16, 16, 12)));
    Assert.That(failure!.Message, Does.Contain("12 bits"));
  }

  [Test]
  [Category("Unit")]
  public void RefusesAPalettisedStreamWithNoPalette() {
    var failure = Assert.Throws<InvalidDataException>(() => TsccVideoDecoder.Create(_Stream(16, 16, 8)));
    Assert.That(failure!.Message, Does.Contain("carries no palette"));
  }

  // ============================================================================================
  // A packet that is not zlib carries no picture
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void APacketWithNoValidZlibHeaderProducesNoFrame() {
    var decoder = TsccVideoDecoder.Create(_Stream(4, 2, 24));

    // Neither byte here can be a zlib header: the compression-method nibble is not eight.
    var packet = new CodedPacket(0, new byte[] { 0x00, 0x01 });
    Assert.That(decoder.TryDecode(packet, out _), Is.False);
  }

  [Test]
  [Category("Unit")]
  public void AZlibPacketDecodesToAFrame() {
    const int _WIDTH = 4;
    const int _HEIGHT = 2;

    var decoder = TsccVideoDecoder.Create(_Stream(_WIDTH, _HEIGHT, 24));

    // One run of four identical 24-bit pixels a row, coded bottom row first, then end of bitmap.
    var rle = new byte[] {
      0x04, 0x10, 0x20, 0x30, 0x00, 0x00, // row (coded first = bottom = display row 1): run of 4
      0x04, 0x40, 0x50, 0x60, 0x00, 0x00, // row (coded second = top = display row 0): run of 4
      0x00, 0x01, // end of bitmap
    };

    var packet = new CodedPacket(0, Zlib(rle));
    Assert.That(decoder.TryDecode(packet, out var frame), Is.True);
    Assert.That(frame.Format, Is.EqualTo(PixelFormat.Bgr24));

    // Display row 0 is the coded stream's second row (0x40,0x50,0x60), since TSCC codes bottom to top.
    for (var x = 0; x < _WIDTH; ++x) {
      var at = x * 3;
      Assert.That(frame.PixelData[at..(at + 3)], Is.EqualTo(new byte[] { 0x40, 0x50, 0x60 }), $"display row 0, column {x}");
    }

    for (var x = 0; x < _WIDTH; ++x) {
      var at = (_WIDTH + x) * 3;
      Assert.That(frame.PixelData[at..(at + 3)], Is.EqualTo(new byte[] { 0x10, 0x20, 0x30 }), $"display row 1, column {x}");
    }
  }

  // ============================================================================================
  // The run-length opcodes: run, absolute, position change, end of line
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void APositionChangeSkipsPixelsAndLinesLeavingThemAsTheCanvasHadThem() {
    const int _WIDTH = 4;
    const int _HEIGHT = 2;

    var decoder = TsccVideoDecoder.Create(_Stream(_WIDTH, _HEIGHT, 24));

    // Key frame: everything set to (1,2,3).
    var key = new byte[] {
      0x04, 1, 2, 3, 0x00, 0x00,
      0x04, 1, 2, 3, 0x00, 0x00,
      0x00, 0x01,
    };
    Assert.That(decoder.TryDecode(new(0, Zlib(key)), out _), Is.True);

    // Delta: move two pixels right on the first coded (bottom) row, write one pixel of (9,9,9),
    // then finish — everything else, including the row above, is left exactly as it was. A run of
    // exactly one pixel is coded with the repeated-value form ("b0 is not 0"), since the escape
    // form that spells pixels out individually is defined only for counts of three and above.
    var delta = new byte[] {
      0x00, 0x02, 0x02, 0x00, // position change: skip 2 pixels, skip 0 lines
      0x01, 9, 9, 9,          // run of 1 pixel: (9,9,9)
      0x00, 0x01,             // end of bitmap
    };
    Assert.That(decoder.TryDecode(new(0, Zlib(delta)), out var frame), Is.True);

    // Coded bottom row (display row 1): columns 0-1 unchanged (1,2,3); column 2 is (9,9,9); column 3
    // untouched, still (1,2,3).
    var at0 = (_WIDTH + 0) * 3;
    var at2 = (_WIDTH + 2) * 3;
    var at3 = (_WIDTH + 3) * 3;
    Assert.That(frame.PixelData[at0..(at0 + 3)], Is.EqualTo(new byte[] { 1, 2, 3 }));
    Assert.That(frame.PixelData[at2..(at2 + 3)], Is.EqualTo(new byte[] { 9, 9, 9 }));
    Assert.That(frame.PixelData[at3..(at3 + 3)], Is.EqualTo(new byte[] { 1, 2, 3 }));

    // Display row 0 (coded second row) was never touched by this delta at all.
    Assert.That(frame.PixelData[0..3], Is.EqualTo(new byte[] { 1, 2, 3 }));
  }

  [Test]
  [Category("Unit")]
  public void APositionChangeOffThePictureRefusesByName() {
    var decoder = TsccVideoDecoder.Create(_Stream(4, 2, 24));

    var key = new byte[] { 0x04, 1, 2, 3, 0x00, 0x00, 0x04, 1, 2, 3, 0x00, 0x00, 0x00, 0x01 };
    Assert.That(decoder.TryDecode(new(0, Zlib(key)), out _), Is.True);

    var delta = new byte[] { 0x00, 0x02, 0xFF, 0x00 }; // skip 255 pixels: off a 4-pixel-wide picture
    var failure = Assert.Throws<InvalidDataException>(() => decoder.TryDecode(new(0, Zlib(delta)), out _));
    Assert.That(failure!.Message, Does.Contain("outside a 4x2 picture"));
  }

  [Test]
  [Category("Unit")]
  public void ARunThatDoesNotFitTheRowRefusesByName() {
    var decoder = TsccVideoDecoder.Create(_Stream(4, 2, 24));

    // A run of five pixels on a four-pixel-wide row.
    var key = new byte[] { 0x05, 1, 2, 3 };
    var failure = Assert.Throws<InvalidDataException>(() => decoder.TryDecode(new(0, Zlib(key)), out _));
    Assert.That(failure!.Message, Does.Contain("does not fit"));
  }

  // ============================================================================================
  // The palette
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void APalettisedFrameIsIndexedAgainstTheContainersPalette() {
    const int _WIDTH = 2;
    const int _HEIGHT = 1;

    // BITMAPINFOHEADER (40 bytes) then two RGBQUAD entries the frame's indices point into.
    var header = new byte[40];
    BitConverter.GetBytes(40).CopyTo(header, 0);
    BitConverter.GetBytes(_WIDTH).CopyTo(header, 4);
    BitConverter.GetBytes(_HEIGHT).CopyTo(header, 8);
    BitConverter.GetBytes((short)1).CopyTo(header, 12);
    BitConverter.GetBytes((short)8).CopyTo(header, 14);
    BitConverter.GetBytes(2).CopyTo(header, 32); // biClrUsed = 2

    var rgbquad = new byte[] { 10, 20, 30, 0, 40, 50, 60, 0 }; // index 0: B,G,R ; index 1: B,G,R
    var privateData = new byte[header.Length + rgbquad.Length];
    header.CopyTo(privateData, 0);
    rgbquad.CopyTo(privateData, header.Length);

    var decoder = TsccVideoDecoder.Create(_Stream(_WIDTH, _HEIGHT, 8, privateData));

    var rle = new byte[] { 0x01, 0x00, 0x01, 0x01, 0x00, 0x01 }; // pixel 0 = index 0, pixel 1 = index 1
    var frame2 = decoder.TryDecode(new(0, Zlib(rle)), out var frame);

    Assert.That(frame2, Is.True);
    Assert.That(frame.Format, Is.EqualTo(PixelFormat.Indexed8));
    Assert.That(frame.PixelData, Is.EqualTo(new byte[] { 0, 1 }));
    Assert.That(frame.PaletteCount, Is.EqualTo(2));
    Assert.That(frame.Palette, Is.EqualTo(new byte[] { 30, 20, 10, 60, 50, 40 }));
  }
}
