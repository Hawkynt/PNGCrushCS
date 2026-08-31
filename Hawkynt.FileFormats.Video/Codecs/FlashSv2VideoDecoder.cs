using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using FileFormat.Codecs.FlashSv2;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Decodes Flash Screen Video 2 (FSV2): the same block grid as FSV1, extended with a colourspace that
/// packs a pixel into one byte or two, a compression technique that primes a block against the bytes
/// its cell held at the last key frame rather than restating them, and updates that touch only a run
/// of a cell's rows rather than the whole thing. Read from the SWF File Format Specification's own
/// appendix, like FSV1, and despite the name a genuinely different bitstream rather than a variant of
/// it — nothing below the grid header is shared with <see cref="FlashSvVideoDecoder"/>.
/// </summary>
/// <remarks>
/// <b>The grid header gains one byte of flags.</b> Behind FSV1's four-byte <c>BlockWidth</c>/
/// <c>ImageWidth</c>/<c>BlockHeight</c>/<c>ImageHeight</c> word sits six reserved bits, then
/// <c>HasIFrameImage</c> and <c>HasPaletteInfo</c>. The specification describes <c>HasIFrameImage</c>'s
/// second list of blocks only as interblocks "that must be combined with the previous keyblocks to
/// produce the image", without saying how, and no stream measured sets the flag to check a reading
/// against — so it refuses by name rather than guess at a compositing rule nothing here can verify.
/// <c>HasPaletteInfo</c> carries a new 128-entry colour table as a v1 <c>IMAGEBLOCK</c>: 384 bytes,
/// three a colour, decompressed exactly the way this package's FSV1 decoder already reads one.
/// <para/>
/// <b>Every block carries a format byte the grid itself never had.</b> Three reserved bits, a two-bit
/// <c>ColorDepth</c> — 24-bit RGB, or, measured on every one of hundreds of blocks across every stream
/// this was built against, the 15/7-bit hybrid colourspace — and three coding flags: <c>HasDiffBlocks</c>,
/// <c>ZlibPrimeCompressCurrent</c> and <c>ZlibPrimeCompressPrevious</c>.
/// <para/>
/// <b>The hybrid colourspace is a per-pixel choice, not a per-block one.</b> The specification states the
/// decode directly: fetch a byte; a set high bit means fetch a second byte and read the pair as a 15-bit
/// colour, the first byte's low seven bits over bits 14-8 and the second byte whole over bits 7-0, which
/// this package widens to 24 bits the same way its other 5-5-5 formats already do — five bits repeated
/// rather than shifted; a clear high bit means the low seven bits index the 128-entry palette directly.
/// That makes a block's decompressed byte count unknowable in advance, so a block is decompressed whole
/// and then walked pixel by pixel.
/// <para/>
/// <b>"ZLIB priming" is a preset dictionary keyed to the container's own key frames, not a continued
/// stream.</b> The first reading tried here was this package's own ZMBV decoder's trick — one zlib
/// stream held open per cell — and it is wrong: an unprimed block decompresses alone as a complete,
/// terminated zlib stream, and a stream that already reached its own end cannot be resumed. A primed
/// block's raw bytes, fed to an ordinary DEFLATE decoder with no history, fail outright with a match
/// reaching before the start of the data — the diagnostic a genuine preset dictionary produces and
/// nothing else does. What primes a block, verified against ffmpeg's own decode pixel for pixel, is the
/// exact byte sequence — in this format's own one-or-two-byte coded form — that this same grid cell held
/// the last time the <em>container</em> stated a key frame; a full-coverage block sent on an ordinary
/// interframe does not become that reference, which was found the hard way: two consecutive full,
/// unprimed blocks on interframes decode correctly on their own but are not what the block after them
/// primes against, where the key frame twelve frames earlier still is. Since neither .NET's zlib
/// wrapper nor DEFLATE exposes a preset dictionary, <see cref="RawDeflate"/> reads RFC 1951 itself.
/// <para/>
/// <c>ZlibPrimeCompressCurrent</c> — priming against a *different* cell's data, named by an
/// <c>IMAGEPRIMEPOSITION</c> the header would carry in that case — is not this and never appears in
/// anything measured, so it refuses by name.
/// <para/>
/// <b>Every block is composed onto the reference, not onto the frame before it.</b> Before a block's own
/// rows are written, the whole cell is repainted from the reference the last key frame established — so
/// a transient interframe update, primed or not, that leaves part of a cell untouched shows that
/// reference there and not whatever an earlier interframe happened to leave behind. A diff block's own
/// rows are named by an <c>IMAGEDIFFPOSITION</c> — a row and a count, both counted the way every row in
/// this format already is, from the cell's own bottom — ahead of the pixel data. A block whose count is
/// zero and carries no data at all is not an error: it still repaints the cell from the reference and
/// writes nothing further, which is how a cell that drifted away from its reference through several
/// interframes is put back with three bytes.
/// <para/>
/// <b>Measured against ffmpeg</b>, built with its own flashsv2 encoder. See the codec's section of
/// <c>README.md</c> for the streams, the frame count and what each one exercises.
/// <para/>
/// <b>What refuses.</b> Everything FSV1 already refuses, at the same points; a grid header setting
/// <c>HasIFrameImage</c>; a block format byte naming a colour depth the specification does not define,
/// or setting <c>ZlibPrimeCompressCurrent</c>; a diff block whose row range reaches outside its own
/// cell; a key frame block that does not cover its whole cell, since nothing measured exercises what a
/// partial reference would mean; a primed block whose cell has no reference to prime against; and a
/// decompressed pixel stream that runs out before the pixel count a block's position in the grid calls
/// for.
/// </remarks>
public sealed class FlashSv2VideoDecoder : IVideoCodecDecoder<FlashSv2VideoDecoder> {

  private static readonly CodecTag _Tag = CodecTag.FromCharacters("FSV2");

  /// <summary>DEFLATE's own limit on how far back a match may reach, and so on how much of a cell's
  /// reference a preset dictionary can use.</summary>
  private const int _MaxDictionary = 32768;

  private readonly int _streamIndex;

  private int _width;
  private int _height;
  private int _blockWidth;
  private int _blockHeight;
  private int _columns;
  private int _rows;

  /// <summary>The picture as coded, bottom row first, three bytes (B, G, R) a pixel.</summary>
  private byte[]? _canvas;

  /// <summary>128 entries, three bytes (B, G, R) apiece, replaced whenever a packet carries a palette of
  /// its own and the default from <see cref="FlashSv2Palette"/> until one does.</summary>
  private byte[] _paletteBgr = FlashSv2Palette.DefaultBgr();

  /// <summary>
  /// One grid cell's complete decoded byte buffer as it stood at the last container key frame — the
  /// whole cell repaints from this before any block's own rows are written, and it is the only thing a
  /// primed block's preset dictionary is ever built from.
  /// </summary>
  private readonly Dictionary<int, byte[]> _reference = [];

  private FlashSv2VideoDecoder(int streamIndex) => this._streamIndex = streamIndex;

  /// <summary>Gets the codec name.</summary>
  public static string CodecName => "Flash Screen Video 2";

  /// <summary>Determines whether the specified media stream is supported.</summary>
  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    return stream.Kind == MediaStreamKind.Video && stream.Codec.EqualsIgnoringCase(_Tag);
  }

  /// <summary>Creates a decoder for the specified media stream.</summary>
  public static FlashSv2VideoDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    return new(stream.Index);
  }

  /// <summary>Attempts to decode the specified coded packet into a raw image frame.</summary>
  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    var data = packet.Data.Span;
    if (data.Length < 5)
      throw new InvalidDataException(
        $"Video stream {this._streamIndex} carries a Flash Screen Video 2 packet of {data.Length} byte(s), where "
        + "the grid header alone is five.");

    var blockWidth = (((data[0] >> 4) & 0xF) + 1) * 16;
    var imageWidth = ((data[0] & 0xF) << 8) | data[1];
    var blockHeight = (((data[2] >> 4) & 0xF) + 1) * 16;
    var imageHeight = ((data[2] & 0xF) << 8) | data[3];

    if (imageWidth <= 0 || imageHeight <= 0)
      throw new InvalidDataException(
        $"Video stream {this._streamIndex} states a Flash Screen Video 2 picture size of {imageWidth}x{imageHeight}, "
        + "which no frame can be decoded into.");

    var flags = data[4];
    var hasIFrameImage = (flags & 0x02) != 0;
    var hasPaletteInfo = (flags & 0x01) != 0;

    if (hasIFrameImage)
      throw new NotSupportedException(
        $"Video stream {this._streamIndex} carries a Flash Screen Video 2 packet whose grid header sets "
        + "HasIFrameImage. The specification describes that second list of blocks only as interblocks that must "
        + "be combined with the previous keyblocks, without saying how, and no stream this was measured against "
        + "sets the flag to check a reading against.");

    this._EnsureGeometry(imageWidth, imageHeight, blockWidth, blockHeight);

    var offset = 5;

    if (hasPaletteInfo) {
      if (offset + 2 > data.Length)
        throw new InvalidDataException(
          $"Video stream {this._streamIndex} carries a Flash Screen Video 2 packet whose palette block has no "
          + $"two-byte length left at offset {offset} of {data.Length}.");

      var paletteSize = (data[offset] << 8) | data[offset + 1];
      offset += 2;

      if (paletteSize > 0) {
        if (offset + paletteSize > data.Length)
          throw new InvalidDataException(
            $"Video stream {this._streamIndex} carries a Flash Screen Video 2 palette block stating {paletteSize} "
            + $"compressed byte(s), where only {data.Length - offset} remain in the packet.");

        this._paletteBgr = _InflatePalette(packet.Data.Slice(offset, paletteSize), this._streamIndex);
        offset += paletteSize;
      }
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

        if (blockSize == 0)
          continue; // Unchanged since the picture before this one.

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
    if (block.Length < 1)
      throw new InvalidDataException(
        $"Video stream {this._streamIndex} carries a Flash Screen Video 2 block at grid position "
        + $"({gridColumn},{gridRow}) with no format byte.");

    var format = block[0];
    var colorDepth = (format >> 3) & 0x3;
    var hasDiffBlocks = (format & 0x04) != 0;
    var primeCurrent = (format & 0x02) != 0;
    var primePrevious = (format & 0x01) != 0;

    if (primeCurrent)
      throw new NotSupportedException(
        $"Video stream {this._streamIndex} carries a Flash Screen Video 2 block at grid position "
        + $"({gridColumn},{gridRow}) whose format byte sets ZlibPrimeCompressCurrent — priming against a "
        + "different cell's data, named by an IMAGEPRIMEPOSITION. No stream this was measured against sets it.");

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

      if (pixelRowStart < 0 || pixelRowCount < 0 || pixelRowStart + pixelRowCount > rowHeight)
        throw new InvalidDataException(
          $"Video stream {this._streamIndex} carries a Flash Screen Video 2 diff block at grid position "
          + $"({gridColumn},{gridRow}) stating rows {pixelRowStart}..{pixelRowStart + pixelRowCount}, outside its "
          + $"own {rowHeight}-row cell.");
    }

    if (isKeyFrame && (pixelRowStart != 0 || pixelRowCount != rowHeight))
      throw new NotSupportedException(
        $"Video stream {this._streamIndex} carries a Flash Screen Video 2 block at grid position "
        + $"({gridColumn},{gridRow}) on a key frame that does not cover its whole {rowHeight}-row cell. No stream "
        + "this was measured against does this, and what a partial reference would mean for a later primed block "
        + "is not stated anywhere.");

    var cellKey = gridRow * this._columns + gridColumn;
    var compressed = block[headerOffset..];

    byte[] decoded;
    if (primePrevious) {
      if (!this._reference.TryGetValue(cellKey, out var reference))
        throw new InvalidDataException(
          $"Video stream {this._streamIndex} carries a Flash Screen Video 2 block at grid position "
          + $"({gridColumn},{gridRow}) priming against its own cell before any key frame established one.");

      var dictionary = reference.Length > _MaxDictionary ? reference[^_MaxDictionary..] : reference;
      decoded = RawDeflate.Decode(compressed, dictionary);
    } else
      decoded = _InflateAll(compressed);

    // Every block composes onto the reference the last key frame established, not onto whatever an
    // earlier interframe happened to leave on screen.
    if (this._reference.TryGetValue(cellKey, out var current))
      this._PaintReference(current, canvas, canvasRow, canvasColumn, columnWidth, rowHeight, gridColumn, gridRow);

    if (pixelRowCount > 0)
      this._PaintRows(decoded, canvas, canvasRow + pixelRowStart, canvasColumn, columnWidth, pixelRowCount, gridColumn, gridRow);

    if (isKeyFrame)
      this._reference[cellKey] = decoded;
  }

  private void _PaintReference(byte[] reference, byte[] canvas, int canvasRow, int canvasColumn, int columnWidth, int rowHeight, int gridColumn, int gridRow)
    => this._PaintRows(reference, canvas, canvasRow, canvasColumn, columnWidth, rowHeight, gridColumn, gridRow);

  private void _PaintRows(byte[] decoded, byte[] canvas, int canvasRowStart, int canvasColumn, int columnWidth, int rowCount, int gridColumn, int gridRow) {
    var cursor = 0;
    var stride = this._width * 3;

    for (var i = 0; i < rowCount; ++i) {
      var rowOffset = (canvasRowStart + i) * stride + canvasColumn * 3;

      for (var j = 0; j < columnWidth; ++j)
        cursor = this._DecodePixel(decoded, cursor, canvas, rowOffset + j * 3, gridColumn, gridRow);
    }
  }

  private int _DecodePixel(byte[] decoded, int cursor, byte[] canvas, int destination, int gridColumn, int gridRow) {
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

  private static byte _NextByte(byte[] decoded, ref int cursor, int streamIndex, int gridColumn, int gridRow) {
    if (cursor >= decoded.Length)
      throw new InvalidDataException(
        $"Video stream {streamIndex} carries a Flash Screen Video 2 block at grid position "
        + $"({gridColumn},{gridRow}) whose decompressed data ran out before its pixels did.");

    return decoded[cursor++];
  }

  private static byte _Widen(int channel) => (byte)((channel << 3) | (channel >> 2));

  /// <summary>Decompresses a complete zlib stream to whatever it holds — an unprimed block's decompressed
  /// length is not stated anywhere, since the hybrid colourspace makes it depend on the pixels.</summary>
  private static byte[] _InflateAll(ReadOnlySpan<byte> compressed) {
    if (compressed.IsEmpty)
      return [];

    using var source = new MemoryStream(compressed.ToArray(), writable: false);
    using var zlib = new ZLibStream(source, CompressionMode.Decompress);
    using var output = new MemoryStream();
    zlib.CopyTo(output);
    return output.ToArray();
  }

  /// <summary>Decompresses a v1-shaped <c>IMAGEBLOCK</c> to exactly the 384 bytes a 128-entry, three-byte
  /// colour table needs.</summary>
  private static byte[] _InflatePalette(ReadOnlyMemory<byte> compressed, int streamIndex) {
    const int _paletteBytes = 128 * 3;
    using var source = new MemoryStream(compressed.ToArray(), writable: false);
    using var zlib = new ZLibStream(source, CompressionMode.Decompress);
    var decompressed = new byte[_paletteBytes];
    try {
      zlib.ReadExactly(decompressed);
    } catch (EndOfStreamException ex) {
      throw new InvalidDataException(
        $"Video stream {streamIndex} carries a Flash Screen Video 2 palette block whose zlib data decompresses to "
        + $"fewer than the {_paletteBytes} byte(s) a 128-entry colour table needs.", ex);
    }

    return decompressed;
  }

  private void _EnsureGeometry(int imageWidth, int imageHeight, int blockWidth, int blockHeight) {
    if (this._canvas == null) {
      this._width = imageWidth;
      this._height = imageHeight;
      this._blockWidth = blockWidth;
      this._blockHeight = blockHeight;
      this._columns = _BlockCount(imageWidth, blockWidth);
      this._rows = _BlockCount(imageHeight, blockHeight);
      this._canvas = new byte[imageWidth * imageHeight * 3];
      return;
    }

    if (this._width == imageWidth && this._height == imageHeight
        && this._blockWidth == blockWidth && this._blockHeight == blockHeight)
      return;

    throw new NotSupportedException(
      $"Video stream {this._streamIndex} changes its Flash Screen Video 2 geometry from {this._width}x{this._height} "
      + $"in {this._blockWidth}x{this._blockHeight} blocks to {imageWidth}x{imageHeight} in {blockWidth}x{blockHeight} "
      + "blocks part way through, and neither the canvas nor the per-cell references this decoder built follow it.");
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
