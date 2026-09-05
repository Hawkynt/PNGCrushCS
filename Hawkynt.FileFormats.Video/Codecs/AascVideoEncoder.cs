using System;
using System.Buffers.Binary;
using FileFormat.Bmp;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Encodes Autodesk Animator Codec (<c>AASC</c>): twenty-four-bit BGR pictures as runs and literal
/// runs of raw bytes, coded bottom row first, with the reposition escape standing in for whatever did
/// not change since the frame before.
/// </summary>
/// <remarks>
/// The mirror of <see cref="AascVideoDecoder"/>, whose remarks carry where the coding was read from
/// and what had to be measured to settle it. The one thing worth repeating here, because it is what
/// this encoder is built around: <b>a run counts bytes of the <c>width * 3</c>-wide row, not pixels</b>.
/// A run opcode repeats a single byte, so a run may start and stop in the middle of a colour, and a
/// solid stretch of one colour is not one run but three interleaved ones — which is why this codes the
/// row as a flat byte string and never looks at where a triple begins.
/// <para/>
/// <b>What is written.</b> Every packet opens on the four-byte compression word the decoder reads
/// before any opcode. Compression 1 is the run-length coding; compression 0 is the picture with no
/// coding at all, rows bottom up and each padded to a four-byte word, and is written for the frame
/// where it comes out shorter — which noise does, since coding it costs two bytes an opcode and saves
/// nothing.
/// <para/>
/// <b>Which frames are key frames.</b> The first, and any later one in which every byte was written.
/// A delta frame walks each row beside the same row of the frame before and repositions the pen past
/// stretches of at least five unchanged bytes; four or fewer are written again, because the four-byte
/// escape that would skip them costs as much as they do. Rows with nothing to write are left as bare
/// end-of-rows, and four or more of those collapse into one vertical reposition. A frame that reached
/// for no escape at all needs nothing from the frame before it and is flagged as a key frame whether
/// or not it was the first — and a frame written uncompressed always is, since it states every byte.
/// <para/>
/// <b>What is accepted.</b> Any picture that converts to eight-bit colour without changing a sample —
/// RGB and BGR with or without alpha, grey, palettised, 5-6-5 — with alpha dropped, the format having
/// no place for it. Deeper, floating-point and YUV pictures are refused by name rather than quantised.
/// The depth is twenty-four bits and nothing else: the decoder reads no other, so a stream written at
/// another depth would be one nothing here could read back.
/// <para/>
/// The walk over the escapes is <see cref="MicrosoftRleEncoder"/>'s, which was itself adapted from
/// FFmpeg's <c>libavcodec/msrleenc.c</c>, copyright (c) 2023 Tomas Härdin, distributed there under
/// LGPL-2.1-or-later. This adaptation is distributed with PNGCrushCS under LGPL-3.0-or-later.
/// </remarks>
public sealed class AascVideoEncoder : IVideoCodecEncoder<AascVideoEncoder> {

  private static readonly CodecTag _Tag = CodecTag.FromCharacters("AASC");

  /// <summary>The frame's opening word: the picture as it is, rows bottom up, each padded to a word.</summary>
  private const uint _UNCOMPRESSED = 0;

  /// <summary>The frame's opening word: the run-length coding.</summary>
  private const uint _RUN_LENGTH_CODED = 1;

  private const byte _ESCAPE = 0x00;
  private const byte _END_OF_ROW = 0x00;
  private const byte _END_OF_FRAME = 0x01;
  private const byte _REPOSITION = 0x02;

  /// <summary>The longest run one opcode can state.</summary>
  private const int _LONGEST_RUN = 255;

  /// <summary>
  /// The most bytes one literal run is given. Not 255, because an odd count is padded to a word and a
  /// run of 255 would carry a wasted byte every time.
  /// </summary>
  private const int _LONGEST_LITERAL = 254;

  /// <summary>The furthest one reposition can move the pen along either axis.</summary>
  private const int _LONGEST_REPOSITION = 255;

  /// <summary>
  /// How many unchanged bytes in a row it takes for a reposition to pay for itself: the escape is four
  /// bytes, and four bytes written into a literal run beside their neighbours cost the same.
  /// </summary>
  private const int _SHORTEST_SKIP = 5;

  /// <summary>
  /// How many unchanged rows in a row it takes for a vertical reposition to beat the end-of-rows that
  /// already skipped them: the reposition and its end-of-row are six bytes, four end-of-rows are eight.
  /// </summary>
  private const int _SHORTEST_ROW_SKIP = 4;

  private readonly MediaStreamInfo _stream;
  private readonly int _width;
  private readonly int _height;
  private readonly int _stride;

  /// <summary>The picture before as BGR bytes in display order, or null before the first frame.</summary>
  private byte[]? _previous;

  private byte[] _buffer = new byte[4096];
  private int _length;

  private AascVideoEncoder(MediaStreamInfo stream) {
    this._width = stream.Width;
    this._height = stream.Height;
    this._stride = stream.Width * 3;

    var header = new BitmapInfoHeader(
      HeaderSize: BitmapInfoHeader.StructSize,
      Width: stream.Width,
      Height: stream.Height,
      Planes: 1,
      BitsPerPixel: 24,
      Compression: unchecked((int)_Tag.Value),
      ImageSize: 0,
      XPixelsPerMeter: 0,
      YPixelsPerMeter: 0,
      ColorsUsed: 0,
      ImportantColors: 0);

    var format = new byte[BitmapInfoHeader.StructSize];
    header.WriteTo(format);

    this._stream = new() {
      Index = stream.Index,
      Kind = MediaStreamKind.Video,
      Codec = _Tag,
      Handler = _Tag,
      CodecId = "V_MS/VFW/FOURCC",
      TimeBase = stream.TimeBase,
      FrameRate = stream.FrameRate,
      DeclaredFrameCount = stream.DeclaredFrameCount,
      Width = stream.Width,
      Height = stream.Height,
      BitsPerPixel = 24,
      CodecPrivateData = format,
      Language = stream.Language,
      Name = stream.Name,
    };
  }

  public static string CodecName => "Autodesk Animator Codec";

  public static CodecTag Codec => _Tag;

  /// <summary>Builds an encoder for the stream described, refusing a depth the coding is not read at.</summary>
  public static AascVideoEncoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.Kind != MediaStreamKind.Video)
      throw new NotSupportedException("The Autodesk Animator Codec can only encode a video stream.");
    if (stream.Width <= 0 || stream.Height <= 0)
      throw new NotSupportedException(
        $"An Autodesk Animator Codec encoder needs the picture size up front; {stream.Width}x{stream.Height} was supplied.");
    if ((long)stream.Width * stream.Height * 3 > int.MaxValue)
      throw new NotSupportedException(
        $"A picture of {stream.Width}x{stream.Height} is more bytes than an AASC frame can hold.");
    if (stream.BitsPerPixel is not (0 or 24))
      throw new NotSupportedException(
        $"Video stream {stream.Index} asks for {stream.BitsPerPixel} bits per pixel. This coding is written at "
        + "twenty-four bits a pixel and nothing else, since nothing here reads another depth back.");

    return new(stream);
  }

  /// <summary>Codes one picture against the one before it, or whole when there is none.</summary>
  /// <remarks>
  /// Always produces a packet: this codec holds no frame back, and a picture identical to the one
  /// before it is written as a single vertical reposition over the whole picture, which is how the
  /// format spells "nothing changed".
  /// </remarks>
  public bool TryEncode(RawImage frame, long? presentationTimestamp, out CodedPacket packet) {
    var picture = LosslessEncoderInput.Prepare(frame, PixelFormat.Bgr24, this._width, this._height, CodecName);
    var current = picture.PixelData.AsSpan(0, this._stride * this._height).ToArray();

    this._length = 0;
    this._PutWord(_RUN_LENGTH_CODED);
    var wholePicture = this._EncodeFrame(current, this._previous == null);

    // Noise costs two bytes an opcode and saves nothing, so where the coding came out longer than the
    // picture itself the picture goes in instead — which also makes that frame a key frame, since an
    // uncompressed frame states every byte and predicts from nothing.
    var padded = (this._stride + 3) & ~3;
    if (4 + padded * this._height < this._length) {
      this._WriteUncompressed(current);
      wholePicture = true;
    }

    this._previous = current;

    packet = new(
      this._stream.Index,
      this._buffer.AsSpan(0, this._length).ToArray(),
      PresentationTimestamp: presentationTimestamp,
      DecodeTimestamp: presentationTimestamp,
      IsKeyFrame: wholePicture);
    return true;
  }

  public MediaStreamInfo DescribeStream() => this._stream;

  // ============================================================================================
  // The frame
  // ============================================================================================

  /// <summary>
  /// Writes one frame's opcodes, bottom row first, and says whether every byte of it was written.
  /// </summary>
  /// <remarks>
  /// Ported from <see cref="MicrosoftRleEncoder"/>'s walk, which is the same walk over the same
  /// escapes; what differs is that a unit here is one byte of a <c>width * 3</c>-wide row rather than
  /// one palette index, so the whole of the arithmetic is in bytes and a colour is never assembled.
  /// </remarks>
  private bool _EncodeFrame(byte[] current, bool keyFrame) {
    var stride = this._stride;
    var wholePicture = true;

    if (keyFrame) {
      for (var y = this._height - 1; y >= 0; --y) {
        this._EncodeRow(current.AsSpan(y * stride, stride));
        this._Put(_ESCAPE, _END_OF_ROW);
      }
    } else {
      var previous = this._previous!;
      var skippedRows = 0;
      for (var y = this._height - 1; y >= 0; --y) {
        var row = current.AsSpan(y * stride, stride);
        var before = previous.AsSpan(y * stride, stride);
        var unchanged = 0;
        var rowStart = 0;
        var encoded = false;

        for (var x = 0; x < stride; ++x) {
          if (row[x] == before[x]) {
            ++unchanged;
            if (unchanged != _SHORTEST_SKIP)
              continue;

            // The pen is about to skip; whatever changed before this run of unchanged bytes is
            // written now, up to where the run began.
            var length = x - rowStart - (_SHORTEST_SKIP - 1);
            if (length > 0) {
              this._WriteRowSkip(skippedRows);
              skippedRows = 0;
              this._EncodeRow(row.Slice(rowStart, length));
              encoded = true;
            }

            rowStart = -1;
            continue;
          }

          if (unchanged >= _SHORTEST_SKIP) {
            this._WriteRowSkip(skippedRows);
            skippedRows = 0;
            this._WriteReposition(unchanged);
            wholePicture = false;
            encoded = true;
          }

          unchanged = 0;
          if (rowStart == -1)
            rowStart = x;
        }

        if (unchanged < _SHORTEST_SKIP) {
          this._WriteRowSkip(skippedRows);
          skippedRows = 0;
          this._EncodeRow(row[rowStart..]);
          encoded = true;
        } else
          wholePicture = false;

        this._Put(_ESCAPE, _END_OF_ROW);
        skippedRows = encoded ? 0 : skippedRows + 1;
      }

      this._WriteRowSkip(skippedRows);
    }

    this._Put(_ESCAPE, _END_OF_FRAME);
    return wholePicture;
  }

  /// <summary>
  /// Writes a stretch of bytes as runs where three or more repeat and as literal runs elsewhere.
  /// </summary>
  /// <remarks>
  /// Three is the shortest run worth an opcode of its own: two repeated bytes inside a literal run
  /// cost two bytes, and a run opcode costs the same.
  /// </remarks>
  private void _EncodeRow(ReadOnlySpan<byte> row) {
    if (row.IsEmpty)
      return;

    var run = 0;
    var last = -1;
    var literalStart = 0;
    for (var x = 0; x < row.Length; ++x) {
      if (last == row[x]) {
        ++run;
        if (run == 3)
          this._WriteLiteral(row.Slice(literalStart, x - literalStart - 2));
      } else {
        if (run >= 3) {
          this._WriteRun(run, (byte)last);
          literalStart = x;
        }

        run = 1;
      }

      last = row[x];
    }

    if (run >= 3)
      this._WriteRun(run, (byte)last);
    else
      this._WriteLiteral(row[literalStart..]);
  }

  /// <summary>The whole picture with no coding at all, rows bottom up and each padded to a word.</summary>
  private void _WriteUncompressed(byte[] current) {
    var stride = this._stride;
    var padded = (stride + 3) & ~3;

    this._length = 0;
    this._PutWord(_UNCOMPRESSED);
    for (var y = this._height - 1; y >= 0; --y) {
      this._Put(current.AsSpan(y * stride, stride));
      for (var pad = stride; pad < padded; ++pad)
        this._Put(0);
    }
  }

  // ============================================================================================
  // The opcodes
  // ============================================================================================

  /// <summary>One byte repeated, in as many opcodes as its count needs.</summary>
  private void _WriteRun(int count, byte value) {
    for (; count >= _LONGEST_RUN; count -= _LONGEST_RUN)
      this._Put(_LONGEST_RUN, value);

    if (count >= 1)
      this._Put((byte)count, value);
  }

  /// <summary>
  /// Bytes spelled out one after another.
  /// </summary>
  /// <remarks>
  /// A literal run states at least three bytes, because a count of nought, one or two is one of the
  /// escapes. One byte is written as a run of one, and two as two runs of one.
  /// </remarks>
  private void _WriteLiteral(ReadOnlySpan<byte> bytes) {
    for (; bytes.Length >= _LONGEST_LITERAL; bytes = bytes[_LONGEST_LITERAL..])
      this._WriteLiteralOpcode(bytes[.._LONGEST_LITERAL]);

    switch (bytes.Length) {
      case 0:
        return;
      case 1:
        this._WriteRun(1, bytes[0]);
        return;
      case 2:
        this._WriteRun(1, bytes[0]);
        this._WriteRun(1, bytes[1]);
        return;
      default:
        this._WriteLiteralOpcode(bytes);
        return;
    }
  }

  /// <summary>One literal opcode: the count, the bytes, and a pad byte to reach a word.</summary>
  private void _WriteLiteralOpcode(ReadOnlySpan<byte> bytes) {
    this._Put(_ESCAPE, (byte)bytes.Length);
    this._Put(bytes);
    if ((bytes.Length & 1) != 0)
      this._Put(0);
  }

  /// <summary>Moves the pen along the row, in as many escapes as the distance needs.</summary>
  private void _WriteReposition(int columns) {
    for (; columns >= _LONGEST_REPOSITION; columns -= _LONGEST_REPOSITION)
      this._Put(_ESCAPE, _REPOSITION, _LONGEST_REPOSITION, 0);

    if (columns > 0)
      this._Put(_ESCAPE, _REPOSITION, (byte)columns, 0);
  }

  /// <summary>
  /// Replaces a stretch of bare end-of-rows with one vertical reposition and a single end-of-row.
  /// </summary>
  /// <remarks>
  /// The rows were already skipped as they went by, each as an end-of-row with nothing in front of it;
  /// those two bytes a row are taken back here and rewritten as a reposition once there are enough of
  /// them to save anything. The end-of-row that follows the reposition is itself one row of the skip,
  /// which is why the reposition moves one row fewer.
  /// </remarks>
  private void _WriteRowSkip(int rows) {
    if (rows < _SHORTEST_ROW_SKIP)
      return;

    this._length -= 2 * rows;
    --rows;
    for (; rows >= _LONGEST_REPOSITION; rows -= _LONGEST_REPOSITION)
      this._Put(_ESCAPE, _REPOSITION, 0, _LONGEST_REPOSITION);

    if (rows > 0)
      this._Put(_ESCAPE, _REPOSITION, 0, (byte)rows);

    this._Put(_ESCAPE, _END_OF_ROW);
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

  private void _PutWord(uint word) {
    Span<byte> bytes = stackalloc byte[4];
    BinaryPrimitives.WriteUInt32LittleEndian(bytes, word);
    this._Put(bytes);
  }

  private void _Put(ReadOnlySpan<byte> bytes) {
    if (this._length + bytes.Length > this._buffer.Length)
      Array.Resize(ref this._buffer, Math.Max(this._buffer.Length * 2, this._length + bytes.Length));

    bytes.CopyTo(this._buffer.AsSpan(this._length));
    this._length += bytes.Length;
  }
}
