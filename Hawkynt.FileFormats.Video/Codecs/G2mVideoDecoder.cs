using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using FileFormat.Codecs.G2m;
using FileFormat.Codecs.Mpeg4;
using FileFormat.Codecs.MsMpeg4;
using FileFormat.Core;
using FileFormat.Jpeg;

namespace FileFormat.Codecs;

/// <summary>
/// Decodes Go2Meeting / Go2Webinar screen video (<c>G2M2</c>, <c>G2M3</c>, <c>G2M4</c>): tiles coded
/// with ePIC and JPEG, or with Kempf's palette, zlib and JPEG, and the cursor drawn over them.
/// </summary>
/// <remarks>
/// Converted from FFmpeg's <c>libavcodec/g2meet.c</c> and <c>elsdec.c</c> (LGPL-2.1-or-later); see
/// <c>G2m/THIRD-PARTY-NOTICE.FFmpeg.txt</c>. A packet is a list of chunks: display information that
/// sets the picture and tile sizes, tiles, the cursor's shape and position. The picture lives in a
/// frame buffer the tiles paint and every packet hands out, with the cursor alpha-blended over the copy.
/// A tile that fails to decode is left as it was, as FFmpeg leaves it, and a packet FFmpeg refuses
/// whole gives no frame. Hands out <see cref="PixelFormat.Rgb24"/> at the size the stream states.
/// </remarks>
public sealed class G2mVideoDecoder : IVideoCodecDecoder<G2mVideoDecoder> {

  private static readonly CodecTag[] _Tags = [
    CodecTag.FromCharacters("G2M2"), CodecTag.FromCharacters("G2M3"), CodecTag.FromCharacters("G2M4"), CodecTag.FromCharacters("G2M5"),
  ];

  private const int _DISPLAY_INFO = 0xC8;
  private const int _TILE_DATA = 0xC9;
  private const int _CURSOR_POSITION = 0xCA;
  private const int _CURSOR_SHAPE = 0xCB;

  private static readonly MsMpeg4VlcTable[] _Dc = [
    _Huffman(JpegStandardTables.DcLuminanceBits, JpegStandardTables.DcLuminanceValues, ac: false),
    _Huffman(JpegStandardTables.DcChrominanceBits, JpegStandardTables.DcChrominanceValues, ac: false),
  ];

  private static readonly MsMpeg4VlcTable[] _Ac = [
    _Huffman(JpegStandardTables.AcLuminanceBits, JpegStandardTables.AcLuminanceValues, ac: true),
    _Huffman(JpegStandardTables.AcChrominanceBits, JpegStandardTables.AcChrominanceValues, ac: true),
  ];

  private readonly EpicDecoder _epic = new();
  private readonly short[][] _blocks = [new short[64], new short[64], new short[64], new short[64], new short[64], new short[64]];
  private readonly int[] _previousDc = new int[3];

  private int _width;
  private int _height;
  private int _compression;
  private int _tileWidth;
  private int _tileHeight;
  private int _tilesX;
  private int _tilesY;
  private bool _swapUv;

  private byte[]? _frame;
  private int _frameStride;
  private byte[] _jpegTile = [];
  private int _tileStride;
  private uint[] _epicBuffer = [];
  private int _epicStride;
  private byte[] _kempfFlags = [];
  private int _oldTileWidth;
  private int _oldTileHeight;

  private byte[]? _cursor;
  private int _cursorStride;
  private int _cursorWidth;
  private int _cursorHeight;
  private int _cursorX;
  private int _cursorY;
  private int _cursorHotX;
  private int _cursorHotY;

  private G2mVideoDecoder() {
  }

  public static string CodecName => "Go2Meeting";

  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.Kind != MediaStreamKind.Video)
      return false;
    foreach (var tag in _Tags)
      if (stream.Codec.EqualsIgnoringCase(tag))
        return true;
    return false;
  }

  public static G2mVideoDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (!Accepts(stream))
      throw new NotSupportedException($"Stream {stream.Index} is not Go2Meeting video.");
    return new();
  }

  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    frame = null!;
    var data = packet.Data.ToArray();
    if (data.Length < 12)
      return false;

    var magic = BinaryPrimitives.ReadUInt32BigEndian(data);
    if ((magic & ~0xFu) != 0x47324D30u || (magic & 0xF) < 2 || (magic & 0xF) > 5)
      return false;
    this._swapUv = magic == 0x47324D32u;

    var at = 4;
    while (data.Length - at > 5) {
      var chunkSize = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(at)) - 1;
      var chunkType = data[at + 4];
      at += 5;
      var chunkStart = at;
      if (chunkSize > (uint)(data.Length - at))
        break;
      var size = (int)chunkSize;

      switch (chunkType) {
        case _DISPLAY_INFO:
          if (size >= 21 && !this._DisplayInfo(data, at, size))
            return false;
          break;
        case _TILE_DATA:
          if (this._tilesX == 0 || this._tilesY == 0 || size < 2)
            break;
          var tileX = data[at];
          var tileY = data[at + 1];
          if (tileX >= this._tilesX || tileY >= this._tilesY)
            break;
          try {
            if (this._compression == 2)
              this._EpicTile(tileX, tileY, data, at + 2, size - 2);
            else
              this._KempfTile(tileX, tileY, data, at + 2, size - 2);
          } catch (InvalidDataException) {
            // FFmpeg logs the tile and goes on with the rest of the packet.
          }

          break;
        case _CURSOR_POSITION:
          if (size >= 5) {
            this._cursorX = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(at));
            this._cursorY = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(at + 2));
          }

          break;
        case _CURSOR_SHAPE:
          if (size >= 8)
            this._LoadCursor(data, at, size - 4);
          break;
      }

      at = chunkStart + size;
    }

    if (this._width == 0 || this._height == 0 || this._frame == null)
      return false;

    var output = new byte[this._width * this._height * 3];
    for (var y = 0; y < this._height; ++y)
      Array.Copy(this._frame, y * this._frameStride, output, y * this._width * 3, this._width * 3);
    this._PaintCursor(output);
    frame = new() { Width = this._width, Height = this._height, Format = PixelFormat.Rgb24, PixelData = output };
    return true;
  }

  // ============================================================================================
  // Display information and buffers
  // ============================================================================================

  /// <summary>The display information chunk; <c>false</c> where FFmpeg fails the packet over it.</summary>
  private bool _DisplayInfo(byte[] data, int at, int size) {
    bool Fail() {
      this._width = this._height = this._tilesX = this._tilesY = this._tileWidth = this._tileHeight = 0;
      return false;
    }

    var width = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(at));
    var height = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(at + 4));
    if (width < 16 || height < 16)
      return Fail();
    this._width = width;
    this._height = height;
    this._compression = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(at + 8));
    if (this._compression is not (2 or 3))
      return Fail();
    this._tileWidth = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(at + 12));
    this._tileHeight = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(at + 16));
    if (this._tileWidth <= 0 || this._tileHeight <= 0 || ((this._tileWidth | this._tileHeight) & 0xF) != 0
        || (long)this._tileWidth * this._tileHeight >= int.MaxValue / 4)
      return Fail();
    this._tilesX = (this._width + this._tileWidth - 1) / this._tileWidth;
    this._tilesY = (this._height + this._tileHeight - 1) / this._tileHeight;
    var bpp = data[at + 20];
    if (bpp != 32 || data.Length - (at + 21) < 16 || size - 21 < 16)
      return Fail();
    if (BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(at + 21)) != 0xFF0000
        || BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(at + 25)) != 0xFF00
        || BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(at + 29)) != 0xFF)
      return Fail();

    // g2m_init_buffers: the frame buffer only ever grows, keeping what it holds; the tile buffers are
    // made afresh, zeroed, when the tiles grow.
    this._frameStride = ((this._width + 15 + 15) & ~15) * 3;
    var frameSize = this._frameStride * (this._height + 15);
    if (this._frame == null || this._frame.Length < frameSize)
      this._frame = new byte[frameSize];
    if (this._jpegTile.Length == 0 || (this._compression == 2 && this._epicBuffer.Length == 0)
        || this._oldTileWidth < this._tileWidth || this._oldTileHeight < this._tileHeight) {
      this._tileStride = ((this._tileWidth + 15) & ~15) * 3;
      this._epicStride = ((this._tileWidth * 4 + 15) & ~15) / 4;
      var alignedHeight = (this._tileHeight + 15) & ~15;
      this._jpegTile = new byte[this._tileStride * alignedHeight];
      this._kempfFlags = new byte[this._tileWidth * alignedHeight];
      this._epicBuffer = this._compression == 2 ? new uint[this._epicStride * alignedHeight + 1] : [];
    }

    return true;
  }

  // ============================================================================================
  // ePIC tiles
  // ============================================================================================

  private void _EpicTile(int tileX, int tileY, byte[] data, int at, int size) {
    if (size == 0)
      return;

    var prefix = data[at++];
    --size;
    var mask = 0x80;
    var extra = 0;
    for (; (prefix & mask) != 0 && extra < 7; ++extra)
      mask >>= 1;
    if (extra > 3 || size < extra)
      throw new InvalidDataException("A Go2Meeting ePIC tile states an invalid data size.");
    long elsSize = prefix & ((0x80 >> extra) - 1);
    while (extra-- > 0) {
      elsSize = (elsSize << 8) | data[at++];
      --size;
    }

    if (size < elsSize)
      throw new InvalidDataException("A Go2Meeting ePIC tile is shorter than it says.");

    var tileWidth = Math.Min(this._width - tileX * this._tileWidth, this._tileWidth);
    var tileHeight = Math.Min(this._height - tileY * this._tileHeight, this._tileHeight);
    var alignedWidth = (tileWidth + 15) & ~15;
    var alignedHeight = (tileHeight + 15) & ~15;
    if (tileWidth > EpicDecoder.MaxTileWidth)
      throw new InvalidDataException("A Go2Meeting ePIC tile is wider than its run coder can describe.");

    var frameAt = tileX * this._tileWidth * 3 + tileY * this._tileHeight * this._frameStride;
    if (elsSize == 0) {
      this._JpegDecode(tileWidth, tileHeight, data, at, size, this._frame!, frameAt, this._frameStride, null, 0, 0, this._swapUv);
      return;
    }

    var (r, g, b) = this._epic.Begin(data, at, (int)elsSize);
    this._epic.DecodeTile(this._epicBuffer, 1, this._epicStride, tileWidth, tileHeight);

    for (var j = 0; j < tileHeight; ++j)
      for (var i = 0; i < tileWidth; ++i) {
        var pixel = this._epicBuffer[1 + j * this._epicStride + i];
        var target = frameAt + j * this._frameStride + i * 3;
        this._frame![target] = (byte)(pixel >> 16);
        this._frame[target + 1] = (byte)(pixel >> 8);
        this._frame[target + 2] = (byte)pixel;
      }

    if (size <= elsSize)
      return;

    at += (int)elsSize;
    size -= (int)elsSize;
    var transparent = (uint)(r << 16 | g << 8 | b);
    var blockStride = alignedWidth >> 3;
    var blocks = 0;
    Array.Clear(this._kempfFlags, 0, (alignedHeight >> 3) * blockStride);
    for (var j = 0; j < tileHeight; j += 8)
      for (var i = 0; i < tileWidth; i += 8) {
        this._kempfFlags[(i >> 3) + (j >> 3) * blockStride] = 0;
        for (var k = 0; k < 64; ++k)
          if (this._epicBuffer[1 + (j + (k >> 3)) * this._epicStride + i + (k & 7)] == transparent) {
            this._kempfFlags[(i >> 3) + (j >> 3) * blockStride] = 1;
            ++blocks;
            break;
          }
      }

    Array.Clear(this._jpegTile, 0, this._tileStride * alignedHeight);
    try {
      this._JpegDecode(alignedWidth, alignedHeight, data, at, size, this._jpegTile, 0, this._tileStride, this._kempfFlags, blockStride, blocks, this._swapUv);
    } catch (InvalidDataException) {
      // FFmpeg ignores the JPEG decode's result here and composites what it got.
    }

    for (var j = 0; j < tileHeight; ++j)
      for (var i = 0; i < tileWidth; ++i)
        if (this._epicBuffer[1 + j * this._epicStride + i] == transparent)
          Array.Copy(this._jpegTile, j * this._tileStride + i * 3, this._frame!, frameAt + j * this._frameStride + i * 3, 3);
  }

  // ============================================================================================
  // Kempf tiles
  // ============================================================================================

  private void _KempfTile(int tileX, int tileY, byte[] data, int at, int size) {
    if (size < 2)
      throw new InvalidDataException("A Go2Meeting Kempf tile is too short.");
    var end = at + size;
    var width = Math.Min(this._width - tileX * this._tileWidth, this._tileWidth);
    var height = Math.Min(this._height - tileY * this._tileHeight, this._tileHeight);
    var frameAt = tileX * this._tileWidth * 3 + tileY * this._tileHeight * this._frameStride;

    var header = data[at++];
    var subType = header >> 5;
    if (subType == 0) {
      for (var j = 0; j < height; ++j)
        for (var i = 0; i < width; ++i)
          Array.Copy(data, at, this._frame!, frameAt + j * this._frameStride + i * 3, 3);
      return;
    }

    if (subType == 1) {
      this._JpegDecode(width, height, data, at, end - at, this._frame!, frameAt, this._frameStride, null, 0, 0, false);
      return;
    }

    var transparent = -1;
    var transparentAt = at;
    if (subType != 2)
      at += 3;
    var paletteSize = data[at++] + 1;
    if (end - at < paletteSize * 3)
      throw new InvalidDataException("A Go2Meeting Kempf tile's palette runs past its end.");
    var palette = data.AsSpan(at, paletteSize * 3).ToArray();
    at += paletteSize * 3;
    if (subType != 2)
      for (var i = 0; i < paletteSize; ++i)
        if (palette[i * 3] == data[transparentAt] && palette[i * 3 + 1] == data[transparentAt + 1] && palette[i * 3 + 2] == data[transparentAt + 2]) {
          transparent = i;
          break;
        }

    if (end - at < 2)
      return;
    var zsize = (data[at] << 8) | data[at + 1];
    at += 2;
    if (end - at < zsize + (subType != 2 ? 1 : 0))
      throw new InvalidDataException("A Go2Meeting Kempf tile's compressed part runs past its end.");

    var inflated = _Inflate(data.AsSpan(at, zsize), (this._tileWidth + 1) * this._tileHeight);
    at += zsize;

    if (subType == 2) {
      this._KempfRestore(inflated, frameAt, null, width, height, palette, paletteSize, transparent);
      return;
    }

    var blockCount = data[at++] + 1;
    var coded = 0;
    var blockStride = ((width + 15) & ~15) >> 3;
    var bits = 0;
    var bitBuffer = 0;
    for (var i = 0; i < ((height + 15) & ~15) >> 4; ++i)
      for (var j = 0; j < ((width + 15) & ~15) >> 4; ++j) {
        if (bits == 0) {
          if (at >= end)
            throw new InvalidDataException("A Go2Meeting Kempf tile ends inside its block mask.");
          bitBuffer = data[at++];
          bits = 8;
        }

        var flag = (byte)(bitBuffer & 1);
        --bits;
        bitBuffer >>= 1;
        coded += flag;
        if (coded > blockCount)
          throw new InvalidDataException("A Go2Meeting Kempf tile codes more blocks than it states.");
        this._kempfFlags[j * 2 + i * 2 * blockStride] = flag;
        this._kempfFlags[j * 2 + 1 + i * 2 * blockStride] = flag;
        this._kempfFlags[j * 2 + (i * 2 + 1) * blockStride] = flag;
        this._kempfFlags[j * 2 + 1 + (i * 2 + 1) * blockStride] = flag;
      }

    Array.Clear(this._jpegTile, 0, this._tileStride * height);
    try {
      this._JpegDecode(width, height, data, at, end - at, this._jpegTile, 0, this._tileStride, this._kempfFlags, blockStride, blockCount * 4, false);
    } catch (InvalidDataException) {
      // As FFmpeg, the restore goes ahead with whatever the JPEG part produced.
    }

    this._KempfRestore(inflated, frameAt, this._jpegTile, width, height, palette, paletteSize, transparent);
  }

  /// <summary><c>kempf_restore_buf</c>.</summary>
  private void _KempfRestore(byte[] source, int frameAt, byte[]? jpeg, int width, int height, byte[] palette, int paletteSize, int transparent) {
    var bitsPerPixel = paletteSize <= 2 ? 1 : paletteSize <= 4 ? 2 : paletteSize <= 16 ? 4 : 8;
    var alignedWidth = (width + 15) & ~15;
    var reader = new Mpeg4BitReader(source);
    for (var j = 0; j < height; ++j) {
      if (_Bits(ref reader, 8) != 0)
        continue;
      for (var i = 0; i < width; ++i) {
        var colour = _Bits(ref reader, bitsPerPixel);
        if (colour >= paletteSize)
          throw new InvalidDataException("A Go2Meeting Kempf pixel names a colour past its palette.");
        var target = frameAt + j * this._frameStride + i * 3;
        if (colour != transparent)
          Array.Copy(palette, colour * 3, this._frame!, target, 3);
        else
          Array.Copy(jpeg!, j * this._tileStride + i * 3, this._frame!, target, 3);
      }

      reader.Skip(bitsPerPixel * (alignedWidth - width));
    }
  }

  private static byte[] _Inflate(ReadOnlySpan<byte> compressed, int maximum) {
    try {
      using var input = new MemoryStream(compressed.ToArray(), writable: false);
      using var zlib = new ZLibStream(input, CompressionMode.Decompress);
      using var output = new MemoryStream();
      var buffer = new byte[8192];
      int read;
      while ((read = zlib.Read(buffer, 0, buffer.Length)) > 0) {
        if (output.Length + read > maximum)
          throw new InvalidDataException("A Go2Meeting Kempf tile inflates past the size zlib was given.");
        output.Write(buffer, 0, read);
      }

      return output.ToArray();
    } catch (Exception ex) when (ex is IOException or NotSupportedException) {
      throw new InvalidDataException("A Go2Meeting Kempf tile carries invalid zlib data.", ex);
    }
  }

  // ============================================================================================
  // JPEG: jpg_decode_data and jpg_decode_block
  // ============================================================================================

  private void _JpegDecode(
    int width, int height, byte[] data, int at, int size, byte[] target, int targetAt, int targetStride,
    byte[]? mask, int maskStride, int blockCount, bool swapUv) {
    // Unescape the stuffed 0xFF 0x00 pairs.
    var unescaped = new byte[size];
    var length = 0;
    for (var i = at; i < at + size;) {
      var x = data[i++];
      unescaped[length++] = x;
      if (x == 0xFF && i < at + size && data[i] == 0)
        ++i;
    }

    var reader = new Mpeg4BitReader(unescaped.AsSpan(0, length));
    width = (width + 15) & ~15;
    var macroblockWidth = width >> 4;
    var macroblockHeight = (height + 15) >> 4;
    if (blockCount == 0)
      blockCount = macroblockWidth * macroblockHeight * 4;

    this._previousDc[0] = this._previousDc[1] = this._previousDc[2] = 1024;
    foreach (var block in this._blocks)
      Array.Clear(block);

    var red = swapUv ? 2 : 0;
    var maskAt = 0;
    var bx = 0;
    var by = 0;
    for (var mbY = 0; mbY < macroblockHeight; ++mbY) {
      for (var mbX = 0; mbX < macroblockWidth; ++mbX) {
        if (mask != null && mask[maskAt + mbX * 2] == 0 && mask[maskAt + mbX * 2 + 1] == 0
            && mask[maskAt + mbX * 2 + maskStride] == 0 && mask[maskAt + mbX * 2 + 1 + maskStride] == 0) {
          bx += 16;
          continue;
        }

        for (var j = 0; j < 2; ++j)
          for (var i = 0; i < 2; ++i) {
            if (mask != null && mask[maskAt + mbX * 2 + i + j * maskStride] == 0)
              continue;
            --blockCount;
            this._JpegBlock(ref reader, 0, this._blocks[i + j * 2]);
          }

        for (var i = 1; i < 3; ++i)
          this._JpegBlock(ref reader, i, this._blocks[i + 3]);

        for (var j = 0; j < 16; ++j) {
          var row = targetAt + bx * 3 + (by + j) * targetStride;
          for (var i = 0; i < 16; ++i) {
            int y = this._blocks[(j >> 3) * 2 + (i >> 3)][(i & 7) + (j & 7) * 8];
            var u = this._blocks[4][(i >> 1) + (j >> 1) * 8] - 128;
            var v = this._blocks[5][(i >> 1) + (j >> 1) * 8] - 128;
            var o = row + i * 3;
            target[o + red] = _Clip(y + ((91881 * v + 32768) >> 16));
            target[o + 1] = _Clip(y + ((-22554 * u - 46802 * v + 32768) >> 16));
            target[o + 2 - red] = _Clip(y + ((116130 * u + 32768) >> 16));
          }
        }

        if (blockCount == 0)
          return;
        bx += 16;
      }

      bx = 0;
      by += 16;
      if (mask != null)
        maskAt += maskStride * 2;
    }
  }

  /// <summary><c>jpg_decode_block</c>, then the integer inverse transform FFmpeg's <c>idct</c> is with <c>-flags +bitexact</c>.</summary>
  private void _JpegBlock(ref Mpeg4BitReader reader, int plane, short[] block) {
    if (reader.BitsRemaining < 1)
      throw new InvalidDataException("A Go2Meeting JPEG part ends before its last block.");

    var chroma = plane != 0 ? 1 : 0;
    var quant = chroma != 0 ? G2mTables.ChromaQuant : G2mTables.LumaQuant;
    Array.Clear(block);
    var dc = _Dc[chroma].Read(ref reader);
    if (dc != 0)
      dc = _ExtendedBits(ref reader, dc);
    dc = dc * quant[0] + this._previousDc[plane];
    block[0] = (short)dc;
    this._previousDc[plane] = dc;

    var position = 0;
    while (position < 63) {
      var value = _Ac[chroma].Read(ref reader);
      position += value >> 4;
      value &= 0xF;
      if (position > 63) {
        if (value != 0)
          throw new InvalidDataException("A Go2Meeting JPEG block runs past its last coefficient.");
        break;
      }

      if (value != 0)
        block[Mpeg4Quantisation.ZigZag[position]] = (short)(_ExtendedBits(ref reader, value) * quant[position]);
    }

    Span<int> samples = stackalloc int[64];
    for (var i = 0; i < 64; ++i)
      samples[i] = block[i];
    Dv.DvInverseDct.Transform(samples);
    for (var i = 0; i < 64; ++i)
      block[i] = (short)samples[i];
  }

  /// <summary><c>get_xbits</c>: JPEG's sign-folded magnitude.</summary>
  private static int _ExtendedBits(ref Mpeg4BitReader reader, int count) {
    var value = _Bits(ref reader, count);
    return value >> (count - 1) != 0 ? value : value - ((1 << count) - 1);
  }

  /// <summary>Bits read with FFmpeg's zero padding past the end.</summary>
  private static int _Bits(ref Mpeg4BitReader reader, int count) {
    var value = reader.NextBits(count);
    reader.Skip(count);
    return value;
  }

  /// <summary>An MJPEG table as <c>ff_mjpeg_build_vlc</c> builds it: AC symbols shifted up by sixteen, end of block 4096.</summary>
  private static MsMpeg4VlcTable _Huffman(byte[] bits, byte[] values, bool ac) {
    var codes = new int[values.Length];
    var lengths = new int[values.Length];
    var symbols = new int[values.Length];
    var code = 0;
    var k = 0;
    for (var length = 1; length <= 16; ++length) {
      for (var i = 0; i < bits[length - 1]; ++i, ++k) {
        codes[k] = code++;
        lengths[k] = length;
        symbols[k] = !ac ? values[k] : values[k] == 0 ? 16 * 256 : values[k] + 16;
      }

      code <<= 1;
    }

    return MsMpeg4VlcTable.FromCodes("a Go2Meeting JPEG table", codes[..k], lengths[..k], symbols[..k]);
  }

  private static byte _Clip(int value) => value < 0 ? (byte)0 : value > 255 ? (byte)255 : (byte)value;

  // ============================================================================================
  // The cursor
  // ============================================================================================

  /// <summary><c>g2m_load_cursor</c>.</summary>
  private void _LoadCursor(byte[] data, int at, int size) {
    var end = at + size;
    int Byte() => at < end ? data[at++] : 0;
    uint Be32() => (uint)(Byte() << 24 | Byte() << 16 | Byte() << 8 | Byte());

    var cursorSize = Be32();
    var width = Byte();
    var height = Byte();
    var hotX = Byte();
    var hotY = Byte();
    var format = Byte();
    var stride = ((width + (format == 1 ? 31 : 0)) & (format == 1 ? ~31 : ~0)) * 4;

    if (width is < 1 or > 256 || height is < 1 or > 256)
      return;
    if (hotX > width || hotY > height) {
      hotX = Math.Min(hotX, width - 1);
      hotY = Math.Min(hotY, height - 1);
    }

    if (cursorSize - 9 > (uint)(end - at) || this._cursorWidth * this._cursorHeight / 4 > cursorSize)
      return;
    if (format is not (1 or 32))
      return;

    var cursor = new byte[stride * height];
    if (this._cursor != null)
      Array.Copy(this._cursor, cursor, Math.Min(cursor.Length, this._cursor.Length));
    this._cursor = cursor;
    this._cursorWidth = width;
    this._cursorHeight = height;
    this._cursorHotX = hotX;
    this._cursorHotY = hotY;
    this._cursorStride = stride;

    if (format == 1) {
      var d = 0;
      for (var j = 0; j < height; ++j)
        for (var i = 0; i < width; i += 32) {
          var bits = Be32();
          for (var k = 0; k < 32; ++k, d += 4, bits <<= 1)
            cursor[d] = (byte)(bits >> 31);
        }

      d = 0;
      for (var j = 0; j < height; ++j)
        for (var i = 0; i < width; i += 32) {
          var bits = Be32();
          for (var k = 0; k < 32; ++k, d += 4, bits <<= 1) {
            var maskBit = (int)(bits >> 31);
            switch (cursor[d] * 2 + maskBit) {
              case 0:
                cursor[d] = 0xFF;
                cursor[d + 1] = cursor[d + 2] = cursor[d + 3] = 0;
                break;
              case 1:
                cursor[d] = cursor[d + 1] = cursor[d + 2] = cursor[d + 3] = 0xFF;
                break;
              default:
                cursor[d] = cursor[d + 1] = cursor[d + 2] = cursor[d + 3] = 0;
                break;
            }
          }
        }
    } else {
      at = Math.Min(end, at + height * (((width + 31) & ~31) >> 3));
      var d = 0;
      for (var j = 0; j < height; ++j)
        for (var i = 0; i < width; ++i) {
          var value = Be32();
          cursor[d++] = (byte)value;
          cursor[d++] = (byte)(value >> 8);
          cursor[d++] = (byte)(value >> 16);
          cursor[d++] = (byte)(value >> 24);
        }
    }
  }

  /// <summary><c>g2m_paint_cursor</c>: alpha-blended over the frame handed out, never over the frame buffer.</summary>
  private void _PaintCursor(byte[] output) {
    if (this._cursor == null)
      return;
    var x = this._cursorX - this._cursorHotX;
    var y = this._cursorY - this._cursorHotY;
    var w = this._cursorWidth;
    var h = this._cursorHeight;
    var stride = this._width * 3;
    var cursorAt = 0;
    var target = 0;
    if (x + w > this._width)
      w = this._width - x;
    if (y + h > this._height)
      h = this._height - y;
    if (x < 0) {
      w += x;
      cursorAt += -x * 4;
    } else {
      target += x * 3;
    }

    if (y < 0)
      h += y;
    if (w < 0 || h < 0)
      return;
    if (y < 0)
      cursorAt += -y * this._cursorStride;
    else
      target += y * stride;

    for (var j = 0; j < h; ++j, target += stride, cursorAt += this._cursorStride)
      for (var i = 0; i < w; ++i) {
        int alpha = this._cursor[cursorAt + i * 4];
        for (var c = 0; c < 3; ++c) {
          var o = target + i * 3 + c;
          output[o] = (byte)((output[o] * (256 - alpha) + this._cursor[cursorAt + i * 4 + 1 + c] * alpha) >> 8);
        }
      }
  }
}
