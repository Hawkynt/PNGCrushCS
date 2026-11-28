using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.Codecs.ProSumer;

/// <summary>
/// Brooktree ProSumer's code: a prefix code over four-bit nibbles for pairs of residual bytes, runs of zero
/// words, and an escape to a literal pair. FFmpeg's <c>libavcodec/prosumer.c</c> (LGPL-2.1-or-later).
/// </summary>
internal static class ProSumerCode {

  internal const int HeaderLength = 32;
  internal const uint Tag = 0x43495442; // "BTIC"

  /// <summary>FFmpeg's <c>table</c>: each entry a byte pair (or a run, 0x80NN) and its code, left-aligned in twelve bits.</summary>
  private static readonly ushort[] _Table = [
    0x0000, 0x100, 0x0101, 0x200, 0x0202, 0x300, 0xFFFF, 0x400, 0xFEFE, 0x500,
    0x0001, 0x700, 0x0100, 0x800, 0x00FF, 0x900, 0xFF00, 0xA00, 0x8001, 0x600,
    0x8002, 0xB00, 0xFCFC, 0x010, 0x0404, 0x030, 0x0002, 0xD30, 0xFEFC, 0x020,
    0xFCFE, 0x040, 0xFEFF, 0xD20, 0x0808, 0x060, 0xFFFE, 0x050, 0x0402, 0xC00,
    0x0204, 0xC10, 0xF8F8, 0xC30, 0x0201, 0xC40, 0x0102, 0xC60, 0x0804, 0xF30,
    0x0408, 0xE00, 0xF8FC, 0xE10, 0xFCF8, 0xC70, 0x00FE, 0xD00, 0xFE00, 0xD40,
    0xFF01, 0xD50, 0x01FF, 0xD60, 0x0200, 0xD70, 0xFCFF, 0xE20, 0x0104, 0xE30,
    0xF0F0, 0xE50, 0x0401, 0xE70, 0x02FE, 0xF00, 0xFE02, 0xF10, 0xFE01, 0xF20,
    0x01FE, 0xF40, 0xFF02, 0xF50, 0x02FF, 0xF60, 0x8003, 0xC20, 0x8004, 0x070,
    0x8005, 0xD10, 0x8006, 0xC50, 0x8007, 0xE60, 0x8008, 0xE40, 0x8009, 0xF70,
    0xFC02, 0x080, 0xFE04, 0x081, 0xFC00, 0x082, 0x02FC, 0x083, 0x1010, 0x084,
    0x00FC, 0x085, 0x0004, 0x086, 0x0400, 0x087, 0xFFFC, 0x088, 0x1008, 0x089,
    0x0810, 0x08A, 0x0802, 0x08B, 0x0208, 0x08C, 0xFEF8, 0x08D, 0xFC01, 0x08E,
    0x04FF, 0x08F, 0xF8FE, 0x090, 0xFC04, 0x091, 0x04FC, 0x092, 0xFF04, 0x093,
    0x01FC, 0x094, 0xF0F8, 0x095, 0xF8F0, 0x096, 0x04FE, 0x097, 0xF0FC, 0x098,
    0x0008, 0x099, 0x08FE, 0x09A, 0x01F8, 0x09B, 0x0800, 0x09C, 0x08FC, 0x09D,
    0xFE08, 0x09E, 0xFC08, 0x09F, 0xF800, 0x0A0, 0x0108, 0x0A1, 0xF802, 0x0A2,
    0x0801, 0x0A3, 0x00F8, 0x0A4, 0xF804, 0x0A5, 0xF8FF, 0x0A6, 0xFFF8, 0x0A7,
    0x04F8, 0x0A8, 0x02F8, 0x0A9, 0x1004, 0x0AA, 0x08F8, 0x0AB, 0xF808, 0x0AC,
    0x0410, 0x0AD, 0xFF08, 0x0AE, 0x08FF, 0x0AF, 0xFCF0, 0x0B0, 0xF801, 0x0B1,
    0xE0F0, 0x0B2, 0xF3F3, 0x0B3, 0xF0E0, 0x0B4, 0xFAFA, 0x0B5, 0xF7F7, 0x0B6,
    0xFEF0, 0x0B7, 0xF0FE, 0x0B8, 0xE9E9, 0x0B9, 0xF9F9, 0x0BA, 0x2020, 0x0BB,
    0xE0E0, 0x0BC, 0x02F0, 0x0BD, 0x04F0, 0x0BE, 0x2010, 0x0BF, 0xECEC, 0x0C0,
    0xEFEF, 0x0C1, 0x1020, 0x0C2, 0xF5F5, 0x0C3, 0xF4F4, 0x0C4, 0xEDED, 0x0C5,
    0xEAEA, 0x0C6, 0xFBFB, 0x0C7, 0x1002, 0x0C8, 0xF2F2, 0x0C9, 0xF6F6, 0x0CA,
    0xF1F1, 0x0CB, 0xFDFD, 0x0CC, 0x0210, 0x0CD, 0x10FF, 0x0CE, 0xFDFE, 0x0CF,
    0x10F8, 0x0D0, 0x1000, 0x0D1, 0xF001, 0x0D2, 0x1001, 0x0D3, 0x0010, 0x0D4,
    0x10FE, 0x0D5, 0xEBEB, 0x0D6, 0xFE10, 0x0D7, 0x0110, 0x0D8, 0xF000, 0x0D9,
    0x08F0, 0x0DA, 0x01F0, 0x0DB, 0x0303, 0x0DC, 0x00F0, 0x0DD, 0xF002, 0x0DE,
    0x10FC, 0x0DF, 0xFC10, 0x0E0, 0xF0FF, 0x0E1, 0xEEEE, 0x0E2, 0xF004, 0x0E3,
    0xFFF0, 0x0E4, 0xF7F8, 0x0E5, 0xF3F2, 0x0E6, 0xF9FA, 0x0E7, 0x0820, 0x0E8,
    0x0302, 0x0E9, 0xE0F8, 0x0EA, 0x0505, 0x0EB, 0x2008, 0x0EC, 0xE8E8, 0x0ED,
    0x0403, 0x0EE, 0xFBFC, 0x0EF, 0xFCFD, 0x0F0, 0xFBFA, 0x0F1, 0x0203, 0x0F2,
    0xFCFB, 0x0F3, 0x0304, 0x0F4, 0xF810, 0x0F5, 0xFF10, 0x0F6, 0xF008, 0x0F7,
    0xFEFD, 0x0F8, 0xF7F6, 0x0F9, 0xF2F1, 0x0FA, 0xF3F4, 0x0FB, 0xEDEC, 0x0FC,
    0xF4F1, 0x0FD, 0xF5F6, 0x0FE, 0xF0F1, 0x0FF, 0xF9F8, 0xC80, 0x10F0, 0xC81,
    0xF2F3, 0xC82, 0xF7F9, 0xC83, 0xF6F5, 0xC84, 0xF0EF, 0xC85, 0xF4F5, 0xC86,
    0xF6F7, 0xC87, 0xFAF9, 0xC88, 0x0405, 0xC89, 0xF8F9, 0xC8A, 0xFAFB, 0xC8B,
    0xF1F0, 0xC8C, 0xF4F3, 0xC8D, 0xF1F2, 0xC8E, 0xF8E0, 0xC8F, 0xF8F7, 0xC90,
    0xFDFC, 0xC91, 0xF8FA, 0xC92, 0xFAF6, 0xC93, 0xEEEF, 0xC94, 0xF5F7, 0xC95,
    0xFDFB, 0xC96, 0xF4F6, 0xC97, 0xFCFA, 0xC98, 0xECED, 0xC99, 0xF0F3, 0xC9A,
    0xF3F1, 0xC9B, 0xECEB, 0xC9C, 0xEDEE, 0xC9D, 0xF9F7, 0xC9E, 0x0420, 0xC9F,
    0xEBEA, 0xCA0, 0xF0F4, 0xCA1, 0xF3F5, 0xCA2, 0xFAF7, 0xCA3, 0x0301, 0xCA4,
    0xF3F7, 0xCA5, 0xF7F3, 0xCA6, 0xEFF0, 0xCA7, 0xF9F6, 0xCA8, 0xEFEE, 0xCA9,
    0xF4F7, 0xCAA, 0x0504, 0xCAB, 0xF5F4, 0xCAC, 0xF1F3, 0xCAD, 0xEBEE, 0xCAE,
    0xF2F5, 0xCAF, 0xF3EF, 0xCB0, 0xF5F1, 0xCB1, 0xF9F3, 0xCB2, 0xEDF0, 0xCB3,
    0xEEF1, 0xCB4, 0xF6F9, 0xCB5, 0xF8FB, 0xCB6, 0xF010, 0xCB7, 0xF2F6, 0xCB8,
    0xF4ED, 0xCB9, 0xF7FB, 0xCBA, 0xF8F3, 0xCBB, 0xEDEB, 0xCBC, 0xF0F2, 0xCBD,
    0xF2F9, 0xCBE, 0xF8F1, 0xCBF, 0xFAFC, 0xCC0, 0xFBF8, 0xCC1, 0xF6F0, 0xCC2,
    0xFAF8, 0xCC3, 0x0103, 0xCC4, 0xF3F6, 0xCC5, 0xF4F9, 0xCC6, 0xF7F2, 0xCC7,
    0x2004, 0xCC8, 0xF2F0, 0xCC9, 0xF4F2, 0xCCA, 0xEEED, 0xCCB, 0xFCE0, 0xCCC,
    0xEAE9, 0xCCD, 0xEAEB, 0xCCE, 0xF6F4, 0xCCF, 0xFFFD, 0xCD0, 0xE9EA, 0xCD1,
    0xF1F4, 0xCD2, 0xF6EF, 0xCD3, 0xF6F8, 0xCD4, 0xF8F6, 0xCD5, 0xEFF2, 0xCD6,
    0xEFF1, 0xCD7, 0xF7F1, 0xCD8, 0xFBFD, 0xCD9, 0xFEF6, 0xCDA, 0xFFF7, 0xCDB,
    0x0605, 0xCDC, 0xF0F5, 0xCDD, 0xF0FA, 0xCDE, 0xF1F9, 0xCDF, 0xF2FC, 0xCE0,
    0xF7EE, 0xCE1, 0xF7F5, 0xCE2, 0xF9FC, 0xCE3, 0xFAF5, 0xCE4, 0xFBF1, 0xCE5,
    0xF1EF, 0xCE6, 0xF1FA, 0xCE7, 0xF4F8, 0xCE8, 0xF7F0, 0xCE9, 0xF7F4, 0xCEA,
    0xF7FC, 0xCEB, 0xF9FB, 0xCEC, 0xFAF1, 0xCED, 0xFBF9, 0xCEE, 0xFDFF, 0xCEF,
    0xE0FC, 0xCF0, 0xEBEC, 0xCF1, 0xEDEF, 0xCF2, 0xEFED, 0xCF3, 0xF1F6, 0xCF4,
    0xF2F7, 0xCF5, 0xF3EE, 0xCF6, 0xF3F8, 0xCF7, 0xF5F2, 0xCF8, 0xF8F2, 0xCF9,
    0xF9F1, 0xCFA, 0xF9F2, 0xCFB, 0xFBEF, 0xCFC, 0x00FD, 0xCFD, 0xECEE, 0xCFE,
    0xF2EF, 0xCFF, 0xF2F8, 0xD80, 0xF5F0, 0xD81, 0xF6F2, 0xD82, 0xFCF7, 0xD83,
    0xFCF9, 0xD84, 0x0506, 0xD85, 0xEEEC, 0xD86, 0xF0F6, 0xD87, 0xF2F4, 0xD88,
    0xF6F1, 0xD89, 0xF8F5, 0xD8A, 0xF9F4, 0xD8B, 0xFBF7, 0xD8C, 0x0503, 0xD8D,
    0xEFEC, 0xD8E, 0xF3F0, 0xD8F, 0xF4F0, 0xD90, 0xF5F3, 0xD91, 0xF6F3, 0xD92,
    0xF7FA, 0xD93, 0x800A, 0xD94, 0x800B, 0xD95, 0x800C, 0xD96, 0x800D, 0xD97,
    0x800E, 0xD98, 0x800F, 0xD99, 0x8010, 0xD9A, 0x8011, 0xD9B, 0x8012, 0xD9C,
    0x8013, 0xD9D, 0x8014, 0xD9E, 0x8015, 0xD9F, 0x8016, 0xDA0, 0x8017, 0xDA1,
    0x8018, 0xDA2, 0x8019, 0xDA3, 0x801A, 0xDA4, 0x801B, 0xDA5, 0x801C, 0xDA6,
    0x801D, 0xDA7, 0x801E, 0xDA8, 0x801F, 0xDA9, 0x8020, 0xDAA, 0x8021, 0xDAB,
    0x8022, 0xDAC, 0x8023, 0xDAD, 0x8024, 0xDAE, 0x8025, 0xDAF, 0x8026, 0xDB0,
    0x8027, 0xDB1, 0x8028, 0xDB2, 0x8029, 0xDB3, 0x802A, 0xDB4, 0x802B, 0xDB5,
    0x802C, 0xDB6, 0x802D, 0xDB7, 0x802E, 0xDB8, 0x802F, 0xDB9, 0x80FF, 0xDBA,
  ];

  /// <summary>FFmpeg's <c>TB</c>: how many nibbles entry <paramref name="i"/>'s code has.</summary>
  private static uint _Nibbles(int i) => (uint)(1 + (i > 10 ? 1 : 0) + (i > 49 ? 1 : 0));

  internal static readonly uint[] Lut = _BuildLut();

  /// <summary>FFmpeg's <c>fill_elements</c>: up to two more codes that fit in the look-ahead after the first.</summary>
  private static void _FillElements(uint index, uint shift, ref uint e0, ref uint e1) {
    var h = index << (int)(32 - shift);
    for (var j = 0; j < 2; ++j)
      for (var i = 0; i < 43; ++i) {
        var b = 4 * _Nibbles(i);
        if (shift >= b && ((h & (0xFFF00000u << (int)(12 - b))) >> 20) == _Table[2 * i + 1]) {
          if (_Table[2 * i] >> 8 == 0x80u)
            return;
          e0 = (e0 & 0xFFFFFFu) | (((12 + b - shift) | (0x40u << j)) << 22);
          if (j == 0) {
            e1 = _Table[2 * i];
            shift -= b;
            h <<= (int)b;
          } else
            e1 |= (uint)_Table[2 * i] << 16;
          break;
        }
      }
  }

  /// <summary>FFmpeg's <c>fill_lut</c>: for every twelve-bit look-ahead, what it decodes to and how far it reaches.</summary>
  private static uint[] _BuildLut() {
    var lut = new uint[0x2000];
    for (var i = 1; i < _Table.Length; i += 2) {
      uint a = _Table[i];
      var b = _Nibbles(i >> 1);
      var c = (b << 16) | _Table[i - 1];
      var d = 4 * (3 - b);
      if (d == 0) {
        lut[2 * a] = c;
        lut[2 * a + 1] = 0;
      } else
        for (uint j = 0; j < 1u << (int)d; ++j) {
          var f = 0xFFFFFFFFu;
          c &= 0xFFFFFFu;
          if ((c & 0xFF00u) != 0x8000u)
            _FillElements(j, d, ref c, ref f);
          lut[2 * a + 2 * j] = c;
          lut[2 * a + 2 * j + 1] = f;
        }
    }
    for (var i = 0; i < 32; i += 2) {
      lut[i] = 0x68000;
      lut[i + 1] = 0;
    }
    return lut;
  }

  /// <summary>Each byte pair's code, and each run's, as nibbles: (code, count).</summary>
  internal static readonly Dictionary<int, (int Code, int Count)> Codes = _BuildCodes();

  private static Dictionary<int, (int, int)> _BuildCodes() {
    var codes = new Dictionary<int, (int, int)>();
    for (var i = 0; i < _Table.Length / 2; ++i) {
      var count = (int)_Nibbles(i);
      codes.TryAdd(_Table[2 * i], (_Table[2 * i + 1] >> (12 - 4 * count), count));
    }
    return codes;
  }

  /// <summary>
  /// FFmpeg's <c>decompress</c>: nibbles read from the top of little-endian 32-bit words, looked up twelve
  /// bits at a time, into byte pairs, zero words and literal pairs, until the output is full or the input runs out.
  /// </summary>
  internal static void Decompress(ReadOnlySpan<byte> source, byte[] output, out int written) {
    var packet = source.ToArray();
    var at = HeaderLength;
    var o = 0;
    var full = false;

    void Put16(uint v) {
      if (!full && output.Length - o >= 2) {
        output[o++] = (byte)v;
        output[o++] = (byte)(v >> 8);
      } else
        full = true;
    }
    void Put32(uint v) {
      if (!full && output.Length - o >= 4) {
        BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(o), v);
        o += 4;
      } else
        full = true;
    }

    uint ReadLe32() {
      if (packet.Length - at < 4) {
        at = packet.Length;
        return 0;
      }
      var v = BinaryPrimitives.ReadUInt32LittleEndian(packet[at..]);
      at += 4;
      return v;
    }

    ReadOnlySpan<byte> span = packet;
    var cnt = 4;
    var a = ReadLe32();
    var idx = a >> 20;
    var b = Lut[2 * idx];
    while (true) {
      if (output.Length - o <= 0 || full)
        break;
      if ((b & 0xFF00u) != 0x8000u || (b & 0xFFu) != 0) {
        if ((b & 0xFF00u) != 0x8000u)
          Put16(b);
        else {
          idx = 0;
          for (var i = 0; i < (b & 0xFFu); ++i)
            Put32(0);
        }
        var c = b >> 16;
        if ((c & 0xFF00u) != 0) {
          var fill = Lut[2 * idx + 1];
          if ((c & 0xF000u) == 0x1000)
            Put16(fill);
          else
            Put32(fill);
          c = (c >> 8) & 0x0Fu;
        }
        var ended = false;
        while (c != 0) {
          a <<= 4;
          if (--cnt == 0) {
            if (span.Length - at <= 0) {
              if (a == 0) {
                ended = true;
                break;
              }
            } else
              a = _Refill(span, ref at, a);
            cnt = 4;
          }
          --c;
        }
        if (ended)
          break;
        idx = a >> 20;
        b = Lut[2 * idx];
        if (b == 0)
          throw new InvalidDataException("A ProSumer frame holds a code its table does not.");
        continue;
      }

      idx = 2;
      var stop = false;
      while (idx != 0) {
        a <<= 4;
        if (--cnt != 0) {
          --idx;
          continue;
        }
        if (span.Length - at <= 0) {
          if (a != 0) {
            cnt = 4;
            --idx;
            continue;
          }
          stop = true;
          break;
        }
        a = _Refill(span, ref at, a);
        cnt = 4;
        --idx;
      }
      if (stop)
        break;
      b = 0x40000u | (a >> 16);
    }
    written = o;
  }

  /// <summary>The next sixteen bits, taken from the other half of their 32-bit word as FFmpeg's <c>pos ^ 2</c> does.</summary>
  private static uint _Refill(ReadOnlySpan<byte> data, ref int at, uint a) {
    var swapped = at ^ 2;
    uint value = swapped + 2 <= data.Length ? BinaryPrimitives.ReadUInt16LittleEndian(data[swapped..]) : swapped < data.Length ? data[swapped] : 0u;
    at = Math.Min(at + 2, data.Length);
    return (a & 0xFFFF0000u) | value;
  }

  /// <summary>Codes residual byte pairs as nibbles: runs of zero words, table pairs, and escaped literals.</summary>
  internal static byte[] Compress(ReadOnlySpan<byte> residuals) {
    var nibbles = new List<byte>();
    void Put(int code, int count) {
      for (var i = count - 1; i >= 0; --i)
        nibbles.Add((byte)((code >> (4 * i)) & 0xF));
    }

    for (var at = 0; at < residuals.Length;) {
      // A run of zero words, as long as the table has one for.
      if ((at & 3) == 0) {
        var words = 0;
        while (at + 4 * (words + 1) <= residuals.Length && residuals.Slice(at + 4 * words, 4).IndexOfAnyExcept((byte)0) < 0 && words < 0x2F)
          ++words;
        if (words >= 2) {
          var (code, count) = Codes[0x8000 | words];
          Put(code, count);
          at += 4 * words;
          continue;
        }
      }
      var pair = residuals[at] | (at + 1 < residuals.Length ? residuals[at + 1] : 0) << 8;
      if (Codes.TryGetValue(pair, out var known) && (pair & 0xFF00) != 0x8000)
        Put(known.Code, known.Count);
      else {
        Put(0, 2);
        Put(pair >> 8, 2);
        Put(pair & 0xFF, 2);
      }
      at += 2;
    }

    // Nibbles fill little-endian 32-bit words from the top.
    while (nibbles.Count % 8 != 0)
      nibbles.Add(0);
    var packed = new byte[nibbles.Count / 2];
    for (var w = 0; w < nibbles.Count / 8; ++w) {
      uint word = 0;
      for (var n = 0; n < 8; ++n)
        word = word << 4 | nibbles[w * 8 + n];
      BinaryPrimitives.WriteUInt32LittleEndian(packed.AsSpan(w * 4), word);
    }
    return packed;
  }
}

/// <summary>The ProSumer picture: residual bytes, a vertical predictor over 32-bit words, and 4:1:1 samples stored bottom up.</summary>
internal sealed class ProSumerFrame(int width, int height) {

  internal readonly int Stride = 3 * ((width + 7) & ~7) / 2;
  internal int Width => width;
  internal int Height => height;

  /// <summary>FFmpeg's <c>vertical_predict</c> for one row: each word from the one above, the shift carrying bits between bytes.</summary>
  internal static uint Predict(uint above, uint residual) => unchecked((((above >> 3) + (0x3F3F3F3Fu & residual)) << 3) & 0xFCFCFCFCu);

  internal byte[] Decode(ReadOnlySpan<byte> packet) {
    if (packet.Length <= ProSumerCode.HeaderLength)
      throw new InvalidDataException("A ProSumer frame is more than its 32-byte header.");
    var buffer = new byte[this.Stride * height];
    ProSumerCode.Decompress(packet, buffer, out var written);
    // FFmpeg refuses a frame that fills less than a twentieth of the picture.
    if (buffer.Length - written > buffer.Length * 95L / 100)
      throw new InvalidDataException("This ProSumer frame fills too little of its picture.");
    var words = this.Stride / 4;
    for (var j = 0; j < words; ++j)
      _Set(buffer, j, Predict(0x80808080u, _Get(buffer, j)));
    for (var row = 1; row < height; ++row)
      for (var j = 0; j < words; ++j)
        _Set(buffer, row * words + j, Predict(_Get(buffer, (row - 1) * words + j), _Get(buffer, row * words + j)));
    return buffer;
  }

  private static uint _Get(byte[] buffer, int word) => BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(word * 4));
  private static void _Set(byte[] buffer, int word, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(word * 4), value);

  /// <summary>The predicted buffer as 4:1:1 planes, top row first: each eight pixels are U Y V Y U Y V Y Y Y Y Y.</summary>
  internal byte[] ToYuv411(byte[] buffer) {
    var padded = (width + 7) & ~7;
    var chroma = padded / 4;
    var y = new byte[padded * height];
    var u = new byte[chroma * height];
    var v = new byte[chroma * height];
    for (var row = 0; row < height; ++row) {
      var src = (height - 1 - row) * this.Stride;
      int yi = row * padded, ci = row * chroma;
      for (var x = 0; x < padded; x += 8) {
        u[ci] = buffer[src++];
        y[yi++] = buffer[src++];
        v[ci] = buffer[src++];
        y[yi++] = buffer[src++];
        u[ci + 1] = buffer[src++];
        y[yi++] = buffer[src++];
        v[ci + 1] = buffer[src++];
        y[yi++] = buffer[src++];
        for (var k = 0; k < 4; ++k)
          y[yi++] = buffer[src++];
        ci += 2;
      }
    }
    var cw = (width + 3) / 4;
    var picture = new byte[width * height + 2 * cw * height];
    for (var row = 0; row < height; ++row) {
      y.AsSpan(row * padded, width).CopyTo(picture.AsSpan(row * width));
      u.AsSpan(row * chroma, cw).CopyTo(picture.AsSpan(width * height + row * cw));
      v.AsSpan(row * chroma, cw).CopyTo(picture.AsSpan(width * height + cw * height + row * cw));
    }
    return picture;
  }

  /// <summary>
  /// Residuals for a picture: each row's words chosen byte by byte against the decoded row above so the
  /// predictor lands as near each six-bit sample as its carries allow; the rows as decoded follow.
  /// </summary>
  internal byte[] Residuals(ReadOnlySpan<byte> luma, ReadOnlySpan<byte> blue, ReadOnlySpan<byte> red) {
    byte[] yPlane = luma.ToArray(), uPlane = blue.ToArray(), vPlane = red.ToArray();
    var padded = (width + 7) & ~7;
    var cw = (width + 3) / 4;
    var target = new byte[this.Stride * height];
    for (var row = 0; row < height; ++row) {
      var at = (height - 1 - row) * this.Stride;
      for (var x = 0; x < padded; x += 8) {
        byte Y(int dx) => yPlane[row * width + Math.Min(x + dx, width - 1)];
        byte U(int c) => uPlane[row * cw + Math.Min(x / 4 + c, cw - 1)];
        byte V(int c) => vPlane[row * cw + Math.Min(x / 4 + c, cw - 1)];
        ReadOnlySpan<byte> group = [U(0), Y(0), V(0), Y(1), U(1), Y(2), V(1), Y(3), Y(4), Y(5), Y(6), Y(7)];
        group.CopyTo(target.AsSpan(at));
        at += 12;
      }
    }

    var residuals = new byte[target.Length];
    var words = this.Stride / 4;
    var above = new uint[words];
    Array.Fill(above, 0x80808080u);
    var decoded = new uint[words];
    for (var row = 0; row < height; ++row) {
      for (var j = 0; j < words; ++j) {
        var want = BinaryPrimitives.ReadUInt32LittleEndian(target.AsSpan((row * words + j) * 4));
        var residual = _Best(above[j], want);
        BinaryPrimitives.WriteUInt32LittleEndian(residuals.AsSpan((row * words + j) * 4), residual);
        decoded[j] = Predict(above[j], residual);
      }
      (above, decoded) = (decoded, above);
    }
    return residuals;
  }

  /// <summary>The residual word, its bytes up to 0x3F, whose prediction from <paramref name="above"/> lies nearest <paramref name="want"/>, chosen a byte at a time with the carry in mind.</summary>
  private static uint _Best(uint above, uint want) {
    uint residual = 0;
    for (var k = 0; k < 4; ++k) {
      var bestError = int.MaxValue;
      uint bestByte = 0;
      for (uint r = 0; r < 64; ++r) {
        var trial = residual | (r << (8 * k));
        var result = Predict(above, trial);
        var error = 0;
        // This byte decides itself and the low bit of the next.
        for (var m = k; m <= Math.Min(k + 1, 3); ++m) {
          var got = (int)((result >> (8 * m)) & 0xFF);
          var wanted = (int)((want >> (8 * m)) & 0xFF);
          error += m == k ? Math.Abs(got - wanted) * 4 : Math.Abs((got & 4) - (wanted & 4));
        }
        if (error < bestError) {
          bestError = error;
          bestByte = r;
          if (error == 0)
            break;
        }
      }
      residual |= bestByte << (8 * k);
    }
    return residual;
  }
}
