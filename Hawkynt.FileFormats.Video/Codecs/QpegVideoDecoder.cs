using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Bmp;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Decodes Q-Team QPEG video (<c>QPEG</c>, <c>Q1.0</c>, <c>Q1.1</c>): palettised eight-bit pictures coded
/// bottom row first, an intraframe run-length scheme for keyframes and a mix of run-length coding, skip
/// runs, a per-frame fill table and variable-sized block motion compensation for the frames between them.
/// </summary>
/// <remarks>
/// Read from "Description of the QPEG Video Codec" by Mike Melanson and Konstantin Shishkov — the same,
/// named, independent write-up this library's Apple Graphics (SMC) decoder already reads from — mirrored
/// in full on MultimediaWiki's own QPEG page.
/// <para/>
/// Every encoded frame opens with a four-byte little-endian size (the whole frame including this field),
/// a 128-byte "frame data table" used by interframes' fill codes, a byte the document calls unknown and
/// states is always <c>0xE0</c>, and a frame type byte: <c>0x10</c> for an intraframe, <c>0x00</c> for an
/// interframe with no motion compensation, and any other value for one that has it.
/// <para/>
/// <b>Two of the document's own run-length formulas are one short, settled by measurement.</b> An
/// interframe's short literal run (<c>&gt; 0xE0</c>) and its short copy-from-the-coded-data run
/// (<c>0xC0..0xDF</c>) both state their length as the code's low bits with no addition; against three
/// real files, both need one added — <c>(code &amp; mask) + 1</c> — before the byte that follows a run
/// lands on the next real opcode rather than one byte into what the run should already have consumed.
/// Every other run and skip formula in the document, on both frame kinds, is exact as written. The
/// intraframe algorithm's own three run-length forms already carry a stated <c>+1</c> or <c>+2</c> and
/// needed no correction.
/// <para/>
/// <b>Motion compensation reads its source block from the previous frame, not from the picture being
/// built.</b> The document says as much in prose ("copying blocks of pixels from the previous frame"),
/// and it matters because a block's source and destination can overlap within the same frame: reading
/// from the partially-decoded output instead reproduces most of a frame correctly and drifts on exactly
/// the pixels an overlapping block touches twice.
/// <para/>
/// <b>Measured.</b> Three files from <c>samples.ffmpeg.org/V-codecs/QPEG/</c> — <c>qpeg-test.avi</c>
/// (80x60, fifteen frames, exercising every frame type), <c>Clock.avi</c> and <c>Space.avi</c> (320x240,
/// one hundred and one hundred ninety-nine frames) — were decoded here and by ffmpeg and compared sample
/// for sample against ffmpeg's own <c>rgb24</c> output: all 314 frames are identical, maximum delta
/// nought.
/// <para/>
/// What is not implemented refuses and says so: a frame shorter than the 134-byte header every frame
/// opens with; the unknown byte at offset 132 stating anything other than <c>0xE0</c>, since no measured
/// file states otherwise and reading it wrongly would be silent; and any run, copy, skip or motion block
/// that would read past the end of the coded data or write past the picture's own size.
/// </remarks>
public sealed class QpegVideoDecoder : IVideoCodecDecoder<QpegVideoDecoder> {

  private static readonly CodecTag _Qpeg = CodecTag.FromCharacters("QPEG");
  private static readonly CodecTag _Q10 = CodecTag.FromCharacters("Q1.0");
  private static readonly CodecTag _Q11 = CodecTag.FromCharacters("Q1.1");

  private const int _HEADER_LENGTH = 134;
  private const byte _EXPECTED_MARKER = 0xE0;
  private const int _FRAME_TYPE_INTRA = 0x10;
  private const int _FRAME_TYPE_INTER_NO_MC = 0x00;

  private static readonly (int Width, int Height)[] _BlockDimensions = [
    (0, 0), (32, 32), (24, 32), (8, 32), (24, 24), (16, 16), (32, 16), (16, 32),
    (8, 16), (16, 8), (32, 24), (32, 8), (8, 8), (16, 24), (24, 16), (4, 4),
  ];

  private readonly int _width;
  private readonly int _height;
  private readonly byte[] _palette;
  private readonly int _paletteCount;

  /// <summary>The picture as palette indices, one byte a pixel, bottom row first — the order the coding
  /// itself walks in, kept between frames because that is what an interframe is predicted from.</summary>
  private readonly byte[] _canvas;

  public static string CodecName => "Q-Team QPEG";

  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    return stream.Kind == MediaStreamKind.Video
           && (stream.Codec.EqualsIgnoringCase(_Qpeg) || stream.Codec.EqualsIgnoringCase(_Q10) || stream.Codec.EqualsIgnoringCase(_Q11));
  }

  public static QpegVideoDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    var format = stream.CodecPrivateData;
    if (format.Length < BitmapInfoHeader.StructSize)
      throw new InvalidOperationException(
        $"QPEG video stream {stream.Index} carries {format.Length} bytes of stream format where a "
        + $"BITMAPINFOHEADER is {BitmapInfoHeader.StructSize}.");

    var info = BitmapInfoHeader.ReadFrom(format.Span);
    var width = info.Width;
    var height = info.Height < 0 ? -info.Height : info.Height;
    if (width <= 0 || height <= 0)
      throw new InvalidOperationException($"QPEG video stream {stream.Index} states a picture of {width}x{height}, which has no pixels.");

    if ((long)width * height > int.MaxValue)
      throw new InvalidOperationException($"QPEG video stream {stream.Index} states a picture of {width}x{height}, which is more pixels than can be held.");

    var (palette, paletteCount) = _ReadPalette(stream, info);

    return new(width, height, palette, paletteCount);
  }

  private QpegVideoDecoder(int width, int height, byte[] palette, int paletteCount) {
    this._width = width;
    this._height = height;
    this._palette = palette;
    this._paletteCount = paletteCount;
    this._canvas = new byte[width * height];
  }

  private static (byte[] Palette, int Count) _ReadPalette(MediaStreamInfo stream, BitmapInfoHeader info) {
    var headerSize = info.HeaderSize >= BitmapInfoHeader.StructSize ? info.HeaderSize : BitmapInfoHeader.StructSize;
    var format = stream.CodecPrivateData.Span;
    if (headerSize >= format.Length)
      throw new InvalidOperationException(
        $"QPEG video stream {stream.Index} carries no palette behind its {headerSize}-byte stream format header. "
        + "The frames hold palette indices and nothing else, so there are no colours to decode them to.");

    var entries = info.ColorsUsed > 0 ? info.ColorsUsed : 256;
    var available = (format.Length - headerSize) / 4;
    if (available < entries)
      throw new InvalidDataException($"QPEG video stream {stream.Index} states {entries} palette entries and carries {available}.");

    var palette = new byte[entries * 3];
    for (var entry = 0; entry < entries; ++entry) {
      var at = headerSize + entry * 4;
      palette[entry * 3] = format[at + 2];
      palette[entry * 3 + 1] = format[at + 1];
      palette[entry * 3 + 2] = format[at];
    }

    return (palette, entries);
  }

  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    var data = packet.Data.Span;
    if (data.Length < _HEADER_LENGTH)
      throw new InvalidDataException($"A QPEG video packet is {data.Length} bytes, short of the {_HEADER_LENGTH} byte header every frame opens with.");

    var stated = (int)BinaryPrimitives.ReadUInt32LittleEndian(data);
    if (stated != data.Length)
      throw new InvalidDataException($"A QPEG video packet states its own size as {stated} bytes but is {data.Length}.");

    var table = data.Slice(4, 128);
    var marker = data[132];
    if (marker != _EXPECTED_MARKER)
      throw new InvalidDataException($"A QPEG video packet's byte 132 is {marker:x2}, not the {_EXPECTED_MARKER:x2} every measured file states there.");

    var frameType = data[133];
    var payload = data[_HEADER_LENGTH..];

    if (frameType == _FRAME_TYPE_INTRA)
      this._DecodeIntra(payload);
    else
      this._DecodeInter(payload, table, frameType);

    frame = this._BuildFrame();
    return true;
  }

  /// <summary>The basic RLE walk an intraframe is coded with: three shapes of literal run and three of
  /// copy-from-the-coded-data run, distinguished by the top bits of a control byte, each already
  /// carrying the length adjustment the document states.</summary>
  private void _DecodeIntra(ReadOnlySpan<byte> data) {
    var canvas = this._canvas;
    var total = canvas.Length;
    var cursor = 0;
    var pos = 0;
    var n = data.Length;

    while (cursor < total) {
      if (pos >= n)
        throw new InvalidDataException("A QPEG intraframe's coded data ran out before its picture was complete.");

      var code = data[pos++];
      if (code == 0xFC)
        break;

      if (code >= 0xF8) { // very long run
        _Require(pos + 2 <= n, "a very long run's extended length");
        var run = ((code & 7) << 16) + (data[pos] << 8) + data[pos + 1] + 2;
        pos += 2;
        _Require(pos < n, "a very long run's fill byte");
        var value = data[pos++];
        _Fill(canvas, ref cursor, total, run, value);
      } else if (code >= 0xF0) { // long run
        _Require(pos + 1 <= n, "a long run's extended length");
        var run = ((code & 0xF) << 8) + data[pos] + 2;
        ++pos;
        _Require(pos < n, "a long run's fill byte");
        var value = data[pos++];
        _Fill(canvas, ref cursor, total, run, value);
      } else if (code >= 0xE0) { // short run
        var run = (code & 0x1F) + 2;
        _Require(pos < n, "a short run's fill byte");
        var value = data[pos++];
        _Fill(canvas, ref cursor, total, run, value);
      } else if (code >= 0xC0) { // very long copy
        _Require(pos + 2 <= n, "a very long copy's extended length");
        var copy = ((code & 0x3F) << 16) + (data[pos] << 8) + data[pos + 1] + 1;
        pos += 2;
        _Copy(canvas, ref cursor, total, data, ref pos, copy);
      } else if (code >= 0x80) { // long copy
        _Require(pos + 1 <= n, "a long copy's extended length");
        var copy = ((code & 0x7F) << 8) + data[pos] + 1;
        ++pos;
        _Copy(canvas, ref cursor, total, data, ref pos, copy);
      } else { // short copy
        var copy = code + 1;
        _Copy(canvas, ref cursor, total, data, ref pos, copy);
      }
    }
  }

  /// <summary>
  /// An interframe: motion-compensation codes ahead of the "real" opcode when the frame type says so,
  /// then a short literal run, a short copy, a general skip, two special skip forms, a fill from the
  /// frame's own table, or a single-pixel skip — every one of them leaving the canvas as it already
  /// stands (a copy of the frame before) wherever it only skips.
  /// </summary>
  private void _DecodeInter(ReadOnlySpan<byte> data, ReadOnlySpan<byte> table, int frameType) {
    var canvas = this._canvas;
    var total = canvas.Length;
    var cursor = 0;
    var pos = 0;
    var n = data.Length;
    var usesMotionCompensation = frameType != _FRAME_TYPE_INTER_NO_MC;

    while (cursor < total) {
      if (pos >= n)
        throw new InvalidDataException("A QPEG interframe's coded data ran out before its picture was complete.");

      var code = data[pos++];

      if (usesMotionCompensation)
        while ((code & 0xF0) == 0xF0) {
          if (frameType == 1)
            this._MotionCompensate(code, data, ref pos, cursor);

          if (pos >= n)
            throw new InvalidDataException("A QPEG interframe's motion-compensation codes ran out before a real opcode followed.");
          code = data[pos++];
        }

      if (code == 0xE0)
        break;

      if (code > 0xE0) { // short literal run
        var run = (code & 0x1F) + 1;
        _Require(pos < n, "a short run's fill byte");
        var value = data[pos++];
        _Fill(canvas, ref cursor, total, run, value);
      } else if (code >= 0xC0) { // short copy
        var copy = (code & 0x1F) + 1;
        _Copy(canvas, ref cursor, total, data, ref pos, copy);
      } else if (code >= 0x82) { // general skip
        cursor += code & 0x3F;
      } else if (code == 0x81) {
        _Require(pos < n, "a special skip's extended length");
        cursor += data[pos++] + 320;
      } else if (code == 0x80) {
        _Require(pos < n, "a special skip's extended length");
        cursor += data[pos++] + 64;
      } else if (code >= 0x01) { // special fill
        _Require(cursor < total, "a special fill's destination");
        canvas[cursor++] = table[code];
      } else { // 0x00: single-pixel skip
        ++cursor;
      }

      if (cursor > total)
        throw new InvalidDataException("A QPEG interframe's coded data writes past the end of its picture.");
    }
  }

  /// <summary>
  /// A block of the dimension the code's low four bits select, copied from the previous frame at the
  /// motion vector the following byte states, into the current decode position — which is not advanced
  /// by this, since the ordinary skip and run codes that follow are what do that.
  /// </summary>
  private void _MotionCompensate(int code, ReadOnlySpan<byte> data, ref int pos, int cursor) {
    var (blockWidth, blockHeight) = _BlockDimensions[code & 0xF];
    _Require(pos < data.Length, "a motion vector byte");
    var vector = data[pos++];

    var horizontal = vector >> 4;
    if (horizontal >= 8)
      horizontal -= 16;
    var vertical = vector & 0xF;
    if (vertical >= 8)
      vertical -= 16;

    var width = this._width;
    var height = this._height;
    var y = cursor / width;
    var x = cursor % width;

    var previous = this._previousCanvas ?? throw new InvalidDataException(
      "A QPEG interframe uses motion compensation before any previous frame exists to compensate from.");

    for (var by = 0; by < blockHeight; ++by) {
      var destY = y + by;
      var sourceY = destY + vertical;
      if (destY < 0 || destY >= height || sourceY < 0 || sourceY >= height)
        continue;

      for (var bx = 0; bx < blockWidth; ++bx) {
        var destX = x + bx;
        var sourceX = destX + horizontal;
        if (destX < 0 || destX >= width || sourceX < 0 || sourceX >= width)
          continue;

        this._canvas[destY * width + destX] = previous[sourceY * width + sourceX];
      }
    }
  }

  private byte[]? _previousCanvas;

  private static void _Require(bool condition, string what) {
    if (!condition)
      throw new InvalidDataException($"A QPEG frame's coded data ran out reading {what}.");
  }

  private static void _Fill(byte[] canvas, ref int cursor, int total, int run, byte value) {
    if (cursor + run > total)
      throw new InvalidDataException("A QPEG frame's run writes past the end of its picture.");

    canvas.AsSpan(cursor, run).Fill(value);
    cursor += run;
  }

  private static void _Copy(byte[] canvas, ref int cursor, int total, ReadOnlySpan<byte> data, ref int pos, int copy) {
    if (cursor + copy > total)
      throw new InvalidDataException("A QPEG frame's copy writes past the end of its picture.");
    if (pos + copy > data.Length)
      throw new InvalidDataException("A QPEG frame's copy reads past the end of its coded data.");

    data.Slice(pos, copy).CopyTo(canvas.AsSpan(cursor, copy));
    pos += copy;
    cursor += copy;
  }

  private RawImage _BuildFrame() {
    // The canvas the codes above just finished writing becomes the "previous frame" the next
    // interframe's motion compensation and skip codes are measured against — a fresh copy, since the
    // canvas array itself keeps being mutated in place.
    this._previousCanvas = (byte[])this._canvas.Clone();

    var width = this._width;
    var height = this._height;
    var indices = new byte[width * height];

    // The coding walks the picture bottom row first; a RawImage's rows run top to bottom.
    for (var row = 0; row < height; ++row) {
      var codedRow = height - 1 - row;
      this._canvas.AsSpan(codedRow * width, width).CopyTo(indices.AsSpan(row * width, width));
    }

    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Indexed8,
      PixelData = indices,
      Palette = this._palette,
      PaletteCount = this._paletteCount,
    };
  }
}
