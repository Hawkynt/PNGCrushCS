using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;

namespace FileFormat.Codecs.Tests;

/// <summary>
/// The FLIC decoder, on packets built here byte by byte.
/// </summary>
/// <remarks>
/// The coding is lossless, so the arithmetic that matters was settled against ffmpeg rather than
/// here: eleven files pulled from ffmpeg's own <c>fli-flc</c> sample corpus — both magic numbers, 320x200
/// up to 720x360, chains up to 384 frames long with no drift anywhere along them — decode to exactly
/// ffmpeg's own frames, sample for sample, with every chunk type but <c>COPY</c> and <c>BLACK</c>
/// exercised (no sample carries either — ffmpeg's own <c>flic</c> demuxer decodes but does not encode,
/// and every sample fetched opens with a byte-run first frame). What these tests add is what that
/// comparison cannot reach: the refusals, the two chunk types no sample carries, and the sign
/// conventions confirmed against two independent primary sources rather than against a file.
/// </remarks>
[TestFixture]
public sealed class FlicVideoDecoderTests {

  // ============================================================================================
  // Which streams it takes
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void AFlicVideoStreamIsTaken()
    => Assert.That(FlicVideoDecoder.Accepts(_Stream(4, 4)), Is.True);

  [Test]
  [Category("Unit")]
  public void TheFourCharacterCodeIsTakenInEitherSpelling() {
    var lower = _Retagged(_Stream(4, 4), CodecTag.FromCharacters("flic"));
    Assert.That(FlicVideoDecoder.Accepts(lower), Is.True);
  }

  [Test]
  [Category("Unit")]
  public void ASoundStreamIsNotTaken() {
    var sound = new MediaStreamInfo { Index = 0, Kind = MediaStreamKind.Audio, Codec = CodecTag.FromCharacters("FLIC") };

    Assert.That(FlicVideoDecoder.Accepts(sound), Is.False);
  }

  [Test]
  [Category("Unit")]
  public void ADifferentCodecIsNotTaken()
    => Assert.That(FlicVideoDecoder.Accepts(_Retagged(_Stream(4, 4), CodecTag.FromCharacters("MRLE"))), Is.False);

  [Test]
  [Category("Unit")]
  public void TheCodecIsRegistered() {
    Assert.That(VideoFormatRegistry.AllCodecs.Select(c => c.CodecName), Does.Contain("FLIC"));
    Assert.That(VideoFormatRegistry.CanDecode(_Stream(4, 4)), Is.True);
    Assert.That(VideoFormatRegistry.CreateDecoder(_Stream(4, 4)), Is.InstanceOf<FlicVideoDecoder>());
  }

  [Test]
  [Category("Unit")]
  public void AZeroSizedPictureIsRefused() {
    var failure = Assert.Throws<InvalidOperationException>(() => FlicVideoDecoder.Create(_Stream(0, 4)));
    Assert.That(failure!.Message, Does.Contain("has no pixels"));
  }

  [Test]
  [Category("Unit")]
  public void APictureLargerThanCanBeHeldIsRefused() {
    // FLIC's own width and height are sixteen bits each, so the largest picture the header can state
    // — 65535x65535 — overflows an int product to a negative number. Caught before the allocation
    // rather than left to surface as an unnamed array-size failure.
    var stream = new MediaStreamInfo {
      Index = 0, Kind = MediaStreamKind.Video, Codec = CodecTag.FromCharacters("FLIC"),
      Width = 65535, Height = 65535, BitsPerPixel = 8,
    };
    var failure = Assert.Throws<InvalidOperationException>(() => FlicVideoDecoder.Create(stream));
    Assert.That(failure!.Message, Does.Contain("more pixels than"));
  }

  [Test]
  [Category("Unit")]
  public void ADepthOtherThanEightIsRefused() {
    var stream = new MediaStreamInfo {
      Index = 0, Kind = MediaStreamKind.Video, Codec = CodecTag.FromCharacters("FLIC"),
      Width = 4, Height = 4, BitsPerPixel = 16,
    };
    var failure = Assert.Throws<NotSupportedException>(() => FlicVideoDecoder.Create(stream));
    Assert.That(failure!.Message, Does.Contain("16 bits per pixel"));
  }

  // ============================================================================================
  // Whole-frame chunks
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void ByteRunFillsEveryRowTopDown() {
    // Row 0 (top) index 3, row 1 index 5 — top row first, which is the opposite orientation from
    // the bottom-up Windows bitmap layouts this package's other palettised codecs read.
    var frame = _DecodeOne(4, 2, [_Brun((4, 3), (4, 5))]);

    Assert.That(_Row(frame, 0), Is.EqualTo(new byte[] { 3, 3, 3, 3 }));
    Assert.That(_Row(frame, 1), Is.EqualTo(new byte[] { 5, 5, 5, 5 }));
  }

  [Test]
  [Category("Unit")]
  public void ByteRunReadsLiteralAndReplicatedPacketsInOneRow() {
    // -3 (literal): three distinct bytes; +1 (replicate): one byte repeated once.
    var payload = new byte[] {
      0, // packet count, ignored
      unchecked((byte)-3), 9, 8, 7,
      1, 6,
    };
    var chunk = _Chunk(15, payload);

    var frame = _DecodeOne(4, 1, [chunk]);
    Assert.That(_Row(frame, 0), Is.EqualTo(new byte[] { 9, 8, 7, 6 }));
  }

  [Test]
  [Category("Unit")]
  public void AZeroCountByteRunPacketIsRefused() {
    var payload = new byte[] { 0, 0 }; // packet count, then a count byte of zero
    var chunk = _Chunk(15, payload);

    var failure = Assert.Throws<NotSupportedException>(() => _DecodeOne(4, 1, [chunk]));
    Assert.That(failure!.Message, Does.Contain("count of zero"));
  }

  [Test]
  [Category("Unit")]
  public void ByteRunPastTheRowIsRefused() {
    var payload = new byte[] { 0, 8, 1 }; // replicate 8 pixels into a 4-wide row
    var chunk = _Chunk(15, payload);

    var failure = Assert.Throws<InvalidDataException>(() => _DecodeOne(4, 1, [chunk]));
    Assert.That(failure!.Message, Does.Contain("reaches past"));
  }

  [Test]
  [Category("Unit")]
  public void CopyIsAnUncompressedTopDownRaster() {
    var pixels = new byte[] { 1, 2, 3, 4, 5, 6 };
    var frame = _DecodeOne(3, 2, [_Chunk(16, pixels)]);

    Assert.That(_Row(frame, 0), Is.EqualTo(new byte[] { 1, 2, 3 }));
    Assert.That(_Row(frame, 1), Is.EqualTo(new byte[] { 4, 5, 6 }));
  }

  [Test]
  [Category("Unit")]
  public void CopyOfTheWrongSizeIsRefused() {
    var failure = Assert.Throws<InvalidDataException>(() => _DecodeOne(3, 2, [_Chunk(16, new byte[5])]));
    Assert.That(failure!.Message, Does.Contain("needs exactly 6"));
  }

  [Test]
  [Category("Unit")]
  public void BlackSetsEveryPixelToIndexZero() {
    var frame = _DecodeOne(2, 2, [_Chunk(16, [1, 2, 3, 4]), _Chunk(13, [])]);

    Assert.That(frame.PixelData, Is.All.Zero);
  }

  // ============================================================================================
  // Delta chunks
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void ADeltaFrameLeavesEveryPixelItDoesNotNameAsTheFrameBeforeLeftIt() {
    var first = _Chunk(15, [
      0, 4, 7, // row 0: replicate 7, four times
      0, 4, 7, // row 1: replicate 7, four times
    ]);

    // FLI_LC changes only row 1, columns 0..1, to 9.
    var lc = _Chunk(12, [
      1, 0, // first changed line = 1
      1, 0, // one line follows
      1, // one packet on that line
      0, 2, 9, 9, // skip 0, literal run of 2: 9, 9
    ]);

    var frames = _Decode(4, 2, [[first], [lc]]);

    Assert.That(_Row(frames[0], 0), Is.EqualTo(new byte[] { 7, 7, 7, 7 }));
    Assert.That(_Row(frames[0], 1), Is.EqualTo(new byte[] { 7, 7, 7, 7 }));
    Assert.That(_Row(frames[1], 0), Is.EqualTo(new byte[] { 7, 7, 7, 7 }), "row 0 named by neither frame's delta");
    Assert.That(_Row(frames[1], 1), Is.EqualTo(new byte[] { 9, 9, 7, 7 }), "the two pixels the delta named");
  }

  [Test]
  [Category("Unit")]
  public void LcReplicatesOnNegativeAndCopiesLiteralOnPositive() {
    // Row 0: skip 0, size -3 (replicate next byte three times) = 5,5,5; skip 0, size 1 (literal) = 6.
    var lc = _Chunk(12, [
      0, 0, // first changed line 0
      1, 0, // one line
      2, // two packets
      0, unchecked((byte)-3), 5,
      0, 1, 6,
    ]);

    var frame = _DecodeOne(4, 1, [lc]);
    Assert.That(_Row(frame, 0), Is.EqualTo(new byte[] { 5, 5, 5, 6 }));
  }

  [Test]
  [Category("Unit")]
  public void LcLinesPastThePictureAreRefused() {
    var lc = _Chunk(12, [0, 0, 5, 0]); // first line 0, five lines in a 2-row picture

    var failure = Assert.Throws<InvalidDataException>(() => _DecodeOne(2, 2, [lc]));
    Assert.That(failure!.Message, Does.Contain("reaches past"));
  }

  [Test]
  [Category("Unit")]
  public void Ss2CopiesAndReplicatesWholePixelPairs() {
    // Width 4: one packet, skip 0, size +1 (copy one word = two literal pixels 9,8); size -1
    // (replicate one word = two pixels 6,6) fills the rest.
    var payload = new List<byte>();
    payload.AddRange(_U16(1)); // one line
    payload.AddRange(_U16(2)); // two packets on that line
    payload.Add(0); payload.Add(1); payload.Add(9); payload.Add(8); // skip 0, copy 1 word: 9, 8
    payload.Add(0); payload.Add(unchecked((byte)-1)); payload.Add(6); payload.Add(6); // skip 0, replicate word 6,6

    var ss2 = _Chunk(7, payload.ToArray());
    var frame = _DecodeOne(4, 1, [ss2]);

    Assert.That(_Row(frame, 0), Is.EqualTo(new byte[] { 9, 8, 6, 6 }));
  }

  [Test]
  [Category("Unit")]
  public void Ss2SkipsLinesAndSetsTheLastPixelOfAnOddWidthLine() {
    // A 3-wide line: one word-pair packet paints columns 0..1, and the "set last pixel" opcode
    // paints column 2 directly, since a word cannot reach an odd width's last column.
    var payload = new List<byte>();
    payload.AddRange(_U16(2)); // two line-entries

    // Line 0: skip straight to the packet count of zero (an unchanged line).
    payload.AddRange(_U16(0));

    // Line 1: set last pixel to 4, then one packet copying one word (1, 2) into columns 0..1.
    payload.AddRange(_U16(0x8000 | 4));
    payload.AddRange(_U16(1));
    payload.Add(0); payload.Add(1); payload.Add(1); payload.Add(2);

    var ss2 = _Chunk(7, payload.ToArray());
    var frame = _DecodeOne(3, 2, [ss2]);

    Assert.That(_Row(frame, 0), Is.EqualTo(new byte[] { 0, 0, 0 }), "line 0 named no packets and stays as it was");
    Assert.That(_Row(frame, 1), Is.EqualTo(new byte[] { 1, 2, 4 }));
  }

  [Test]
  [Category("Unit")]
  public void Ss2LineSkipOpcodeMovesPastUnchangedLines() {
    var payload = new List<byte>();
    payload.AddRange(_U16(1)); // one line-entry, whose skip moves y before painting

    // The one entry: skip one line (top bits 11, value -1 as a signed word).
    payload.AddRange(_U16(unchecked((ushort)-1)));
    // Then that same entry's packet count: one packet replicating word 3,3 across row 1.
    payload.AddRange(_U16(1));
    payload.Add(0); payload.Add(unchecked((byte)-1)); payload.Add(3); payload.Add(3);

    var ss2 = _Chunk(7, payload.ToArray());
    var frame = _DecodeOne(2, 2, [ss2]);

    Assert.That(_Row(frame, 0), Is.EqualTo(new byte[] { 0, 0 }), "the skipped line");
    Assert.That(_Row(frame, 1), Is.EqualTo(new byte[] { 3, 3 }));
  }

  // ============================================================================================
  // Palette chunks
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void Color256CopiesRgbDirectly() {
    var payload = new byte[] {
      1, 0, // one packet
      2, 1, // skip 2, change 1
      10, 20, 30,
    };
    var frame = _DecodeOne(1, 1, [_Chunk(4, payload), _Chunk(15, [0, 1, 0])]);

    Assert.That(frame.Palette![2 * 3], Is.EqualTo(10));
    Assert.That(frame.Palette[2 * 3 + 1], Is.EqualTo(20));
    Assert.That(frame.Palette[2 * 3 + 2], Is.EqualTo(30));
    Assert.That(frame.PaletteCount, Is.EqualTo(256));
  }

  [Test]
  [Category("Unit")]
  public void Color64WidensSixBitComponentsByRepeatingTheTopBitsRatherThanShifting() {
    var payload = new byte[] {
      1, 0, // one packet
      0, 1, // skip 0, change 1
      63, 32, 0,
    };
    var frame = _DecodeOne(1, 1, [_Chunk(11, payload), _Chunk(15, [0, 1, 0])]);

    // 63 (all six bits set) must reach 255, not 252 a plain left-shift by two gives.
    Assert.That(frame.Palette![0], Is.EqualTo(255));
    Assert.That(frame.Palette[1], Is.EqualTo(ChannelScaling.Expand6(32)));
    Assert.That(frame.Palette[2], Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void AChangeCountOfZeroMeansTwoHundredAndFiftySixEntries() {
    var payload = new byte[2 + 2 + 256 * 3];
    BinaryPrimitives.WriteUInt16LittleEndian(payload, 1); // one packet
    payload[2] = 0; // skip 0
    payload[3] = 0; // change count 0 -> 256
    for (var i = 0; i < 256; ++i)
      payload[4 + i * 3 + 1] = (byte)i; // green channel carries the index, for a distinctive check

    var frame = _DecodeOne(1, 1, [_Chunk(4, payload), _Chunk(15, [0, 1, 0])]);

    Assert.That(frame.Palette![255 * 3 + 1], Is.EqualTo(255));
  }

  [Test]
  [Category("Unit")]
  public void APaletteWriteReachingPastTheLastEntryIsRefused() {
    var payload = new byte[] { 1, 0, 254, 4, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }; // skip 254, change 4
    var failure = Assert.Throws<InvalidDataException>(() => _DecodeOne(1, 1, [_Chunk(4, payload)]));
    Assert.That(failure!.Message, Does.Contain("reaches past"));
  }

  [Test]
  [Category("Unit")]
  public void ThePaletteCarriesOverToAFrameThatStatesNoColourChunk() {
    var colour = _Chunk(4, [1, 0, 0, 1, 11, 22, 33]);
    var picture = _Chunk(15, [0, 1, 0]);

    var frames = _Decode(1, 1, [[colour, picture], [picture]]);

    Assert.That(frames[1].Palette![0], Is.EqualTo(11));
    Assert.That(frames[1].Palette[1], Is.EqualTo(22));
    Assert.That(frames[1].Palette[2], Is.EqualTo(33));
  }

  // ============================================================================================
  // PSTAMP, unknown chunks, and truncation
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void PstampIsSkippedRatherThanDecodedIntoTheCanvas() {
    // A postage-stamp thumbnail ahead of the real picture chunk, the shape ffmpeg's own
    // fli-flc/2422.FLC sample carries on its first frame: skipped whole by its outer size, whatever
    // its own internal height/width/xlate/embedded-chunk fields say.
    var pstamp = _Chunk(18, [1, 0, 2, 0, 1, 0, 0xFF, 0xFF, 0xFF, 0xFF]);
    var picture = _Chunk(15, [0, 4, 5]);

    var frame = _DecodeOne(4, 1, [pstamp, picture]);
    Assert.That(_Row(frame, 0), Is.EqualTo(new byte[] { 5, 5, 5, 5 }));
  }

  [Test]
  [Category("Unit")]
  public void AnUnknownChunkTypeIsRefusedByName() {
    var failure = Assert.Throws<NotSupportedException>(() => _DecodeOne(2, 2, [_Chunk(99, [1, 2])]));
    Assert.That(failure!.Message, Does.Contain("type 99"));
  }

  [Test]
  [Category("Unit")]
  public void ASubChunkShorterThanItsOwnHeaderIsRefused() {
    var chunk = new byte[] { 4, 0, 0, 0, 15, 0 }; // states size 4, which is shorter than six
    var failure = Assert.Throws<InvalidDataException>(() => _DecodeOne(2, 2, [chunk]));
    Assert.That(failure!.Message, Does.Contain("shorter than its own six-byte header"));
  }

  [Test]
  [Category("Unit")]
  public void ASubChunkLongerThanThePacketIsRefused() {
    var chunk = new byte[] { 100, 0, 0, 0, 15, 0 }; // states 100 bytes and the packet holds six
    var failure = Assert.Throws<InvalidDataException>(() => _DecodeOne(2, 2, [chunk]));
    Assert.That(failure!.Message, Does.Contain("runs past the frame"));
  }

  [Test]
  [Category("Unit")]
  public void APacketEndingMidOpcodeIsRefused() {
    var chunk = _Chunk(15, [0, 4]); // a replicate count with no pixel byte behind it
    var failure = Assert.Throws<InvalidDataException>(() => _DecodeOne(4, 1, [chunk]));
    Assert.That(failure!.Message, Does.Contain("ends before"));
  }

  // ============================================================================================
  // Each frame is its own picture
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void EachFrameIsItsOwnPictureAndNotAViewOfTheCanvas() {
    var frames = _Decode(2, 1, [[_Chunk(15, [0, 2, 7])], [_Chunk(15, [0, 2, 8])]]);

    Assert.That(_Row(frames[0], 0), Is.EqualTo(new byte[] { 7, 7 }));
    Assert.That(_Row(frames[1], 0), Is.EqualTo(new byte[] { 8, 8 }));
  }

  // ============================================================================================
  // Fixtures
  // ============================================================================================

  private static MediaStreamInfo _Stream(int width, int height) => new() {
    Index = 0,
    Kind = MediaStreamKind.Video,
    Codec = CodecTag.FromCharacters("FLIC"),
    Width = width,
    Height = height,
    BitsPerPixel = 8,
  };

  private static MediaStreamInfo _Retagged(MediaStreamInfo stream, CodecTag codec) => new() {
    Index = stream.Index,
    Kind = stream.Kind,
    Codec = codec,
    Width = stream.Width,
    Height = stream.Height,
    BitsPerPixel = stream.BitsPerPixel,
  };

  /// <summary>A little-endian word, spelled explicitly rather than through the host's own endianness.</summary>
  private static byte[] _U16(ushort value) {
    var bytes = new byte[2];
    BinaryPrimitives.WriteUInt16LittleEndian(bytes, value);
    return bytes;
  }

  private static byte[] _Chunk(ushort type, IReadOnlyList<byte> payload) {
    var chunk = new byte[6 + payload.Count];
    BinaryPrimitives.WriteUInt32LittleEndian(chunk, (uint)chunk.Length);
    BinaryPrimitives.WriteUInt16LittleEndian(chunk.AsSpan(4), type);
    for (var i = 0; i < payload.Count; ++i)
      chunk[6 + i] = payload[i];

    return chunk;
  }

  /// <summary>A <c>FLI_BRUN</c> chunk from one (count, colour) pair a row, each replicated across the width.</summary>
  private static byte[] _Brun(params (int width, byte colour)[] rows) {
    var payload = new List<byte>();
    foreach (var (width, colour) in rows) {
      payload.Add(1); // packet count, ignored
      payload.Add((byte)width); // positive: replicate
      payload.Add(colour);
    }

    return _Chunk(15, payload);
  }

  private static RawImage _DecodeOne(int width, int height, IReadOnlyList<byte[]> subChunks)
    => _Decode(width, height, [subChunks])[0];

  private static IReadOnlyList<RawImage> _Decode(int width, int height, IReadOnlyList<IReadOnlyList<byte[]>> packets) {
    var decoder = FlicVideoDecoder.Create(_Stream(width, height));
    var pictures = new List<RawImage>(packets.Count);

    foreach (var subChunks in packets) {
      var data = new byte[subChunks.Sum(c => c.Length)];
      var at = 0;
      foreach (var chunk in subChunks) {
        chunk.CopyTo(data, at);
        at += chunk.Length;
      }

      if (decoder.TryDecode(new(0, data), out var picture))
        pictures.Add(picture);
    }

    return pictures;
  }

  private static byte[] _Row(RawImage picture, int row)
    => picture.PixelData.AsSpan(row * picture.Width, picture.Width).ToArray();
}
