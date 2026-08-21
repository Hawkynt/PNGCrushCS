using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Codecs.Tests;

/// <summary>
/// The 8BPS decoder, on frames built here byte by byte.
/// </summary>
/// <remarks>
/// The decoder as a whole was measured against ffmpeg's own decode of three real files from
/// samples.ffmpeg.org, one at each depth this codec reads — 34 frames of 24-bit RGB, 150 of 32-bit
/// RGB with alpha, 169 of 8-bit through an embedded colour table — 353 frames in all, every plane of
/// every one identical. What these tests pin down is the one place the format's own documentation
/// disagrees with every real file (the literal run's length), the repeat run, the plane-to-pixel
/// packing, the colour table reading, and everything that refuses.
/// </remarks>
[TestFixture]
public class EightBpsVideoDecoderTests {

  private static readonly CodecTag _EightBps = CodecTag.FromCharacters("8BPS");

  private static MediaStreamInfo _Stream(
    int width, int height, int bitsPerPixel, CodecTag? codec = null, byte[]? description = null) => new() {
    Index = 0,
    Kind = MediaStreamKind.Video,
    Codec = codec ?? _EightBps,
    Width = width,
    Height = height,
    BitsPerPixel = bitsPerPixel,
    CodecPrivateData = description ?? [],
  };

  /// <summary>
  /// Builds a QuickTime visual sample entry: an eight-byte box header, seventy-four bytes of fields
  /// this decoder never reads, the depth, the colour table identifier and — for an indexed depth — the
  /// table itself.
  /// </summary>
  private static byte[] _SampleDescription(int depth, short colourTableId, byte[]? table = null) {
    var body = new List<byte>();
    body.AddRange(new byte[74]); // reserved through compressor name — none of it is read here
    body.Add((byte)(depth >> 8));
    body.Add((byte)depth);
    body.Add((byte)(colourTableId >> 8));
    body.Add((byte)colourTableId);
    if (table != null)
      body.AddRange(table);

    var entry = new List<byte>();
    var size = 8 + body.Count;
    entry.Add((byte)(size >> 24));
    entry.Add((byte)(size >> 16));
    entry.Add((byte)(size >> 8));
    entry.Add((byte)size);
    entry.AddRange("8BPS"u8.ToArray());
    entry.AddRange(body);
    return entry.ToArray();
  }

  /// <summary>A custom colour table: a seed, no flags, and the given (index, r, g, b) entries, each
  /// colour component widened to sixteen bits by shifting the given eight-bit value into the high
  /// byte — the same place this decoder reads it back out of.</summary>
  private static byte[] _ColourTable(params (int Index, byte R, byte G, byte B)[] entries) {
    var table = new List<byte> { 0, 0, 0, 0, 0, 0 }; // seed (4) + flags (2)
    var size = entries.Length - 1;
    table.Add((byte)(size >> 8));
    table.Add((byte)size);
    foreach (var (index, r, g, b) in entries) {
      table.Add((byte)(index >> 8));
      table.Add((byte)index);
      table.Add(r);
      table.Add(0);
      table.Add(g);
      table.Add(0);
      table.Add(b);
      table.Add(0);
    }

    return table.ToArray();
  }

  // ============================================================================================
  // Accepts / Create
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void AcceptsThe8BpsTagIgnoringCase() {
    Assert.That(EightBpsVideoDecoder.Accepts(_Stream(4, 1, 24)), Is.True);
    Assert.That(EightBpsVideoDecoder.Accepts(_Stream(4, 1, 24, CodecTag.FromCharacters("8bps"))), Is.True);
  }

  [Test]
  [Category("Unit")]
  public void RefusesAnythingElse()
    => Assert.That(EightBpsVideoDecoder.Accepts(_Stream(4, 1, 24, CodecTag.FromCharacters("rle "))), Is.False);

  [Test]
  [Category("Unit")]
  public void RefusesAPictureWithNoPixels() {
    var failure = Assert.Throws<InvalidDataException>(() => EightBpsVideoDecoder.Create(_Stream(0, 4, 24)));
    Assert.That(failure!.Message, Does.Contain("0x4"));
  }

  [Test]
  [Category("Unit")]
  public void RefusesADepthTheFormatDoesNotDefine() {
    var failure = Assert.Throws<NotSupportedException>(() => EightBpsVideoDecoder.Create(_Stream(4, 1, 16)));
    Assert.That(failure!.Message, Does.Contain("16"));
  }

  // ============================================================================================
  // Line decompression: PackBits, with the literal run at control + 1 and not the document's own
  // "control", settled against a real file
  // ============================================================================================

  /// <summary>An indexed stream with a minimal, valid colour table — used for every test below that
  /// is really about the line-decompression mechanics and not about depth or packing, since a single
  /// plane of raw index bytes exercises exactly the same decode loop every other plane does.</summary>
  private static MediaStreamInfo _IndexedStream(int width, int height)
    => _Stream(width, height, 8, description: _SampleDescription(8, 0, _ColourTable((0, 0, 0, 0))));

  [Test]
  [Category("Unit")]
  public void ALiteralRunCopiesControlPlusOneBytesNotControlBytes() {
    // control = 3 must copy four literal bytes, not three — the one place the format's own document
    // disagrees with every real file measured.
    var row = new byte[] { 0x03, 0x0A, 0x14, 0x1E, 0x28 }; // control=3, then 10,20,30,40
    var frame = _BuildOnePlaneFrame(4, 1, row);
    var decoder = EightBpsVideoDecoder.Create(_IndexedStream(4, 1));

    var planes = decoder.DecodePlanes(frame);

    Assert.That(planes[0], Is.EqualTo(new byte[] { 10, 20, 30, 40 }));
  }

  [Test]
  [Category("Unit")]
  public void ARepeatRunStoresTheFollowingByteTwoFiftySevenMinusControlTimes() {
    // control = 255 must repeat the next byte 257-255 = 2 times.
    var row = new byte[] { 0xFF, 0x07, 0xFF, 0x09 }; // two repeat runs of two each: 7,7,9,9
    var frame = _BuildOnePlaneFrame(4, 1, row);
    var decoder = EightBpsVideoDecoder.Create(_IndexedStream(4, 1));

    var planes = decoder.DecodePlanes(frame);

    Assert.That(planes[0], Is.EqualTo(new byte[] { 7, 7, 9, 9 }));
  }

  [Test]
  [Category("Unit")]
  public void LiteralAndRepeatRunsCanShareOneRow() {
    // literal run of 2 (control=1: "AB"), then a repeat run of 2 (control=255, value 'C') = A,B,C,C
    var row = new byte[] { 0x01, 0x41, 0x42, 0xFF, 0x43 };
    var frame = _BuildOnePlaneFrame(4, 1, row);
    var decoder = EightBpsVideoDecoder.Create(_IndexedStream(4, 1));

    var planes = decoder.DecodePlanes(frame);

    Assert.That(planes[0], Is.EqualTo(new byte[] { 0x41, 0x42, 0x43, 0x43 }));
  }

  // ============================================================================================
  // Refusals during line decompression
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void RefusesALiteralRunReachingPastThePictureWidth() {
    var row = new byte[] { 0x04, 1, 2, 3, 4, 5 }; // control=4 -> 5 literal bytes, width is only 4
    var frame = _BuildOnePlaneFrame(4, 1, row);
    var decoder = EightBpsVideoDecoder.Create(_IndexedStream(4, 1));

    var failure = Assert.Throws<InvalidDataException>(() => decoder.DecodePlanes(frame));
    Assert.That(failure!.Message, Does.Contain("reaching past the picture's width"));
  }

  [Test]
  [Category("Unit")]
  public void RefusesARepeatRunReachingPastThePictureWidth() {
    var row = new byte[] { 0xF8, 9 }; // control=248 -> count = 257-248 = 9, width is only 4
    var frame = _BuildOnePlaneFrame(4, 1, row);
    var decoder = EightBpsVideoDecoder.Create(_IndexedStream(4, 1));

    var failure = Assert.Throws<InvalidDataException>(() => decoder.DecodePlanes(frame));
    Assert.That(failure!.Message, Does.Contain("reaching past the picture's width"));
  }

  [Test]
  [Category("Unit")]
  public void RefusesARowThatRunsOutOfItsAllottedBytesBeforeFillingTheWidth() {
    // A literal run of two fills exactly the three bytes (control + two values) the table allots the
    // row, leaving two of the picture's four pixels undecoded with nothing left to read.
    var row = new byte[] { 0x01, 0xAA, 0xBB };
    var frame = _BuildOnePlaneFrame(4, 1, row);
    var decoder = EightBpsVideoDecoder.Create(_IndexedStream(4, 1));

    var failure = Assert.Throws<InvalidDataException>(() => decoder.DecodePlanes(frame));
    Assert.That(failure!.Message, Does.Contain("ran out of its allotted compressed bytes"));
  }

  [Test]
  [Category("Unit")]
  public void RefusesAPacketShorterThanItsLineLengthTables() {
    var decoder = EightBpsVideoDecoder.Create(_Stream(4, 2, 24));
    var failure = Assert.Throws<InvalidDataException>(() => decoder.DecodePlanes(new byte[4]));
    Assert.That(failure!.Message, Does.Contain("4 byte(s)"));
  }

  [Test]
  [Category("Unit")]
  public void RefusesALineLengthReachingPastThePacket() {
    // One plane, one row, stating a compressed length of 100 bytes in a packet that has none.
    var frame = new byte[] { 0, 100 };
    var decoder = EightBpsVideoDecoder.Create(_Stream(4, 1, 8, description: _SampleDescription(8, 0, _ColourTable((0, 0, 0, 0), (1, 255, 255, 255)))));
    var failure = Assert.Throws<InvalidDataException>(() => decoder.DecodePlanes(frame));
    Assert.That(failure!.Message, Does.Contain("reaching past the end of"));
  }

  // ============================================================================================
  // Packing: red, green, blue (and alpha) per pixel, for the depths that carry more than one plane
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void TryDecodePacksThreePlanesIntoRgb24InPlaneOrder() {
    var r = new byte[] { 0x01, 10, 20 }; // literal run of 2: 10, 20
    var g = new byte[] { 0x01, 30, 40 };
    var b = new byte[] { 0x01, 50, 60 };
    var frame = _BuildFrame(2, 1, [r, g, b]);
    var decoder = EightBpsVideoDecoder.Create(_Stream(2, 1, 24));

    var decoded = decoder.TryDecode(new(0, frame), out var picture);

    Assert.That(decoded, Is.True);
    Assert.That(picture.Format, Is.EqualTo(PixelFormat.Rgb24));
    Assert.That(picture.PixelData, Is.EqualTo(new byte[] { 10, 30, 50, 20, 40, 60 }));
  }

  [Test]
  [Category("Unit")]
  public void TryDecodePacksFourPlanesIntoRgba32WithAlphaLast() {
    var r = new byte[] { 0x00, 1 }; // literal run of 1
    var g = new byte[] { 0x00, 2 };
    var b = new byte[] { 0x00, 3 };
    var a = new byte[] { 0x00, 4 };
    var frame = _BuildFrame(1, 1, [r, g, b, a]);
    var decoder = EightBpsVideoDecoder.Create(_Stream(1, 1, 32));

    var decoded = decoder.TryDecode(new(0, frame), out var picture);

    Assert.That(decoded, Is.True);
    Assert.That(picture.Format, Is.EqualTo(PixelFormat.Rgba32));
    Assert.That(picture.PixelData, Is.EqualTo(new byte[] { 1, 2, 3, 4 }));
  }

  [Test]
  [Category("Unit")]
  public void TryDecodeOfAnIndexedPictureLeavesTheSinglePlaneUntouchedAsIndices() {
    var indices = new byte[] { 0x01, 0, 1 }; // literal run of 2: index 0, index 1
    var frame = _BuildOnePlaneFrame(2, 1, indices);
    var table = _ColourTable((0, 10, 20, 30), (1, 40, 50, 60));
    var decoder = EightBpsVideoDecoder.Create(_Stream(2, 1, 8, description: _SampleDescription(8, 0, table)));

    var decoded = decoder.TryDecode(new(0, frame), out var picture);

    Assert.That(decoded, Is.True);
    Assert.That(picture.Format, Is.EqualTo(PixelFormat.Indexed8));
    Assert.That(picture.PixelData, Is.EqualTo(new byte[] { 0, 1 }));
    Assert.That(picture.PaletteCount, Is.EqualTo(2));
    Assert.That(picture.Palette, Is.EqualTo(new byte[] { 10, 20, 30, 40, 50, 60 }));
  }

  // ============================================================================================
  // Colour table reading
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void ReadsAColourTablesHighByteOfEachSixteenBitComponent() {
    // Verified against a real file's embedded table and ffmpeg's own decoded palette, entry for
    // entry: only the high byte of each 16-bit component survives.
    var table = _ColourTable((0, 0xAB, 0xCD, 0xEF));
    var stream = _Stream(1, 1, 8, description: _SampleDescription(8, 0, table));

    var decoder = EightBpsVideoDecoder.Create(stream);
    var indices = new byte[] { 0x00, 0 };
    var frame = _BuildOnePlaneFrame(1, 1, indices);

    decoder.TryDecode(new(0, frame), out var picture);

    Assert.That(picture.Palette, Is.EqualTo(new byte[] { 0xAB, 0xCD, 0xEF }));
  }

  [Test]
  [Category("Unit")]
  public void RefusesAColourTableIdentifierThatIsNotTheCustomTableValue() {
    var stream = _Stream(1, 1, 8, description: _SampleDescription(8, -1)); // "no table"

    var failure = Assert.Throws<NotSupportedException>(() => EightBpsVideoDecoder.Create(stream));
    Assert.That(failure!.Message, Does.Contain("-1"));
  }

  [Test]
  [Category("Unit")]
  public void RefusesAColourTableEntryNamingAnIndexOutsideTheTablesOwnSize() {
    // Table states two entries (size = 1) but the first entry given names index 5.
    var table = new byte[] {
      0, 0, 0, 0, 0, 0, 0, 1, // seed, flags, size = 1 (two entries)
      0, 5, 1, 0, 2, 0, 3, 0, // entry: index 5, r=1, g=2, b=3
      0, 0, 0, 0, 0, 0, 0, 0, // entry: index 0, r=0, g=0, b=0
    };
    var stream = _Stream(1, 1, 8, description: _SampleDescription(8, 0, table));

    var failure = Assert.Throws<InvalidDataException>(() => EightBpsVideoDecoder.Create(stream));
    Assert.That(failure!.Message, Does.Contain("names entry 5"));
  }

  [Test]
  [Category("Unit")]
  public void ReadsTheDepthFromTheSampleDescriptionWhenTheContainerStatesNone() {
    var table = _ColourTable((0, 1, 2, 3));
    var stream = _Stream(1, 1, 0, description: _SampleDescription(8, 0, table));

    var decoder = EightBpsVideoDecoder.Create(stream);
    var frame = _BuildOnePlaneFrame(1, 1, new byte[] { 0x00, 0 });
    var decoded = decoder.TryDecode(new(0, frame), out var picture);

    Assert.That(decoded, Is.True);
    Assert.That(picture.Format, Is.EqualTo(PixelFormat.Indexed8));
  }

  // ============================================================================================
  // Helpers
  // ============================================================================================

  private static byte[] _BuildOnePlaneFrame(int width, int height, byte[] row) => _BuildFrame(width, height, [row]);

  private static byte[] _BuildFrame(int width, int height, byte[][] planeRows) {
    var table = new List<byte>();
    foreach (var row in planeRows) {
      var length = row.Length;
      table.Add((byte)(length >> 8));
      table.Add((byte)length);
    }

    var frame = new List<byte>(table);
    foreach (var row in planeRows)
      frame.AddRange(row);

    return frame.ToArray();
  }
}
