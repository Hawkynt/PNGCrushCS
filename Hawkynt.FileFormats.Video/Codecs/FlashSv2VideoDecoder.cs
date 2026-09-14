using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using FileFormat.Codecs.FlashSv2;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Decodes Flash Screen Video 2 (FSV2): block-grid keyframes and interframes, 24-bit BGR and the
/// hybrid 15/7-bit colorspace, partial-row updates, custom palettes, and the measured
/// <c>ZlibPrimeCompressPrevious</c> form of ZLIB priming.
/// </summary>
/// <remarks>
/// The packet and block syntax comes from Adobe's SWF File Format Specification v19. The priming
/// behavior that the specification leaves underspecified was measured against FFmpeg-generated FSV2
/// streams: a primed block's raw DEFLATE stream uses the corresponding cell's coded bytes from the
/// last container key frame as its preset dictionary. <see cref="RawDeflate"/> implements that RFC 1951
/// path because the .NET compression wrappers do not expose preset dictionaries.
/// <para/>
/// A non-empty diff block is composed on the key-frame reference for its cell, not on the immediately
/// previous displayed frame. A zero-length IMAGEBLOCKV2 is different: it means the displayed cell is
/// unchanged and therefore leaves the current canvas alone. References keep both the coded bytes used
/// for later DEFLATE priming and an already-decoded BGR copy used for composition, so changing the
/// hybrid palette cannot retroactively recolor the last key frame.
/// <para/>
/// <b>What remains deliberately unsupported.</b> <c>HasIFrameImage</c> is described only as a second
/// grid of interblocks that must be combined with previous keyblocks, without defining the state
/// transition. <c>ZlibPrimeCompressCurrent</c> names another block by row and column but does not define
/// the byte sequence used as the priming dictionary. FFmpeg still marks both paths unsupported as
/// well. Guessing either would create a private codec variant, so both are rejected explicitly.
/// Flash Screen Video 2 has no B-picture syntax or bidirectional temporal prediction.
/// </remarks>
public sealed class FlashSv2VideoDecoder : IVideoCodecDecoder<FlashSv2VideoDecoder> {

  private static readonly CodecTag _Tag = CodecTag.FromCharacters("FSV2");

  private const int _MaxDictionary = 32768;

  private sealed record _ReferenceBlock(byte[] Encoded, byte[] PixelsBgr);

  private readonly int _streamIndex;

  private int _width;
  private int _height;
  private int _blockWidth;
  private int _blockHeight;
  private int _columns;
  private int _rows;

  /// <summary>The displayed picture as coded, bottom row first, three bytes (B, G, R) per pixel.</summary>
  private byte[]? _canvas;

  /// <summary>128 entries, three bytes (B, G, R) apiece.</summary>
  private byte[] _paletteBgr = FlashSv2Palette.DefaultBgr();

  /// <summary>Per-cell state established by the most recent container key frame.</summary>
  private readonly Dictionary<int, _ReferenceBlock> _reference = [];

  private FlashSv2VideoDecoder(int streamIndex) => this._streamIndex = streamIndex;

  public static string CodecName => "Flash Screen Video 2";

  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    return stream.Kind == MediaStreamKind.Video && stream.Codec.EqualsIgnoringCase(_Tag);
  }

  public static FlashSv2VideoDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    return new(stream.Index);
  }

  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    var data = packet.Data.Span;
    if (data.Length < 5)
      throw new InvalidDataException(
        $"Video stream {this._streamIndex} carries a Flash Screen Video 2 packet of {data.Length} byte(s), where "
        + "the grid header alone is five bytes.");

    var blockWidth = (((data[0] >> 4) & 0xF) + 1) * 16;
    var imageWidth = ((data[0] & 0xF) << 8) | data[1];
    var blockHeight = (((data[2] >> 4) & 0xF) + 1) * 16;
    var imageHeight = ((data[2] & 0xF) << 8) | data[3];

    if (imageWidth <= 0 || imageHeight <= 0)
      throw new InvalidDataException(
        $"Video stream {this._streamIndex} states a Flash Screen Video 2 picture size of {imageWidth}x{imageHeight}, "
        + "which no frame can be decoded into.");

    var flags = data[4];
    if ((flags & 0xFC) != 0)
      throw new InvalidDataException(
        $"Video stream {this._streamIndex} carries a Flash Screen Video 2 grid header whose six reserved bits are "
        + $"not zero (0x{flags:X2}).");

    var hasIFrameImage = (flags & 0x02) != 0;
    var hasPaletteInfo = (flags & 0x01) != 0;

    if (hasIFrameImage)
      throw new NotSupportedException(
        $"Video stream {this._streamIndex} carries a Flash Screen Video 2 packet whose grid header sets "
        + "HasIFrameImage. The specification does not define how that second interblock grid changes the key-block "
        + "state, and implementing an unverified interpretation would create an incompatible codec variant.");

    this._EnsureGeometry(imageWidth, imageHeight, blockWidth, blockHeight, packet.IsKeyFrame);

    var offset = 5;

    if (hasPaletteInfo) {
      if (offset + 2 > data.Length)
        throw new InvalidDataException(
          $"Video stream {this._streamIndex} carries a Flash Screen Video 2 packet whose palette block has no "
          + $"two-byte length left at offset {offset} of {data.Length}.");

      var paletteSize = (data[offset] << 8) | data[offset + 1];
      offset += 2;

      if (paletteSize == 0)
        throw new InvalidDataException(
          $"Video stream {this._streamIndex} carries a Flash Screen Video 2 packet with HasPaletteInfo set but "
          + "a palette DataSize of 0. A custom palette must carry all 128 three-byte entries.");

      if (offset + paletteSize > data.Length)
        throw new InvalidDataException(
          $"Video stream {this._streamIndex} carries a Flash Screen Video 2 palette block stating {paletteSize} "
          + $"compressed byte(s), where only {data.Length - offset} remain in the packet.");

      this._paletteBgr = _InflatePalette(packet.Data.Slice(offset, paletteSize), this._streamIndex);
      offset += paletteSize;
    }

    var canvas = this._canvas!;
    var isKeyFrame = packet.IsKeyFrame;

    for (var row = 0; row < this._rows; ++row) {
      var rowHeight = _BlockExtent(row, this._rows, blockHeight, imageHeight);
      var canvasRow = row * blockHeight;

      for (var column = 0; column < this._columns; ++column) {
        var columnWidth = _BlockExtent(column, this._columns, blockWidth, imageWidth);

        if (offset + 2 > data.Length)
          throw new InvalidDataException(
            $"Video stream {this._streamIndex} carries a Flash Screen Video 2 packet whose block at grid position "
            + $"({column},{row}) has no two-byte length left at offset {offset} of {data.Length}.");

        var blockSize = (data[offset] << 8) | data[offset + 1];
        offset += 2;

        if (blockSize == 0) {
          if (isKeyFrame)
            throw new InvalidDataException(
              $"Video stream {this._streamIndex} carries a Flash Screen Video 2 key frame whose block at grid "
              + $"position ({column},{row}) has DataSize 0. Zero-sized blocks are the interframe unchanged-block "
              + "form and cannot establish a keyblock reference.");

          continue;
        }

        if (offset + blockSize > data.Length)
          throw new InvalidDataException(
            $"Video stream {this._streamIndex} carries a Flash Screen Video 2 block at grid position "
            + $"({column},{row}) stating {blockSize} byte(s), where only {data.Length - offset} remain in the "
            + "packet.");

        var block = packet.Data.Slice(offset, blockSize);
        offset += blockSize;

        this._DecodeBlock(block.Span, canvas, column, row, canvasRow, column * blockWidth, columnWidth, rowHeight, isKeyFrame);
      }
    }

    if (offset != data.Length)
      throw new InvalidDataException(
        $"Video stream {this._streamIndex} carries {data.Length - offset} trailing byte(s) after the final Flash "
        + "Screen Video 2 block.");

    frame = new() {
      Width = this._width,
      Height = this._height,
      Format = PixelFormat.Bgr24,
      PixelData = _FlipVertically(canvas, this._height, this._width * 3),
    };
    return true;
  }

  private void _DecodeBlock(
    ReadOnlySpan<byte> block, byte[] canvas, int gridColumn, int gridRow, int canvasRow, int canvasColumn,
    int columnWidth, int rowHeight, bool isKeyFrame) {
    if (block.IsEmpty)
      throw new InvalidDataException(
        $"Video stream {this._streamIndex} carries a Flash Screen Video 2 block at grid position "
        + $"({gridColumn},{gridRow}) with no format byte.");

    var format = block[0];
    if ((format & 0xE0) != 0)
      throw new InvalidDataException(
        $"Video stream {this._streamIndex} carries a Flash Screen Video 2 block at grid position "
        + $"({gridColumn},{gridRow}) whose three reserved format bits are not zero (0x{format:X2}).");

    var colorDepth = (format >> 3) & 0x3;
    var hasDiffBlocks = (format & 0x04) != 0;
    var primeCurrent = (format & 0x02) != 0;
    var primePrevious = (format & 0x01) != 0;

    if (primeCurrent)
      throw new NotSupportedException(
        $"Video stream {this._streamIndex} carries a Flash Screen Video 2 block at grid position "
        + $"({gridColumn},{gridRow}) whose format byte sets ZlibPrimeCompressCurrent. The format names a source "
        + "block but does not define the exact priming byte sequence, and no independent implementation provides "
        + "an interoperability oracle for it.");

    if (colorDepth != 0 && colorDepth != 2)
      throw new NotSupportedException(
        $"Video stream {this._streamIndex} carries a Flash Screen Video 2 block at grid position "
        + $"({gridColumn},{gridRow}) whose format byte states colour depth {colorDepth}. Only 24-bit RGB (0) and "
        + "the 15/7-bit hybrid colourspace (2) are defined.");

    var headerOffset = 1;
    var pixelRowStart = 0;
    var pixelRowCount = rowHeight;

    if (hasDiffBlocks) {
      if (block.Length < headerOffset + 2)
        throw new InvalidDataException(
          $"Video stream {this._streamIndex} carries a Flash Screen Video 2 block at grid position "
          + $"({gridColumn},{gridRow}) whose diff position does not fit in {block.Length} byte(s).");

      pixelRowStart = block[headerOffset];
      pixelRowCount = block[headerOffset + 1];
      headerOffset += 2;

      if (pixelRowStart + pixelRowCount > rowHeight)
        throw new InvalidDataException(
          $"Video stream {this._streamIndex} carries a Flash Screen Video 2 diff block at grid position "
          + $"({gridColumn},{gridRow}) stating rows {pixelRowStart}..{pixelRowStart + pixelRowCount}, outside its "
          + $"own {rowHeight}-row cell.");
    }

    if (isKeyFrame && (pixelRowStart != 0 || pixelRowCount != rowHeight))
      throw new NotSupportedException(
        $"Video stream {this._streamIndex} carries a Flash Screen Video 2 block at grid position "
        + $"({gridColumn},{gridRow}) on a key frame that does not cover its whole {rowHeight}-row cell. A keyblock "
        + "must contain the complete block image before it can become a reference.");

    var cellKey = gridRow * this._columns + gridColumn;
    this._reference.TryGetValue(cellKey, out var reference);

    if (hasDiffBlocks && reference == null)
      throw new InvalidDataException(
        $"Video stream {this._streamIndex} carries a Flash Screen Video 2 diff block at grid position "
        + $"({gridColumn},{gridRow}) before a key frame established that cell's reference.");

    var compressed = block[headerOffset..];

    byte[] decoded;
    if (primePrevious) {
      if (reference == null)
        throw new InvalidDataException(
          $"Video stream {this._streamIndex} carries a Flash Screen Video 2 block at grid position "
          + $"({gridColumn},{gridRow}) priming against its own cell before any key frame established one.");

      var encodedReference = reference.Encoded;
      var dictionary = encodedReference.Length > _MaxDictionary ? encodedReference[^_MaxDictionary..] : encodedReference;
      decoded = RawDeflate.Decode(compressed, dictionary);
    } else
      decoded = _InflateAll(compressed);

    if (hasDiffBlocks)
      this._PaintReference(reference!.PixelsBgr, canvas, canvasRow, canvasColumn, columnWidth, rowHeight);

    if (pixelRowCount > 0)
      this._PaintRows(decoded, colorDepth, canvas, canvasRow + pixelRowStart, canvasColumn, columnWidth, pixelRowCount, gridColumn, gridRow);
    else if (decoded.Length != 0)
      throw new InvalidDataException(
        $"Video stream {this._streamIndex} carries a zero-height Flash Screen Video 2 diff block at grid position "
        + $"({gridColumn},{gridRow}) that nevertheless decompresses to {decoded.Length} byte(s).");

    if (isKeyFrame)
      this._reference[cellKey] = new(decoded, this._CaptureCell(canvas, canvasRow, canvasColumn, columnWidth, rowHeight));
  }

  private void _PaintReference(
    byte[] reference, byte[] canvas, int canvasRow, int canvasColumn, int columnWidth, int rowHeight) {
    var rowBytes = columnWidth * 3;
    var stride = this._width * 3;
    for (var row = 0; row < rowHeight; ++row)
      reference.AsSpan(row * rowBytes, rowBytes).CopyTo(canvas.AsSpan((canvasRow + row) * stride + canvasColumn * 3, rowBytes));
  }

  private void _PaintRows(
    byte[] decoded, int colorDepth, byte[] canvas, int canvasRowStart, int canvasColumn, int columnWidth, int rowCount,
    int gridColumn, int gridRow) {
    var stride = this._width * 3;

    if (colorDepth == 0) {
      var rowBytes = columnWidth * 3;
      var expected = rowBytes * rowCount;
      if (decoded.Length != expected)
        throw new InvalidDataException(
          $"Video stream {this._streamIndex} carries a 24-bit Flash Screen Video 2 block at grid position "
          + $"({gridColumn},{gridRow}) that decompresses to {decoded.Length} byte(s), where {expected} are required.");

      for (var row = 0; row < rowCount; ++row)
        decoded.AsSpan(row * rowBytes, rowBytes)
          .CopyTo(canvas.AsSpan((canvasRowStart + row) * stride + canvasColumn * 3, rowBytes));
      return;
    }

    var cursor = 0;
    for (var row = 0; row < rowCount; ++row) {
      var rowOffset = (canvasRowStart + row) * stride + canvasColumn * 3;
      for (var column = 0; column < columnWidth; ++column)
        cursor = this._DecodeHybridPixel(decoded, cursor, canvas, rowOffset + column * 3, gridColumn, gridRow);
    }

    if (cursor != decoded.Length)
      throw new InvalidDataException(
        $"Video stream {this._streamIndex} carries a hybrid Flash Screen Video 2 block at grid position "
        + $"({gridColumn},{gridRow}) with {decoded.Length - cursor} unused decompressed byte(s) after its pixels.");
  }

  private int _DecodeHybridPixel(byte[] decoded, int cursor, byte[] canvas, int destination, int gridColumn, int gridRow) {
    var first = _NextByte(decoded, ref cursor, this._streamIndex, gridColumn, gridRow);
    if ((first & 0x80) == 0) {
      var entry = (first & 0x7F) * 3;
      canvas[destination] = this._paletteBgr[entry];
      canvas[destination + 1] = this._paletteBgr[entry + 1];
      canvas[destination + 2] = this._paletteBgr[entry + 2];
      return cursor;
    }

    var second = _NextByte(decoded, ref cursor, this._streamIndex, gridColumn, gridRow);
    var colour15 = ((first & 0x7F) << 8) | second;
    var red = (colour15 >> 10) & 0x1F;
    var green = (colour15 >> 5) & 0x1F;
    var blue = colour15 & 0x1F;
    canvas[destination] = _Widen(blue);
    canvas[destination + 1] = _Widen(green);
    canvas[destination + 2] = _Widen(red);
    return cursor;
  }

  private byte[] _CaptureCell(byte[] canvas, int canvasRow, int canvasColumn, int columnWidth, int rowHeight) {
    var rowBytes = columnWidth * 3;
    var result = new byte[rowBytes * rowHeight];
    var stride = this._width * 3;
    for (var row = 0; row < rowHeight; ++row)
      canvas.AsSpan((canvasRow + row) * stride + canvasColumn * 3, rowBytes)
        .CopyTo(result.AsSpan(row * rowBytes, rowBytes));

    return result;
  }

  private static byte _NextByte(byte[] decoded, ref int cursor, int streamIndex, int gridColumn, int gridRow) {
    if (cursor >= decoded.Length)
      throw new InvalidDataException(
        $"Video stream {streamIndex} carries a Flash Screen Video 2 block at grid position "
        + $"({gridColumn},{gridRow}) whose decompressed data ran out before its pixels did.");

    return decoded[cursor++];
  }

  private static byte _Widen(int channel) => (byte)((channel << 3) | (channel >> 2));

  private static byte[] _InflateAll(ReadOnlySpan<byte> compressed) {
    if (compressed.IsEmpty)
      return [];

    using var source = new MemoryStream(compressed.ToArray(), writable: false);
    using var zlib = new ZLibStream(source, CompressionMode.Decompress);
    using var output = new MemoryStream();
    zlib.CopyTo(output);
    return output.ToArray();
  }

  private static byte[] _InflatePalette(ReadOnlyMemory<byte> compressed, int streamIndex) {
    const int _PALETTE_BYTES = 128 * 3;
    var decompressed = _InflateAll(compressed.Span);
    if (decompressed.Length != _PALETTE_BYTES)
      throw new InvalidDataException(
        $"Video stream {streamIndex} carries a Flash Screen Video 2 palette block whose zlib data decompresses to "
        + $"{decompressed.Length} byte(s), where exactly {_PALETTE_BYTES} are required for 128 three-byte entries.");

    return decompressed;
  }

  private void _EnsureGeometry(int imageWidth, int imageHeight, int blockWidth, int blockHeight, bool isKeyFrame) {
    if (this._canvas == null) {
      this._ConfigureGeometry(imageWidth, imageHeight, blockWidth, blockHeight);
      return;
    }

    if (this._width == imageWidth && this._height == imageHeight
        && this._blockWidth == blockWidth && this._blockHeight == blockHeight)
      return;

    if (!isKeyFrame)
      throw new NotSupportedException(
        $"Video stream {this._streamIndex} changes its Flash Screen Video 2 geometry from {this._width}x{this._height} "
        + $"in {this._blockWidth}x{this._blockHeight} blocks to {imageWidth}x{imageHeight} in {blockWidth}x{blockHeight} "
        + "blocks on an interframe. A new grid can only establish its references at a key frame.");

    this._reference.Clear();
    this._ConfigureGeometry(imageWidth, imageHeight, blockWidth, blockHeight);
  }

  private void _ConfigureGeometry(int imageWidth, int imageHeight, int blockWidth, int blockHeight) {
    this._width = imageWidth;
    this._height = imageHeight;
    this._blockWidth = blockWidth;
    this._blockHeight = blockHeight;
    this._columns = _BlockCount(imageWidth, blockWidth);
    this._rows = _BlockCount(imageHeight, blockHeight);
    this._canvas = new byte[imageWidth * imageHeight * 3];
  }

  private static int _BlockCount(int imageSize, int blockSize) => (imageSize + blockSize - 1) / blockSize;

  private static int _BlockExtent(int index, int count, int blockSize, int imageSize)
    => index == count - 1 ? imageSize - index * blockSize : blockSize;

  private static byte[] _FlipVertically(byte[] canvas, int height, int stride) {
    var picture = new byte[canvas.Length];
    for (var row = 0; row < height; ++row)
      Array.Copy(canvas, (height - 1 - row) * stride, picture, row * stride, stride);

    return picture;
  }
}
