using System;
using System.IO;
using FileFormat.Bmp;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Encodes Microsoft RLE video (<c>MRLE</c>, <c>BI_RLE8</c> and <c>BI_RLE4</c>): palettised frames as
/// runs and literal pixels, with the delta escapes standing in for whatever did not change since the
/// frame before.
/// </summary>
/// <remarks>
/// Adapted from FFmpeg's <c>libavcodec/msrleenc.c</c>, copyright (c) 2023 Tomas Härdin, distributed
/// there under LGPL-2.1-or-later. This adaptation is distributed with PNGCrushCS under
/// LGPL-3.0-or-later.
/// <para/>
/// The opcodes are the ones a run-length Windows bitmap is stored with, and <see cref="MicrosoftRleDecoder"/>
/// reads them back through the same walk a still goes through. What makes this a video codec is
/// what the three escapes are used for: the first frame is written whole, and every frame after it
/// is compared with the one before, pixel by pixel. A stretch of five or more unchanged pixels
/// becomes a delta escape that moves the pen past them, a whole unchanged row becomes an end-of-line
/// with nothing in front of it, and four or more of those in a row collapse into one vertical delta.
/// Fewer than five unchanged pixels are written out again, because the escape that would skip them
/// is as long as they are.
/// <para/>
/// <b>Lossless, and only from indices.</b> The frames carry palette entries, so the input is a
/// palettised picture and nothing else: <see cref="PixelFormat.Indexed8"/> for the eight-bit coding
/// and <see cref="PixelFormat.Indexed4"/> for the four-bit one, each with its palette. A picture of
/// any other format is refused by name rather than quantised — which colours a picture should be
/// reduced to is a decision this codec has no business making silently. The palette sits in the
/// stream header and not in the frames, so it is fixed by the first picture (or by the stream
/// description handed in, where that already carries one) and a later picture that brings a
/// different one is refused too.
/// <para/>
/// A frame that used no escape at all is a picture the decoder can start at, and is flagged as a key
/// frame whether it was the first one or merely a frame in which every pixel happened to change.
/// The reference encoder does the same.
/// </remarks>
public sealed class MicrosoftRleEncoder : IVideoCodecEncoder<MicrosoftRleEncoder> {

  /// <summary>The four-character code containers name this codec with where they name it at all.</summary>
  private static readonly CodecTag _MRLE = CodecTag.FromCharacters("MRLE");

  private const uint _BI_RLE8 = 1;
  private const uint _BI_RLE4 = 2;

  private const byte _ESCAPE = 0x00;
  private const byte _END_OF_LINE = 0x00;
  private const byte _END_OF_BITMAP = 0x01;
  private const byte _DELTA = 0x02;

  /// <summary>The longest run one opcode can state.</summary>
  private const int _LONGEST_RUN = 255;

  /// <summary>
  /// The most pixels one absolute run is given. Not 255, because an odd byte count is padded to a
  /// word and a run of 255 would carry a wasted byte every time.
  /// </summary>
  private const int _LONGEST_ABSOLUTE = 254;

  /// <summary>The furthest one delta escape can move the pen along either axis.</summary>
  private const int _LONGEST_DELTA = 255;

  /// <summary>
  /// How many unchanged pixels in a row it takes for a delta escape to pay for itself: the escape
  /// is four bytes, and four literal pixels cost the same.
  /// </summary>
  private const int _SHORTEST_SKIP = 5;

  /// <summary>
  /// How many unchanged rows in a row it takes for a vertical delta to beat the end-of-lines that
  /// already skipped them: the delta and its end-of-line are six bytes, four end-of-lines are eight.
  /// </summary>
  private const int _SHORTEST_ROW_SKIP = 4;

  private readonly MediaStreamInfo _requested;
  private readonly int _width;
  private readonly int _height;
  private int _bitsPerPixel;
  private byte[]? _palette;
  private int _paletteCount;
  private MediaStreamInfo? _stream;

  /// <summary>The picture before, as one index per pixel in display order, or null before the first.</summary>
  private byte[]? _previous;

  private byte[] _buffer = new byte[4096];
  private int _length;

  private MicrosoftRleEncoder(MediaStreamInfo stream, int bitsPerPixel) {
    this._requested = stream;
    this._width = stream.Width;
    this._height = stream.Height;
    this._bitsPerPixel = bitsPerPixel;

    if (bitsPerPixel != 0)
      this._AdoptPaletteFrom(stream.CodecPrivateData.Span);
  }

  public static string CodecName => "Microsoft RLE";

  public static CodecTag Codec => _MRLE;

  /// <summary>
  /// Builds an encoder for the stream described, refusing a depth the coding is not defined at.
  /// </summary>
  /// <remarks>
  /// The depth is taken from the description where it states one and from the first picture where
  /// it does not. A description that already carries a <c>BITMAPINFOHEADER</c> of that depth with a
  /// palette behind it — one that came out of a demuxer, say — lends its palette, so that the stream
  /// can be described before a single picture has been seen.
  /// </remarks>
  public static MicrosoftRleEncoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.Kind != MediaStreamKind.Video)
      throw new NotSupportedException("Microsoft RLE can only encode a video stream.");
    if (stream.Width <= 0 || stream.Height <= 0)
      throw new NotSupportedException(
        $"A Microsoft RLE encoder needs the picture size up front; {stream.Width}x{stream.Height} was supplied.");
    if ((long)stream.Width * stream.Height > int.MaxValue)
      throw new NotSupportedException(
        $"A picture of {stream.Width}x{stream.Height} is more pixels than a Microsoft RLE frame can hold.");
    if (stream.BitsPerPixel is not (0 or 4 or 8))
      throw new NotSupportedException(
        $"Video stream {stream.Index} asks for {stream.BitsPerPixel} bits per pixel. Microsoft run-length coding is "
        + "defined at four bits a pixel and at eight, and nothing else is written.");

    return new(stream, stream.BitsPerPixel);
  }

  /// <summary>
  /// Codes one picture against the one before it, or whole when there is none.
  /// </summary>
  /// <remarks>
  /// Always produces a packet: this codec has no frame it holds back, and a picture identical to the
  /// one before it is written as a single vertical delta over the whole picture, which is how the
  /// format spells "nothing changed".
  /// </remarks>
  public bool TryEncode(RawImage frame, long? presentationTimestamp, out CodedPacket packet) {
    ArgumentNullException.ThrowIfNull(frame);
    if (frame.Width != this._width || frame.Height != this._height)
      throw new InvalidDataException(
        $"Microsoft RLE geometry is fixed at {this._width}x{this._height}; received {frame.Width}x{frame.Height}.");

    this._TakeDepthFrom(frame);
    this._TakePaletteFrom(frame);
    if (!frame.HasEnoughPixelData)
      throw new InvalidDataException(
        "The source RawImage does not contain enough pixel data for its declared format and dimensions.");

    var indices = this._Indices(frame);
    var keyFrame = this._previous == null;
    this._length = 0;
    var wholePicture = this._EncodeFrame(indices, keyFrame);
    this._previous = indices;

    packet = new(
      this._requested.Index,
      this._buffer.AsSpan(0, this._length).ToArray(),
      PresentationTimestamp: presentationTimestamp,
      DecodeTimestamp: presentationTimestamp,
      IsKeyFrame: wholePicture);
    return true;
  }

  /// <summary>
  /// The stream as a muxer needs it: a <c>BITMAPINFOHEADER</c> stating the run-length compression,
  /// with the palette behind it as <c>RGBQUAD</c>s.
  /// </summary>
  /// <remarks>
  /// That is the format an AVI's <c>strf</c> is and a Matroska <c>V_MS/VFW/FOURCC</c> track's private
  /// data is, and it is what <see cref="MicrosoftRleDecoder"/> reads its palette out of. The palette
  /// is not known until the first picture has been seen unless the description handed in carried
  /// one, so asking before then is refused rather than answered with a header naming no colours.
  /// </remarks>
  public MediaStreamInfo DescribeStream() {
    if (this._stream != null)
      return this._stream;

    if (this._bitsPerPixel == 0 || this._palette == null)
      throw new InvalidOperationException(
        "A Microsoft RLE stream cannot be described before its palette is known. Encode the first picture first, or "
        + "hand Create a stream whose CodecPrivateData is a BITMAPINFOHEADER with a palette behind it.");

    var header = new BitmapInfoHeader(
      HeaderSize: BitmapInfoHeader.StructSize,
      Width: this._width,
      Height: this._height,
      Planes: 1,
      BitsPerPixel: (short)this._bitsPerPixel,
      Compression: (int)(this._bitsPerPixel == 8 ? _BI_RLE8 : _BI_RLE4),
      ImageSize: 0,
      XPixelsPerMeter: 0,
      YPixelsPerMeter: 0,
      ColorsUsed: this._paletteCount,
      ImportantColors: 0);

    var format = new byte[BitmapInfoHeader.StructSize + this._paletteCount * 4];
    header.WriteTo(format);
    for (var entry = 0; entry < this._paletteCount; ++entry) {
      var at = BitmapInfoHeader.StructSize + entry * 4;
      format[at] = this._palette[entry * 3 + 2];
      format[at + 1] = this._palette[entry * 3 + 1];
      format[at + 2] = this._palette[entry * 3];
    }

    return this._stream = new() {
      Index = this._requested.Index,
      Kind = MediaStreamKind.Video,
      Codec = _MRLE,
      Handler = _MRLE,
      CodecId = "V_MS/VFW/FOURCC",
      TimeBase = this._requested.TimeBase,
      FrameRate = this._requested.FrameRate,
      DeclaredFrameCount = this._requested.DeclaredFrameCount,
      Width = this._width,
      Height = this._height,
      BitsPerPixel = this._bitsPerPixel,
      CodecPrivateData = format,
      Language = this._requested.Language,
      Name = this._requested.Name,
    };
  }

  // ============================================================================================
  // What goes in
  // ============================================================================================

  /// <summary>Fixes the depth from the first picture, or checks a later one against it.</summary>
  private void _TakeDepthFrom(RawImage frame) {
    var depth = frame.Format switch {
      PixelFormat.Indexed8 => 8,
      PixelFormat.Indexed4 => 4,
      _ => throw new NotSupportedException(
        $"Microsoft RLE codes palette indices and takes only Indexed8 and Indexed4 pictures; a {frame.Format} picture "
        + "would have to be quantised first, and which colours to reduce it to is not this codec's decision."),
    };

    if (this._bitsPerPixel == 0) {
      this._bitsPerPixel = depth;
      return;
    }

    if (depth != this._bitsPerPixel)
      throw new InvalidDataException(
        $"The stream is coded at {this._bitsPerPixel} bits a pixel and a {frame.Format} picture is {depth}; the depth "
        + "is stated in the stream header and cannot change between frames.");
  }

  /// <summary>Fixes the palette from the first picture, or checks a later one against it.</summary>
  /// <remarks>
  /// The palette is in the stream header, once, so every frame has to be drawn through the same one.
  /// A picture bringing another is refused rather than written through the first — its indices would
  /// decode to the wrong colours and nothing in the file would say so.
  /// </remarks>
  private void _TakePaletteFrom(RawImage frame) {
    if (frame.Palette == null || frame.PaletteCount <= 0)
      throw new InvalidDataException(
        "A palettised picture without a palette cannot be coded: the frames hold indices and the header holds the "
        + "colours, and there are none to put there.");

    var entries = Math.Min(frame.PaletteCount, 1 << this._bitsPerPixel);
    if (frame.Palette.Length < entries * 3)
      throw new InvalidDataException(
        $"The picture states a palette of {frame.PaletteCount} entries but carries {frame.Palette.Length / 3}.");

    if (this._palette == null) {
      this._palette = frame.Palette.AsSpan(0, entries * 3).ToArray();
      this._paletteCount = entries;
      return;
    }

    if (entries == this._paletteCount && frame.Palette.AsSpan(0, entries * 3).SequenceEqual(this._palette))
      return;

    throw new InvalidDataException(
      "The picture carries a different palette from the one the stream was described with. The palette is stated "
      + "once in the stream header, so it cannot change between frames.");
  }

  /// <summary>Takes a palette out of a stream format that already carries one at this depth.</summary>
  private void _AdoptPaletteFrom(ReadOnlySpan<byte> format) {
    if (format.Length < BitmapInfoHeader.StructSize)
      return;

    var info = BitmapInfoHeader.ReadFrom(format);
    if (info.BitsPerPixel != this._bitsPerPixel || info.HeaderSize < BitmapInfoHeader.StructSize)
      return;

    var entries = info.ColorsUsed > 0 ? info.ColorsUsed : 1 << this._bitsPerPixel;
    if (entries > 1 << this._bitsPerPixel || format.Length < info.HeaderSize + entries * 4)
      return;

    var palette = new byte[entries * 3];
    for (var entry = 0; entry < entries; ++entry) {
      var at = info.HeaderSize + entry * 4;
      palette[entry * 3] = format[at + 2];
      palette[entry * 3 + 1] = format[at + 1];
      palette[entry * 3 + 2] = format[at];
    }

    this._palette = palette;
    this._paletteCount = entries;
  }

  /// <summary>One index per pixel in display order, whatever the picture packed them as.</summary>
  /// <remarks>
  /// An index past the end of the palette is refused here. The header states how many colours
  /// there are and the decoder reads exactly that many, so such an index would decode to whatever
  /// happened to follow the palette in the file — a colour nothing chose.
  /// </remarks>
  private byte[] _Indices(RawImage frame) {
    var pixels = this._width * this._height;
    var indices = new byte[pixels];
    if (this._bitsPerPixel == 8)
      frame.PixelData.AsSpan(0, pixels).CopyTo(indices);
    else {
      var packed = frame.PixelData;
      for (var i = 0; i < pixels; ++i)
        indices[i] = (byte)((i & 1) == 0 ? packed[i >> 1] >> 4 : packed[i >> 1] & 0x0F);
    }

    for (var i = 0; i < pixels; ++i)
      if (indices[i] >= this._paletteCount)
        throw new InvalidDataException(
          $"Pixel {i % this._width},{i / this._width} is palette index {indices[i]} and the palette has "
          + $"{this._paletteCount} entries; the stream header names that many colours and no more.");

    return indices;
  }

  // ============================================================================================
  // The frame
  // ============================================================================================

  /// <summary>
  /// Writes one frame, bottom row first, and says whether every pixel of it was written.
  /// </summary>
  /// <remarks>
  /// A key frame is every row coded in full. A delta frame walks each row beside the same row of the
  /// frame before, writing the pixels that changed and skipping past the ones that did not — but only
  /// past runs of at least <see cref="_SHORTEST_SKIP"/>, since a shorter skip costs more than the
  /// pixels it saves. Rows with nothing to write are left as bare end-of-lines, which
  /// <see cref="_WriteRowSkip"/> collapses into one vertical delta once there are enough of them.
  /// </remarks>
  private bool _EncodeFrame(byte[] current, bool keyFrame) {
    var width = this._width;
    var wholePicture = true;

    if (keyFrame) {
      for (var y = this._height - 1; y >= 0; --y) {
        this._EncodeLine(current.AsSpan(y * width, width));
        this._Put(_ESCAPE, _END_OF_LINE);
      }
    } else {
      var previous = this._previous!;
      var skippedRows = 0;
      for (var y = this._height - 1; y >= 0; --y) {
        var line = current.AsSpan(y * width, width);
        var before = previous.AsSpan(y * width, width);
        var unchanged = 0;
        var lineStart = 0;
        var encoded = false;

        for (var x = 0; x < width; ++x) {
          if (line[x] == before[x]) {
            ++unchanged;
            if (unchanged != _SHORTEST_SKIP)
              continue;

            // The pen is about to skip; whatever changed before this run of unchanged pixels
            // is written now, up to where the run began.
            var length = x - lineStart - (_SHORTEST_SKIP - 1);
            if (length > 0) {
              this._WriteRowSkip(skippedRows);
              skippedRows = 0;
              this._EncodeLine(line.Slice(lineStart, length));
              encoded = true;
            }

            lineStart = -1;
            continue;
          }

          if (unchanged >= _SHORTEST_SKIP) {
            this._WriteRowSkip(skippedRows);
            skippedRows = 0;
            this._WriteDelta(unchanged);
            wholePicture = false;
            encoded = true;
          }

          unchanged = 0;
          if (lineStart == -1)
            lineStart = x;
        }

        if (unchanged < _SHORTEST_SKIP) {
          this._WriteRowSkip(skippedRows);
          skippedRows = 0;
          this._EncodeLine(line[lineStart..]);
          encoded = true;
        } else
          wholePicture = false;

        this._Put(_ESCAPE, _END_OF_LINE);
        skippedRows = encoded ? 0 : skippedRows + 1;
      }

      this._WriteRowSkip(skippedRows);
    }

    this._Put(_ESCAPE, _END_OF_BITMAP);
    return wholePicture;
  }

  /// <summary>
  /// Writes a stretch of pixels as runs where three or more repeat and as absolute runs elsewhere.
  /// </summary>
  /// <remarks>
  /// Used both for the rows of a key frame and for the changed stretches of a delta frame; it knows
  /// nothing about where in the row it is. Three is the shortest run worth a run opcode: two
  /// repeated pixels inside an absolute run cost two bytes, and a run opcode costs the same.
  /// </remarks>
  private void _EncodeLine(ReadOnlySpan<byte> line) {
    if (line.IsEmpty)
      return;

    var run = 0;
    var last = -1;
    var absoluteStart = 0;
    for (var x = 0; x < line.Length; ++x) {
      if (last == line[x]) {
        ++run;
        if (run == 3)
          this._WriteAbsolute(line.Slice(absoluteStart, x - absoluteStart - 2));
      } else {
        if (run >= 3) {
          this._WriteRun(run, (byte)last);
          absoluteStart = x;
        }

        run = 1;
      }

      last = line[x];
    }

    if (run >= 3)
      this._WriteRun(run, (byte)last);
    else
      this._WriteAbsolute(line[absoluteStart..]);
  }

  // ============================================================================================
  // The opcodes
  // ============================================================================================

  /// <summary>One index repeated, in as many opcodes as its count needs.</summary>
  private void _WriteRun(int count, byte index) {
    var value = this._bitsPerPixel == 8 ? index : (byte)((index << 4) | index);
    for (; count >= _LONGEST_RUN; count -= _LONGEST_RUN)
      this._Put(_LONGEST_RUN, value);

    if (count >= 1)
      this._Put((byte)count, value);
  }

  /// <summary>
  /// Pixels spelled out one after another.
  /// </summary>
  /// <remarks>
  /// An absolute run is at least three pixels, because a count under three is one of the escapes.
  /// One pixel is written as a run of one; two are written as two runs at eight bits, where each run
  /// carries one index, and as one run of two at four bits, where a run's colour byte carries two
  /// indices that alternate.
  /// </remarks>
  private void _WriteAbsolute(ReadOnlySpan<byte> pixels) {
    for (; pixels.Length >= _LONGEST_ABSOLUTE; pixels = pixels[_LONGEST_ABSOLUTE..])
      this._WriteAbsoluteOpcode(pixels[.._LONGEST_ABSOLUTE]);

    switch (pixels.Length) {
      case 0:
        return;
      case 1:
        this._WriteRun(1, pixels[0]);
        return;
      case 2 when this._bitsPerPixel == 8:
        this._WriteRun(1, pixels[0]);
        this._WriteRun(1, pixels[1]);
        return;
      case 2:
        this._Put(2, (byte)((pixels[0] << 4) | pixels[1]));
        return;
      default:
        this._WriteAbsoluteOpcode(pixels);
        return;
    }
  }

  /// <summary>One absolute opcode: the count, the packed pixels, and a pad byte to reach a word.</summary>
  private void _WriteAbsoluteOpcode(ReadOnlySpan<byte> pixels) {
    this._Put(_ESCAPE, (byte)pixels.Length);
    int bytes;
    if (this._bitsPerPixel == 8) {
      bytes = pixels.Length;
      this._Put(pixels);
    } else {
      bytes = (pixels.Length + 1) / 2;
      for (var i = 0; i < bytes; ++i) {
        var high = pixels[i * 2];
        var low = i * 2 + 1 < pixels.Length ? pixels[i * 2 + 1] : (byte)0;
        this._Put((byte)((high << 4) | low));
      }
    }

    if ((bytes & 1) != 0)
      this._Put(0);
  }

  /// <summary>Moves the pen along the row, in as many escapes as the distance needs.</summary>
  private void _WriteDelta(int columns) {
    for (; columns >= _LONGEST_DELTA; columns -= _LONGEST_DELTA)
      this._Put(_ESCAPE, _DELTA, _LONGEST_DELTA, 0);

    if (columns > 0)
      this._Put(_ESCAPE, _DELTA, (byte)columns, 0);
  }

  /// <summary>
  /// Replaces a stretch of bare end-of-lines with one vertical delta and a single end-of-line.
  /// </summary>
  /// <remarks>
  /// The rows were already skipped as they went by, each as an end-of-line with nothing in front of
  /// it; those two bytes a row are taken back here and rewritten as a delta once there are enough of
  /// them to save anything. The end-of-line that follows the delta is itself one row of the skip,
  /// which is why the delta moves one row fewer.
  /// </remarks>
  private void _WriteRowSkip(int rows) {
    if (rows < _SHORTEST_ROW_SKIP)
      return;

    this._length -= 2 * rows;
    --rows;
    for (; rows >= _LONGEST_DELTA; rows -= _LONGEST_DELTA)
      this._Put(_ESCAPE, _DELTA, 0, _LONGEST_DELTA);

    if (rows > 0)
      this._Put(_ESCAPE, _DELTA, 0, (byte)rows);

    this._Put(_ESCAPE, _END_OF_LINE);
  }

  // ============================================================================================
  // The output buffer
  // ============================================================================================

  private void _Put(byte value) {
    if (this._length == this._buffer.Length)
      Array.Resize(ref this._buffer, this._buffer.Length * 2);

    this._buffer[this._length++] = value;
  }

  private void _Put(byte first, byte second) {
    this._Put(first);
    this._Put(second);
  }

  private void _Put(byte first, byte second, byte third, byte fourth) {
    this._Put(first);
    this._Put(second);
    this._Put(third);
    this._Put(fourth);
  }

  private void _Put(ReadOnlySpan<byte> bytes) {
    if (this._length + bytes.Length > this._buffer.Length)
      Array.Resize(ref this._buffer, Math.Max(this._buffer.Length * 2, this._length + bytes.Length));

    bytes.CopyTo(this._buffer.AsSpan(this._length));
    this._length += bytes.Length;
  }
}
