using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Codecs.Mpeg;

namespace FileFormat.Codecs.Ea;

/// <summary>The constants Electronic Arts' DCT codecs share with FFmpeg's MPEG code.</summary>
/// <remarks>
/// <see cref="InverseAanScales"/> is FFmpeg's <c>ff_inv_aanscales</c> (<c>libavcodec/aandcttab.c</c>),
/// <see cref="Mpeg1DefaultIntra"/> its <c>ff_mpeg1_default_intra_matrix</c>, and <see cref="ZigZag"/>
/// its <c>ff_zigzag_direct</c>, all LGPL-2.1-or-later; fixed data, reproduced exactly.
/// </remarks>
internal static class EaTables {

  internal static ReadOnlySpan<ushort> InverseAanScales => [
    4096, 2953, 3135, 3483, 4096, 5213, 7568, 14846,
    2953, 2129, 2260, 2511, 2953, 3759, 5457, 10703,
    3135, 2260, 2399, 2666, 3135, 3990, 5793, 11363,
    3483, 2511, 2666, 2962, 3483, 4433, 6436, 12625,
    4096, 2953, 3135, 3483, 4096, 5213, 7568, 14846,
    5213, 3759, 3990, 4433, 5213, 6635, 9633, 18895,
    7568, 5457, 5793, 6436, 7568, 9633, 13985, 27432,
    14846, 10703, 11363, 12625, 14846, 18895, 27432, 53809,
  ];

  internal static ReadOnlySpan<byte> Mpeg1DefaultIntra => [
    8, 16, 19, 22, 26, 27, 29, 34,
    16, 16, 22, 24, 27, 29, 34, 37,
    19, 22, 26, 27, 29, 34, 34, 38,
    22, 22, 26, 27, 29, 34, 37, 40,
    22, 26, 27, 29, 32, 35, 40, 48,
    26, 27, 29, 32, 35, 40, 48, 58,
    26, 27, 29, 34, 38, 46, 56, 69,
    27, 29, 35, 38, 46, 56, 69, 83,
  ];

  internal static ReadOnlySpan<byte> ZigZag => [
    0, 1, 8, 16, 9, 2, 3, 10, 17, 24, 32, 25, 18, 11, 4, 5,
    12, 19, 26, 33, 40, 48, 41, 34, 27, 20, 13, 6, 7, 14, 21, 28,
    35, 42, 49, 56, 57, 50, 43, 36, 29, 22, 15, 23, 30, 37, 44, 51,
    58, 59, 52, 45, 38, 31, 39, 46, 53, 60, 61, 54, 47, 55, 62, 63,
  ];
}

/// <summary>
/// Electronic Arts' inverse DCT, FFmpeg's <c>ff_ea_idct_put_c</c> (<c>libavcodec/eaidct.c</c>,
/// LGPL-2.1-or-later): columns at full precision, rows shifted down four and clamped.
/// </summary>
internal static class EaIdct {

  private const int _ASQRT = 181;
  private const int _A4 = 669;
  private const int _A2 = 277;
  private const int _A5 = 196;

  /// <summary>Transforms <paramref name="block"/> and puts the samples into the plane.</summary>
  internal static void Put(Span<short> block, byte[] plane, int offset, int stride) {
    Span<short> temporary = stackalloc short[64];
    block[0] += 4;
    for (var i = 0; i < 8; ++i) {
      if ((block[i + 8] | block[i + 16] | block[i + 24] | block[i + 32] | block[i + 40] | block[i + 48] | block[i + 56]) == 0) {
        for (var k = 0; k < 8; ++k)
          temporary[i + k * 8] = block[i];
      } else
        _Transform(block, i, 8, temporary, i, 8, false, null, 0);
    }

    for (var i = 0; i < 8; ++i)
      _Transform(temporary, i * 8, 1, default, 0, 1, true, plane, offset + i * stride);
  }

  private static void _Transform(
    ReadOnlySpan<short> source, int s, int step, Span<short> destination, int d, int dstep, bool munge, byte[]? plane, int at) {
    int s0 = source[s], s1 = source[s + step], s2 = source[s + 2 * step], s3 = source[s + 3 * step];
    int s4 = source[s + 4 * step], s5 = source[s + 5 * step], s6 = source[s + 6 * step], s7 = source[s + 7 * step];
    var a1 = s1 + s7;
    var a7 = s1 - s7;
    var a5 = s5 + s3;
    var a3 = s5 - s3;
    var a2 = s2 + s6;
    var a6 = (_ASQRT * (s2 - s6)) >> 8;
    var a0 = s0 + s4;
    var a4 = s0 - s4;
    var b0 = (((_A4 - _A5) * a7 - _A5 * a3) >> 9) + a1 + a5;
    var b1 = (((_A4 - _A5) * a7 - _A5 * a3) >> 9) + ((_ASQRT * (a1 - a5)) >> 8);
    var b2 = (((_A2 + _A5) * a3 + _A5 * a7) >> 9) + ((_ASQRT * (a1 - a5)) >> 8);
    var b3 = ((_A2 + _A5) * a3 + _A5 * a7) >> 9;
    Span<int> output = [a0 + a2 + a6 + b0, a4 + a6 + b1, a4 - a6 + b2, a0 - a2 - a6 + b3,
      a0 - a2 - a6 - b3, a4 - a6 - b2, a4 + a6 - b1, a0 + a2 + a6 - b0];
    for (var k = 0; k < 8; ++k)
      if (munge) {
        var value = output[k] >> 4;
        plane![at + k] = (byte)(value < 0 ? 0 : value > 255 ? 255 : value);
      } else
        destination[d + k * dstep] = (short)output[k];
  }
}

/// <summary>Planes at the macroblock-aligned size, which both codecs write into and crop.</summary>
internal sealed class EaPicture {
  internal readonly int Width, Height, LumaStride, ChromaStride, LumaRows, ChromaRows;
  internal readonly byte[] Y, U, V;

  internal EaPicture(int width, int height) {
    this.Width = width;
    this.Height = height;
    this.LumaStride = (width + 15) & ~15;
    this.LumaRows = (height + 15) & ~15;
    this.ChromaStride = this.LumaStride / 2;
    this.ChromaRows = this.LumaRows / 2;
    this.Y = new byte[this.LumaStride * this.LumaRows];
    this.U = new byte[this.ChromaStride * this.ChromaRows];
    this.V = new byte[this.ChromaStride * this.ChromaRows];
  }

  /// <summary>The visible part as planar 4:2:0.</summary>
  internal byte[] ToYuv420() {
    int cw = (this.Width + 1) / 2, ch = (this.Height + 1) / 2;
    var data = new byte[this.Width * this.Height + 2 * cw * ch];
    for (var y = 0; y < this.Height; ++y)
      this.Y.AsSpan(y * this.LumaStride, this.Width).CopyTo(data.AsSpan(y * this.Width));
    var u = this.Width * this.Height;
    for (var y = 0; y < ch; ++y) {
      this.U.AsSpan(y * this.ChromaStride, cw).CopyTo(data.AsSpan(u + y * cw));
      this.V.AsSpan(y * this.ChromaStride, cw).CopyTo(data.AsSpan(u + cw * ch + y * cw));
    }
    return data;
  }

  /// <summary>Puts six transformed blocks into macroblock (<paramref name="column"/>, <paramref name="row"/>).</summary>
  internal void PutMacroblock(short[] blocks, int column, int row) {
    var y = row * 16 * this.LumaStride + column * 16;
    EaIdct.Put(blocks.AsSpan(0, 64), this.Y, y, this.LumaStride);
    EaIdct.Put(blocks.AsSpan(64, 64), this.Y, y + 8, this.LumaStride);
    EaIdct.Put(blocks.AsSpan(128, 64), this.Y, y + 8 * this.LumaStride, this.LumaStride);
    EaIdct.Put(blocks.AsSpan(192, 64), this.Y, y + 8 * this.LumaStride + 8, this.LumaStride);
    var c = row * 8 * this.ChromaStride + column * 8;
    EaIdct.Put(blocks.AsSpan(256, 64), this.U, c, this.ChromaStride);
    EaIdct.Put(blocks.AsSpan(320, 64), this.V, c, this.ChromaStride);
  }
}

/// <summary>
/// The TQI picture decoder: MPEG-1 intra blocks with a quality-derived AAN matrix, FFmpeg's
/// <c>libavcodec/eatqi.c</c> and <c>ff_mpeg1_decode_block_intra</c> (LGPL-2.1-or-later).
/// </summary>
internal static class TqiCodec {

  /// <summary>FFmpeg's <c>tqi_calculate_qtable</c>.</summary>
  internal static ushort[] Matrix(int quality) {
    var matrix = new ushort[64];
    long scale = (215 - 2 * quality) * 5;
    matrix[0] = (ushort)((EaTables.InverseAanScales[0] * EaTables.Mpeg1DefaultIntra[0]) >> 11);
    for (var i = 1; i < 64; ++i)
      matrix[i] = (ushort)((EaTables.InverseAanScales[i] * EaTables.Mpeg1DefaultIntra[i] * scale + 32) >> 14);
    return matrix;
  }

  internal static EaPicture Decode(ReadOnlySpan<byte> packet) {
    if (packet.Length < 12)
      throw new InvalidDataException($"A TQI picture is at least twelve bytes; this one is {packet.Length}.");
    int width = BinaryPrimitives.ReadUInt16LittleEndian(packet), height = BinaryPrimitives.ReadUInt16LittleEndian(packet[2..]);
    if (width == 0 || height == 0)
      throw new InvalidDataException("A TQI picture states no size.");
    var matrix = Matrix(packet[4]);

    // The bitstream is read from its whole 32-bit words byte-swapped.
    var body = packet[8..];
    var bytes = new byte[body.Length + 8];
    body.CopyTo(bytes);
    for (var w = 0; w < body.Length / 4; ++w)
      Array.Reverse(bytes, w * 4, 4);
    for (var i = body.Length / 4 * 4; i < body.Length; ++i)
      bytes[i] = 0;
    var reader = new MpegBitReader(bytes.AsSpan(0, body.Length));

    var picture = new EaPicture(width, height);
    Span<int> lastDc = stackalloc int[3];
    var blocks = new short[6 * 64];
    for (var row = 0; row < (height + 15) / 16; ++row)
      for (var column = 0; column < (width + 15) / 16; ++column) {
        Array.Clear(blocks);
        for (var n = 0; n < 6; ++n)
          if (!ReadIntraBlock(ref reader, blocks.AsSpan(n * 64, 64), n, matrix, lastDc))
            return picture; // FFmpeg keeps what it decoded and stops.
        picture.PutMacroblock(blocks, column, row);
      }
    return picture;
  }

  /// <summary>FFmpeg's <c>ff_mpeg1_decode_block_intra</c> at quantiser 1; false where the block is damaged.</summary>
  internal static bool ReadIntraBlock(ref MpegBitReader reader, scoped Span<short> block, int index, ushort[] matrix, scoped Span<int> lastDc) {
    var component = index <= 3 ? 0 : index - 3;
    var size = (component == 0 ? MpegVlcTables.Mpeg2LuminanceDcSize : MpegVlcTables.Mpeg2ChrominanceDcSize).Read(ref reader);
    var difference = 0;
    if (size != 0) {
      difference = reader.ReadBits(size);
      if ((difference & (1 << (size - 1))) == 0)
        difference -= (1 << size) - 1;
    }
    lastDc[component] += difference;
    block[0] = (short)(lastDc[component] * matrix[0]);

    var zigzag = EaTables.ZigZag;
    var i = 0;
    while (true) {
      if (reader.NextBits(2) == 2) {
        reader.Skip(2);
        return true;
      }

      int run, level;
      var code = MpegVlcTables.Coefficient.Read(ref reader);
      if (code == MpegVlcTables.CoefficientEscape) {
        run = reader.ReadBits(6) + 1;
        level = (sbyte)reader.ReadBits(8);
        if (level == -128)
          level = reader.ReadBits(8) - 256;
        else if (level == 0)
          level = reader.ReadBits(8);
      } else {
        run = MpegVlcTables.RunOf(code) + 1;
        level = MpegVlcTables.LevelOf(code);
        if (reader.ReadBit() == 1)
          level = -level;
      }

      i += run;
      if (i > 63)
        return false;
      var j = zigzag[i];
      var magnitude = Math.Abs(level);
      magnitude = (magnitude * matrix[j]) >> 4;
      magnitude = (magnitude - 1) | 1;
      block[j] = (short)(level < 0 ? -magnitude : magnitude);
    }
  }
}

/// <summary>
/// The TGQ picture decoder, FFmpeg's <c>libavcodec/eatgq.c</c> (LGPL-2.1-or-later): macroblocks coded
/// with their own three-bit code, as DC values alone, or copied from the last picture.
/// </summary>
internal static class TgqCodec {

  /// <summary>FFmpeg's <c>tgq_calculate_qtable</c>.</summary>
  internal static int[] Table(int quality) {
    var table = new int[64];
    var a = (14 * (100 - quality)) / 100 + 1;
    var b = (11 * (100 - quality)) / 100 + 4;
    for (var j = 0; j < 8; ++j)
      for (var i = 0; i < 8; ++i)
        table[j * 8 + i] = ((a * (j + i) / (7 + 7) + b) * EaTables.InverseAanScales[j * 8 + i]) >> (14 - 4);
    return table;
  }

  /// <summary>Decodes one packet, chunk header included, against the last picture.</summary>
  internal static EaPicture Decode(ReadOnlySpan<byte> packet, EaPicture? last) {
    if (packet.Length < 16)
      throw new InvalidDataException($"A TGQ picture is at least sixteen bytes; this one is {packet.Length}.");
    var big = BinaryPrimitives.ReadUInt32LittleEndian(packet[4..]) > 0x000FFFFF;
    int width = big ? BinaryPrimitives.ReadUInt16BigEndian(packet[8..]) : BinaryPrimitives.ReadUInt16LittleEndian(packet[8..]);
    int height = big ? BinaryPrimitives.ReadUInt16BigEndian(packet[10..]) : BinaryPrimitives.ReadUInt16LittleEndian(packet[10..]);
    var table = Table(packet[12]);
    if (last != null && (last.Width != width || last.Height != height))
      last = null;

    var columns = (width + 15) >> 4;
    var rows = (height + 15) >> 4;
    var at = 16;
    if (packet.Length - at < columns * rows)
      throw new InvalidDataException("A TGQ picture is shorter than one byte a macroblock.");

    var picture = new EaPicture(width, height);
    var blocks = new short[6 * 64];
    Span<sbyte> dc = stackalloc sbyte[6];
    for (var row = 0; row < rows; ++row)
      for (var column = 0; column < columns; ++column) {
        if (at >= packet.Length)
          throw new InvalidDataException("A TGQ picture ends before its macroblocks do.");
        int mode = packet[at++];
        if (mode > 12) {
          var reader = new LsbReader(packet.Slice(at, Math.Min(packet.Length - at, mode)));
          for (var n = 0; n < 6; ++n)
            _ReadBlock(ref reader, blocks.AsSpan(n * 64, 64), table);
          picture.PutMacroblock(blocks, column, row);
          at += mode;
          continue;
        }

        if (mode == 1) {
          if (last == null)
            throw new InvalidDataException("A TGQ macroblock copies from a picture before the first one.");
          int vector = packet[at++];
          int vx = vector >> 4, vy = vector & 0x0F;
          if (vx >= 8)
            vx -= 16;
          if (vy >= 8)
            vy -= 16;
          var x = column * 16 - vx;
          var y = row * 16 - vy;
          if (x < 0 || x + 16 > width || y < 0 || y + 16 > height)
            throw new InvalidDataException("A TGQ macroblock copies from outside the last picture.");
          for (var r = 0; r < 16; ++r)
            last.Y.AsSpan((y + r) * last.LumaStride + x, 16).CopyTo(picture.Y.AsSpan((row * 16 + r) * picture.LumaStride + column * 16));
          for (var r = 0; r < 8; ++r) {
            last.U.AsSpan(((y >> 1) + r) * last.ChromaStride + (x >> 1), 8).CopyTo(picture.U.AsSpan((row * 8 + r) * picture.ChromaStride + column * 8));
            last.V.AsSpan(((y >> 1) + r) * last.ChromaStride + (x >> 1), 8).CopyTo(picture.V.AsSpan((row * 8 + r) * picture.ChromaStride + column * 8));
          }
          continue;
        }

        switch (mode) {
          case 3:
            dc[0] = dc[1] = dc[2] = dc[3] = (sbyte)packet[at++];
            dc[4] = (sbyte)packet[at++];
            dc[5] = (sbyte)packet[at++];
            break;
          case 6:
            if (packet.Length - at < 6)
              throw new InvalidDataException("A TGQ macroblock ends inside its DC values.");
            for (var n = 0; n < 6; ++n)
              dc[n] = (sbyte)packet[at++];
            break;
          case 12:
            for (var n = 0; n < 6; ++n) {
              dc[n] = (sbyte)packet[at++];
              ++at;
            }
            break;
          default:
            throw new InvalidDataException($"A TGQ macroblock states mode {mode}, which TGQ does not define.");
        }

        _DcOnly(picture.Y, picture.LumaStride, row * 16 * picture.LumaStride + column * 16, dc[0], table[0]);
        _DcOnly(picture.Y, picture.LumaStride, row * 16 * picture.LumaStride + column * 16 + 8, dc[1], table[0]);
        _DcOnly(picture.Y, picture.LumaStride, (row * 16 + 8) * picture.LumaStride + column * 16, dc[2], table[0]);
        _DcOnly(picture.Y, picture.LumaStride, (row * 16 + 8) * picture.LumaStride + column * 16 + 8, dc[3], table[0]);
        _DcOnly(picture.U, picture.ChromaStride, row * 8 * picture.ChromaStride + column * 8, dc[4], table[0]);
        _DcOnly(picture.V, picture.ChromaStride, row * 8 * picture.ChromaStride + column * 8, dc[5], table[0]);
      }
    return picture;
  }

  private static void _DcOnly(byte[] plane, int stride, int offset, int dc, int step) {
    var level = (dc * step + 2056) >> 4;
    var value = (byte)(level < 0 ? 0 : level > 255 ? 255 : level);
    for (var r = 0; r < 8; ++r)
      plane.AsSpan(offset + r * stride, 8).Fill(value);
  }

  /// <summary>FFmpeg's <c>tgq_decode_block</c>.</summary>
  /// <summary>TGQ reads its blocks least significant bit first: FFmpeg builds it with <c>BITSTREAM_READER_LE</c>.</summary>
  internal ref struct LsbReader(ReadOnlySpan<byte> data) {
    private readonly ReadOnlySpan<byte> _data = data;
    private int _bit;

    internal readonly int NextBits(int count) {
      var value = 0;
      for (var i = 0; i < count; ++i) {
        var at = this._bit + i;
        var bit = (at >> 3) < this._data.Length ? (this._data[at >> 3] >> (at & 7)) & 1 : 0;
        value |= bit << i;
      }
      return value;
    }

    internal int ReadBits(int count) {
      var value = this.NextBits(count);
      this._bit += count;
      return value;
    }

    internal void Skip(int count) => this._bit += count;

    internal readonly int BitsRemaining => this._data.Length * 8 - this._bit;
  }

  private static void _ReadBlock(ref LsbReader reader, Span<short> block, int[] table) {
    var zigzag = EaTables.ZigZag;
    block.Clear();
    block[0] = (short)((sbyte)reader.ReadBits(8) * table[0]);
    for (var i = 1; i < 64;) {
      switch (reader.NextBits(3)) {
        case 4:
          if (i >= 63)
            throw new InvalidDataException("A TGQ block's zero pair runs past its last coefficient.");
          block[zigzag[i++]] = 0;
          block[zigzag[i++]] = 0;
          reader.Skip(3);
          break;
        case 0:
          block[zigzag[i++]] = 0;
          reader.Skip(3);
          break;
        case 5:
        case 1: {
          reader.Skip(2);
          var count = reader.ReadBits(6);
          if (count > 64 - i)
            throw new InvalidDataException("A TGQ block's zero run runs past its last coefficient.");
          for (var j = 0; j < count; ++j)
            block[zigzag[i++]] = 0;
          break;
        }
        case 6:
          reader.Skip(3);
          block[zigzag[i]] = (short)-table[zigzag[i]];
          ++i;
          break;
        case 2:
          reader.Skip(3);
          block[zigzag[i]] = (short)table[zigzag[i]];
          ++i;
          break;
        default: {
          reader.Skip(2);
          int value;
          if (reader.NextBits(6) == 0x3F) {
            reader.Skip(6);
            value = (sbyte)reader.ReadBits(8);
          } else {
            value = reader.ReadBits(6);
            if (value >= 32)
              value -= 64;
          }
          block[zigzag[i]] = (short)(value * table[zigzag[i]]);
          ++i;
          break;
        }
      }
    }
    block[0] += 128 << 4;
  }
}

/// <summary>
/// The Madcow (MAD) picture decoder, FFmpeg's <c>libavcodec/eamad.c</c> (LGPL-2.1-or-later): intra blocks
/// under MPEG-1's run-level code with MAD's own escape, and predicted macroblocks whose blocks are copied
/// from the last reference picture at a whole-sample vector with a DC offset, or coded.
/// </summary>
internal static class MadCodec {

  internal const uint KeyTag = 0x6B44414D;
  internal const uint PredictedTag = 0x6D44414D;
  internal const uint LowQualityTag = 0x6544414D;

  /// <summary>FFmpeg's <c>calc_quant_matrix</c>.</summary>
  internal static ushort[] Matrix(int scale) {
    var matrix = new ushort[64];
    matrix[0] = (ushort)((EaTables.InverseAanScales[0] * EaTables.Mpeg1DefaultIntra[0]) >> 11);
    for (var i = 1; i < 64; ++i)
      matrix[i] = (ushort)((EaTables.InverseAanScales[i] * EaTables.Mpeg1DefaultIntra[i] * scale + 32) >> 10);
    return matrix;
  }

  /// <summary>Decodes one packet, chunk header included, against the last reference picture.</summary>
  internal static EaPicture Decode(ReadOnlySpan<byte> packet, EaPicture? reference, out bool isReference) {
    if (packet.Length < 26)
      throw new InvalidDataException($"A MAD picture is at least twenty-six bytes; this one is {packet.Length}.");
    var tag = BinaryPrimitives.ReadUInt32LittleEndian(packet);
    var inter = tag is PredictedTag or LowQualityTag;
    isReference = tag != LowQualityTag;
    int width = BinaryPrimitives.ReadUInt16LittleEndian(packet[16..]), height = BinaryPrimitives.ReadUInt16LittleEndian(packet[18..]);
    if (width < 16 || height < 16)
      throw new InvalidDataException($"A MAD picture states {width}x{height}; it is at least 16x16.");
    var matrix = Matrix(packet[21]);
    if (reference != null && (reference.Width != width || reference.Height != height))
      reference = null;
    if (inter && reference == null) {
      // FFmpeg predicts a first picture that is not a key frame from black.
      reference = new EaPicture(width, height);
      reference.U.AsSpan().Fill(0x80);
      reference.V.AsSpan().Fill(0x80);
    }

    // The bitstream is read from its whole 16-bit words byte-swapped.
    var body = packet[24..];
    var bytes = new byte[body.Length + 8];
    body.CopyTo(bytes);
    for (var w = 0; w < body.Length / 2; ++w)
      (bytes[2 * w], bytes[2 * w + 1]) = (bytes[2 * w + 1], bytes[2 * w]);
    if ((body.Length & 1) != 0)
      bytes[body.Length - 1] = 0;
    var reader = new MpegBitReader(bytes.AsSpan(0, body.Length));

    var picture = new EaPicture(width, height);
    var block = new short[64];
    for (var row = 0; row < (height + 15) / 16; ++row)
      for (var column = 0; column < (width + 15) / 16; ++column) {
        var map = 0;
        int vx = 0, vy = 0;
        if (inter) {
          var v = reader.ReadBit() == 1 ? 0 : 2 - reader.ReadBit();
          if (v < 2) {
            map = v != 0 ? reader.ReadBits(6) : 63;
            vx = _Motion(ref reader);
            vy = _Motion(ref reader);
          }
        }

        for (var j = 0; j < 6; ++j) {
          if ((map & (1 << j)) != 0) {
            var add = 2 * _Motion(ref reader);
            _Compensate(picture, reference!, column, row, j, vx, vy, add);
            continue;
          }

          Array.Clear(block);
          if (!_ReadIntraBlock(ref reader, block, matrix))
            throw new InvalidDataException($"A MAD picture's coefficients are damaged at macroblock {column},{row}.");
          if (j < 4)
            EaIdct.Put(block, picture.Y, (row * 16 + ((j & 2) << 2)) * picture.LumaStride + column * 16 + ((j & 1) << 3), picture.LumaStride);
          else
            EaIdct.Put(block, j == 4 ? picture.U : picture.V, row * 8 * picture.ChromaStride + column * 8, picture.ChromaStride);
        }
      }
    return picture;
  }

  private static int _Motion(ref MpegBitReader reader) {
    var value = 0;
    if (reader.ReadBit() == 1) {
      if (reader.ReadBit() == 1)
        value = -17;
      value += reader.ReadBits(4) + 1;
    }
    return value;
  }

  /// <summary>
  /// FFmpeg's <c>comp_block</c>: a block copied from the reference at the vector, its samples offset by
  /// <paramref name="add"/>. The bounds test is FFmpeg's, on the linear offset into the reference, so a
  /// column offset left of the picture reads the end of the row above, as there.
  /// </summary>
  private static void _Compensate(EaPicture picture, EaPicture reference, int column, int row, int j, int vx, int vy, int add) {
    byte[] source, target;
    int stride, sourceOffset, targetOffset;
    if (j < 4) {
      stride = reference.LumaStride;
      var offset = (long)(row * 16 + ((j & 2) << 2) + vy) * stride + column * 16 + ((j & 1) << 3) + vx;
      if ((ulong)offset >= (ulong)((long)(picture.Height - 7) * stride - 7))
        return;
      source = reference.Y;
      target = picture.Y;
      sourceOffset = (int)offset;
      targetOffset = (row * 16 + ((j & 2) << 2)) * stride + column * 16 + ((j & 1) << 3);
    } else {
      stride = reference.ChromaStride;
      var offset = (long)(row * 8 + vy / 2) * stride + column * 8 + vx / 2;
      if ((ulong)offset >= (ulong)((long)(picture.Height / 2 - 7) * stride - 7))
        return;
      source = j == 4 ? reference.U : reference.V;
      target = j == 4 ? picture.U : picture.V;
      sourceOffset = (int)offset;
      targetOffset = row * 8 * stride + column * 8;
    }

    for (var y = 0; y < 8; ++y)
      for (var x = 0; x < 8; ++x) {
        var at = sourceOffset + y * stride + x;
        var value = (at < source.Length ? source[at] : 0) + add;
        target[targetOffset + y * stride + x] = (byte)(value < 0 ? 0 : value > 255 ? 255 : value);
      }
  }

  /// <summary>FFmpeg's <c>decode_block_intra</c>: MPEG-1's code, its escape a ten-bit level then a six-bit run.</summary>
  /// <remarks>
  /// FFmpeg keeps the matrix as unsigned sixteen-bit values and reads it here through a signed pointer,
  /// so an entry of 32,768 or more — the highest frequencies at coarse quantisers — dequantises with its
  /// sign turned round. Every MAD decoder in use is FFmpeg's, so that is what the format decodes to.
  /// </remarks>
  private static bool _ReadIntraBlock(ref MpegBitReader reader, Span<short> block, ushort[] matrix) {
    block[0] = (short)((128 + (sbyte)reader.ReadBits(8)) * (short)matrix[0]);
    var zigzag = EaTables.ZigZag;
    var i = 0;
    while (true) {
      var code = MpegVlcTables.Coefficient.Read(ref reader);
      if (code == MpegVlcTables.EndOfBlock)
        return true;

      int run, level;
      if (code == MpegVlcTables.CoefficientEscape) {
        level = reader.ReadBits(10);
        if (level >= 512)
          level -= 1024;
        run = reader.ReadBits(6) + 1;
      } else {
        run = MpegVlcTables.RunOf(code) + 1;
        level = MpegVlcTables.LevelOf(code);
        if (reader.ReadBit() == 1)
          level = -level;
      }

      i += run;
      if (i > 63)
        return false;
      var j = zigzag[i];
      var magnitude = Math.Abs(level);
      magnitude = (magnitude * (short)matrix[j]) >> 4;
      magnitude = (magnitude - 1) | 1;
      block[j] = (short)(level < 0 ? -magnitude : magnitude);
    }
  }
}

/// <summary>
/// The TGV picture decoder, FFmpeg's <c>libavcodec/eatgv.c</c> (LGPL-2.1-or-later): palettised key frames
/// packed by an LZ77 variant, and predicted frames built from four-by-four cells, each copied from the last
/// picture at a vector from a codebook or drawn from a codebook of raw or two-bit-packed cells. Its bit
/// fields are read least significant bit first, as FFmpeg builds it with <c>BITSTREAM_READER_LE</c>.
/// </summary>
internal sealed class TgvCodec {

  internal const uint KeyTag = 0x5447566B;

  private byte[]? _unpacked;
  private byte[]? _last;
  private readonly byte[] _palette = new byte[256 * 3];

  internal int Width { get; private set; }
  internal int Height { get; private set; }
  internal byte[] Palette => this._palette;

  /// <summary>Decodes one packet, chunk header included; null where FFmpeg gives no picture.</summary>
  internal byte[]? Decode(ReadOnlySpan<byte> packet) {
    if (packet.Length < 8)
      throw new InvalidDataException("A TGV picture is shorter than its chunk header.");
    var tag = BinaryPrimitives.ReadUInt32LittleEndian(packet);
    var body = packet[8..];

    if (tag == KeyTag) {
      if (body.Length < 12)
        throw new InvalidDataException("A TGV key frame is shorter than its twelve-byte header.");
      int width = BinaryPrimitives.ReadUInt16LittleEndian(body), height = BinaryPrimitives.ReadUInt16LittleEndian(body[2..]);
      if (width != this.Width || height != this.Height || this._unpacked == null) {
        this.Width = width;
        this.Height = height;
        this._unpacked = new byte[width * height];
        this._last = null;
      }

      int count = BinaryPrimitives.ReadUInt16LittleEndian(body[6..]);
      var at = 12;
      for (var i = 0; i < count && i < 256 && body.Length - at >= 3; ++i, at += 3)
        body.Slice(at, 3).CopyTo(this._palette.AsSpan(i * 3));
      if (body.Length - at < 5)
        throw new InvalidDataException("A TGV key frame ends inside its palette.");

      _Unpack(body[at..], this._unpacked);
      var picture = (byte[])this._unpacked.Clone();
      this._last = picture;
      return picture;
    }

    if (this._last == null)
      return null; // FFmpeg skips a predicted frame with nothing to predict from
    var frame = (byte[])this._last.Clone();
    this._DecodeInter(body, frame);
    this._last = frame;
    return frame;
  }

  /// <summary>FFmpeg's <c>unpack</c>: literal runs and back-references in four encodings.</summary>
  private static void _Unpack(ReadOnlySpan<byte> source, byte[] target) {
    var src = (source[0] & 0x01) != 0 ? 5 : 2;
    if (source.Length - src < 3)
      throw new InvalidDataException("A TGV key frame is shorter than its packed size.");
    var size = (source[src] << 16) | (source[src + 1] << 8) | source[src + 2];
    src += 3;
    var dst = 0;
    var end = target.Length;

    while (size > 0 && src < source.Length) {
      int first = source[src];
      var literal = first & 3;
      int offset = 0, copy;
      if ((first & 0x80) != 0) {
        if ((first & 0x40) != 0) {
          if ((first & 0x20) != 0) {
            if (first < 0xFC)
              literal = ((first & 31) + 1) << 2;
            src++;
            copy = 0;
          } else {
            if (source.Length - src < 4)
              break;
            offset = ((first & 0x10) << 12) + ((source[src + 1] << 8) | source[src + 2]) + 1;
            copy = ((first & 0xC) << 6) + source[src + 3] + 5;
            src += 4;
          }
        } else {
          if (source.Length - src < 3)
            break;
          literal = (source[src + 1] & 0xC0) >> 6;
          offset = (((source[src + 1] << 8) | source[src + 2]) & 0x3FFF) + 1;
          copy = (first & 0x3F) + 4;
          src += 3;
        }
      } else {
        if (source.Length - src < 2)
          break;
        offset = ((first & 0x60) << 3) + source[src + 1] + 1;
        copy = ((first & 0x1C) >> 2) + 3;
        src += 2;
      }

      if (literal > source.Length - src)
        break;
      if (literal > 0) {
        size -= literal;
        var run = Math.Min(literal, end - dst);
        source.Slice(src, run).CopyTo(target.AsSpan(dst));
        dst += run;
        src += run;
      }

      if (copy > 0) {
        if (dst < offset)
          return;
        size -= copy;
        var run = Math.Min(copy, end - dst);
        for (var i = 0; i < run; ++i, ++dst)
          target[dst] = target[dst - offset];
      }
    }
  }

  private void _DecodeInter(ReadOnlySpan<byte> body, byte[] frame) {
    if (body.Length < 12)
      throw new InvalidDataException("A TGV predicted frame is shorter than its header.");
    int vectors = BinaryPrimitives.ReadUInt16LittleEndian(body);
    int rawCells = BinaryPrimitives.ReadUInt16LittleEndian(body[2..]);
    int packedCells = BinaryPrimitives.ReadUInt16LittleEndian(body[4..]);
    int bits = BinaryPrimitives.ReadUInt16LittleEndian(body[6..]);
    if (bits is 0 or > 25)
      throw new InvalidDataException($"A TGV predicted frame states {bits}-bit cell indices.");
    var at = 12;

    var vectorBits = (vectors * 2 * 10 + 31) & ~31;
    if (body.Length - at < (vectorBits >> 3) + 16 * rawCells + 8 * packedCells)
      throw new InvalidDataException("A TGV predicted frame is shorter than its codebooks.");
    var mv = new int[vectors * 2];
    var reader = new TgqCodec.LsbReader(body.Slice(at, vectorBits >> 3));
    for (var i = 0; i < vectors * 2; ++i) {
      var value = reader.ReadBits(10);
      mv[i] = value >= 512 ? value - 1024 : value;
    }
    at += vectorBits >> 3;

    var raw = body.Slice(at, rawCells * 16).ToArray();
    at += rawCells * 16;

    var cells = new TgqCodec.LsbReader(body[at..]);
    var packed = new byte[packedCells * 16];
    Span<byte> colours = stackalloc byte[4];
    for (var i = 0; i < packedCells; ++i) {
      for (var j = 0; j < 4; ++j)
        colours[j] = (byte)cells.ReadBits(8);
      for (var j = 0; j < 16; ++j)
        packed[i * 16 + 15 - j] = colours[cells.ReadBits(2)];
    }

    int width = this.Width, height = this.Height;
    if (cells.BitsRemaining < bits * (height / 4) * (width / 4))
      throw new InvalidDataException("A TGV predicted frame is shorter than its cell indices.");
    var last = this._last!;
    for (var y = 0; y < height / 4; ++y)
      for (var x = 0; x < width / 4; ++x) {
        var index = cells.ReadBits(bits);
        if (index < vectors) {
          var mx = x * 4 + mv[index * 2];
          var my = y * 4 + mv[index * 2 + 1];
          if (mx < 0 || mx + 4 > width || my < 0 || my + 4 > height)
            continue;
          for (var j = 0; j < 4; ++j)
            for (var i = 0; i < 4; ++i)
              frame[(y * 4 + j) * width + x * 4 + i] = last[(my + j) * width + mx + i];
          continue;
        }

        var offset = index - vectors;
        byte[] source;
        int start;
        if (offset < rawCells)
          (source, start) = (raw, 16 * offset);
        else if (offset - rawCells < packedCells)
          (source, start) = (packed, 16 * (offset - rawCells));
        else
          continue;
        for (var j = 0; j < 4; ++j)
          for (var i = 0; i < 4; ++i)
            frame[(y * 4 + j) * width + x * 4 + i] = source[start + j * 4 + i];
      }
  }
}
