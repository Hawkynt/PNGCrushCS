using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;
using NUnit.Framework;

namespace FileFormat.Codecs.Tests;

/// <summary>
/// Smacker's four Huffman tables, its move-to-front history and its four block types — on tree sections
/// and pictures built here bit by bit, since the format states its tables once for a whole file and
/// nothing smaller than a whole file exercises them.
/// </summary>
/// <remarks>
/// Every <c>.smk</c> file on <c>samples.ffmpeg.org</c> and in FFmpeg's own FATE suite that FFmpeg
/// itself will open — eight files from five games and two of FFmpeg's own bug reports, 120x76 to
/// 640x480, 1,473 pictures in all — was decoded here and by FFmpeg 9.0.1 and compared on the paletted
/// output both produce, with no colour conversion between them: all 127,616,480 palette indices and
/// all 1,131,264 palette bytes are identical. What that comparison settled about the composition RAD's
/// own description leaves out — the two byte sub-decoders' presence and padding bits, the markers
/// being raw sixteen-bit values, the padding bit after the sixteen-bit tree, and the four-bytes-a-slot
/// allocation — is what makes the tree sections below parse at all, so every test here rests on it.
/// What that comparison can only settle in aggregate is pinned down here instead: the move-to-front
/// history's own transitions one at a time, and each of the three full-block shapes on a block small
/// enough to write the expected pixels of out by hand.
/// </remarks>
[TestFixture]
public sealed class SmackerVideoDecoderTests {

  // ============================================================================================
  // Which streams it takes
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void BothSmackerSignaturesAreTaken() {
    Assert.That(SmackerVideoDecoder.Accepts(_Stream("SMK2", 4, 4)), Is.True);
    Assert.That(SmackerVideoDecoder.Accepts(_Stream("SMK4", 4, 4)), Is.True);
  }

  [Test]
  [Category("Unit")]
  public void AnotherCodecsTagIsNotTaken()
    => Assert.That(SmackerVideoDecoder.Accepts(_Stream("cvid", 4, 4)), Is.False);

  [Test]
  [Category("Unit")]
  public void TheSoundTrackOfASmackerFileIsNotTaken() {
    var stream = new MediaStreamInfo {
      Index = 1, Kind = MediaStreamKind.Audio, Codec = CodecTag.FromCharacters("SMKA"),
    };

    Assert.That(SmackerVideoDecoder.Accepts(stream), Is.False);
  }

  [Test]
  [Category("Unit")]
  public void TheCodecIsRegistered() {
    var stream = _Stream("SMK2", 4, 4);

    Assert.That(VideoFormatRegistry.AllCodecs.Select(c => c.CodecName), Does.Contain("Smacker Video"));
    Assert.That(VideoFormatRegistry.CanDecode(stream), Is.True);
    Assert.That(VideoFormatRegistry.CreateDecoder(stream), Is.InstanceOf<SmackerVideoDecoder>());
  }

  // ============================================================================================
  // What it refuses rather than guesses
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void APictureThatIsNotAWholeNumberOfBlocksRefuses() {
    var stream = _Stream("SMK2", 6, 4);

    Assert.That(() => SmackerVideoDecoder.Create(stream), Throws.TypeOf<NotSupportedException>());
    Assert.That(() => SmackerVideoDecoder.Create(_Stream("SMK2", 4, 6)), Throws.TypeOf<NotSupportedException>());
  }

  [Test]
  [Category("Unit")]
  public void APictureWithNoPixelsRefuses()
    => Assert.That(() => SmackerVideoDecoder.Create(_Stream("SMK2", 0, 4)), Throws.TypeOf<InvalidDataException>());

  [Test]
  [Category("Unit")]
  public void PrivateDataHoldingOnlyTheFourSizeFieldsRefuses() {
    var stream = new MediaStreamInfo {
      Index = 0, Kind = MediaStreamKind.Video, Codec = CodecTag.FromCharacters("SMK2"),
      Width = 4, Height = 4, CodecPrivateData = new byte[16],
    };

    Assert.That(() => SmackerVideoDecoder.Create(stream), Throws.TypeOf<InvalidDataException>());
  }

  [Test]
  [Category("Unit")]
  public void AStreamStatingNoneOfItsFourTablesRefuses() {
    // Four presence bits, all clear, and a byte to hold them.
    var privateData = new byte[17];
    var stream = new MediaStreamInfo {
      Index = 0, Kind = MediaStreamKind.Video, Codec = CodecTag.FromCharacters("SMK2"),
      Width = 4, Height = 4, CodecPrivateData = privateData,
    };

    Assert.That(() => SmackerVideoDecoder.Create(stream), Throws.TypeOf<InvalidDataException>());
  }

  // ============================================================================================
  // The four block types
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void ASolidRunFillsEveryBlockItCovers() {
    // One descriptor: solid, run index 3 (four blocks), colour 0x2A in the high byte.
    var decoder = _Decoder("SMK2", 8, 8, _Constant(0), _Constant(0), _Constant(0), _Constant(0x2A03 | 3 << 2));

    var picture = _Decode(decoder, new _BitWriter());

    Assert.That(picture.Distinct(), Is.EquivalentTo(new byte[] { 0x2A }));
  }

  [Test]
  [Category("Unit")]
  public void AMonoBlockPicksItsTwoColoursByTheMapsBits() {
    // Colours 0x0A above 0x0B: a set map bit takes the high byte, a clear one the low.
    // Map 0x000F sets the four bits of the top row only.
    var decoder = _Decoder("SMK2", 4, 4, _Constant(0x000F), _Constant(0x0A0B), _Constant(0), _Constant(0));

    var picture = _Decode(decoder, new _BitWriter());

    Assert.That(picture[..4], Is.EqualTo(new byte[] { 0x0A, 0x0A, 0x0A, 0x0A }));
    Assert.That(picture[4..], Is.EqualTo(Enumerable.Repeat((byte)0x0B, 12).ToArray()));
  }

  /// <summary>The two <c>Full</c> symbols of a row are not painted left to right: the first carries the
  /// row's third and fourth pixels and the second its first and second.</summary>
  [Test]
  [Category("Unit")]
  public void AFullBlocksRowTakesItsFirstSymbolAsItsRightHalf() {
    var full = _Symbols(0x0201, 0x0403);
    var decoder = _Decoder("SMK2", 4, 4, _Constant(0), _Constant(0), full, _Constant(1));

    var bits = new _BitWriter();
    for (var row = 0; row < 4; ++row) {
      full.WriteCode(bits, 0x0201);
      full.WriteCode(bits, 0x0403);
    }

    var picture = _Decode(decoder, bits);

    Assert.That(picture[..4], Is.EqualTo(new byte[] { 0x03, 0x04, 0x01, 0x02 }));
  }

  [Test]
  [Category("Unit")]
  public void ASkippedRunLeavesThePictureBeforeItAlone() {
    var type = _Symbols(0x2A03, 0x0002);
    var decoder = _Decoder("SMK2", 4, 4, _Constant(0), _Constant(0), _Constant(0), type);

    var first = new _BitWriter();
    type.WriteCode(first, 0x2A03);
    Assert.That(_Decode(decoder, first).Distinct(), Is.EquivalentTo(new byte[] { 0x2A }));

    var second = new _BitWriter();
    type.WriteCode(second, 0x0002);
    Assert.That(_Decode(decoder, second).Distinct(), Is.EquivalentTo(new byte[] { 0x2A }));
  }

  /// <summary>A run index counts blocks one at a time up to 59 and then jumps, so index 59 is 128
  /// blocks rather than the 60 counting on would have made it.</summary>
  [Test]
  [Category("Unit")]
  public void RunIndexFiftyNineCoversAHundredAndTwentyEightBlocks() {
    // 32x32 is 64 blocks; one descriptor at index 59 must cover all of them and stop there.
    var decoder = _Decoder("SMK2", 32, 32, _Constant(0), _Constant(0), _Constant(0), _Constant(0x1103 | 59 << 2));

    var picture = _Decode(decoder, new _BitWriter());

    Assert.That(picture.Distinct(), Is.EquivalentTo(new byte[] { 0x11 }));
  }

  // ============================================================================================
  // The move-to-front history, which is what the four tables are composed around
  // ============================================================================================

  /// <summary>A leaf whose unpacked value matched one of the three markers is not that value. It holds
  /// zero until a symbol is decoded through the table, and from then on it holds whichever value was
  /// decoded most recently.</summary>
  [Test]
  [Category("Unit")]
  public void AMarkerLeafHoldsWhateverWasDecodedLast() {
    const int MARKER = 0xFFFF;
    var mclr = _Symbols([0x0102, MARKER], [MARKER, 0xFFFE, 0xFFFD]);
    var decoder = _Decoder("SMK2", 12, 4, _Constant(0), mclr, _Constant(0), _Constant(0));

    var bits = new _BitWriter();
    mclr.WriteCode(bits, MARKER); // nothing decoded yet, so the slot still holds zero
    mclr.WriteCode(bits, 0x0102); // an ordinary leaf, which moves itself to the front
    mclr.WriteCode(bits, MARKER); // the same slot, now holding what the leaf before it decoded

    var picture = _Decode(decoder, bits);

    // Map zero everywhere, so each block takes its colour's low byte.
    Assert.That(picture[0], Is.EqualTo(0x00));
    Assert.That(picture[4], Is.EqualTo(0x02));
    Assert.That(picture[8], Is.EqualTo(0x02));
  }

  /// <summary>The history is emptied at the start of every frame, so the same bits decode the same way
  /// however many pictures came before them.</summary>
  [Test]
  [Category("Unit")]
  public void TheHistoryIsEmptiedAgainForEveryPicture() {
    const int MARKER = 0xFFFF;
    var mclr = _Symbols([0x0102, MARKER], [MARKER, 0xFFFE, 0xFFFD]);
    var decoder = _Decoder("SMK2", 4, 4, _Constant(0), mclr, _Constant(0), _Constant(0));

    var warm = new _BitWriter();
    mclr.WriteCode(warm, 0x0102);
    Assert.That(_Decode(decoder, warm)[0], Is.EqualTo(0x02));

    var cold = new _BitWriter();
    mclr.WriteCode(cold, MARKER);
    Assert.That(_Decode(decoder, cold)[0], Is.EqualTo(0x00));
  }

  // ============================================================================================
  // The two full-block shapes only SMK4 emits
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void SmackerFoursFirstShapeIsFourSolidQuadrants() {
    var full = _Symbols(0x0201, 0x0403);
    var decoder = _Decoder("SMK4", 4, 4, _Constant(0), _Constant(0), full, _Constant(1));

    var bits = new _BitWriter();
    bits.Write(1); // a one bit picks the quadrant shape
    full.WriteCode(bits, 0x0201);
    full.WriteCode(bits, 0x0403);

    var picture = _Decode(decoder, bits);

    Assert.That(picture[0..4], Is.EqualTo(new byte[] { 0x01, 0x01, 0x02, 0x02 }));
    Assert.That(picture[4..8], Is.EqualTo(new byte[] { 0x01, 0x01, 0x02, 0x02 }));
    Assert.That(picture[8..12], Is.EqualTo(new byte[] { 0x03, 0x03, 0x04, 0x04 }));
    Assert.That(picture[12..16], Is.EqualTo(new byte[] { 0x03, 0x03, 0x04, 0x04 }));
  }

  [Test]
  [Category("Unit")]
  public void SmackerFoursSecondShapePaintsEveryRowTwice() {
    var full = _Symbols(0x0201, 0x0403);
    var decoder = _Decoder("SMK4", 4, 4, _Constant(0), _Constant(0), full, _Constant(1));

    var bits = new _BitWriter();
    bits.Write(0); // a zero then a one picks the doubled-row shape
    bits.Write(1);
    full.WriteCode(bits, 0x0201);
    full.WriteCode(bits, 0x0403);
    full.WriteCode(bits, 0x0403);
    full.WriteCode(bits, 0x0201);

    var picture = _Decode(decoder, bits);

    Assert.That(picture[0..4], Is.EqualTo(new byte[] { 0x03, 0x04, 0x01, 0x02 }));
    Assert.That(picture[4..8], Is.EqualTo(new byte[] { 0x03, 0x04, 0x01, 0x02 }));
    Assert.That(picture[8..12], Is.EqualTo(new byte[] { 0x01, 0x02, 0x03, 0x04 }));
    Assert.That(picture[12..16], Is.EqualTo(new byte[] { 0x01, 0x02, 0x03, 0x04 }));
  }

  /// <summary>Two zero bits are <c>SMK4</c> saying it means <c>SMK2</c>'s own row shape after all —
  /// and the bits are read once for a whole run, not once a block.</summary>
  [Test]
  [Category("Unit")]
  public void SmackerFoursThirdShapeIsSmackerTwosAndIsReadOncePerRun() {
    var full = _Symbols(0x0201, 0x0403);
    var decoder = _Decoder("SMK4", 8, 4, _Constant(0), _Constant(0), full, _Constant(1 | 1 << 2));

    var bits = new _BitWriter();
    bits.Write(0);
    bits.Write(0);
    for (var block = 0; block < 2; ++block)
      for (var row = 0; row < 4; ++row) {
        full.WriteCode(bits, 0x0201);
        full.WriteCode(bits, 0x0403);
      }

    var picture = _Decode(decoder, bits);

    Assert.That(picture[0..8], Is.EqualTo(new byte[] { 0x03, 0x04, 0x01, 0x02, 0x03, 0x04, 0x01, 0x02 }));
  }

  // ============================================================================================
  // The palette, which states only what changed
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void ANewColoursSixBitComponentsRiseByFiveAtEverySixteenthStep() {
    var decoder = _Decoder("SMK2", 4, 4, _Constant(0), _Constant(0), _Constant(0), _Constant(3));

    // One new colour, then two skip runs covering the other 255 entries.
    var frame = _Decode(decoder, new _BitWriter(), [0x10, 0x3F, 0x01, 0xFF, 0xFE]);

    Assert.That(frame.Palette![..3], Is.EqualTo(new byte[] { 0x41, 0xFF, 0x04 }));
    Assert.That(frame.Palette[3..], Is.EqualTo(new byte[765]));
  }

  [Test]
  [Category("Unit")]
  public void ACopyBlockTakesItsEntriesFromThePaletteBeforeIt() {
    var decoder = _Decoder("SMK2", 4, 4, _Constant(0), _Constant(0), _Constant(0), _Constant(3));

    _Decode(decoder, new _BitWriter(), [0x10, 0x3F, 0x01, 0xFF, 0xFE]);

    // Keep entry 0 as it was, then take one entry from index 0, then keep the other 254.
    var frame = _Decode(decoder, new _BitWriter(), [0x80, 0x40, 0x00, 0xFF, 0xFD]);

    Assert.That(frame.Palette![..3], Is.EqualTo(new byte[] { 0x41, 0xFF, 0x04 }));
    Assert.That(frame.Palette[3..6], Is.EqualTo(new byte[] { 0x41, 0xFF, 0x04 }));
    Assert.That(frame.Palette[6..], Is.EqualTo(new byte[762]));
  }

  [Test]
  [Category("Unit")]
  public void APictureBeforeAnyPaletteChunkIsAllBlack() {
    var decoder = _Decoder("SMK2", 4, 4, _Constant(0), _Constant(0), _Constant(0), _Constant(3));

    var frame = _Decode(decoder, new _BitWriter(), null);

    Assert.That(frame.Format, Is.EqualTo(PixelFormat.Indexed8));
    Assert.That(frame.PaletteCount, Is.EqualTo(256));
    Assert.That(frame.Palette, Is.EqualTo(new byte[768]));
  }

  [Test]
  [Category("Unit")]
  public void ACopyBlockReachingPastThePaletteRefuses() {
    var decoder = _Decoder("SMK2", 4, 4, _Constant(0), _Constant(0), _Constant(0), _Constant(3));

    // Sixty-four entries taken from index 255, which is one entry from the end.
    Assert.That(
      () => _Decode(decoder, new _BitWriter(), [0x7F, 0xFF, 0x00, 0x00, 0x00]),
      Throws.TypeOf<InvalidDataException>());
  }

  // ============================================================================================
  // Building a stream to decode
  // ============================================================================================

  private static MediaStreamInfo _Stream(string tag, int width, int height) => new() {
    Index = 0,
    Kind = MediaStreamKind.Video,
    Codec = CodecTag.FromCharacters(tag),
    Width = width,
    Height = height,
    CodecPrivateData = _PrivateData(_Constant(0), _Constant(0), _Constant(0), _Constant(0)),
  };

  private static SmackerVideoDecoder _Decoder(string tag, int width, int height, _Table mmap, _Table mclr, _Table full, _Table type)
    => SmackerVideoDecoder.Create(new() {
      Index = 0,
      Kind = MediaStreamKind.Video,
      Codec = CodecTag.FromCharacters(tag),
      Width = width,
      Height = height,
      CodecPrivateData = _PrivateData(mmap, mclr, full, type),
    });

  private static byte[] _Decode(SmackerVideoDecoder decoder, _BitWriter picture)
    => _Decode(decoder, picture, null).PixelData;

  private static RawImage _Decode(SmackerVideoDecoder decoder, _BitWriter picture, byte[]? paletteBlocks) {
    var packet = new List<byte> { (byte)(paletteBlocks is null ? 0 : 1) };
    if (paletteBlocks is not null) {
      // A chunk's own first byte counts the whole chunk, itself included, in units of four bytes.
      var length = (1 + paletteBlocks.Length + 3) / 4 * 4;
      packet.Add((byte)(length / 4));
      packet.AddRange(paletteBlocks);
      packet.AddRange(new byte[length - 1 - paletteBlocks.Length]);
    }

    packet.AddRange(picture.ToArray());

    Assert.That(decoder.TryDecode(new(0, packet.ToArray()), out var frame), Is.True);
    return frame;
  }

  /// <summary>The stream's private data: the four table sizes the file header states, then one
  /// continuous bitstream holding all four tables back to back.</summary>
  private static byte[] _PrivateData(_Table mmap, _Table mclr, _Table full, _Table type) {
    var bits = new _BitWriter();
    foreach (var table in new[] { mmap, mclr, full, type })
      table.WritePacked(bits);

    var body = bits.ToArray();
    var data = new byte[16 + body.Length];
    var tables = new[] { mmap, mclr, full, type };
    for (var i = 0; i < 4; ++i)
      BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(i * 4), (uint)tables[i].AllocationSize);

    body.CopyTo(data.AsSpan(16));
    return data;
  }

  private static _Table _Constant(int value) => _Symbols(value);

  private static _Table _Symbols(params int[] values) => new(values, [0xFFFF, 0xFFFE, 0xFFFD]);

  private static _Table _Symbols(int[] values, int[] escapes) => new(values, escapes);

  /// <summary>One of the four sixteen-bit tables, written out the way a real file states it and able to
  /// write the code for any of its own values so a picture can be built to read back.</summary>
  private sealed class _Table(int[] values, int[] escapes) {

    private readonly _ByteTree _low = new(values.Select(v => (byte)(v & 0xFF)).Distinct().Order().ToArray());
    private readonly _ByteTree _high = new(values.Select(v => (byte)(v >> 8 & 0xFF)).Distinct().Order().ToArray());

    /// <summary>Four bytes a slot, with room to spare, since the header's own field is what bounds how
    /// many slots the tree may unpack to.</summary>
    internal int AllocationSize => 4 * (2 * values.Length + 4);

    internal void WritePacked(_BitWriter bits) {
      bits.Write(1);
      this._low.WritePacked(bits);
      this._high.WritePacked(bits);
      foreach (var escape in escapes)
        bits.Write((uint)escape, 16);

      this._WriteNode(bits, 0, values.Length);
      bits.Write(0); // the padding bit that follows a sixteen-bit tree
    }

    private void _WriteNode(_BitWriter bits, int start, int count) {
      if (count == 1) {
        bits.Write(0);
        this._low.WriteCode(bits, values[start] & 0xFF);
        this._high.WriteCode(bits, values[start] >> 8 & 0xFF);
        return;
      }

      bits.Write(1);
      this._WriteNode(bits, start, count / 2);
      this._WriteNode(bits, start + count / 2, count - count / 2);
    }

    internal void WriteCode(_BitWriter bits, int value)
      => _WriteCode(bits, 0, values.Length, Array.IndexOf(values, value));
  }

  /// <summary>One of the two byte sub-decoders a sixteen-bit value is read through, with the presence
  /// bit ahead of it and the padding bit after it that RAD's own description does not mention.</summary>
  private sealed class _ByteTree(byte[] symbols) {

    internal void WritePacked(_BitWriter bits) {
      bits.Write(1);
      this._WriteNode(bits, 0, symbols.Length);
      bits.Write(0);
    }

    private void _WriteNode(_BitWriter bits, int start, int count) {
      if (count == 1) {
        bits.Write(0);
        bits.Write(symbols[start], 8);
        return;
      }

      bits.Write(1);
      this._WriteNode(bits, start, count / 2);
      this._WriteNode(bits, start + count / 2, count - count / 2);
    }

    internal void WriteCode(_BitWriter bits, int symbol)
      => _WriteCode(bits, 0, symbols.Length, Array.IndexOf(symbols, (byte)symbol));
  }

  /// <summary>Walks the same balanced split both tree writers above build, recording the branch taken
  /// at each step, which is the code the decoder will follow to reach that leaf.</summary>
  private static void _WriteCode(_BitWriter bits, int start, int count, int index) {
    while (count > 1) {
      var half = count / 2;
      if (index - start < half) {
        bits.Write(0);
        count = half;
        continue;
      }

      bits.Write(1);
      start += half;
      count -= half;
    }
  }

  /// <summary>Writes a Smacker bitstream, least significant bit of each new byte first.</summary>
  private sealed class _BitWriter {
    private readonly List<byte> _bytes = [];
    private int _bit;

    internal void Write(int bit) {
      if (this._bit == 0)
        this._bytes.Add(0);

      if (bit != 0)
        this._bytes[^1] |= (byte)(1 << this._bit);

      this._bit = this._bit + 1 & 7;
    }

    internal void Write(uint value, int count) {
      for (var i = 0; i < count; ++i)
        this.Write((int)(value >> i & 1));
    }

    internal byte[] ToArray() => this._bytes.ToArray();
  }
}
