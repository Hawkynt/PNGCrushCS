using System;
using System.IO;
using System.IO.Compression;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Encodes Zip Motion Blocks Video (ZMBV), the screen-capture codec DOSBox writes: every picture cut
/// into 16x16 blocks, each copied from wherever in the frame before a short motion search found it
/// best and corrected with an XOR where the copy is not exact, the whole of it riding one zlib
/// stream that only an intraframe restarts.
/// </summary>
/// <remarks>
/// Adapted from FFmpeg's <c>libavcodec/zmbvenc.c</c>, copyright (c) 2006 Konstantin Shishkov,
/// LGPL-2.1-or-later; this adaptation is distributed with PNGCrushCS under LGPL-3.0-or-later.
/// <para/>
/// <b>Which pixel layout is written</b> is decided once, from the <see cref="MediaStreamInfo.BitsPerPixel"/>
/// the stream was requested with, because the layout is stated in every intraframe and cannot change
/// between them: 8 codes format 4, palettised, and takes <see cref="PixelFormat.Indexed8"/> pictures
/// only; 16 codes format 6, 5-6-5, and takes <see cref="PixelFormat.Rgb565"/> pictures only; 32 — or
/// nothing stated — codes format 8, four bytes B, G, R and one the format leaves undefined, and
/// takes any picture that converts to eight-bit colour without changing a sample, with the fourth
/// byte carried through verbatim where the source had one and every other decoder free to ignore it.
/// Any other bit count, and any picture that would have to be quantised or palettised to fit the
/// stream's layout, is refused by name rather than approximated: this is a lossless codec. The
/// format's 15-bit layout is not offered because no <see cref="RawImage"/> layout holds 5-5-5 samples
/// without widening them first, and 24-bit is not because no decoder in existence reads it.
/// <para/>
/// <b>What a packet holds.</b> The first frame and every twenty-fifth after it — FFmpeg's default
/// minimum key interval — is an intraframe: a seven-byte header naming version 0.1, zlib, the
/// layout and the block size, then the compressed 768-byte palette where there is one and the
/// picture top row first. Every other frame is an interframe: one flags byte, then compressed, an
/// XOR of the palette against the last one where it changed, a two-byte entry per block in raster
/// order — the motion vector doubled with the XOR bit in the low bit of the first byte — padded to
/// a multiple of four, and then the XOR correction of every block whose entry says it has one.
/// <para/>
/// <b>The motion search is FFmpeg's</b>, scored the way FFmpeg scores it: the zero vector first,
/// the previous block's vector next, then every offset within eight pixels, each candidate's XOR
/// against the picture rated by the entropy of its byte histogram so the vector whose correction
/// compresses best wins and an exact match ends the search at once. A source pixel the vector
/// places outside the picture is zero, which is what the decoder substitutes for it.
/// <para/>
/// <b>The zlib stream crosses packets.</b> One compressor is opened at each intraframe and kept for
/// every interframe after it, each packet ending on a sync-flush boundary so the decoder can produce
/// that packet's frame from that packet's bytes alone while the dictionary carries on across them —
/// the stateful half of this format that <see cref="Zmbv.ZmbvInflater"/> describes from the other side.
/// </remarks>
public sealed class ZmbvVideoEncoder : IVideoCodecEncoder<ZmbvVideoEncoder> {

  private static readonly CodecTag _Tag = CodecTag.FromCharacters("ZMBV");

  private const int _BLOCK_SIZE = 16;
  private const int _SEARCH_RANGE = 8;
  private const int _PALETTE_BYTES = 768;
  private const int _PALETTE_ENTRIES = 256;

  /// <summary>How many frames apart intraframes are forced, FFmpeg's default minimum key interval.</summary>
  private const int _KEY_FRAME_INTERVAL = 25;

  private const byte _FLAG_KEY_FRAME = 1;
  private const byte _FLAG_PALETTE_DELTA = 2;
  private const byte _FORMAT_8BPP = 4;
  private const byte _FORMAT_16BPP = 6;
  private const byte _FORMAT_32BPP = 8;

  private readonly MediaStreamInfo _stream;
  private readonly int _width;
  private readonly int _height;
  private readonly int _blocksX;
  private readonly int _blocksY;
  private readonly byte _format;
  private readonly int _bytesPerPixel;
  private readonly PixelFormat _codedFormat;
  private readonly int[] _scoreTable;
  private readonly byte[] _palette = new byte[_PALETTE_BYTES];

  /// <summary>The previous picture as coded — top row first, <see cref="_bytesPerPixel"/> a pixel —
  /// or <c>null</c> before the first frame, which is what makes that frame an intraframe.</summary>
  private byte[]? _previous;
  private int _framesSinceKeyFrame;

  private MemoryStream _compressed = new();
  private ZLibStream? _deflater;

  private ZmbvVideoEncoder(MediaStreamInfo stream, byte format, int bytesPerPixel, PixelFormat codedFormat) {
    this._width = stream.Width;
    this._height = stream.Height;
    this._blocksX = (stream.Width + _BLOCK_SIZE - 1) / _BLOCK_SIZE;
    this._blocksY = (stream.Height + _BLOCK_SIZE - 1) / _BLOCK_SIZE;
    this._format = format;
    this._bytesPerPixel = bytesPerPixel;
    this._codedFormat = codedFormat;
    this._scoreTable = _ScoreTable(_BLOCK_SIZE * _BLOCK_SIZE * bytesPerPixel);
    this._stream = new() {
      Index = stream.Index,
      Kind = MediaStreamKind.Video,
      Codec = _Tag,
      Handler = _Tag,
      TimeBase = stream.TimeBase,
      FrameRate = stream.FrameRate,
      DeclaredFrameCount = stream.DeclaredFrameCount,
      Width = stream.Width,
      Height = stream.Height,
      BitsPerPixel = bytesPerPixel * 8,
      Language = stream.Language,
      Name = stream.Name,
    };
  }

  public static string CodecName => "Zip Motion Blocks Video";

  public static CodecTag Codec => _Tag;

  public static ZmbvVideoEncoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.Kind != MediaStreamKind.Video)
      throw new NotSupportedException("Zip Motion Blocks Video can only encode a video stream.");
    if (stream.Width <= 0 || stream.Height <= 0)
      throw new NotSupportedException(
        $"A Zip Motion Blocks Video encoder needs positive picture dimensions before the muxer is created; {stream.Width}x{stream.Height} was supplied.");

    return stream.BitsPerPixel switch {
      8 => new(stream, _FORMAT_8BPP, 1, PixelFormat.Indexed8),
      16 => new(stream, _FORMAT_16BPP, 2, PixelFormat.Rgb565),
      0 or 32 => new(stream, _FORMAT_32BPP, 4, PixelFormat.Bgra32),
      var bits => throw new NotSupportedException(
        $"Zip Motion Blocks Video is asked for {bits} bits a pixel. The layouts written here are 8 (palettised), 16 "
        + "(5-6-5) and 32; 15 has no RawImage layout to take losslessly and 24 has no decoder to read it."),
    };
  }

  public bool TryEncode(RawImage frame, long? presentationTimestamp, out CodedPacket packet) {
    var pixels = this._Prepare(frame, out var palette);
    var isKeyFrame = this._previous == null || this._framesSinceKeyFrame >= _KEY_FRAME_INTERVAL;

    using var work = new MemoryStream();
    var flags = isKeyFrame ? _FLAG_KEY_FRAME : (byte)0;

    if (isKeyFrame) {
      if (palette != null) {
        palette.CopyTo(this._palette, 0);
        work.Write(this._palette);
      }

      work.Write(pixels);
      this._RestartCompressor();
    } else {
      if (palette != null && !palette.AsSpan().SequenceEqual(this._palette)) {
        flags |= _FLAG_PALETTE_DELTA;
        for (var i = 0; i < _PALETTE_BYTES; ++i) {
          work.WriteByte((byte)(palette[i] ^ this._palette[i]));
          this._palette[i] = palette[i];
        }
      }

      this._WriteInterframe(work, pixels, this._previous!);
    }

    var deflater = this._deflater!;
    deflater.Write(work.GetBuffer(), 0, (int)work.Length);
    deflater.Flush();

    var headerLength = isKeyFrame ? 7 : 1;
    var data = new byte[headerLength + this._compressed.Length];
    data[0] = flags;
    if (isKeyFrame) {
      data[1] = 0;
      data[2] = 1;
      data[3] = 1;
      data[4] = this._format;
      data[5] = _BLOCK_SIZE;
      data[6] = _BLOCK_SIZE;
    }

    this._compressed.GetBuffer().AsSpan(0, (int)this._compressed.Length).CopyTo(data.AsSpan(headerLength));
    this._compressed.SetLength(0);

    this._previous = pixels;
    this._framesSinceKeyFrame = isKeyFrame ? 1 : this._framesSinceKeyFrame + 1;

    packet = new(
      this._stream.Index,
      data,
      PresentationTimestamp: presentationTimestamp,
      DecodeTimestamp: presentationTimestamp,
      IsKeyFrame: isKeyFrame);
    return true;
  }

  public MediaStreamInfo DescribeStream() => this._stream;

  // ============================================================================================
  // What goes in
  // ============================================================================================

  /// <summary>
  /// Checks the picture against the stream's geometry and layout and hands back exactly the bytes
  /// the codec will see — a private copy of <c>width × height × bytesPerPixel</c> — plus the palette
  /// as 256 entries of red, green and blue where the layout is palettised.
  /// </summary>
  private byte[] _Prepare(RawImage frame, out byte[]? palette) {
    ArgumentNullException.ThrowIfNull(frame);
    palette = null;

    switch (this._format) {
      case _FORMAT_8BPP:
        if (frame.Format != PixelFormat.Indexed8)
          throw new NotSupportedException(
            $"This Zip Motion Blocks Video stream is palettised and a {frame.Format} picture has no palette to code it "
            + "by; it is refused rather than quantised to one.");
        if (frame.Palette == null || frame.PaletteCount <= 0)
          throw new InvalidDataException("An Indexed8 picture offered to Zip Motion Blocks Video carries no palette.");
        if (frame.PaletteCount > _PALETTE_ENTRIES || frame.Palette.Length < frame.PaletteCount * 3)
          throw new InvalidDataException(
            $"An Indexed8 picture offered to Zip Motion Blocks Video states {frame.PaletteCount} palette entries in "
            + $"{frame.Palette.Length} byte(s); the format holds exactly 256 entries of three bytes.");

        palette = new byte[_PALETTE_BYTES];
        Array.Copy(frame.Palette, palette, frame.PaletteCount * 3);
        break;
      case _FORMAT_16BPP:
        if (frame.Format != PixelFormat.Rgb565)
          throw new NotSupportedException(
            $"This Zip Motion Blocks Video stream codes 5-6-5 and a {frame.Format} picture cannot become that without "
            + "changing sample values; it is refused rather than quantised.");
        break;
    }

    var picture = LosslessEncoderInput.Prepare(frame, this._codedFormat, this._width, this._height, CodecName);
    var frameBytes = this._width * this._height * this._bytesPerPixel;
    var pixels = new byte[frameBytes];
    Array.Copy(picture.PixelData, pixels, frameBytes);
    return pixels;
  }

  // ============================================================================================
  // The interframe: a block grid, each block a motion vector and maybe an XOR correction
  // ============================================================================================

  private void _WriteInterframe(MemoryStream work, byte[] current, byte[] previous) {
    var blockCount = this._blocksX * this._blocksY;
    var table = new byte[(blockCount * 2 + 3) & ~3];
    var stride = this._width * this._bytesPerPixel;
    var reference = new byte[_BLOCK_SIZE * this._bytesPerPixel];
    var xor = new byte[_BLOCK_SIZE * _BLOCK_SIZE * this._bytesPerPixel];

    using var corrections = new MemoryStream();
    var mx = 0;
    var my = 0;
    var index = 0;

    for (var y0 = 0; y0 < this._height; y0 += _BLOCK_SIZE) {
      var blockHeight = Math.Min(_BLOCK_SIZE, this._height - y0);

      for (var x0 = 0; x0 < this._width; x0 += _BLOCK_SIZE, index += 2) {
        var blockWidth = Math.Min(_BLOCK_SIZE, this._width - x0);

        this._SearchMotion(current, previous, stride, reference, x0, y0, blockWidth, blockHeight, ref mx, ref my, out var xored);
        table[index] = unchecked((byte)((mx << 1) | (xored ? 1 : 0)));
        table[index + 1] = unchecked((byte)(my << 1));

        if (!xored)
          continue;

        var rowBytes = blockWidth * this._bytesPerPixel;
        for (var row = 0; row < blockHeight; ++row) {
          this._ReferenceRow(previous, stride, x0 + mx, y0 + row + my, blockWidth, reference);
          var source = current.AsSpan((y0 + row) * stride + x0 * this._bytesPerPixel, rowBytes);
          for (var i = 0; i < rowBytes; ++i)
            xor[row * rowBytes + i] = (byte)(source[i] ^ reference[i]);
        }

        corrections.Write(xor, 0, rowBytes * blockHeight);
      }
    }

    work.Write(table);
    work.Write(corrections.GetBuffer(), 0, (int)corrections.Length);
  }

  /// <summary>
  /// Finds the vector whose copy of the frame before needs the cheapest correction, in FFmpeg's
  /// order: the zero vector, the vector the previous block settled on, then the whole search window
  /// — stopping at the first exact match, and otherwise keeping the lowest-entropy XOR.
  /// </summary>
  private void _SearchMotion(
    byte[] current, byte[] previous, int stride, byte[] reference, int x0, int y0, int blockWidth, int blockHeight,
    ref int mx, ref int my, out bool xored) {
    var previousMx = mx;
    var previousMy = my;

    var best = this._Score(current, previous, stride, reference, x0, y0, 0, 0, blockWidth, blockHeight, out xored);
    mx = 0;
    my = 0;
    if (best == 0)
      return;

    if (previousMx != 0 || previousMy != 0) {
      var score = this._Score(current, previous, stride, reference, x0, y0, previousMx, previousMy, blockWidth, blockHeight, out var candidateXored);
      if (score < best) {
        best = score;
        mx = previousMx;
        my = previousMy;
        xored = candidateXored;
        if (best == 0)
          return;
      }
    }

    for (var dy = -_SEARCH_RANGE; dy <= _SEARCH_RANGE; ++dy)
      for (var dx = -_SEARCH_RANGE; dx <= _SEARCH_RANGE; ++dx) {
        if (dx == 0 && dy == 0)
          continue;
        if (dx == previousMx && dy == previousMy)
          continue;

        var score = this._Score(current, previous, stride, reference, x0, y0, dx, dy, blockWidth, blockHeight, out var candidateXored);
        if (score >= best)
          continue;

        best = score;
        mx = dx;
        my = dy;
        xored = candidateXored;
        if (best == 0)
          return;
      }
  }

  /// <summary>
  /// Rates one candidate vector by the entropy of the XOR it would leave behind — nought for an
  /// exact copy, larger the more varied the correction's bytes — and says whether any correction
  /// is needed at all.
  /// </summary>
  private int _Score(
    byte[] current, byte[] previous, int stride, byte[] reference, int x0, int y0, int dx, int dy, int blockWidth, int blockHeight,
    out bool xored) {
    Span<int> histogram = stackalloc int[256];
    var rowBytes = blockWidth * this._bytesPerPixel;

    for (var row = 0; row < blockHeight; ++row) {
      this._ReferenceRow(previous, stride, x0 + dx, y0 + row + dy, blockWidth, reference);
      var source = current.AsSpan((y0 + row) * stride + x0 * this._bytesPerPixel, rowBytes);
      for (var i = 0; i < rowBytes; ++i)
        ++histogram[source[i] ^ reference[i]];
    }

    xored = histogram[0] < rowBytes * blockHeight;
    if (!xored)
      return 0;

    var sum = 0;
    for (var i = 0; i < 256; ++i)
      sum += this._scoreTable[histogram[i]];

    return sum;
  }

  /// <summary>
  /// Fills <paramref name="reference"/> with one row of the previous picture starting at
  /// (<paramref name="sourceX"/>, <paramref name="sourceY"/>), zero wherever that lies outside the
  /// picture — exactly the pixels the decoder will copy for the same vector.
  /// </summary>
  private void _ReferenceRow(byte[] previous, int stride, int sourceX, int sourceY, int blockWidth, byte[] reference) {
    var bpp = this._bytesPerPixel;
    var rowBytes = blockWidth * bpp;
    if (sourceY < 0 || sourceY >= this._height) {
      reference.AsSpan(0, rowBytes).Clear();
      return;
    }

    if (sourceX >= 0 && sourceX + blockWidth <= this._width) {
      previous.AsSpan(sourceY * stride + sourceX * bpp, rowBytes).CopyTo(reference);
      return;
    }

    for (var column = 0; column < blockWidth; ++column) {
      var x = sourceX + column;
      var target = reference.AsSpan(column * bpp, bpp);
      if (x < 0 || x >= this._width)
        target.Clear();
      else
        previous.AsSpan(sourceY * stride + x * bpp, bpp).CopyTo(target);
    }
  }

  /// <summary>
  /// FFmpeg's entropy table: the cost of a byte value that occurs <c>i</c> times in a block of
  /// <paramref name="blockBytes"/>, so a histogram's summed cost is lowest for the flattest XOR.
  /// </summary>
  private static int[] _ScoreTable(int blockBytes) {
    var table = new int[blockBytes + 1];
    for (var i = 1; i <= blockBytes; ++i)
      table[i] = (int)(-i * Math.Log2(i / (double)blockBytes) * 256);

    return table;
  }

  // ============================================================================================
  // The zlib stream, restarted at every intraframe and carried across every interframe
  // ============================================================================================

  private void _RestartCompressor() {
    this._deflater?.Dispose();
    this._compressed = new();
    this._deflater = new(this._compressed, CompressionLevel.SmallestSize, leaveOpen: true);
  }
}
