using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Encodes Apple Video, the codec QuickTime calls <c>rpza</c>: 4x4 blocks of 15-bit RGB colour as
/// runs of skipped blocks, runs of one colour, and blocks stating all sixteen of their pixels.
/// </summary>
/// <remarks>
/// Adapted from FFmpeg's <c>libavcodec/rpzaenc.c</c>, copyright (c) Todd Kirby and David Adler,
/// distributed there under LGPL-2.1-or-later. This adaptation is distributed with PNGCrushCS under
/// LGPL-3.0-or-later. The thresholds the reference exposes as options are fixed here at the values
/// it defaults to; see <see cref="_OPEN_ONE_COLOUR_THRESHOLD"/> for why they are not a setting.
/// <para/>
/// <b>The block walk.</b> Blocks run left to right, top to bottom. At each block the coding is
/// decided in the reference's own order: a run of blocks that have not changed since the frame
/// before is written as a skip; failing that, a run of blocks flat enough to be one colour is written
/// as that colour; failing that, the block states its sixteen pixels one after another. A run stops at
/// thirty-two blocks, which is all its opcode's five count bits can say, and at the end of a block
/// row, because a multi-block opcode that crossed one is what QuickTime's own player was measured to
/// mistrack.
/// <para/>
/// <b>What is not written: the four-colour opcode.</b> <see cref="AppleVideoDecoder"/> reads both its
/// spellings and this writes neither. The reference's four-colour path is unreachable at the
/// reference's own thresholds — six lavfi sources over eighteen frames, every one of them coded
/// without a single four-colour block — and forcing it reveals why nobody noticed: the path indexes
/// its colour triple by the channel numbering the bitstream uses, blue first, and then hands it to a
/// packer that reads the same triple red first, so the block it writes has red and blue exchanged;
/// and it clips the two endpoints its least-squares fit produces to eight bits before shifting them
/// into a five-bit field, which at a raised threshold measures as a channel error of 31 out of 31.
/// A block that would have been coded that way is written as sixteen colours here instead, which is
/// exact and costs twenty-four bytes more. It is not reproduced and not reinvented: a four-colour
/// block wants a quantiser, and choosing one is a decision this encoder has no evidence to make.
/// <para/>
/// <b>Lossy, and by exactly one level.</b> Eight-bit colour is rounded to the five bits the format
/// stores, and after that a pixel moves at most one further: a block coded as sixteen colours states
/// every pixel exactly, a skipped block is written only where every pixel still equals what was coded
/// there before, and a one-colour run is opened only while every pixel of it stays within one of the
/// run's own average. So no channel of any pixel is ever more than one 32nd of full scale from the
/// picture that was handed in, and nothing accumulates over a stream — a skip is exact by
/// construction and cannot carry an error forward.
/// <para/>
/// <b>What the previous-frame buffer holds</b> is not the last picture. It is the last picture as far
/// as the blocks that were coded: a skipped block leaves the buffer alone, so the next frame's skip
/// test still measures against the block a decoder is actually holding rather than against something
/// nobody wrote. That is the reference's own choice and its reason — a slow fade otherwise skips its
/// way through every step of itself.
/// </remarks>
[VerifiedBy(ConformanceOracle.FFmpeg)]
public sealed class AppleVideoEncoder : IVideoCodecEncoder<AppleVideoEncoder> {

  /// <summary>The code a QuickTime file names this codec with.</summary>
  /// <remarks>
  /// Not the AVI spelling <c>azpr</c>, which <see cref="AppleVideoDecoder"/> also takes: the two name
  /// one bitstream and this is the one every encoder writes.
  /// </remarks>
  private static readonly CodecTag _RPZA = CodecTag.FromCharacters("rpza");

  /// <summary>The side of a coded block, in pixels.</summary>
  private const int _BLOCK = 4;

  /// <summary>The most blocks one opcode's five count bits can state.</summary>
  private const int _LONGEST_RUN = 32;

  /// <summary>The byte a chunk opens with, ahead of its three-byte length.</summary>
  private const byte _CHUNK = 0xE1;

  private const byte _SKIP_BLOCKS = 0x80;
  private const byte _SINGLE_COLOUR = 0xA0;

  /// <summary>The largest chunk length the three-byte header field can state.</summary>
  private const int _LONGEST_CHUNK = 0xFFFFFF;

  /// <summary>What a block may differ by from the one coded there before and still be skipped.</summary>
  /// <remarks>
  /// One, which is the reference's default and means the block is skipped only where every channel of
  /// every pixel is unchanged — a difference of one already fails the test. Nothing weaker would keep
  /// the guarantee that a skip carries no error forward.
  /// </remarks>
  private const int _SKIP_THRESHOLD = 1;

  /// <summary>How far a block's colours may spread from their average for a one-colour run to open.</summary>
  /// <remarks>
  /// The reference makes this and its three companions options; they are constants here. Two of the
  /// four decide how much colour the coding is allowed to lose, and the loss they permit at the
  /// default — one level of thirty-two, once, per pixel — is the only setting under which this codec
  /// can state what it costs. The other two decide whether a four-colour block is written, which
  /// nothing here writes at all.
  /// </remarks>
  private const int _OPEN_ONE_COLOUR_THRESHOLD = 1;

  /// <summary>How far the run's colours may spread from their average for a further block to join it.</summary>
  /// <remarks>
  /// Zero: a block joins an open run only while the run's average, recomputed over every pixel coded
  /// so far, still lands exactly on the extremes of all of them. Tighter than the threshold that opens
  /// the run, deliberately so — a run that has already spent its one level of slack has none left to
  /// give a block that would spend it again in the other direction.
  /// </remarks>
  private const int _CONTINUE_ONE_COLOUR_THRESHOLD = 0;

  /// <summary>The largest value a five-bit channel can hold.</summary>
  private const int _FULL_SCALE = 31;

  private readonly MediaStreamInfo _requested;
  private readonly int _width;
  private readonly int _height;
  private readonly int _blocksAcross;
  private readonly int _totalBlocks;

  /// <summary>
  /// The source pixels of every block coded so far, five bits a channel, or null before the first
  /// picture.
  /// </summary>
  private ushort[]? _previous;

  private MediaStreamInfo? _stream;
  private byte[] _buffer = new byte[4096];
  private int _length;

  /// <summary>The colours of a one-colour run so far, and the average they would be coded as.</summary>
  /// <remarks>
  /// Carried across the blocks of one run rather than recomputed per block, because the test that
  /// admits a further block is about the run entire: the average moves as blocks join, and a block
  /// that would sit inside its own average may sit outside the run's.
  /// </remarks>
  private struct _Flat {
    public int MinRed, MinGreen, MinBlue;
    public int MaxRed, MaxGreen, MaxBlue;
    public int TotalRed, TotalGreen, TotalBlue;
    public int Pixels;
    public int AverageRed, AverageGreen, AverageBlue;
  }

  private AppleVideoEncoder(MediaStreamInfo stream) {
    this._requested = stream;
    this._width = stream.Width;
    this._height = stream.Height;
    this._blocksAcross = (stream.Width + _BLOCK - 1) / _BLOCK;
    this._totalBlocks = this._blocksAcross * ((stream.Height + _BLOCK - 1) / _BLOCK);
  }

  public static string CodecName => "Apple Video (RPZA)";

  public static CodecTag Codec => _RPZA;

  /// <summary>
  /// Builds an encoder for the stream described, refusing a picture the chunk header could not state
  /// the length of.
  /// </summary>
  /// <remarks>
  /// Any size is coded, whole blocks or not: a picture that does not divide by four is coded as whole
  /// blocks over a padded grid, with the pixels past the edge written as a repeat of the last real one
  /// in a run of index bits and as black in a block of sixteen colours, which is what the reference
  /// writes and what the decoder crops back off. The one size refused is one whose worst case — every
  /// block stating its own sixteen colours — would need a chunk longer than the three bytes the
  /// format gives the length.
  /// </remarks>
  public static AppleVideoEncoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.Kind != MediaStreamKind.Video)
      throw new NotSupportedException("Apple Video can only encode a video stream.");
    if (stream.Width <= 0 || stream.Height <= 0)
      throw new NotSupportedException(
        $"An Apple Video encoder needs the picture size up front; {stream.Width}x{stream.Height} was supplied.");

    var blocks = (long)((stream.Width + _BLOCK - 1) / _BLOCK) * ((stream.Height + _BLOCK - 1) / _BLOCK);
    var worstCase = 4 + blocks * _BLOCK * _BLOCK * 2;
    if (worstCase > _LONGEST_CHUNK)
      throw new NotSupportedException(
        $"A picture of {stream.Width}x{stream.Height} can need a chunk of {worstCase} bytes, and an Apple Video chunk "
        + $"states its length in three bytes, which reaches {_LONGEST_CHUNK}. Such a stream is refused rather than "
        + "written with a length that wraps.");

    return new(stream);
  }

  /// <summary>Codes one picture against the blocks coded before it, or whole when there are none.</summary>
  /// <remarks>
  /// Always produces a packet, and flags it as a key frame when the frame skipped nothing — which is
  /// the first frame always, and any later one in which every block happened to change. A frame
  /// identical to the one before it is a run of skips over the whole picture, which is how this format
  /// spells "nothing happened".
  /// </remarks>
  public bool TryEncode(RawImage frame, long? presentationTimestamp, out CodedPacket packet) {
    ArgumentNullException.ThrowIfNull(frame);
    if (frame.Width != this._width || frame.Height != this._height)
      throw new InvalidDataException(
        $"Apple Video geometry is fixed at {this._width}x{this._height}; received {frame.Width}x{frame.Height}.");
    if (!frame.HasEnoughPixelData)
      throw new InvalidDataException(
        "The source RawImage does not contain enough pixel data for its declared format and dimensions.");

    var source = this._FiveBitColours(frame);
    this._length = 0;
    var wholePicture = this._EncodeFrame(source);

    packet = new(
      this._requested.Index,
      this._buffer.AsSpan(0, this._length).ToArray(),
      PresentationTimestamp: presentationTimestamp,
      DecodeTimestamp: presentationTimestamp,
      IsKeyFrame: wholePicture);
    return true;
  }

  /// <summary>The stream as a muxer needs it: a whole <c>rpza</c> visual sample entry.</summary>
  /// <remarks>
  /// Nothing in it is read back by <see cref="AppleVideoDecoder"/>, which needs only the picture size
  /// the container states elsewhere — the codec is defined at one depth and carries every colour it
  /// paints with inside the chunk. It is written because an <c>stsd</c> has to hold something, and what
  /// it holds is what QuickTime's own files hold: sixteen bits, seventy-two dots an inch, the
  /// compressor name Apple ships this codec under, and the value that says there is no colour table.
  /// </remarks>
  public MediaStreamInfo DescribeStream() => this._stream ??= new() {
    Index = this._requested.Index,
    Kind = MediaStreamKind.Video,
    Codec = _RPZA,
    Handler = _RPZA,
    TimeBase = this._requested.TimeBase,
    FrameRate = this._requested.FrameRate,
    DeclaredFrameCount = this._requested.DeclaredFrameCount,
    Width = this._width,
    Height = this._height,
    BitsPerPixel = 16,
    CodecPrivateData = this._SampleEntry(),
    Language = this._requested.Language,
    Name = this._requested.Name,
  };

  // ============================================================================================
  // What goes in
  // ============================================================================================

  /// <summary>The picture as one packed 5-5-5 colour a pixel, top row first.</summary>
  /// <remarks>
  /// Every picture route goes through eight-bit RGB, this codec being an RGB one; the eight bits are
  /// then rounded to five rather than shifted down, so that a full-scale channel stays full scale and
  /// the reduction is the exact inverse of the widening <see cref="AppleVideoDecoder"/> does on the
  /// way back.
  /// </remarks>
  private ushort[] _FiveBitColours(RawImage frame) {
    var rgb = frame.ToRgb24();
    var colours = new ushort[this._width * this._height];
    for (var i = 0; i < colours.Length; ++i) {
      var at = i * 3;
      colours[i] = _Pack(_ToFiveBit(rgb[at]), _ToFiveBit(rgb[at + 1]), _ToFiveBit(rgb[at + 2]));
    }

    return colours;
  }

  private static int _ToFiveBit(byte channel) => (channel * _FULL_SCALE + 127) / 255;

  private static ushort _Pack(int red, int green, int blue) => (ushort)((red << 10) | (green << 5) | blue);

  private static int _Red(ushort colour) => (colour >> 10) & _FULL_SCALE;

  private static int _Green(ushort colour) => (colour >> 5) & _FULL_SCALE;

  private static int _Blue(ushort colour) => colour & _FULL_SCALE;

  // ============================================================================================
  // The block walk
  // ============================================================================================

  /// <summary>Writes one chunk and says whether it skipped nothing.</summary>
  private bool _EncodeFrame(ushort[] source) {
    this._Put(_CHUNK);
    this._Put(0);
    this._Put(0);
    this._Put(0);

    var previous = this._previous;
    this._previous ??= new ushort[source.Length];
    var skipped = false;
    var block = 0;
    var flat = default(_Flat);

    while (block < this._totalBlocks) {
      if (previous != null) {
        var skip = this._SkippableRun(source, previous, block);
        if (skip > 0) {
          this._Put((byte)(_SKIP_BLOCKS | (skip - 1)));
          block += skip;
          skipped = true;
          continue;
        }
      }

      if (this._Admits(ref flat, source, block, opening: true)) {
        this._Keep(source, block);
        var run = 1;
        while (run < _LONGEST_RUN
               && block + run < this._totalBlocks
               && this._SameRow(block + run - 1, block + run)
               && this._Admits(ref flat, source, block + run, opening: false)) {
          this._Keep(source, block + run);
          ++run;
        }

        this._Put((byte)(_SINGLE_COLOUR | (run - 1)));
        this._PutBig16(_Pack(flat.AverageRed, flat.AverageGreen, flat.AverageBlue));
        block += run;
        continue;
      }

      this._PutSixteenColours(source, block);
      this._Keep(source, block);
      ++block;
    }

    this._buffer[1] = (byte)(this._length >> 16);
    this._buffer[2] = (byte)(this._length >> 8);
    this._buffer[3] = (byte)this._length;

    return !skipped;
  }

  /// <summary>How many blocks from <paramref name="block"/> on are unchanged and may be skipped together.</summary>
  /// <remarks>
  /// The reference stops a run by comparing the byte offsets of two consecutive blocks and breaking on
  /// a gap of more than twelve, which is a different block row for every frame stride it allocates —
  /// and its own comment says the row is what it means. The offset it compares against starts at zero
  /// and the first block of a picture sits at zero too, so the test does not bite until a run's second
  /// block, nor until the third of the run that opens a chunk. That exception is kept rather than
  /// tidied away, because at one block to a row — a picture four pixels wide or narrower — it is what
  /// decides whether the first two blocks of a chunk share a skip.
  /// </remarks>
  private int _SkippableRun(ushort[] source, ushort[] previous, int block) {
    var run = 0;
    while (run < _LONGEST_RUN && block + run < this._totalBlocks) {
      var checkRow = run > 0 && !(block == 0 && run == 1);
      if (checkRow && !this._SameRow(block + run - 1, block + run))
        break;
      if (!this._Unchanged(source, previous, block + run))
        break;

      ++run;
    }

    return run;
  }

  /// <summary>Whether a block still holds what was coded there, within the skip threshold.</summary>
  private bool _Unchanged(ushort[] source, ushort[] previous, int block) {
    var (left, top, blockWidth, blockHeight) = this._Block(block);

    for (var y = 0; y < blockHeight; ++y) {
      var row = (top + y) * this._width + left;
      for (var x = 0; x < blockWidth; ++x) {
        var was = previous[row + x];
        var now = source[row + x];
        var difference = Math.Max(
          Math.Abs(_Red(was) - _Red(now)),
          Math.Max(Math.Abs(_Green(was) - _Green(now)), Math.Abs(_Blue(was) - _Blue(now))));

        if (difference >= _SKIP_THRESHOLD)
          return false;
      }
    }

    return true;
  }

  /// <summary>
  /// Whether a one-colour run opens at, or extends over, this block — and if it does, takes the
  /// block's colours into the run.
  /// </summary>
  /// <remarks>
  /// The run is left untouched when the block does not join it, so a caller may offer a block, be
  /// refused, and go on coding it some other way with the run's statistics still describing exactly the
  /// blocks that did join.
  /// </remarks>
  private bool _Admits(ref _Flat run, ushort[] source, int block, bool opening) {
    var (left, top, blockWidth, blockHeight) = this._Block(block);
    var threshold = opening ? _OPEN_ONE_COLOUR_THRESHOLD : _CONTINUE_ONE_COLOUR_THRESHOLD;
    var candidate = opening
      ? new _Flat { MinRed = _FULL_SCALE, MinGreen = _FULL_SCALE, MinBlue = _FULL_SCALE }
      : run;

    candidate.Pixels += blockWidth * blockHeight;
    for (var y = 0; y < blockHeight; ++y) {
      var row = (top + y) * this._width + left;
      for (var x = 0; x < blockWidth; ++x) {
        var colour = source[row + x];
        var red = _Red(colour);
        var green = _Green(colour);
        var blue = _Blue(colour);

        candidate.TotalRed += red;
        candidate.TotalGreen += green;
        candidate.TotalBlue += blue;
        candidate.MinRed = Math.Min(candidate.MinRed, red);
        candidate.MinGreen = Math.Min(candidate.MinGreen, green);
        candidate.MinBlue = Math.Min(candidate.MinBlue, blue);
        candidate.MaxRed = Math.Max(candidate.MaxRed, red);
        candidate.MaxGreen = Math.Max(candidate.MaxGreen, green);
        candidate.MaxBlue = Math.Max(candidate.MaxBlue, blue);
      }
    }

    candidate.AverageRed = candidate.TotalRed / candidate.Pixels;
    candidate.AverageGreen = candidate.TotalGreen / candidate.Pixels;
    candidate.AverageBlue = candidate.TotalBlue / candidate.Pixels;

    var admitted =
      candidate.MaxRed - candidate.AverageRed <= threshold
      && candidate.MaxGreen - candidate.AverageGreen <= threshold
      && candidate.MaxBlue - candidate.AverageBlue <= threshold
      && candidate.AverageRed - candidate.MinRed <= threshold
      && candidate.AverageGreen - candidate.MinGreen <= threshold
      && candidate.AverageBlue - candidate.MinBlue <= threshold;

    if (admitted)
      run = candidate;

    return admitted;
  }

  /// <summary>Writes a block that states all sixteen of its pixels.</summary>
  /// <remarks>
  /// Its first colour is what makes it a block of sixteen rather than an opcode: the top bit of a
  /// colour word is no part of any channel and is always clear here, which is exactly the bit
  /// <see cref="AppleVideoDecoder"/> reads to tell an opcode from a colour, and the second colour's
  /// own top bit is what then tells sixteen colours from a four-colour block written inline.
  /// <para/>
  /// A block past the right or bottom edge of the picture is filled out to a whole 4x4 with black,
  /// which the decoder crops off; those pixels are in the chunk because the format has no shorter
  /// block, not because anything looks at them.
  /// </remarks>
  private void _PutSixteenColours(ushort[] source, int block) {
    var (left, top, blockWidth, blockHeight) = this._Block(block);

    for (var y = 0; y < blockHeight; ++y) {
      var row = (top + y) * this._width + left;
      for (var x = 0; x < blockWidth; ++x)
        this._PutBig16(source[row + x]);
      for (var x = blockWidth; x < _BLOCK; ++x)
        this._PutBig16(0);
    }

    for (var y = blockHeight; y < _BLOCK; ++y)
      for (var x = 0; x < _BLOCK; ++x)
        this._PutBig16(0);
  }

  /// <summary>Records a block's source pixels as what a decoder is now holding there.</summary>
  private void _Keep(ushort[] source, int block) {
    var (left, top, blockWidth, blockHeight) = this._Block(block);
    var previous = this._previous!;

    for (var y = 0; y < blockHeight; ++y) {
      var row = (top + y) * this._width + left;
      source.AsSpan(row, blockWidth).CopyTo(previous.AsSpan(row, blockWidth));
    }
  }

  /// <summary>Where a block starts and how much of it is inside the picture.</summary>
  private (int Left, int Top, int Width, int Height) _Block(int block) {
    var blockRow = block / this._blocksAcross;
    var blockColumn = block % this._blocksAcross;
    var left = blockColumn * _BLOCK;
    var top = blockRow * _BLOCK;

    return (left, top, Math.Min(_BLOCK, this._width - left), Math.Min(_BLOCK, this._height - top));
  }

  private bool _SameRow(int first, int second) => first / this._blocksAcross == second / this._blocksAcross;

  // ============================================================================================
  // The sample description
  // ============================================================================================

  private byte[] _SampleEntry() {
    const int _BODY = 78;
    const ushort _NO_COLOUR_TABLE = 0xFFFF;

    var entry = new byte[8 + _BODY];
    var span = entry.AsSpan();

    BinaryPrimitives.WriteInt32BigEndian(span, entry.Length);
    "rpza"u8.CopyTo(span[4..]);
    var body = span[8..];
    BinaryPrimitives.WriteUInt16BigEndian(body[6..], 1);                   // data reference index
    BinaryPrimitives.WriteUInt16BigEndian(body[24..], (ushort)this._width);
    BinaryPrimitives.WriteUInt16BigEndian(body[26..], (ushort)this._height);
    BinaryPrimitives.WriteUInt32BigEndian(body[28..], 0x00480000);         // 72 dpi
    BinaryPrimitives.WriteUInt32BigEndian(body[32..], 0x00480000);
    BinaryPrimitives.WriteUInt16BigEndian(body[40..], 1);                  // frames per sample
    var name = "Video"u8;
    body[42] = (byte)name.Length;
    name.CopyTo(body[43..]);
    BinaryPrimitives.WriteUInt16BigEndian(body[74..], 16);                 // depth
    BinaryPrimitives.WriteUInt16BigEndian(body[76..], _NO_COLOUR_TABLE);

    return entry;
  }

  // ============================================================================================
  // The output buffer
  // ============================================================================================

  private void _Put(byte value) {
    if (this._length == this._buffer.Length)
      Array.Resize(ref this._buffer, this._buffer.Length * 2);

    this._buffer[this._length++] = value;
  }

  private void _PutBig16(ushort value) {
    this._Put((byte)(value >> 8));
    this._Put((byte)value);
  }
}
