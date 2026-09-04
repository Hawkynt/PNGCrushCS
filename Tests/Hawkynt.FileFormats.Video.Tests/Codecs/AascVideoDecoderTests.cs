using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;

namespace FileFormat.Codecs.Tests;

/// <summary>
/// AASC's byte-granular walk: the compression word every frame opens on, the run and literal-run
/// opcodes read as bytes of a <c>width * 3</c>-wide row rather than as three-byte pixels, the end-of-row
/// and reposition escapes, and the uncompressed form.
/// </summary>
/// <remarks>
/// The one real sample this was measured against — <c>AASC.AVI</c>, 320x175, 113 frames — was decoded
/// here and by ffmpeg and compared sample for sample against ffmpeg's own <c>bgr24</c> output: all 113
/// frames identical, maximum delta nought. No sample file is checked into this repository, so what
/// follows are hand-built streams exercising each opcode this codec reads.
/// </remarks>
[TestFixture]
public sealed class AascVideoDecoderTests {

  // ============================================================================================
  // Which streams it takes
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void TheAascCodeIsTaken()
    => Assert.That(AascVideoDecoder.Accepts(_Stream("AASC")), Is.True);

  [Test]
  [Category("Unit")]
  public void AnotherCodecsCodeIsNotTaken() {
    var stream = new MediaStreamInfo { Index = 0, Kind = MediaStreamKind.Video, Codec = CodecTag.FromCharacters("cvid") };
    Assert.That(AascVideoDecoder.Accepts(stream), Is.False);
  }

  [Test]
  [Category("Unit")]
  public void TheCodecIsRegistered() {
    var stream = _StreamWithFormat(2, 2, bitsPerPixel: 24);

    Assert.That(VideoFormatRegistry.AllCodecs.Select(c => c.CodecName), Does.Contain("Autodesk Animator Codec"));
    Assert.That(VideoFormatRegistry.CanDecode(stream), Is.True);
    Assert.That(VideoFormatRegistry.CreateDecoder(stream), Is.InstanceOf<AascVideoDecoder>());
  }

  // ============================================================================================
  // The opcodes
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void ARunFillsItsCountOfRawBytesWithOneByteValueNotThreeByteColour() {
    // 2x2 (stride 6): compression 1, fill row 1 (the bottom row) with 0x42, end of row (row 1 -> row 0),
    // fill row 0 with 0x24, frame done.
    var decoder = AascVideoDecoder.Create(_StreamWithFormat(2, 2, 24));
    var payload = new byte[] { 1, 0, 0, 0, 6, 0x42, 0, 0, 6, 0x24, 0, 1 };

    Assert.That(decoder.TryDecode(new(0, payload), out var frame), Is.True);
    // Row 0 (top, display-first) is the last one painted; row 1 (bottom) the first.
    Assert.That(frame.PixelData, Is.EqualTo(new byte[] { 0x24, 0x24, 0x24, 0x24, 0x24, 0x24, 0x42, 0x42, 0x42, 0x42, 0x42, 0x42 }));
  }

  [Test]
  [Category("Unit")]
  public void ALiteralRunCountsBytesAndPadsAnOddCountByOneByte() {
    // 2x1 (stride 6): compression 1, a literal run of 5 (odd) raw bytes, a padding byte, frame done. The
    // sixth stride byte is never written and stays at its initial zero.
    var decoder = AascVideoDecoder.Create(_StreamWithFormat(2, 1, 24));
    var payload = new byte[] { 1, 0, 0, 0, 0, 5, 1, 2, 3, 4, 5, 0xFF, 0, 1 };

    Assert.That(decoder.TryDecode(new(0, payload), out var frame), Is.True);
    Assert.That(frame.PixelData, Is.EqualTo(new byte[] { 1, 2, 3, 4, 5, 0 }));
  }

  [Test]
  [Category("Unit")]
  public void ALiteralRunOfAnEvenCountTakesNoPaddingByte() {
    // 2x1 (stride 6): compression 1, a literal run of 4 (even) bytes with nothing behind it but frame done.
    var decoder = AascVideoDecoder.Create(_StreamWithFormat(2, 1, 24));
    var payload = new byte[] { 1, 0, 0, 0, 0, 4, 9, 8, 7, 6, 0, 1 };

    Assert.That(decoder.TryDecode(new(0, payload), out var frame), Is.True);
    Assert.That(frame.PixelData, Is.EqualTo(new byte[] { 9, 8, 7, 6, 0, 0 }));
  }

  [Test]
  [Category("Unit")]
  public void RepositionMovesThePenWithoutPaintingAnything() {
    // 3x1 (stride 9): compression 1, reposition 3 bytes right and 0 rows up, a run of 3 bytes, frame
    // done. The first byte-triple (pixel 0) is left at its initial zero.
    var decoder = AascVideoDecoder.Create(_StreamWithFormat(3, 1, 24));
    var payload = new byte[] { 1, 0, 0, 0, 0, 2, 3, 0, 3, 0x77, 0, 1 };

    Assert.That(decoder.TryDecode(new(0, payload), out var frame), Is.True);
    Assert.That(frame.PixelData, Is.EqualTo(new byte[] { 0, 0, 0, 0x77, 0x77, 0x77, 0, 0, 0 }));
  }

  [Test]
  [Category("Unit")]
  public void RepositionsUpOffsetMovesTowardRowZero() {
    // 2x3 (stride 6): compression 1, end of row (row 2 -> row 1), reposition 0 right and 1 up (row 1
    // -> row 0), fill row 0, frame done. Rows 1 and 2 stay at their initial zero.
    var decoder = AascVideoDecoder.Create(_StreamWithFormat(2, 3, 24));
    var payload = new byte[] { 1, 0, 0, 0, 0, 0, 0, 2, 0, 1, 6, 0x33, 0, 1 };

    Assert.That(decoder.TryDecode(new(0, payload), out var frame), Is.True);
    var expected = new byte[18];
    for (var i = 0; i < 6; ++i)
      expected[i] = 0x33; // row 0, the picture's top and display-first row.
    Assert.That(frame.PixelData, Is.EqualTo(expected));
  }

  [Test]
  [Category("Unit")]
  public void TheFirstOpcodeOfAFramePaintsTheBottomRow() {
    // 2x2 (stride 6): compression 1, then a run addressing the bottom row straight away — no end-of-row
    // escape precedes it — and frame done. Row 1 (the bottom row) is painted; row 0 stays at zero.
    var decoder = AascVideoDecoder.Create(_StreamWithFormat(2, 2, 24));
    var payload = new byte[] { 1, 0, 0, 0, 6, 0x11, 0, 1 };

    Assert.That(decoder.TryDecode(new(0, payload), out var frame), Is.True);
    Assert.That(frame.PixelData, Is.EqualTo(new byte[] { 0, 0, 0, 0, 0, 0, 0x11, 0x11, 0x11, 0x11, 0x11, 0x11 }));
  }

  [Test]
  [Category("Unit")]
  public void CompressionZeroCopiesPaddedRowsBottomUp() {
    // 1x2 (stride 3, padded to 4): compression 0, the bottom row's three bytes and a padding byte, then
    // the top row's three bytes and a padding byte.
    var decoder = AascVideoDecoder.Create(_StreamWithFormat(1, 2, 24));
    var payload = new byte[] { 0, 0, 0, 0, 1, 2, 3, 0xEE, 4, 5, 6, 0xEE };

    Assert.That(decoder.TryDecode(new(0, payload), out var frame), Is.True);
    Assert.That(frame.PixelData, Is.EqualTo(new byte[] { 4, 5, 6, 1, 2, 3 }));
  }

  [Test]
  [Category("Unit")]
  public void CompressionZeroWithTooFewBytesRefuses() {
    var decoder = AascVideoDecoder.Create(_StreamWithFormat(1, 2, 24));
    var payload = new byte[] { 0, 0, 0, 0, 1, 2, 3, 0xEE };

    Assert.Throws<InvalidDataException>(() => decoder.TryDecode(new(0, payload), out _));
  }

  [Test]
  [Category("Unit")]
  public void AnUnknownCompressionWordRefusesByName() {
    var decoder = AascVideoDecoder.Create(_StreamWithFormat(1, 1, 24));
    var payload = new byte[] { 2, 0, 0, 0, 0, 1 };

    Assert.Throws<NotSupportedException>(() => decoder.TryDecode(new(0, payload), out _));
  }

  [Test]
  [Category("Unit")]
  public void AFrameShorterThanTheCompressionWordRefuses() {
    var decoder = AascVideoDecoder.Create(_StreamWithFormat(1, 1, 24));

    Assert.Throws<InvalidDataException>(() => decoder.TryDecode(new(0, new byte[] { 1, 0 }), out _));
  }

  [Test]
  [Category("Unit")]
  public void ASecondPacketPaintsOverTheCanvasTheFirstOneLeftRatherThanAFreshOne() {
    // 2x1 (stride 6): the first packet paints the whole row; the second stops immediately, so the first
    // packet's pixels must still be there.
    var decoder = AascVideoDecoder.Create(_StreamWithFormat(2, 1, 24));
    var first = new byte[] { 1, 0, 0, 0, 6, 0x55, 0, 1 };
    Assert.That(decoder.TryDecode(new(0, first), out _), Is.True);

    var second = new byte[] { 1, 0, 0, 0, 0, 1 };
    Assert.That(decoder.TryDecode(new(0, second), out var frame), Is.True);
    Assert.That(frame.PixelData, Is.EqualTo(new byte[] { 0x55, 0x55, 0x55, 0x55, 0x55, 0x55 }));
  }

  // ============================================================================================
  // Refusals
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void DataRunningOutMidOpcodeRefuses() {
    var decoder = AascVideoDecoder.Create(_StreamWithFormat(2, 1, 24));
    // A literal run announcing 4 bytes with only 2 behind it.
    var payload = new byte[] { 1, 0, 0, 0, 0, 4, 9, 8 };

    Assert.Throws<InvalidDataException>(() => decoder.TryDecode(new(0, payload), out _));
  }

  [Test]
  [Category("Unit")]
  public void ARunReachingPastTheEndOfARowRefuses() {
    var decoder = AascVideoDecoder.Create(_StreamWithFormat(2, 1, 24)); // stride 6
    // A run of 7 bytes -- one more than the row holds.
    var payload = new byte[] { 1, 0, 0, 0, 7, 0x11 };

    Assert.Throws<InvalidDataException>(() => decoder.TryDecode(new(0, payload), out _));
  }

  [Test]
  [Category("Unit")]
  public void AStreamStatingRowsTopDownRefuses() {
    var stream = _StreamWithFormat(2, 2, 24, topDown: true);
    Assert.Throws<NotSupportedException>(() => AascVideoDecoder.Create(stream));
  }

  [Test]
  [Category("Unit")]
  public void ADepthOtherThanTwentyFourBitsRefuses() {
    var stream = _StreamWithFormat(2, 2, bitsPerPixel: 8);
    Assert.Throws<NotSupportedException>(() => AascVideoDecoder.Create(stream));
  }

  // ============================================================================================
  // Helpers
  // ============================================================================================

  private static MediaStreamInfo _Stream(string tag) => new() {
    Index = 0, Kind = MediaStreamKind.Video, Codec = CodecTag.FromCharacters(tag),
  };

  private static MediaStreamInfo _StreamWithFormat(int width, int height, short bitsPerPixel, bool topDown = false) {
    var format = new byte[40];
    BinaryPrimitives.WriteUInt32LittleEndian(format.AsSpan(0), 40);
    BinaryPrimitives.WriteInt32LittleEndian(format.AsSpan(4), width);
    BinaryPrimitives.WriteInt32LittleEndian(format.AsSpan(8), topDown ? -height : height);
    BinaryPrimitives.WriteInt16LittleEndian(format.AsSpan(14), bitsPerPixel);

    return new() {
      Index = 0, Kind = MediaStreamKind.Video, Codec = CodecTag.FromCharacters("AASC"),
      Width = width, Height = height, CodecPrivateData = format,
    };
  }
}
