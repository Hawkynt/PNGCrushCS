using System;
using System.IO;
using System.IO.Compression;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Encodes Flash Screen Video (FSV1): the picture cut into a grid of 64x64 cells, each written as its
/// own independent zlib stream when it differs from the frame before and left as a zero-length entry
/// when it does not.
/// </summary>
/// <remarks>
/// Adapted from FFmpeg's <c>libavcodec/flashsvenc.c</c>, copyright (C) 2004 Alex Beregszaszi and
/// (C) 2006 Benjamin Larsson, LGPL-2.1-or-later; this adaptation is distributed with PNGCrushCS
/// under LGPL-3.0-or-later.
/// <para/>
/// <b>What is written.</b> Every packet opens with the four-byte grid header the decoder reads back:
/// the cell size as <c>(size / 16) − 1</c> in four bits and the picture size in twelve, for width and
/// then height, big-endian. Cells follow bottom grid row first, left to right, upward, each as a
/// two-byte big-endian length and that many bytes of a complete RFC 1950 zlib stream inflating to
/// exactly <c>cellWidth × cellHeight × 3</c> bytes — the cell's own rows bottom-up, three bytes B, G,
/// R a pixel, no padding, the last column and row narrower or shorter by the picture's remainder.
/// The cell size is 64x64 and never changes, which is what FFmpeg's encoder writes and what the
/// decoder here holds its canvas against for the life of the stream.
/// <para/>
/// <b>Which frames are key frames.</b> The first, every twelfth after it — FFmpeg's default group
/// size — and any frame that happened to send every cell anyway, since a packet with no zero-length
/// entry needs nothing from the frame before it whether or not it was meant to. A key frame states
/// every cell; a delta frame states only the cells whose bytes changed, compared exactly rather
/// than by any threshold, because the format has no difference operation and "unchanged" has to be
/// literally true.
/// <para/>
/// <b>What is accepted.</b> Pictures up to 4095 pixels a side, the header's twelve-bit limit, in any
/// format that converts to eight-bit RGB without changing a sample — RGB and BGR with or without
/// alpha, grey, palettised, 5-6-5 — with alpha dropped since the format has no place for it. Deeper,
/// floating-point and YUV pictures are refused by name rather than quantised, and so is a picture
/// whose size differs from the one the stream was created for, since every delta frame's unchanged
/// cells are read against a canvas of exactly that size.
/// </remarks>
public sealed class FlashSvVideoEncoder : IVideoCodecEncoder<FlashSvVideoEncoder> {

  private static readonly CodecTag _Tag = CodecTag.FromCharacters("FSV1");

  private const int _BLOCK_SIZE = 64;
  private const int _MAX_DIMENSION = 4095;

  /// <summary>How many frames apart key frames are forced, FFmpeg's default group-of-pictures size.</summary>
  private const int _KEY_FRAME_INTERVAL = 12;

  private readonly MediaStreamInfo _stream;
  private readonly int _width;
  private readonly int _height;
  private readonly int _columns;
  private readonly int _rows;

  /// <summary>The previous picture as coded — bottom row first, B, G, R — or <c>null</c> before the
  /// first frame, which is what makes that frame a key frame.</summary>
  private byte[]? _previous;
  private long _framesSinceKeyFrame;

  private FlashSvVideoEncoder(MediaStreamInfo stream) {
    this._width = stream.Width;
    this._height = stream.Height;
    this._columns = _BlockCount(stream.Width);
    this._rows = _BlockCount(stream.Height);
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
      BitsPerPixel = 24,
      Language = stream.Language,
      Name = stream.Name,
    };
  }

  public static string CodecName => "Flash Screen Video";

  public static CodecTag Codec => _Tag;

  public static FlashSvVideoEncoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.Kind != MediaStreamKind.Video)
      throw new NotSupportedException("Flash Screen Video can only encode a video stream.");
    if (stream.Width <= 0 || stream.Height <= 0)
      throw new NotSupportedException(
        $"A Flash Screen Video encoder needs positive picture dimensions before the muxer is created; {stream.Width}x{stream.Height} was supplied.");
    if (stream.Width > _MAX_DIMENSION || stream.Height > _MAX_DIMENSION)
      throw new NotSupportedException(
        $"Flash Screen Video states the picture size in twelve bits, so {stream.Width}x{stream.Height} exceeds its "
        + $"{_MAX_DIMENSION}x{_MAX_DIMENSION} limit.");

    return new(stream);
  }

  public bool TryEncode(RawImage frame, long? presentationTimestamp, out CodedPacket packet) {
    var picture = LosslessEncoderInput.Prepare(frame, PixelFormat.Bgr24, this._width, this._height, CodecName);
    var canvas = _FlipVertically(picture.PixelData, this._height, this._width * 3);

    var previous = this._previous;
    var forceKeyFrame = previous == null || this._framesSinceKeyFrame >= _KEY_FRAME_INTERVAL;

    using var output = new MemoryStream();
    output.WriteByte((byte)(((_BLOCK_SIZE / 16 - 1) << 4) | (this._width >> 8)));
    output.WriteByte((byte)this._width);
    output.WriteByte((byte)(((_BLOCK_SIZE / 16 - 1) << 4) | (this._height >> 8)));
    output.WriteByte((byte)this._height);

    var cell = new byte[_BLOCK_SIZE * _BLOCK_SIZE * 3];
    var everyCellSent = true;

    for (var row = 0; row < this._rows; ++row) {
      var cellHeight = _BlockExtent(row, this._rows, this._height);
      var canvasRow = row * _BLOCK_SIZE;

      for (var column = 0; column < this._columns; ++column) {
        var cellWidth = _BlockExtent(column, this._columns, this._width);
        var canvasColumn = column * _BLOCK_SIZE;
        var cellBytes = cellWidth * cellHeight * 3;

        var changed = _GatherCell(canvas, previous, cell, canvasRow, canvasColumn, cellWidth, cellHeight, this._width);
        if (!changed && !forceKeyFrame) {
          everyCellSent = false;
          output.WriteByte(0);
          output.WriteByte(0);
          continue;
        }

        var compressed = _Deflate(cell, cellBytes);
        if (compressed.Length > ushort.MaxValue)
          throw new InvalidDataException(
            $"A Flash Screen Video cell at grid position ({column},{row}) compressed to {compressed.Length} bytes, which its "
            + "two-byte length field cannot state.");

        output.WriteByte((byte)(compressed.Length >> 8));
        output.WriteByte((byte)compressed.Length);
        output.Write(compressed);
      }
    }

    var isKeyFrame = forceKeyFrame || everyCellSent;
    this._framesSinceKeyFrame = isKeyFrame ? 1 : this._framesSinceKeyFrame + 1;
    this._previous = canvas;

    packet = new(
      this._stream.Index,
      output.ToArray(),
      PresentationTimestamp: presentationTimestamp,
      DecodeTimestamp: presentationTimestamp,
      IsKeyFrame: isKeyFrame);
    return true;
  }

  public MediaStreamInfo DescribeStream() => this._stream;

  /// <summary>How many cells cover <paramref name="imageSize"/>, a remainder becoming one partial cell.</summary>
  private static int _BlockCount(int imageSize) => (imageSize + _BLOCK_SIZE - 1) / _BLOCK_SIZE;

  /// <summary>The pixel extent of cell <paramref name="index"/> along one axis: the full cell size
  /// except for the last cell, which is whatever remainder is left of <paramref name="imageSize"/>.</summary>
  private static int _BlockExtent(int index, int count, int imageSize)
    => index == count - 1 ? imageSize - index * _BLOCK_SIZE : _BLOCK_SIZE;

  /// <summary>
  /// Copies one cell's rows out of the bottom-up canvas into <paramref name="cell"/>, packed at the
  /// cell's own width, and says whether any byte of it differs from the same cell of the previous
  /// picture — the exact comparison the format's "unchanged" entry requires.
  /// </summary>
  private static bool _GatherCell(
    byte[] canvas, byte[]? previous, byte[] cell, int canvasRow, int canvasColumn, int cellWidth, int cellHeight, int imageWidth) {
    var rowBytes = cellWidth * 3;
    var changed = previous == null;
    for (var i = 0; i < cellHeight; ++i) {
      var source = ((canvasRow + i) * imageWidth + canvasColumn) * 3;
      var current = canvas.AsSpan(source, rowBytes);
      current.CopyTo(cell.AsSpan(i * rowBytes, rowBytes));
      if (!changed && !current.SequenceEqual(previous.AsSpan(source, rowBytes)))
        changed = true;
    }

    return changed;
  }

  /// <summary>One complete zlib stream — header, deflate data and Adler-32 trailer — for one cell.</summary>
  private static byte[] _Deflate(byte[] cell, int length) {
    using var output = new MemoryStream();
    using (var zlib = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
      zlib.Write(cell, 0, length);

    return output.ToArray();
  }

  /// <summary>Turns a top-down picture into the bottom-up canvas the format is coded in.</summary>
  private static byte[] _FlipVertically(byte[] picture, int height, int stride) {
    var canvas = new byte[height * stride];
    for (var row = 0; row < height; ++row)
      Array.Copy(picture, (height - 1 - row) * stride, canvas, row * stride, stride);

    return canvas;
  }
}
