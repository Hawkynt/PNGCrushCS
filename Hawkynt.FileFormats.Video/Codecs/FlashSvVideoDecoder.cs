using System;
using System.IO;
using System.IO.Compression;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Decodes Flash Screen Video (FSV1): a grid of blocks, each either unchanged since the frame before
/// it or its own independent zlib stream, read from the SWF File Format Specification's own appendix
/// rather than from any decoder's source.
/// </summary>
/// <remarks>
/// Every packet opens with a four-byte header carrying the block grid's cell size and the picture's
/// own size — <c>BlockWidth</c> and <c>BlockHeight</c> as four-bit codes, each <c>(actual / 16) - 1</c>
/// so the cell is a multiple of sixteen up to 256, and <c>ImageWidth</c> and <c>ImageHeight</c> as
/// twelve-bit pixel counts, the whole thirty-two bits packed big-endian with no byte swap. Nothing
/// about the picture's size comes from the container: a Flash Video file states no width or height for
/// this codec at all unless a script tag happens to carry one, so the geometry a decoder needs is
/// exactly what its own packets state, read fresh and held rather than asked of the stream it was
/// built from.
/// <para/>
/// <b>The grid, and which blocks are partial.</b> The picture is covered by cells of the stated size,
/// counted by dividing the picture's width and height by the cell's and rounding up; a remainder lands
/// entirely in the last column and the last row, which are narrower or shorter than the rest by exactly
/// that remainder. Blocks are ordered bottom row first, left to right within a row, upward to the top —
/// stated plainly in the specification and not a convention borrowed from anywhere else — and the
/// pixels inside a block are ordered the same way: its first decompressed row is the block's own bottom
/// row.
/// <para/>
/// <b>A block's two bytes of length say whether it carries anything at all.</b> A key frame states
/// every block; an interframe may leave a block's length at zero, which is the format's way of saying
/// this block is exactly what the canvas already holds and not a delta encoded against it — there is no
/// difference operation here, unlike CSCD, only "sent" and "left alone". That makes the canvas the
/// state a decoder carries between packets and nothing else: a length of zero costs nothing to honour
/// because there is nothing to do.
/// <para/>
/// <b>A block's compressed size is stated; its decompressed size is not</b> — it is implied by the
/// grid position alone, exactly the width and height that cell's row and column give it, whole or
/// partial. Reading anything else out of the zlib stream would be trusting a length the format never
/// wrote down.
/// <para/>
/// <b>Pixels are three bytes, B, G, R</b>, which the specification states directly and which matches
/// this package's own <see cref="PixelFormat.Bgr24"/> byte for byte, so no channel reordering happens
/// anywhere in this decoder.
/// <para/>
/// <b>What refuses.</b> A packet shorter than its own four-byte header; a picture the header states as
/// zero pixels in either direction; a block whose two-byte length runs past the packet's own end; a
/// zlib stream that does not decompress to exactly the byte count its block's position in the grid
/// implies; and a packet whose header states a different picture size or a different block cell size
/// than the one this decoder has already built its canvas against — the specification allows the block
/// size to change at a keyframe and says nothing about the picture size doing the same, and no measured
/// stream does either, so a change already has no fixture to be exercised against safely.
/// </remarks>
public sealed class FlashSvVideoDecoder : IVideoCodecDecoder<FlashSvVideoDecoder> {

  private static readonly CodecTag _Tag = CodecTag.FromCharacters("FSV1");

  private readonly int _streamIndex;

  private int _width;
  private int _height;
  private int _blockWidth;
  private int _blockHeight;

  /// <summary>The picture as coded, bottom row first, three bytes (B, G, R) a pixel, kept between
  /// packets because an interframe's unchanged blocks are read against it rather than restated.</summary>
  private byte[]? _canvas;

  private FlashSvVideoDecoder(int streamIndex) => this._streamIndex = streamIndex;

  public static string CodecName => "Flash Screen Video";

  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    return stream.Kind == MediaStreamKind.Video && stream.Codec.EqualsIgnoringCase(_Tag);
  }

  public static FlashSvVideoDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    return new(stream.Index);
  }

  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    var data = packet.Data.Span;
    if (data.Length < 4)
      throw new InvalidDataException(
        $"Video stream {this._streamIndex} carries a Flash Screen Video packet of {data.Length} byte(s), where the "
        + "grid header alone is four.");

    var blockWidth = (((data[0] >> 4) & 0xF) + 1) * 16;
    var imageWidth = ((data[0] & 0xF) << 8) | data[1];
    var blockHeight = (((data[2] >> 4) & 0xF) + 1) * 16;
    var imageHeight = ((data[2] & 0xF) << 8) | data[3];

    if (imageWidth <= 0 || imageHeight <= 0)
      throw new InvalidDataException(
        $"Video stream {this._streamIndex} states a Flash Screen Video picture size of {imageWidth}x{imageHeight}, "
        + "which no frame can be decoded into.");

    this._EnsureGeometry(imageWidth, imageHeight, blockWidth, blockHeight);

    var columns = _BlockCount(imageWidth, blockWidth);
    var rows = _BlockCount(imageHeight, blockHeight);
    var canvas = this._canvas!;
    var offset = 4;

    for (var row = 0; row < rows; ++row) {
      var rowHeight = _BlockExtent(row, rows, blockHeight, imageHeight);
      var canvasRow = row * blockHeight;

      for (var column = 0; column < columns; ++column) {
        var columnWidth = _BlockExtent(column, columns, blockWidth, imageWidth);

        if (offset + 2 > data.Length)
          throw new InvalidDataException(
            $"Video stream {this._streamIndex} carries a Flash Screen Video packet whose block at grid position "
            + $"({column},{row}) has no two-byte length left at offset {offset} of {data.Length}.");

        var blockSize = (data[offset] << 8) | data[offset + 1];
        offset += 2;

        if (blockSize == 0)
          continue; // Unchanged since the picture before this one; the canvas already holds it.

        if (offset + blockSize > data.Length)
          throw new InvalidDataException(
            $"Video stream {this._streamIndex} carries a Flash Screen Video block at grid position ({column},{row}) "
            + $"stating {blockSize} compressed byte(s), where only {data.Length - offset} remain in the packet.");

        var compressed = packet.Data.Slice(offset, blockSize);
        offset += blockSize;

        var targetSize = columnWidth * rowHeight * 3;
        _InflateBlock(compressed, targetSize, canvas, canvasRow, column * blockWidth, columnWidth, rowHeight, imageWidth, this._streamIndex, column, row);
      }
    }

    frame = new() {
      Width = this._width,
      Height = this._height,
      Format = PixelFormat.Bgr24,
      PixelData = _FlipVertically(canvas, this._height, this._width * 3),
    };
    return true;
  }

  /// <summary>
  /// Establishes the canvas on the first packet and refuses any later one that states a different
  /// geometry, since every interframe's unchanged blocks are read against exactly this canvas.
  /// </summary>
  private void _EnsureGeometry(int imageWidth, int imageHeight, int blockWidth, int blockHeight) {
    if (this._canvas == null) {
      this._width = imageWidth;
      this._height = imageHeight;
      this._blockWidth = blockWidth;
      this._blockHeight = blockHeight;
      this._canvas = new byte[imageWidth * imageHeight * 3];
      return;
    }

    if (this._width == imageWidth && this._height == imageHeight
        && this._blockWidth == blockWidth && this._blockHeight == blockHeight)
      return;

    throw new NotSupportedException(
      $"Video stream {this._streamIndex} changes its Flash Screen Video geometry from {this._width}x{this._height} "
      + $"in {this._blockWidth}x{this._blockHeight} blocks to {imageWidth}x{imageHeight} in {blockWidth}x{blockHeight} "
      + "blocks part way through, and the canvas every unchanged block is read against does not follow it.");
  }

  /// <summary>How many cells of <paramref name="blockSize"/> cover <paramref name="imageSize"/>,
  /// rounding up so a remainder becomes one partial cell rather than being dropped.</summary>
  private static int _BlockCount(int imageSize, int blockSize) => (imageSize + blockSize - 1) / blockSize;

  /// <summary>The pixel extent of cell <paramref name="index"/> along one axis: the full cell size,
  /// except the last cell, which is whatever remainder is left of <paramref name="imageSize"/>.</summary>
  private static int _BlockExtent(int index, int count, int blockSize, int imageSize)
    => index == count - 1 ? imageSize - index * blockSize : blockSize;

  /// <summary>
  /// Decompresses one block's zlib stream directly into its place in the canvas, row by row, since the
  /// block's own rows are exactly as wide as the block and the canvas' rows are exactly as wide as the
  /// picture — the two strides agree only when copied a row at a time.
  /// </summary>
  private static void _InflateBlock(
    ReadOnlyMemory<byte> compressed, int targetSize, byte[] canvas, int canvasRow, int canvasColumn,
    int columnWidth, int rowHeight, int imageWidth, int streamIndex, int column, int row) {
    using var source = new MemoryStream(compressed.ToArray(), writable: false);
    using var zlib = new ZLibStream(source, CompressionMode.Decompress);
    var decompressed = new byte[targetSize];
    try {
      zlib.ReadExactly(decompressed);
    } catch (EndOfStreamException ex) {
      throw new InvalidDataException(
        $"Video stream {streamIndex} carries a Flash Screen Video block at grid position ({column},{row}) whose "
        + $"zlib data decompresses to fewer than the {targetSize} byte(s) its {columnWidth}x{rowHeight} cell needs.",
        ex);
    }

    var rowBytes = columnWidth * 3;
    for (var i = 0; i < rowHeight; ++i)
      Array.Copy(decompressed, i * rowBytes, canvas, ((canvasRow + i) * imageWidth + canvasColumn) * 3, rowBytes);
  }

  /// <summary>Turns the coded, bottom-up canvas the right way up.</summary>
  private static byte[] _FlipVertically(byte[] canvas, int height, int stride) {
    var picture = new byte[canvas.Length];
    for (var row = 0; row < height; ++row)
      Array.Copy(canvas, (height - 1 - row) * stride, picture, row * stride, stride);

    return picture;
  }
}
