using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Bmp;
using FileFormat.Codecs.Cinepak;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Encodes Cinepak (<c>cvid</c>): horizontal strips, each carrying its own pair of vector codebooks,
/// with every 4x4 block coded as one codebook entry, as four, or as nothing at all.
/// </summary>
/// <remarks>
/// The bitstream is FFmpeg's <c>libavcodec/cinepakenc.c</c> — Tomas Härdin's encoder, distributed there
/// under the permissive terms in its own header inside an LGPL-2.1-or-later library, and adapted here
/// under PNGCrushCS's LGPL-3.0-or-later. Everything about the shape of the file comes from there: the
/// chunk types and their order, the strip header written with relative rather than absolute rows, the
/// interleaving of flag words with vector references, and the awkward corner where a block's two mode
/// bits straddle a flag word and its references have to wait for the one after it.
/// <para/>
/// <b>What is not taken from there is the quantiser.</b> FFmpeg reaches for its ELBG, which is seeded
/// from a pseudo-random generator, so the same picture need not encode to the same bytes twice. This
/// uses <see cref="CinepakVectorQuantiser"/> instead — farthest-point seeding and Lloyd's rule, no
/// random state — and it clusters in the colours a codebook entry paints rather than in the luminances
/// and chrominances it is written as. That second difference is what lets a picture the format can
/// state exactly come back exactly: an entry is solved by trying the decoder's own arithmetic rather
/// than by rounding the forward transform, which is a level or two out wherever a channel saturates.
/// <para/>
/// <b>The decision.</b> Every strip is priced at five codebook sizes — 1, 4, 16, 64 and 256 entries —
/// in each of the vector codings it is allowed, and the cheapest is written. The price is the squared
/// error the coding would leave over the whole strip, in RGB, plus <see cref="_BIT_COST"/> for every
/// bit the strip costs: the codebook entries actually referred to, the strip and chunk headers, and the
/// one, eight, nine, ten, thirty-three or thirty-four bits a block takes depending on its coding. Once
/// the blocks have chosen, both codebooks are built again from only the blocks that use them and the
/// strip is priced a second time, which is where a codebook stops spending entries on blocks that
/// ended up coded some other way.
/// <para/>
/// <b>Lossy, and honest about it.</b> Four luminances and one chrominance pair for sixteen pixels is
/// the format, so nothing that varies inside a 2x2 square comes back exactly. Nor does every flat
/// colour: the inverse matrix doubles the red and blue differences, so only 2669700 of the 16777216
/// colours can be stated at all — all 256 greys and all eight corners of the colour cube among them,
/// and those do come back sample for sample.
/// <para/>
/// <b>Measured against ffmpeg.</b> Thirteen sequences of 62 frames, muxed into AVIs, came back from
/// ffmpeg 9.0.1's decoder identical to this package's own decode of them, sample for sample. The
/// numbers, and how the output compares with what ffmpeg's own Cinepak encoder writes from the same
/// pictures, are in <c>CinepakVideoEncoderTests</c>.
/// <para/>
/// <b>What it refuses.</b> A picture that is not a whole number of 4x4 blocks, since the format codes
/// nothing but whole blocks; a picture size that changes part way through a stream, since the frame
/// before is what a skipped block shows; and a picture so large that one strip of it would not fit the
/// two bytes a strip's length is read from.
/// </remarks>
[VerifiedBy(ConformanceOracle.FFmpeg)]
public sealed class CinepakVideoEncoder : IVideoCodecEncoder<CinepakVideoEncoder> {

  /// <summary>The four-character code this writes, which is the spelling ffmpeg's muxers use.</summary>
  private static readonly CodecTag _CVID = CodecTag.FromCharacters("cvid");

  /// <summary>The side of a coded block, in pixels.</summary>
  private const int _BLOCK = 4;

  /// <summary>How many 2x2 quadrants a block holds, which is also how many colours an entry paints.</summary>
  private const int _QUADRANTS = 4;

  private const int _FRAME_HEADER_LENGTH = 10;
  private const int _STRIP_HEADER_LENGTH = 12;
  private const int _CHUNK_HEADER_LENGTH = 4;

  /// <summary>Set in a frame's flags when the strips carry on from the codebooks already loaded.</summary>
  private const int _INHERITS_CODEBOOKS = 0x01;

  private const int _INTRA_STRIP = 0x10;
  private const int _INTER_STRIP = 0x11;

  private const int _V4_CODEBOOK = 0x20;
  private const int _V1_CODEBOOK = 0x22;
  private const int _V4_GREY_CODEBOOK = 0x24;
  private const int _V1_GREY_CODEBOOK = 0x26;

  private const int _INTRA_VECTORS = 0x30;
  private const int _INTER_VECTORS = 0x31;
  private const int _V1_ONLY_VECTORS = 0x32;

  /// <summary>
  /// How many units of squared RGB error one bit of output is worth.
  /// </summary>
  /// <remarks>
  /// FFmpeg's rate control is the same shape — its default lambda prices a bit at two units of squared
  /// error — but its error is measured over sixteen luminances and eight chrominances where this one is
  /// measured over forty-eight colour channels, so the number is not the same number and is not
  /// pretending to be. Sixteen was chosen by measurement: it is where the output stops being both
  /// bigger than what ffmpeg's own encoder writes and further from the source, and lands where it is
  /// smaller and closer on nearly every picture tried — which is the only reading of "a good number"
  /// that does not need a preference between size and quality stated first.
  /// </remarks>
  private const int _BIT_COST = 16;

  /// <summary>The codebook sizes a strip is priced at.</summary>
  /// <remarks>
  /// Powers of four, as the reference encoder tries: a codebook four times the size costs four times as
  /// many bytes, so the sizes worth trying are spread that way rather than evenly.
  /// </remarks>
  private static readonly int[] _CodebookSizes = [1, 4, 16, 64, 256];

  /// <summary>How many blocks a strip is aimed at before the frame is cut into another one.</summary>
  /// <remarks>
  /// A strip is one codebook pair, so more blocks under one pair is fewer bytes and less detail. Four
  /// thousand also keeps the longest strip a picture can produce inside 21 kilobytes, which matters
  /// because a strip states its length in the two bytes a reader takes it from.
  /// </remarks>
  private const int _BLOCKS_A_STRIP = 4096;

  /// <summary>The most strips a frame is cut into, which is what ffmpeg's decoder holds room for.</summary>
  private const int _MAX_STRIPS = 32;

  /// <summary>The longest a strip may be, being what its length field can state.</summary>
  private const int _LONGEST_STRIP = 0xFFFF;

  /// <summary>
  /// How many frames apart whole pictures are forced: the first, then every twenty-fifth after it.
  /// </summary>
  /// <remarks>
  /// The same interval <see cref="MicrosoftVideo1Encoder"/> uses, and reset by any frame in which
  /// nothing was skipped, since such a frame is already one a decoder can start at.
  /// </remarks>
  private const int _KEY_FRAME_INTERVAL = 25;

  private readonly MediaStreamInfo _requested;
  private readonly int _width;
  private readonly int _height;
  private readonly int _blocksAcross;
  private readonly int[] _stripRows;
  private readonly CinepakVectorQuantiser _quantiser = new();

  /// <summary>The picture as the decoder will hold it once this frame is written, or null before the first.</summary>
  private byte[]? _canvas;

  private MediaStreamInfo? _stream;
  private int _sinceKeyFrame;
  private byte[] _buffer = new byte[8192];
  private int _length;

  /// <summary>How one block was decided to be coded.</summary>
  private enum _Coding {
    Skip,
    V1,
    V4,
  }

  /// <summary>Which vector chunk a strip is written as, which is what codings its blocks may take.</summary>
  private enum _Mode {
    V1Only,
    V1AndV4,
    MotionCompensated,
  }

  private CinepakVideoEncoder(MediaStreamInfo stream, int[] stripRows) {
    this._requested = stream;
    this._width = stream.Width;
    this._height = stream.Height;
    this._blocksAcross = stream.Width / _BLOCK;
    this._stripRows = stripRows;
  }

  public static string CodecName => "Cinepak";

  public static CodecTag Codec => _CVID;

  /// <summary>
  /// Builds an encoder for the stream described, refusing a size the coding has no form for.
  /// </summary>
  /// <remarks>
  /// The size has to be a whole number of blocks in both directions — the reference encoder refuses
  /// such a size too rather than padding it — and small enough that the frame cuts into strips none of
  /// which overflows the two bytes a strip's length is read from. That second limit is around four
  /// million pixels, which is well above anything this codec was ever used at.
  /// </remarks>
  public static CinepakVideoEncoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.Kind != MediaStreamKind.Video)
      throw new NotSupportedException("Cinepak can only encode a video stream.");
    if (stream.Width <= 0 || stream.Height <= 0)
      throw new NotSupportedException(
        $"A Cinepak encoder needs the picture size up front; {stream.Width}x{stream.Height} was supplied.");
    if (stream.Width % _BLOCK != 0 || stream.Height % _BLOCK != 0)
      throw new NotSupportedException(
        $"A picture of {stream.Width}x{stream.Height} is not a whole number of 4x4 blocks. Cinepak codes nothing but "
        + "whole blocks and states nowhere what a partial one covers.");

    return new(stream, _CutIntoStrips(stream.Width / _BLOCK, stream.Height / _BLOCK));
  }

  /// <summary>
  /// Codes one picture against the one before it, or whole when there is none.
  /// </summary>
  /// <remarks>
  /// Always produces a packet, and flags it as a key frame when no strip of it was written as the
  /// motion-compensated coding — that being the only coding in which a block may say nothing, and so
  /// the only one that makes a frame depend on the frame before.
  /// </remarks>
  public bool TryEncode(RawImage frame, long? presentationTimestamp, out CodedPacket packet) {
    ArgumentNullException.ThrowIfNull(frame);
    if (frame.Width != this._width || frame.Height != this._height)
      throw new InvalidDataException(
        $"Cinepak geometry is fixed at {this._width}x{this._height}; received {frame.Width}x{frame.Height}.");
    if (!frame.HasEnoughPixelData)
      throw new InvalidDataException(
        "The source RawImage does not contain enough pixel data for its declared format and dimensions.");

    var picture = frame.ToRgb24();
    var intra = this._canvas == null || this._sinceKeyFrame >= _KEY_FRAME_INTERVAL;
    this._canvas ??= new byte[this._width * this._height * 3];

    this._length = 0;
    var wholePicture = this._EncodeFrame(picture, intra);
    this._sinceKeyFrame = wholePicture ? 1 : this._sinceKeyFrame + 1;

    packet = new(
      this._requested.Index,
      this._buffer.AsSpan(0, this._length).ToArray(),
      PresentationTimestamp: presentationTimestamp,
      DecodeTimestamp: presentationTimestamp,
      IsKeyFrame: wholePicture);
    return true;
  }

  /// <summary>The stream as a muxer needs it: a <c>BITMAPINFOHEADER</c> naming this codec.</summary>
  /// <remarks>
  /// Twenty-four bits a pixel, which is what ffmpeg's AVI muxer states for this codec. Nothing in the
  /// frames depends on it — every Cinepak frame states its own size and its own coding — but a
  /// container's stream header has to say something, and this is what existing files say.
  /// </remarks>
  public MediaStreamInfo DescribeStream() {
    if (this._stream != null)
      return this._stream;

    var header = new BitmapInfoHeader(
      HeaderSize: BitmapInfoHeader.StructSize,
      Width: this._width,
      Height: this._height,
      Planes: 1,
      BitsPerPixel: 24,
      Compression: unchecked((int)_CVID.Value),
      ImageSize: 0,
      XPixelsPerMeter: 0,
      YPixelsPerMeter: 0,
      ColorsUsed: 0,
      ImportantColors: 0);

    var format = new byte[BitmapInfoHeader.StructSize];
    header.WriteTo(format);

    return this._stream = new() {
      Index = this._requested.Index,
      Kind = MediaStreamKind.Video,
      Codec = _CVID,
      Handler = _CVID,
      CodecId = "V_MS/VFW/FOURCC",
      TimeBase = this._requested.TimeBase,
      FrameRate = this._requested.FrameRate,
      DeclaredFrameCount = this._requested.DeclaredFrameCount,
      Width = this._width,
      Height = this._height,
      BitsPerPixel = 24,
      CodecPrivateData = format,
      Language = this._requested.Language,
      Name = this._requested.Name,
    };
  }

  // ============================================================================================
  // Cutting the frame up
  // ============================================================================================

  /// <summary>
  /// Where the strips of every frame begin and end, in block rows.
  /// </summary>
  /// <remarks>
  /// Fixed for the whole stream rather than searched a frame at a time. FFmpeg lets the count drift as
  /// the picture changes, which costs it a trial encode per strip count; here a boundary is where a
  /// strip stops being one codebook's worth of picture, and that does not depend on the frame.
  /// </remarks>
  private static int[] _CutIntoStrips(int blocksAcross, int blockRows) {
    var blocks = blocksAcross * blockRows;
    var wanted = Math.Max(1, (blocks + _BLOCKS_A_STRIP - 1) / _BLOCKS_A_STRIP);
    var strips = Math.Min(Math.Min(wanted, _MAX_STRIPS), blockRows);

    var rows = new int[strips + 1];
    for (var strip = 0; strip <= strips; ++strip)
      rows[strip] = strip * blockRows / strips;

    var longest = 0;
    for (var strip = 0; strip < strips; ++strip)
      longest = Math.Max(longest, (rows[strip + 1] - rows[strip]) * blocksAcross);

    var worst = _STRIP_HEADER_LENGTH + 3 * _CHUNK_HEADER_LENGTH
                + 2 * CinepakCodebook.Size * CinepakCodebook.ColourEntryLength
                + longest * _QUADRANTS + (longest * 2 + 31) / 32 * 4;
    if (worst > _LONGEST_STRIP)
      throw new NotSupportedException(
        $"A picture of {blocksAcross * _BLOCK}x{blockRows * _BLOCK} cuts into strips of {longest} blocks, which is up "
        + $"to {worst} bytes where a strip states its length in two. Cinepak has no form for a picture this large.");

    return rows;
  }

  // ============================================================================================
  // The frame
  // ============================================================================================

  /// <summary>Writes one frame and says whether every block of it was coded.</summary>
  /// <remarks>
  /// The strip identifiers say intra or inter and are written before the frame is finished, but whether
  /// the frame is whole is only known once every strip has chosen. So they are stamped again at the end
  /// rather than guessed at — which is also what the reference encoder does, and for the same reason.
  /// </remarks>
  private bool _EncodeFrame(byte[] picture, bool intra) {
    var strips = this._stripRows.Length - 1;
    var header = this._Reserve(_FRAME_HEADER_LENGTH);
    var wholePicture = true;

    for (var strip = 0; strip < strips; ++strip)
      wholePicture &= this._EncodeStrip(picture, this._stripRows[strip], this._stripRows[strip + 1], intra);

    this._buffer[header] = (byte)(wholePicture ? 0 : _INHERITS_CODEBOOKS);
    this._PatchUInt24(header + 1, this._length);
    this._PatchUInt16(header + 4, this._width);
    this._PatchUInt16(header + 6, this._height);
    this._PatchUInt16(header + 8, strips);

    for (int strip = 0, at = _FRAME_HEADER_LENGTH; strip < strips; ++strip) {
      this._buffer[at] = (byte)(wholePicture ? _INTRA_STRIP : _INTER_STRIP);
      at += (this._buffer[at + 1] << 16) | (this._buffer[at + 2] << 8) | this._buffer[at + 3];
    }

    return wholePicture;
  }

  // ============================================================================================
  // One strip
  // ============================================================================================

  /// <summary>Prices one strip in every coding at every codebook size, and writes the cheapest.</summary>
  /// <returns>Whether the coding chosen leaves nothing to the frame before.</returns>
  private bool _EncodeStrip(byte[] picture, int firstRow, int lastRow, bool intra) {
    var blocks = (lastRow - firstRow) * this._blocksAcross;
    var v1Training = this._V1Training(picture, firstRow, blocks);
    var v4Training = this._V4Training(picture, firstRow, blocks);
    var skipError = intra ? null : this._SkipErrors(picture, firstRow, blocks);

    var v1Levels = this._BuildLevels(v1Training, v1Training, blocks, true, picture, firstRow);
    var v4Levels = this._BuildLevels(v4Training, v4Training, blocks, false, picture, firstRow);
    var none = new _Level(0, blocks, 1);

    var chosen = new _Decision(blocks);
    var trial = new _Decision(blocks);
    var best = long.MaxValue;

    foreach (var v1 in v1Levels) {
      best = this._Consider(_Mode.V1Only, v1, none, skipError, blocks, trial, chosen, best);
      if (skipError != null)
        best = this._Consider(_Mode.MotionCompensated, v1, none, skipError, blocks, trial, chosen, best);

      foreach (var v4 in v4Levels) {
        best = this._Consider(_Mode.V1AndV4, v1, v4, skipError, blocks, trial, chosen, best);
        if (skipError != null)
          best = this._Consider(_Mode.MotionCompensated, v1, v4, skipError, blocks, trial, chosen, best);
      }
    }

    this._Retrain(picture, firstRow, blocks, v1Training, v4Training, skipError, chosen, trial, best);
    this._WriteStrip(firstRow, lastRow, blocks, chosen);
    return chosen.Mode != _Mode.MotionCompensated;
  }

  /// <summary>
  /// Builds both codebooks again from only the blocks that ended up using them, and keeps the result if
  /// it prices better.
  /// </summary>
  /// <remarks>
  /// The first codebooks were trained on every block of the strip, including the ones that went on to be
  /// coded some other way or skipped entirely; entries spent on those are entries the blocks that remain
  /// do not get. Training again on what is left is the reference encoder's move too.
  /// <para/>
  /// The comparison is kept because it can lose. A smaller training set is a codebook that fits it
  /// better, but the blocks are then free to choose again, and once in a while what comes out is dearer
  /// than what went in.
  /// </remarks>
  private void _Retrain(
    byte[] picture, int firstRow, int blocks, byte[] v1Training, byte[] v4Training,
    int[]? skipError, _Decision chosen, _Decision trial, long best) {
    const int width = CinepakVectorQuantiser.Dimensions;
    var v1Subset = new byte[blocks * width];
    var v4Subset = new byte[blocks * _QUADRANTS * width];
    var v1Count = 0;
    var v4Count = 0;

    for (var block = 0; block < blocks; ++block)
      switch (chosen.Coding[block]) {
        case _Coding.V1:
          Array.Copy(v1Training, block * width, v1Subset, v1Count++ * width, width);
          break;
        case _Coding.V4:
          Array.Copy(v4Training, block * _QUADRANTS * width, v4Subset, v4Count * width, _QUADRANTS * width);
          v4Count += _QUADRANTS;
          break;
      }

    if (v1Count == 0 && v4Count == 0)
      return;

    var v1 = this._BuildLevel(v1Subset, v1Count, chosen.V1.Size, v1Training, blocks, true, picture, firstRow);
    var v4 = this._BuildLevel(v4Subset, v4Count, chosen.V4.Size, v4Training, blocks, false, picture, firstRow);
    var refined = new _Decision(blocks);
    if (this._Consider(chosen.Mode, v1, v4, skipError, blocks, trial, refined, best) < best)
      refined.CopyTo(chosen);
  }

  // ============================================================================================
  // What the quantiser is fed
  // ============================================================================================

  /// <summary>
  /// One training vector a block, being the four colours its 2x2 quadrants average to.
  /// </summary>
  /// <remarks>
  /// The quadrant means and not the pixels, because a V1 entry paints one colour over each whole
  /// quadrant and the mean is the colour that costs least there whatever else the entry does. What the
  /// block actually costs is measured against the pixels afterwards; this is only what the clustering
  /// sees, and the two orderings agree because they differ by the variance inside the quadrants, which
  /// no choice of entry can change.
  /// </remarks>
  private byte[] _V1Training(byte[] picture, int firstRow, int blocks) {
    var training = new byte[blocks * CinepakVectorQuantiser.Dimensions];
    Span<int> sums = stackalloc int[3];

    for (var block = 0; block < blocks; ++block) {
      var (left, top) = this._Corner(firstRow, block);
      for (var quadrant = 0; quadrant < _QUADRANTS; ++quadrant) {
        sums.Clear();
        for (var pixel = 0; pixel < 4; ++pixel) {
          var at = this._Sample(left, top, quadrant, pixel);
          for (var channel = 0; channel < 3; ++channel)
            sums[channel] += picture[at + channel];
        }

        var into = block * CinepakVectorQuantiser.Dimensions + quadrant * 3;
        for (var channel = 0; channel < 3; ++channel)
          training[into + channel] = (byte)((sums[channel] + 2) / 4);
      }
    }

    return training;
  }

  /// <summary>Four training vectors a block, being the pixels of each 2x2 quadrant as they are.</summary>
  private byte[] _V4Training(byte[] picture, int firstRow, int blocks) {
    var training = new byte[blocks * _QUADRANTS * CinepakVectorQuantiser.Dimensions];

    for (var block = 0; block < blocks; ++block) {
      var (left, top) = this._Corner(firstRow, block);
      for (var quadrant = 0; quadrant < _QUADRANTS; ++quadrant) {
        var into = (block * _QUADRANTS + quadrant) * CinepakVectorQuantiser.Dimensions;
        for (var pixel = 0; pixel < 4; ++pixel) {
          var at = this._Sample(left, top, quadrant, pixel);
          for (var channel = 0; channel < 3; ++channel)
            training[into + pixel * 3 + channel] = picture[at + channel];
        }
      }
    }

    return training;
  }

  /// <summary>What each block costs if it says nothing and the frame before is left showing.</summary>
  private int[] _SkipErrors(byte[] picture, int firstRow, int blocks) {
    var errors = new int[blocks];
    var canvas = this._canvas!;

    for (var block = 0; block < blocks; ++block) {
      var (left, top) = this._Corner(firstRow, block);
      var total = 0;
      for (var row = 0; row < _BLOCK; ++row) {
        var at = ((top + row) * this._width + left) * 3;
        for (var channel = 0; channel < _BLOCK * 3; ++channel) {
          var difference = canvas[at + channel] - picture[at + channel];
          total += difference * difference;
        }
      }

      errors[block] = total;
    }

    return errors;
  }

  // ============================================================================================
  // The codebooks
  // ============================================================================================

  /// <summary>One codebook at one size, with what it costs every block of the strip.</summary>
  private sealed class _Level {

    internal readonly int Size;
    internal readonly byte[] Entries;
    internal readonly byte[] Painted;

    /// <summary>Which entry each block refers to: one a block for V1, one a quadrant for V4.</summary>
    internal readonly int[] Reference;

    /// <summary>What each block costs under this codebook, in squared error over its sixteen pixels.</summary>
    internal readonly int[] Error;

    internal _Level(int size, int blocks, int referencesABlock) {
      this.Size = size;
      this.Entries = new byte[Math.Max(size, 1) * CinepakEntrySolver.EntryLength];
      this.Painted = new byte[Math.Max(size, 1) * CinepakVectorQuantiser.Dimensions];
      this.Reference = new int[blocks * referencesABlock];
      this.Error = new int[blocks];
    }
  }

  /// <summary>
  /// Builds a codebook at every size worth trying, stopping where the strip runs out of distinct blocks.
  /// </summary>
  private _Level[] _BuildLevels(byte[] training, byte[] wanted, int blocks, bool v1, byte[] picture, int firstRow) {
    var count = v1 ? blocks : blocks * _QUADRANTS;
    var levels = new List<_Level>(_CodebookSizes.Length);

    foreach (var size in _CodebookSizes) {
      var level = this._BuildLevel(training, count, size, wanted, blocks, v1, picture, firstRow);
      if (level.Size > 0)
        levels.Add(level);

      // The quantiser stops at the number of distinct vectors, so once a size comes back short there is
      // nothing a bigger one could add — and neither is there once a size already costs nothing, which
      // is what a flat or few-colour strip reaches after one or two of them.
      if (level.Size < size || _Costless(level, blocks))
        break;
    }

    return levels.ToArray();
  }

  private static bool _Costless(_Level level, int blocks) {
    for (var block = 0; block < blocks; ++block)
      if (level.Error[block] != 0)
        return false;

    return true;
  }

  /// <summary>
  /// Builds one codebook out of a training set and works out what every block of the strip costs under
  /// it.
  /// </summary>
  /// <remarks>
  /// What the codebook is built from and what it is then measured against are two arguments and not
  /// one: the training set may be a subset of the strip — only the blocks that ended up using this
  /// codebook — while every block of the strip still has to be told what it would cost.
  /// </remarks>
  private _Level _BuildLevel(
    byte[] training, int count, int size, byte[] wanted, int blocks, bool v1, byte[] picture, int firstRow) {
    const int width = CinepakVectorQuantiser.Dimensions;
    var references = v1 ? 1 : _QUADRANTS;
    if (size <= 0 || count <= 0)
      return new(0, blocks, references);

    var box = new _Level(size, blocks, references);
    var built = this._quantiser.Build(training, count, size, box.Entries, box.Painted);
    var level = built == size ? box : new _Level(built, blocks, references);
    if (built != size) {
      Array.Copy(box.Entries, level.Entries, built * CinepakEntrySolver.EntryLength);
      Array.Copy(box.Painted, level.Painted, built * width);
    }

    for (var block = 0; block < blocks; ++block) {
      if (v1) {
        var entry = CinepakVectorQuantiser.Nearest(level.Painted, level.Size, wanted.AsSpan(block * width, width), out _);
        level.Reference[block] = entry;
        level.Error[block] = this._V1Error(picture, firstRow, block, level.Painted, entry);
        continue;
      }

      var total = 0;
      for (var quadrant = 0; quadrant < _QUADRANTS; ++quadrant) {
        var at = (block * _QUADRANTS + quadrant) * width;
        level.Reference[block * _QUADRANTS + quadrant] =
          CinepakVectorQuantiser.Nearest(level.Painted, level.Size, wanted.AsSpan(at, width), out var error);
        total += error;
      }

      level.Error[block] = total;
    }

    return level;
  }

  /// <summary>What a block costs when one V1 entry's four colours are stretched over its quadrants.</summary>
  /// <remarks>
  /// Measured against the pixels and not against the quadrant means the clustering used, because the
  /// mode decision weighs this against a V4 coding measured over the same sixteen pixels and against a
  /// skip measured over them too. The difference is the variance inside the quadrants, which is the same
  /// whichever entry is chosen but is not the same as nought.
  /// </remarks>
  private int _V1Error(byte[] picture, int firstRow, int block, byte[] painted, int entry) {
    var (left, top) = this._Corner(firstRow, block);
    var total = 0;

    for (var quadrant = 0; quadrant < _QUADRANTS; ++quadrant) {
      var colour = entry * CinepakVectorQuantiser.Dimensions + quadrant * 3;
      for (var pixel = 0; pixel < 4; ++pixel) {
        var at = this._Sample(left, top, quadrant, pixel);
        for (var channel = 0; channel < 3; ++channel) {
          var difference = painted[colour + channel] - picture[at + channel];
          total += difference * difference;
        }
      }
    }

    return total;
  }

  // ============================================================================================
  // Deciding the strip
  // ============================================================================================

  /// <summary>One candidate coding of a strip: which codebooks, and which of them each block takes.</summary>
  private sealed class _Decision {

    internal _Mode Mode;
    internal _Level V1;
    internal _Level V4;
    internal readonly _Coding[] Coding;
    internal readonly int[] V1Reference;
    internal readonly int[] V4Reference;

    internal _Decision(int blocks) {
      this.V1 = new(0, 0, 1);
      this.V4 = new(0, 0, 1);
      this.Coding = new _Coding[blocks];
      this.V1Reference = new int[blocks];
      this.V4Reference = new int[blocks * _QUADRANTS];
    }

    internal void CopyTo(_Decision other) {
      other.Mode = this.Mode;
      other.V1 = this.V1;
      other.V4 = this.V4;
      Array.Copy(this.Coding, other.Coding, this.Coding.Length);
      Array.Copy(this.V1Reference, other.V1Reference, this.V1Reference.Length);
      Array.Copy(this.V4Reference, other.V4Reference, this.V4Reference.Length);
    }
  }

  /// <summary>
  /// Prices one mode at one pair of codebook sizes, and keeps it if it is the cheapest so far.
  /// </summary>
  /// <remarks>
  /// The price is the squared error the strip would carry plus <see cref="_BIT_COST"/> a bit, and the
  /// bits are all of them: the strip header, three chunk headers, the codebook entries the blocks
  /// actually refer to once renumbered, and what each block's own coding costs. Counting the entries
  /// referred to rather than the entries built is what lets a codebook of 256 win on a strip that turns
  /// out to need thirty of them — the size is a ceiling on the search, not a bill.
  /// </remarks>
  private long _Consider(
    _Mode mode, _Level v1, _Level v4, int[]? skipError, int blocks,
    _Decision trial, _Decision chosen, long best) {
    if (v1.Size == 0 && mode != _Mode.MotionCompensated)
      return best;
    if (mode == _Mode.V1AndV4 && v4.Size == 0)
      return best;
    if (mode == _Mode.MotionCompensated && skipError == null)
      return best;

    trial.Mode = mode;
    trial.V1 = v1;
    trial.V4 = v4;

    long error = 0;
    long bits = 8 * (_STRIP_HEADER_LENGTH + 3 * _CHUNK_HEADER_LENGTH);
    Span<bool> usedV1 = stackalloc bool[CinepakCodebook.Size];
    Span<bool> usedV4 = stackalloc bool[CinepakCodebook.Size];

    for (var block = 0; block < blocks; ++block) {
      var coding = _Choose(mode, v1, v4, skipError, block);
      trial.Coding[block] = coding;

      switch (coding) {
        case _Coding.Skip:
          error += skipError![block];
          ++bits;
          break;
        case _Coding.V1:
          error += v1.Error[block];
          bits += mode switch { _Mode.V1Only => 8, _Mode.V1AndV4 => 9, _ => 10 };
          trial.V1Reference[block] = v1.Reference[block];
          usedV1[v1.Reference[block]] = true;
          break;
        default:
          error += v4.Error[block];
          bits += mode == _Mode.V1AndV4 ? 33 : 34;
          for (var quadrant = 0; quadrant < _QUADRANTS; ++quadrant) {
            var entry = v4.Reference[block * _QUADRANTS + quadrant];
            trial.V4Reference[block * _QUADRANTS + quadrant] = entry;
            usedV4[entry] = true;
          }

          break;
      }
    }

    bits += 8 * CinepakCodebook.ColourEntryLength * (_Count(usedV1) + _Count(usedV4));

    var score = error + _BIT_COST * bits;
    if (score >= best)
      return best;

    trial.CopyTo(chosen);
    return score;
  }

  /// <summary>The cheapest coding for one block under one mode.</summary>
  private static _Coding _Choose(_Mode mode, _Level v1, _Level v4, int[]? skipError, int block) {
    if (mode == _Mode.V1Only)
      return _Coding.V1;

    var chosen = _Coding.V1;
    var score = v1.Size > 0
      ? v1.Error[block] + (long)_BIT_COST * (mode == _Mode.V1AndV4 ? 9 : 10)
      : long.MaxValue;

    if (v4.Size > 0) {
      var alternative = v4.Error[block] + (long)_BIT_COST * (mode == _Mode.V1AndV4 ? 33 : 34);
      if (alternative < score) {
        score = alternative;
        chosen = _Coding.V4;
      }
    }

    if (mode != _Mode.MotionCompensated)
      return chosen;

    return skipError![block] + (long)_BIT_COST <= score ? _Coding.Skip : chosen;
  }

  private static int _Count(ReadOnlySpan<bool> used) {
    var count = 0;
    foreach (var entry in used)
      if (entry)
        ++count;

    return count;
  }

  // ============================================================================================
  // Writing the strip
  // ============================================================================================

  /// <summary>Writes one strip's codebooks and vectors, and paints what it says onto the canvas.</summary>
  /// <remarks>
  /// The codebooks are renumbered on the way out: entries are given their new numbers in the order the
  /// blocks first ask for them, so what is written is a run from nought with no gaps in it. A codebook
  /// chunk states its entries from nought and says how many follow, and there is no way in the format
  /// to write entry 200 without writing the 200 before it.
  /// </remarks>
  private void _WriteStrip(int firstRow, int lastRow, int blocks, _Decision decision) {
    Span<int> v1Map = stackalloc int[CinepakCodebook.Size];
    Span<int> v4Map = stackalloc int[CinepakCodebook.Size];
    v1Map.Fill(-1);
    v4Map.Fill(-1);

    var v1Used = 0;
    var v4Used = 0;
    for (var block = 0; block < blocks; ++block)
      switch (decision.Coding[block]) {
        case _Coding.V1:
          if (v1Map[decision.V1Reference[block]] < 0)
            v1Map[decision.V1Reference[block]] = v1Used++;

          break;
        case _Coding.V4:
          for (var quadrant = 0; quadrant < _QUADRANTS; ++quadrant) {
            var entry = decision.V4Reference[block * _QUADRANTS + quadrant];
            if (v4Map[entry] < 0)
              v4Map[entry] = v4Used++;
          }

          break;
      }

    var header = this._Reserve(_STRIP_HEADER_LENGTH);
    this._buffer[header] = _INTRA_STRIP;
    this._PatchUInt16(header + 8, (lastRow - firstRow) * _BLOCK);
    this._PatchUInt16(header + 10, this._width);

    // The V4 codebook first and the V1 one after it, and both written even when empty. That order and
    // that redundancy are what vintage MacOS decoders were built around, and an empty one costs four
    // bytes.
    this._WriteCodebook(decision.V4.Entries, v4Map, v4Used, _V4_CODEBOOK, _V4_GREY_CODEBOOK);
    this._WriteCodebook(decision.V1.Entries, v1Map, v1Used, _V1_CODEBOOK, _V1_GREY_CODEBOOK);

    switch (decision.Mode) {
      case _Mode.V1Only: this._WriteV1OnlyVectors(decision, v1Map, blocks); break;
      case _Mode.V1AndV4: this._WriteIntraVectors(decision, v1Map, v4Map, blocks); break;
      default: this._WriteInterVectors(decision, v1Map, v4Map, blocks); break;
    }

    this._PatchUInt24(header + 1, this._length - header);
    this._Paint(firstRow, blocks, decision);
  }

  /// <summary>
  /// Writes one codebook chunk, in the short form where every entry it holds is grey.
  /// </summary>
  /// <remarks>
  /// The grey chunk types carry four bytes an entry instead of six and mean both chrominances are
  /// nought, which is exactly what a codebook of greys holds — a third of the codebook saved on a
  /// greyscale picture, and nothing given up, since the entries said nought either way.
  /// </remarks>
  private void _WriteCodebook(byte[] entries, ReadOnlySpan<int> map, int used, int colour, int grey) {
    var monochrome = used > 0;
    for (var entry = 0; entry < map.Length && monochrome; ++entry)
      if (map[entry] >= 0 && (entries[entry * CinepakEntrySolver.EntryLength + 4] != 0
                              || entries[entry * CinepakEntrySolver.EntryLength + 5] != 0))
        monochrome = false;

    var stride = monochrome ? CinepakCodebook.GreyEntryLength : CinepakCodebook.ColourEntryLength;
    var header = this._Reserve(_CHUNK_HEADER_LENGTH);
    this._buffer[header] = (byte)(monochrome ? grey : colour);

    var body = this._Reserve(used * stride);
    for (var entry = 0; entry < map.Length; ++entry)
      if (map[entry] >= 0)
        Array.Copy(entries, entry * CinepakEntrySolver.EntryLength, this._buffer, body + map[entry] * stride, stride);

    this._PatchUInt24(header + 1, this._length - header);
  }

  /// <summary>Every block coded from the V1 codebook, one byte each and no flags at all.</summary>
  private void _WriteV1OnlyVectors(_Decision decision, ReadOnlySpan<int> v1Map, int blocks) {
    var header = this._Reserve(_CHUNK_HEADER_LENGTH);
    this._buffer[header] = _V1_ONLY_VECTORS;

    for (var block = 0; block < blocks; ++block)
      this._Put((byte)v1Map[decision.V1Reference[block]]);

    this._PatchUInt24(header + 1, this._length - header);
  }

  /// <summary>Every block coded, one flag bit each: set for V4, clear for V1.</summary>
  private void _WriteIntraVectors(_Decision decision, ReadOnlySpan<int> v1Map, ReadOnlySpan<int> v4Map, int blocks) {
    var header = this._Reserve(_CHUNK_HEADER_LENGTH);
    this._buffer[header] = _INTRA_VECTORS;

    for (var first = 0; first < blocks; first += 32) {
      var last = Math.Min(first + 32, blocks);
      var flags = 0u;
      for (var block = first; block < last; ++block)
        if (decision.Coding[block] == _Coding.V4)
          flags |= 1u << (31 - block + first);

      this._PutUInt32(flags);
      for (var block = first; block < last; ++block)
        if (decision.Coding[block] == _Coding.V4)
          for (var quadrant = 0; quadrant < _QUADRANTS; ++quadrant)
            this._Put((byte)v4Map[decision.V4Reference[block * _QUADRANTS + quadrant]]);
        else
          this._Put((byte)v1Map[decision.V1Reference[block]]);
    }

    this._PatchUInt24(header + 1, this._length - header);
  }

  /// <summary>
  /// A block is skipped, V1 or V4, coded as one flag bit or two.
  /// </summary>
  /// <remarks>
  /// The flag words and the vector references share one stream: a word covers as many blocks as its
  /// thirty-two bits reach, and those blocks' references follow it. The corner is a block whose second
  /// bit does not fit — its first bit closes one word and its second opens the next, so its references
  /// belong after that next word rather than with the ones before it, and are held back while
  /// everything already accounted for goes out. This is the reference encoder's own arrangement, and
  /// there is no other a decoder refilling its flag register only when it runs dry would read the same
  /// way.
  /// </remarks>
  private void _WriteInterVectors(_Decision decision, ReadOnlySpan<int> v1Map, ReadOnlySpan<int> v4Map, int blocks) {
    var header = this._Reserve(_CHUNK_HEADER_LENGTH);
    this._buffer[header] = _INTER_VECTORS;

    Span<byte> held = stackalloc byte[64];
    var heldLength = 0;
    var flags = 0u;
    var bits = 0;

    for (var block = 0; block < blocks; ++block) {
      var coding = decision.Coding[block];
      if (coding != _Coding.Skip)
        flags |= 1u << (31 - bits);

      ++bits;
      var straddles = false;
      var writeAfter = false;

      if (coding != _Coding.Skip) {
        if (bits < 32) {
          if (coding == _Coding.V4)
            flags |= 1u << (31 - bits);

          ++bits;
        } else
          straddles = true;
      }

      if (bits == 32) {
        this._PutUInt32(flags);
        flags = 0;
        bits = 0;

        if (coding == _Coding.Skip || straddles) {
          this._Put(held[..heldLength]);
          heldLength = 0;
        } else
          writeAfter = true;
      }

      if (straddles) {
        flags = coding == _Coding.V4 ? 1u << 31 : 0u;
        bits = 1;
      }

      switch (coding) {
        case _Coding.V1:
          held[heldLength++] = (byte)v1Map[decision.V1Reference[block]];
          break;
        case _Coding.V4:
          for (var quadrant = 0; quadrant < _QUADRANTS; ++quadrant)
            held[heldLength++] = (byte)v4Map[decision.V4Reference[block * _QUADRANTS + quadrant]];

          break;
      }

      if (!writeAfter)
        continue;

      this._Put(held[..heldLength]);
      heldLength = 0;
    }

    if (bits > 0) {
      this._PutUInt32(flags);
      this._Put(held[..heldLength]);
    }

    this._PatchUInt24(header + 1, this._length - header);
  }

  /// <summary>Paints what the strip says onto the canvas the next frame predicts from.</summary>
  private void _Paint(int firstRow, int blocks, _Decision decision) {
    var canvas = this._canvas!;

    for (var block = 0; block < blocks; ++block) {
      var coding = decision.Coding[block];
      if (coding == _Coding.Skip)
        continue;

      var v1 = coding == _Coding.V1;
      var painted = v1 ? decision.V1.Painted : decision.V4.Painted;
      var (left, top) = this._Corner(firstRow, block);

      for (var quadrant = 0; quadrant < _QUADRANTS; ++quadrant) {
        var entry = v1 ? decision.V1Reference[block] : decision.V4Reference[block * _QUADRANTS + quadrant];
        for (var pixel = 0; pixel < 4; ++pixel) {
          var colour = entry * CinepakVectorQuantiser.Dimensions + (v1 ? quadrant : pixel) * 3;
          var at = this._Sample(left, top, quadrant, pixel);
          for (var channel = 0; channel < 3; ++channel)
            canvas[at + channel] = painted[colour + channel];
        }
      }
    }
  }

  // ============================================================================================
  // Where a pixel is
  // ============================================================================================

  /// <summary>Where a block's top-left pixel is, blocks running left to right and top to bottom.</summary>
  private (int Left, int Top) _Corner(int firstRow, int block)
    => (block % this._blocksAcross * _BLOCK, (firstRow + block / this._blocksAcross) * _BLOCK);

  /// <summary>
  /// Where in the picture one pixel of one 2x2 quadrant of a block sits, as an offset into RGB samples.
  /// </summary>
  /// <remarks>
  /// The quadrants run in reading order and so do the pixels inside them, which is the order both
  /// codings state their colours in: a V1 entry's four samples are its four quadrants, and a V4 entry's
  /// four samples are the four pixels of one.
  /// </remarks>
  private int _Sample(int left, int top, int quadrant, int pixel)
    => ((top + quadrant / 2 * 2 + pixel / 2) * this._width + left + quadrant % 2 * 2 + pixel % 2) * 3;

  // ============================================================================================
  // The output buffer
  // ============================================================================================

  /// <summary>Makes room for a header that has to be filled in once what follows it is written.</summary>
  private int _Reserve(int bytes) {
    this._Grow(bytes);
    var at = this._length;
    this._buffer.AsSpan(at, bytes).Clear();
    this._length = at + bytes;
    return at;
  }

  private void _Put(byte value) {
    this._Grow(1);
    this._buffer[this._length++] = value;
  }

  private void _Put(ReadOnlySpan<byte> values) {
    this._Grow(values.Length);
    values.CopyTo(this._buffer.AsSpan(this._length));
    this._length += values.Length;
  }

  private void _PutUInt32(uint value) {
    this._Grow(4);
    this._buffer[this._length++] = (byte)(value >> 24);
    this._buffer[this._length++] = (byte)(value >> 16);
    this._buffer[this._length++] = (byte)(value >> 8);
    this._buffer[this._length++] = (byte)value;
  }

  private void _PatchUInt16(int at, int value) {
    this._buffer[at] = (byte)(value >> 8);
    this._buffer[at + 1] = (byte)value;
  }

  private void _PatchUInt24(int at, int value) {
    this._buffer[at] = (byte)(value >> 16);
    this._buffer[at + 1] = (byte)(value >> 8);
    this._buffer[at + 2] = (byte)value;
  }

  private void _Grow(int bytes) {
    if (this._length + bytes <= this._buffer.Length)
      return;

    var wanted = this._buffer.Length;
    while (wanted < this._length + bytes)
      wanted *= 2;

    Array.Resize(ref this._buffer, wanted);
  }
}
