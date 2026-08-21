using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Core;
using FileFormat.FlicVideo;

namespace FileFormat.Codecs;

/// <summary>
/// Decodes Autodesk FLIC (<c>FLIC</c>): palette updates, delta-coded frames and whole frames over a
/// paletted eight-bit canvas that is never cleared between packets.
/// </summary>
/// <remarks>
/// FLIC fuses container and codec into one file, so the split this package otherwise draws between a
/// demuxer that knows where the packets are and a decoder that knows what is in them happens inside a
/// single format rather than across two. <see cref="FliContainer"/> does the first half — it finds
/// each <c>FRAME_TYPE</c> chunk's boundaries and nothing more — and everything below reads what the
/// container left untouched: every palette packet, every byte-run and word-run opcode.
/// <para/>
/// A packet may carry a palette chunk, a picture chunk, both, or neither — a frame with nothing at all
/// is a legitimate way of saying "unchanged," seen throughout ffmpeg's own <c>.fli</c> sample corpus.
/// Sub-chunks are walked in the order the packet states them and applied to state kept between
/// packets, the same way <see cref="MicrosoftRleDecoder"/> keeps a canvas a delta frame paints on top
/// of: the palette and the canvas are what a whole-frame chunk replaces and a delta chunk edits, and
/// neither is cleared first.
/// <para/>
/// <b>Two details are easy to get quietly wrong.</b> A palette packet's skip and change counts are in
/// palette *entries*, not bytes — a two-byte header in front of up to 256 three-byte colours. And the
/// older <c>FLI_COLOR64</c> form packs each component in six bits rather than eight, which are widened
/// by repeating the top two bits into the bottom rather than by shifting, the same rule this library's
/// other six-bit channels use.
/// <para/>
/// <b>The coding is lossless</b>, so a decoder reading the same bitstream has nothing to round: every
/// sample of every frame measured against ffmpeg's own decode of the same file came out identical.
/// <para/>
/// A <c>PSTAMP</c> sub-chunk — a postage-stamp thumbnail for a file requestor, at its own smaller size
/// and the universal 6x6x6 palette — is skipped rather than decoded into the canvas. It is reachable
/// and exercised: ffmpeg's own <c>fli-flc/2422.FLC</c> sample carries a genuine 100x63 byte-run
/// thumbnail on its first frame, behind a header stating <c>oframe1</c> beyond an intervening
/// undocumented prefix chunk, which is also what confirms <see cref="FliContainer"/> follows that field
/// rather than assuming frame one sits directly behind the header.
/// <para/>
/// <b>What it does not read refuses by name.</b> A chunk type outside the eight this decodes, a
/// palette index or a delta cursor running past the picture, an opcode wanting more bytes than the
/// packet holds, and an ambiguous zero-length byte-run packet all throw and say which. There is no
/// <c>catch</c> handing back a blank frame or the frame before: a repeated frame is exactly what an
/// empty packet legitimately means, so returning one on failure would be indistinguishable from working.
/// </remarks>
public sealed class FlicVideoDecoder : IVideoCodecDecoder<FlicVideoDecoder> {

  private static readonly CodecTag _FLIC = CodecTag.FromCharacters("FLIC");

  private readonly int _width;
  private readonly int _height;

  /// <summary>
  /// The picture as palette indices, one byte a pixel, top row first — the orientation FLIC stores
  /// frames in, unlike the bottom-up Windows bitmap layouts <see cref="MicrosoftRleDecoder"/> and
  /// <see cref="MicrosoftVideo1Decoder"/> read.
  /// </summary>
  /// <remarks>
  /// Kept between packets and never cleared. A delta chunk names only the pixels that changed, and an
  /// empty packet names none at all — both mean "as the frame before left it," which needs the frame
  /// before to still be there.
  /// </remarks>
  private readonly byte[] _canvas;

  /// <summary>
  /// The palette as 256 RGB triples, updated in place by whichever colour chunks a packet carries.
  /// </summary>
  /// <remarks>
  /// Starts at all zeroes. Every sample reachable here opens its first frame with a full-coverage
  /// palette chunk, so nothing was found that depends on what an unstated entry defaults to; a file
  /// that relied on one would need a starting palette this format states nowhere.
  /// </remarks>
  private readonly byte[] _palette = new byte[256 * 3];

  private FlicVideoDecoder(int width, int height) {
    this._width = width;
    this._height = height;
    this._canvas = new byte[width * height];
  }

  public static string CodecName => "FLIC";

  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    return stream.Kind == MediaStreamKind.Video && stream.Codec.EqualsIgnoringCase(_FLIC);
  }

  public static FlicVideoDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    var width = stream.Width;
    var height = stream.Height;
    if (width <= 0 || height <= 0)
      throw new InvalidOperationException(
        $"FLIC video stream {stream.Index} states a picture of {width}x{height}, which has no pixels.");

    // Multiplied as a long before the canvas is asked for. FLIC's own width and height fields are
    // sixteen bits each, so a maximum picture (65535x65535) overflows an int product to a negative
    // number, which would otherwise surface as an unnamed allocation failure rather than a refusal
    // naming the field and the stream.
    if ((long)width * height > int.MaxValue)
      throw new InvalidOperationException(
        $"FLIC video stream {stream.Index} states a picture of {width}x{height}, which is more pixels than "
        + "can be held.");

    if (stream.BitsPerPixel is not (0 or 8))
      throw new NotSupportedException(
        $"FLIC video stream {stream.Index} states {stream.BitsPerPixel} bits per pixel. This codec is paletted "
        + "eight-bit throughout and nothing else is read.");

    return new(width, height);
  }

  /// <summary>Decodes one packet, which for this codec is always exactly one whole frame.</summary>
  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    this._DecodeSubChunks(packet.Data.Span);

    frame = new() {
      Width = this._width,
      Height = this._height,
      Format = PixelFormat.Indexed8,
      PixelData = (byte[])this._canvas.Clone(),
      Palette = (byte[])this._palette.Clone(),
      PaletteCount = 256,
    };

    return true;
  }

  // ============================================================================================
  // Sub-chunk walk
  // ============================================================================================

  private void _DecodeSubChunks(ReadOnlySpan<byte> data) {
    var at = 0;
    while (at < data.Length) {
      if (at + 6 > data.Length)
        throw new InvalidDataException(
          $"A FLIC frame ends {data.Length - at} byte(s) into a sub-chunk header, which is six bytes.");

      var size = BinaryPrimitives.ReadUInt32LittleEndian(data[at..]);
      var type = BinaryPrimitives.ReadUInt16LittleEndian(data[(at + 4)..]);
      if (size < 6 || at + size > data.Length)
        throw new InvalidDataException(
          $"A FLIC sub-chunk of type {type} at byte {at} states a size of {size}, which "
          + (size < 6 ? "is shorter than its own six-byte header." : $"runs past the frame's {data.Length} bytes."));

      var payload = data.Slice(at + 6, (int)size - 6);
      switch (type) {
        case FliChunkType.COLOR256:
          this._DecodeColor(payload, sixBit: false);
          break;
        case FliChunkType.COLOR64:
          this._DecodeColor(payload, sixBit: true);
          break;
        case FliChunkType.SS2:
          this._DecodeSs2(payload);
          break;
        case FliChunkType.LC:
          this._DecodeLc(payload);
          break;
        case FliChunkType.BRUN:
          this._DecodeBrun(payload);
          break;
        case FliChunkType.COPY:
          this._DecodeCopy(payload);
          break;
        case FliChunkType.BLACK:
          Array.Clear(this._canvas);
          break;
        case FliChunkType.PSTAMP:
          // A postage-stamp thumbnail for a file requestor: its own smaller picture at the universal
          // 6x6x6 palette, entirely unrelated to the film's own canvas and palette. Skipped rather
          // than decoded — reachable and exercised (ffmpeg's fli-flc/2422.FLC carries a genuine
          // 100x63 byte-run thumbnail on its first frame), but not a picture of the film.
          break;
        default:
          throw new NotSupportedException(
            $"A FLIC frame carries a sub-chunk of type {type} at byte {at}, which this decoder does not read. "
            + "Chunk types outside {4, 7, 11, 12, 13, 15, 16, 18} are not part of what this codec was built and "
            + "measured against.");
      }

      at += (int)size;
    }
  }

  // ============================================================================================
  // Palette chunks — FLI_COLOR256 (type 4) and FLI_COLOR64 (type 11)
  // ============================================================================================

  /// <summary>
  /// Applies a palette chunk's packets: a skip count, a change count and that many RGB triples,
  /// repeated until the chunk's stated packet count is exhausted.
  /// </summary>
  private void _DecodeColor(ReadOnlySpan<byte> payload, bool sixBit) {
    var at = 0;
    var packetCount = _ReadU16(payload, ref at, "a palette chunk's packet count");
    var index = 0;

    for (var packet = 0; packet < packetCount; ++packet) {
      var skip = _ReadU8(payload, ref at, "a palette packet's skip count");
      index += skip;

      var changeByte = _ReadU8(payload, ref at, "a palette packet's change count");
      var change = changeByte == 0 ? 256 : changeByte;

      if (index + change > 256)
        throw new InvalidDataException(
          $"A FLIC palette chunk writes {change} colour(s) starting at index {index}, which reaches past the "
          + "256 entries a palette holds.");

      for (var entry = 0; entry < change; ++entry, ++index) {
        var r = _ReadU8(payload, ref at, "a palette entry's red component");
        var g = _ReadU8(payload, ref at, "a palette entry's green component");
        var b = _ReadU8(payload, ref at, "a palette entry's blue component");

        this._palette[index * 3] = sixBit ? ChannelScaling.Expand6(r) : r;
        this._palette[index * 3 + 1] = sixBit ? ChannelScaling.Expand6(g) : g;
        this._palette[index * 3 + 2] = sixBit ? ChannelScaling.Expand6(b) : b;
      }
    }
  }

  // ============================================================================================
  // Whole-frame chunks — FLI_BLACK (type 13), FLI_BRUN (type 15) and FLI_COPY (type 16)
  // ============================================================================================

  /// <summary>
  /// One row a line: a packet-count byte the format holds over from the original Animator and never
  /// reads back, then byte-run packets until the row's <see cref="_width"/> pixels are accounted for.
  /// </summary>
  /// <remarks>
  /// The sign convention is the opposite of <see cref="_DecodeLc"/>'s. A positive count replicates the
  /// single byte that follows it; a negative one copies the <c>|count|</c> bytes that follow literally.
  /// Two independent primary sources — the 1993 Dr Dobb's article Autodesk itself contributed to, and
  /// the FLC.txt Animator Pro file format reference — agree on this reading, against at least one
  /// third-party summary that has the two the wrong way round.
  /// </remarks>
  private void _DecodeBrun(ReadOnlySpan<byte> payload) {
    var at = 0;
    for (var row = 0; row < this._height; ++row) {
      _ReadU8(payload, ref at, "a byte-run row's packet count"); // held over from Animator; not used
      var rowStart = row * this._width;
      var x = 0;

      while (x < this._width) {
        var count = unchecked((sbyte)_ReadU8(payload, ref at, "a byte-run packet's count"));
        if (count > 0) {
          var value = _ReadU8(payload, ref at, "a byte-run packet's replicated pixel");
          _RefuseRunPastRow(x, count, row, this._width);
          for (var i = 0; i < count; ++i)
            this._canvas[rowStart + x++] = value;
        } else if (count < 0) {
          var n = -count;
          _RefuseRunPastRow(x, n, row, this._width);
          for (var i = 0; i < n; ++i)
            this._canvas[rowStart + x++] = _ReadU8(payload, ref at, "a byte-run packet's literal pixel");
        } else
          throw new NotSupportedException(
            $"A FLI_BRUN packet on row {row} states a count of zero. Read as a replicated pixel that is a "
            + "no-op consuming one byte; read as a literal run it consumes none. The format does not say "
            + "which, and no encoder writes a packet that costs a byte to do nothing.");
      }
    }
  }

  private void _DecodeCopy(ReadOnlySpan<byte> payload) {
    var expected = this._width * this._height;
    if (payload.Length != expected)
      throw new InvalidDataException(
        $"A FLI_COPY chunk carries {payload.Length} byte(s) for a {this._width}x{this._height} picture, which "
        + $"needs exactly {expected}.");

    payload.CopyTo(this._canvas);
  }

  // ============================================================================================
  // Delta chunks — FLI_LC (type 12) and FLI_SS2 (type 7)
  // ============================================================================================

  /// <summary>
  /// The byte-oriented delta the original Animator writes: a first-changed-line index, a line count,
  /// and per line a packet count followed by that many skip/run packets.
  /// </summary>
  /// <remarks>
  /// The sign convention is the opposite of <see cref="_DecodeBrun"/>'s: positive copies literal bytes,
  /// negative replicates one. A count of zero is unambiguous here — a copy of zero literal bytes is a
  /// well-defined no-op — so unlike the byte-run chunk it is not refused.
  /// </remarks>
  private void _DecodeLc(ReadOnlySpan<byte> payload) {
    var at = 0;
    var firstLine = _ReadU16(payload, ref at, "a delta chunk's first changed line");
    var lineCount = _ReadU16(payload, ref at, "a delta chunk's line count");

    _RefuseLinesPastPicture(firstLine, lineCount, this._height, "FLI_LC");

    for (var line = 0; line < lineCount; ++line) {
      var row = firstLine + line;
      var rowStart = row * this._width;
      var packetCount = _ReadU8(payload, ref at, "a delta line's packet count");
      var x = 0;

      for (var packet = 0; packet < packetCount; ++packet) {
        var skip = _ReadU8(payload, ref at, "a delta packet's skip count");
        x += skip;

        var size = unchecked((sbyte)_ReadU8(payload, ref at, "a delta packet's size"));
        if (size > 0) {
          _RefuseRunPastRow(x, size, row, this._width);
          for (var i = 0; i < size; ++i)
            this._canvas[rowStart + x++] = _ReadU8(payload, ref at, "a delta packet's literal pixel");
        } else if (size < 0) {
          var n = -size;
          var value = _ReadU8(payload, ref at, "a delta packet's replicated pixel");
          _RefuseRunPastRow(x, n, row, this._width);
          for (var i = 0; i < n; ++i)
            this._canvas[rowStart + x++] = value;
        }
      }
    }
  }

  /// <summary>
  /// The word-oriented delta <c>.flc</c> writes: opcode words that skip lines or set a line's last
  /// pixel, a packet count, and per packet a skip/run of whole pixel pairs.
  /// </summary>
  /// <remarks>
  /// Everything here moves in pairs of pixels — a "word" is two adjacent bytes of the canvas, copied
  /// or replicated together — except the column skip, which counts single pixels, and the one opcode
  /// that sets a line's last pixel directly, which exists because a line of odd width has one pixel a
  /// word cannot reach.
  /// </remarks>
  private void _DecodeSs2(ReadOnlySpan<byte> payload) {
    var at = 0;
    var lineCount = _ReadU16(payload, ref at, "a word-delta chunk's line count");
    var y = 0;

    for (var line = 0; line < lineCount; ++line) {
      var word = _ReadU16(payload, ref at, "a word-delta opcode");

      while ((word & 0xC000) == 0xC000) {
        // Top two bits 11: a line-skip count, the word's value taken as negative.
        y += -unchecked((short)word);
        word = _ReadU16(payload, ref at, "a word-delta opcode");
      }

      if (y >= this._height)
        throw new InvalidDataException(
          $"A FLI_SS2 chunk's line skips reach row {y} of a {this._height}-row picture.");

      var rowStart = y * this._width;

      if ((word & 0xC000) == 0x8000) {
        // Top two bits 10: the low byte is this line's last pixel, for a line an even count of whole
        // pixel-pairs cannot reach every column of.
        this._canvas[rowStart + this._width - 1] = unchecked((byte)word);
        word = _ReadU16(payload, ref at, "a word-delta packet count");
      }

      // Top two bits 00: word holds this line's packet count outright.
      var packetCount = word;
      var x = 0;

      for (var packet = 0; packet < packetCount; ++packet) {
        var skip = _ReadU8(payload, ref at, "a word-delta packet's skip count");
        x += skip;

        var size = unchecked((sbyte)_ReadU8(payload, ref at, "a word-delta packet's size"));
        if (size >= 0) {
          var pixels = size * 2;
          _RefuseRunPastRow(x, pixels, y, this._width);
          for (var i = 0; i < size; ++i) {
            this._canvas[rowStart + x] = _ReadU8(payload, ref at, "a word-delta packet's literal low pixel");
            this._canvas[rowStart + x + 1] = _ReadU8(payload, ref at, "a word-delta packet's literal high pixel");
            x += 2;
          }
        } else {
          var n = -size;
          var low = _ReadU8(payload, ref at, "a word-delta packet's replicated low pixel");
          var high = _ReadU8(payload, ref at, "a word-delta packet's replicated high pixel");
          _RefuseRunPastRow(x, n * 2, y, this._width);
          for (var i = 0; i < n; ++i) {
            this._canvas[rowStart + x] = low;
            this._canvas[rowStart + x + 1] = high;
            x += 2;
          }
        }
      }

      ++y;
    }
  }

  // ============================================================================================
  // Byte reading and bounds
  // ============================================================================================

  private static byte _ReadU8(ReadOnlySpan<byte> data, ref int at, string what) {
    if (at + 1 > data.Length)
      throw new InvalidDataException($"A FLIC chunk ends before {what}, {1 - (data.Length - at)} byte(s) short.");

    return data[at++];
  }

  private static ushort _ReadU16(ReadOnlySpan<byte> data, ref int at, string what) {
    if (at + 2 > data.Length)
      throw new InvalidDataException($"A FLIC chunk ends before {what}, {2 - (data.Length - at)} byte(s) short.");

    var value = BinaryPrimitives.ReadUInt16LittleEndian(data[at..]);
    at += 2;
    return value;
  }

  private static void _RefuseRunPastRow(int x, int count, int row, int width) {
    if (x + count > width)
      throw new InvalidDataException(
        $"A FLIC packet on row {row} writes {count} pixel(s) starting at column {x}, which reaches past the "
        + $"picture's width of {width}.");
  }

  private static void _RefuseLinesPastPicture(int firstLine, int lineCount, int height, string chunkName) {
    if (firstLine + lineCount > height)
      throw new InvalidDataException(
        $"A {chunkName} chunk states {lineCount} line(s) starting at row {firstLine}, which reaches past the "
        + $"picture's height of {height}.");
  }
}
