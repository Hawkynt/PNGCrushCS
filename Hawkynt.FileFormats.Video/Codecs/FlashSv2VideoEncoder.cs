using System;
using System.IO;
using System.IO.Compression;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Encodes Flash Screen Video 2 (<c>FSV2</c>) as lossless 24-bit BGR keyblocks and interblocks.
/// </summary>
/// <remarks>
/// The packet and block layout follows Appendix B/C of Adobe's SWF File Format Specification v19.
/// Interframes use the format's row-range updates against the last key frame: a cell is omitted when it
/// is identical to the previous displayed frame, otherwise the encoder sends the smallest contiguous
/// row range that differs from the key-frame reference. Returning a changed cell completely to its key
/// frame is represented by a zero-height diff block, which is distinct from a zero-length unchanged
/// block. This is the same state model used by FFmpeg's <c>flashsv2</c> encoder (Joshua Warner,
/// LGPL-2.1-or-later), independently expressed here for PNGCrushCS under LGPL-3.0-or-later.
/// <para/>
/// The encoder deliberately writes the specification's 24-bit color mode rather than the optional
/// 15/7-bit hybrid mode: the latter is quantized and therefore cannot preserve arbitrary input pixels.
/// ZLIB priming is an optional compression technique rather than a decoding dependency, so output uses
/// ordinary independent RFC 1950 streams. The decoder still accepts the measured
/// <c>ZlibPrimeCompressPrevious</c> form. <c>ZlibPrimeCompressCurrent</c> and <c>HasIFrameImage</c> are
/// not emitted because the specification does not define their reconstruction algorithm precisely
/// enough to invent one, and FFmpeg likewise leaves both unsupported.
/// <para/>
/// Flash Screen Video 2 has key frames and forward-decoded interframes, but no B-picture syntax and no
/// bidirectional temporal prediction. A 64x64 block grid and a twelve-frame key interval match the
/// established encoder behavior used by the sibling FSV1 implementation and FFmpeg's default GOP.
/// </remarks>
[VerifiedBy(ConformanceOracle.FFmpeg)]
public sealed class FlashSv2VideoEncoder : IVideoCodecEncoder<FlashSv2VideoEncoder> {

  private static readonly CodecTag _Tag = CodecTag.FromCharacters("FSV2");

  private const int _BLOCK_SIZE = 64;
  private const int _MAX_DIMENSION = 4095;
  private const int _KEY_FRAME_INTERVAL = 12;

  private readonly MediaStreamInfo _stream;
  private readonly int _width;
  private readonly int _height;
  private readonly int _columns;
  private readonly int _rows;

  /// <summary>The previously displayed picture, bottom row first and B, G, R per pixel.</summary>
  private byte[]? _previous;

  /// <summary>The most recent key frame in the same representation. Interblock row ranges are defined
  /// against this picture, not against the immediately previous frame.</summary>
  private byte[]? _keyFrame;

  private long _framesSinceKeyFrame;

  private FlashSv2VideoEncoder(MediaStreamInfo stream) {
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

  public static string CodecName => "Flash Screen Video 2";

  public static CodecTag Codec => _Tag;

  public static FlashSv2VideoEncoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.Kind != MediaStreamKind.Video)
      throw new NotSupportedException("Flash Screen Video 2 can only encode a video stream.");
    if (stream.Width <= 0 || stream.Height <= 0)
      throw new NotSupportedException(
        $"A Flash Screen Video 2 encoder needs positive picture dimensions before the muxer is created; {stream.Width}x{stream.Height} was supplied.");
    if (stream.Width > _MAX_DIMENSION || stream.Height > _MAX_DIMENSION)
      throw new NotSupportedException(
        $"Flash Screen Video 2 states the picture size in twelve bits, so {stream.Width}x{stream.Height} exceeds its "
        + $"{_MAX_DIMENSION}x{_MAX_DIMENSION} limit.");

    return new(stream);
  }

  public bool TryEncode(RawImage frame, long? presentationTimestamp, out CodedPacket packet) {
    var picture = LosslessEncoderInput.Prepare(frame, PixelFormat.Bgr24, this._width, this._height, CodecName);
    var canvas = _FlipVertically(picture.PixelData, this._height, this._width * 3);

    var previous = this._previous;
    var keyFrame = this._keyFrame;
    var forceKeyFrame = previous == null || keyFrame == null || this._framesSinceKeyFrame >= _KEY_FRAME_INTERVAL;

    using var output = new MemoryStream();
    output.WriteByte((byte)(((_BLOCK_SIZE / 16 - 1) << 4) | (this._width >> 8)));
    output.WriteByte((byte)this._width);
    output.WriteByte((byte)(((_BLOCK_SIZE / 16 - 1) << 4) | (this._height >> 8)));
    output.WriteByte((byte)this._height);
    output.WriteByte(0); // reserved, HasIFrameImage = 0, HasPaletteInfo = 0

    var rowsBuffer = new byte[_BLOCK_SIZE * _BLOCK_SIZE * 3];

    for (var row = 0; row < this._rows; ++row) {
      var cellHeight = _BlockExtent(row, this._rows, this._height);
      var canvasRow = row * _BLOCK_SIZE;

      for (var column = 0; column < this._columns; ++column) {
        var cellWidth = _BlockExtent(column, this._columns, this._width);
        var canvasColumn = column * _BLOCK_SIZE;

        if (!forceKeyFrame && !_CellChanged(canvas, previous!, canvasRow, canvasColumn, cellWidth, cellHeight, this._width)) {
          output.WriteByte(0);
          output.WriteByte(0);
          continue;
        }

        int rowStart;
        int rowCount;
        if (forceKeyFrame) {
          rowStart = 0;
          rowCount = cellHeight;
        } else {
          (rowStart, rowCount) = _DifferenceRowsFromKeyFrame(
            canvas, keyFrame!, canvasRow, canvasColumn, cellWidth, cellHeight, this._width);
        }

        var hasDiffRows = rowStart != 0 || rowCount != cellHeight;
        byte[] compressed;
        if (rowCount == 0) {
          compressed = [];
        } else {
          var rawLength = _GatherRows(
            canvas, rowsBuffer, canvasRow + rowStart, canvasColumn, cellWidth, rowCount, this._width);
          compressed = _Deflate(rowsBuffer, rawLength);
        }

        var blockSize = 1 + (hasDiffRows ? 2 : 0) + compressed.Length;
        if (blockSize > ushort.MaxValue)
          throw new InvalidDataException(
            $"A Flash Screen Video 2 cell at grid position ({column},{row}) compressed to {blockSize} bytes including "
            + "its block header, which its two-byte DataSize field cannot state.");

        output.WriteByte((byte)(blockSize >> 8));
        output.WriteByte((byte)blockSize);
        output.WriteByte(hasDiffRows ? (byte)0x04 : (byte)0x00); // ColorDepth 00 = 24-bit BGR
        if (hasDiffRows) {
          output.WriteByte((byte)rowStart);
          output.WriteByte((byte)rowCount);
        }
        output.Write(compressed);
      }
    }

    if (forceKeyFrame)
      this._keyFrame = (byte[])canvas.Clone();

    this._previous = canvas;
    this._framesSinceKeyFrame = forceKeyFrame ? 1 : this._framesSinceKeyFrame + 1;

    packet = new(
      this._stream.Index,
      output.ToArray(),
      PresentationTimestamp: presentationTimestamp,
      DecodeTimestamp: presentationTimestamp,
      IsKeyFrame: forceKeyFrame);
    return true;
  }

  public MediaStreamInfo DescribeStream() => this._stream;

  private static int _BlockCount(int imageSize) => (imageSize + _BLOCK_SIZE - 1) / _BLOCK_SIZE;

  private static int _BlockExtent(int index, int count, int imageSize)
    => index == count - 1 ? imageSize - index * _BLOCK_SIZE : _BLOCK_SIZE;

  private static bool _CellChanged(
    byte[] current, byte[] previous, int canvasRow, int canvasColumn, int cellWidth, int cellHeight, int imageWidth) {
    var rowBytes = cellWidth * 3;
    for (var row = 0; row < cellHeight; ++row) {
      var offset = ((canvasRow + row) * imageWidth + canvasColumn) * 3;
      if (!current.AsSpan(offset, rowBytes).SequenceEqual(previous.AsSpan(offset, rowBytes)))
        return true;
    }

    return false;
  }

  /// <summary>Returns the smallest contiguous row range whose current pixels differ from the last key
  /// frame. The range may be empty when a cell changed since the previous frame only by returning to
  /// its key-frame contents.</summary>
  private static (int Start, int Count) _DifferenceRowsFromKeyFrame(
    byte[] current, byte[] keyFrame, int canvasRow, int canvasColumn, int cellWidth, int cellHeight, int imageWidth) {
    var rowBytes = cellWidth * 3;
    var first = -1;
    var last = -1;
    for (var row = 0; row < cellHeight; ++row) {
      var offset = ((canvasRow + row) * imageWidth + canvasColumn) * 3;
      if (current.AsSpan(offset, rowBytes).SequenceEqual(keyFrame.AsSpan(offset, rowBytes)))
        continue;

      first = first < 0 ? row : first;
      last = row;
    }

    return first < 0 ? (0, 0) : (first, last - first + 1);
  }

  private static int _GatherRows(
    byte[] canvas, byte[] destination, int canvasRow, int canvasColumn, int cellWidth, int rowCount, int imageWidth) {
    var rowBytes = cellWidth * 3;
    for (var row = 0; row < rowCount; ++row) {
      var sourceOffset = ((canvasRow + row) * imageWidth + canvasColumn) * 3;
      canvas.AsSpan(sourceOffset, rowBytes).CopyTo(destination.AsSpan(row * rowBytes, rowBytes));
    }

    return rowBytes * rowCount;
  }

  private static byte[] _Deflate(byte[] data, int length) {
    using var output = new MemoryStream();
    using (var zlib = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
      zlib.Write(data, 0, length);

    return output.ToArray();
  }

  private static byte[] _FlipVertically(byte[] picture, int height, int stride) {
    var canvas = new byte[height * stride];
    for (var row = 0; row < height; ++row)
      Array.Copy(picture, (height - 1 - row) * stride, canvas, row * stride, stride);

    return canvas;
  }
}
