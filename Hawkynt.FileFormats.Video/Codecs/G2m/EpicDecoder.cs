using System;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.Codecs.G2m;

/// <summary>
/// Go2Meeting's ePIC tile coder: lossless RGB pixels predicted from their neighbours by a context
/// model over the ELS decoder, with runs, a stack of colours already ruled out, and a per-tile cache of
/// which colours have followed which.
/// </summary>
/// <remarks>
/// Converted from the <c>epic_*</c> functions of FFmpeg's <c>libavcodec/g2meet.c</c>
/// (LGPL-2.1-or-later). The tile is decoded into a buffer FFmpeg keeps across tiles and pictures and
/// never clears, padded four bytes in front; that matters, because the block mask that decides which
/// JPEG blocks follow reads whole 8x8 blocks of it, past the tile's edge. So the buffer here is kept the
/// same way.
/// </remarks>
internal sealed class EpicDecoder {

  private const int _STACK_SIZE = 1024;
  private const int _STACK_MAX = _STACK_SIZE - 1;

  private ElsDecoder _els = null!;
  private int _nextRunPosition;
  private ElsDecoder.UnsignedRung _unsignedRung = new();
  private byte _wFlagRung;
  private byte _nFlagRung;
  private readonly byte[] _wContextRung = new byte[256];
  private readonly byte[] _nContextRung = new byte[512];
  private readonly byte[] _nwPredictionRung = new byte[256];
  private readonly byte[] _nePredictionRung = new byte[256];
  private readonly byte[] _previousRowRung = new byte[14];
  private readonly byte[] _runLengthZeroes = new byte[14];
  private byte _runLengthOne;
  private int _stackPosition;
  private readonly uint[] _stack = new uint[_STACK_SIZE];
  private readonly Dictionary<uint, LinkedList<CacheEntry>> _cache = [];

  /// <summary>The longest tile row the run coder's rungs can describe.</summary>
  internal const int MaxTileWidth = 1 << 14;

  private sealed class CacheEntry(uint pixel) {
    internal readonly uint Pixel = pixel;
    internal byte Rung;
  }

  /// <summary>Starts a tile: every rung and the cache back to nought, as FFmpeg's <c>memset</c> of the context does.</summary>
  internal (byte R, byte G, byte B) Begin(byte[] data, int offset, int size) {
    this._els = new(data, offset, size);
    this._nextRunPosition = 0;
    this._unsignedRung = new();
    this._wFlagRung = 0;
    this._nFlagRung = 0;
    Array.Clear(this._wContextRung);
    Array.Clear(this._nContextRung);
    Array.Clear(this._nwPredictionRung);
    Array.Clear(this._nePredictionRung);
    Array.Clear(this._previousRowRung);
    Array.Clear(this._runLengthZeroes);
    this._runLengthOne = 0;
    this._stackPosition = 0;
    Array.Clear(this._stack);
    this._cache.Clear();

    var r = (byte)this._els.DecodeUnsigned(this._unsignedRung);
    var g = (byte)this._els.DecodeUnsigned(this._unsignedRung);
    var b = (byte)this._els.DecodeUnsigned(this._unsignedRung);
    if (this._els.Failed)
      throw new InvalidDataException("A Go2Meeting ePIC tile ends before its transparent colour.");
    return (r, g, b);
  }

  private static int _Signed(uint value) => (int)(value >> 1) ^ -(int)(value & 1);

  private static int _Median(int a, int b, int c) => Math.Max(Math.Min(a, b), Math.Min(Math.Max(a, b), c));

  private bool _OnStack(uint pixel) {
    var n = Math.Min(this._stackPosition, _STACK_SIZE);
    for (var i = 0; i < n; ++i)
      if (this._stack[i] == pixel)
        return true;
    return false;
  }

  private void _Push(uint pixel) => this._stack[this._stackPosition++ & _STACK_MAX] = pixel;

  private int _Bit(ref byte rung) => this._els.DecodeBit(ref rung);

  private int _ComponentPrediction(int n, int w, int nw)
    => _Median(n, n + w - nw, w) - _Signed(this._els.DecodeUnsigned(this._unsignedRung));

  /// <summary><c>epic_decode_pixel_pred</c>.</summary>
  private uint _PixelPrediction(uint[] buffer, int row, int above, int x, int y) {
    int r, g, b;
    if (x != 0 && y != 0) {
      var w = buffer[row + x - 1];
      var n = buffer[above + x];
      var nw = buffer[above + x - 1];
      int gn = (int)(n >> 8) & 0xFF, gw = (int)(w >> 8) & 0xFF, gnw = (int)(nw >> 8) & 0xFF;
      g = this._ComponentPrediction(gn, gw, gnw);
      r = g + this._ComponentPrediction(((int)(n >> 16) & 0xFF) - gn, ((int)(w >> 16) & 0xFF) - gw, ((int)(nw >> 16) & 0xFF) - gnw);
      b = g + this._ComponentPrediction(((int)n & 0xFF) - gn, ((int)w & 0xFF) - gw, ((int)nw & 0xFF) - gnw);
    } else {
      var prediction = x != 0 ? buffer[row + x - 1] : buffer[above + x];
      r = (int)((prediction >> 16) & 0xFF) - _Signed(this._els.DecodeUnsigned(this._unsignedRung));
      g = (int)((prediction >> 8) & 0xFF) - _Signed(this._els.DecodeUnsigned(this._unsignedRung));
      b = (int)(prediction & 0xFF) - _Signed(this._els.DecodeUnsigned(this._unsignedRung));
    }

    if (r is < 0 or > 255 || g is < 0 or > 255 || b is < 0 or > 255)
      return 0;
    return (uint)(r << 16 | g << 8 | b);
  }

  /// <summary><c>epic_predict_pixel</c>: a nought bit means the pixel is the candidate.</summary>
  private bool _Predict(ref byte rung, ref uint pixel, uint candidate) {
    if (this._Bit(ref rung) == 0) {
      pixel = candidate;
      return true;
    }

    this._Push(candidate);
    return false;
  }

  /// <summary><c>epic_predict_pixel2</c>: a one bit means the pixel is the candidate.</summary>
  private bool _Predict2(ref byte rung, ref uint pixel, uint candidate) {
    if (this._Bit(ref rung) != 0) {
      pixel = candidate;
      return true;
    }

    this._Push(candidate);
    return false;
  }

  /// <summary><c>epic_handle_edges</c>.</summary>
  private bool _HandleEdges(uint[] buffer, int row, int above, int x, int y, ref uint pixel) {
    if (x == 0 && y == 0) {
      var r = this._els.DecodeUnsigned(this._unsignedRung);
      var g = this._els.DecodeUnsigned(this._unsignedRung);
      var b = this._els.DecodeUnsigned(this._unsignedRung);
      pixel = (r << 16) | (g << 8) | b;
      return true;
    }

    if (x != 0 && this._Predict(ref this._wFlagRung, ref pixel, buffer[row + x - 1]))
      return true;

    if (y != 0) {
      var n = buffer[above + x];
      if ((this._stackPosition == 0 || this._stack[0] != n) && this._Predict(ref this._nFlagRung, ref pixel, n))
        return true;
    }

    return false;
  }

  /// <summary><c>epic_decode_run_length</c>.</summary>
  private int _RunLength(uint[] buffer, int row, int above, int above2, int x, int tileWidth, ref uint pixel, out int run) {
    var gotPixel = false;
    var oldWWneW = 0;
    run = 0;

    uint w = 0, ww = 0, n = 0, nn = 0, nw = 0, ne = 0, nww = 0, nnw = 0, nne = 0;
    void Load(int at) {
      w = buffer[row + at - 1];
      n = buffer[above + at];
      ww = buffer[row + at - 2];
      nw = buffer[above + at - 1];
      ne = buffer[above + at + 1];
      nn = buffer[above2 + at];
      nnw = buffer[above2 + at - 1];
      nww = buffer[above + at - 2];
      nne = buffer[above2 + at + 1];
    }

    void Update(int at) {
      nnw = nn;
      nn = nne;
      nww = nw;
      nw = n;
      n = ne;
      ne = buffer[above + at + 1];
      nne = buffer[above2 + at + 1];
    }

    static int F(bool v) => v ? 1 : 0;

    Load(x);

    int wwNeW;
    if (this._nextRunPosition == x) {
      wwNeW = 1;
    } else {
      var index = F(ww != w) << 7 | F(nw != w) << 6 | F(n != ne) << 5 | F(nw != n) << 4
        | F(nww != nw) << 3 | F(nne != ne) << 2 | F(nn != n) << 1 | F(nnw != nw);
      wwNeW = this._Bit(ref this._wContextRung[index]);
      if (wwNeW < 0)
        return wwNeW;
    }

    if (wwNeW != 0) {
      this._Push(w);
    } else {
      pixel = w;
      gotPixel = true;
    }

    do {
      var nwNeW = 1;
      if (gotPixel) {
        nwNeW = F(pixel != n);
      } else {
        nwNeW = run != 0 ? nwNeW : F(nw != w);
        var handled = false;
        switch ((F(nw != n) << 2) | (nwNeW << 1) | wwNeW) {
          case 0:
            handled = true;
            break;
          case 3:
          case 5:
          case 6:
          case 7:
            if (!this._OnStack(n)) {
              var index = wwNeW << 8 | (run != 0 ? oldWWneW : F(ww != w)) << 7 | nwNeW << 6 | F(n != ne) << 5
                | F(nw != n) << 4 | F(nww != nw) << 3 | F(nne != ne) << 2 | F(nn != n) << 1 | F(nnw != nw);
              if (this._Bit(ref this._nContextRung[index]) == 0) {
                nwNeW = 0;
                pixel = n;
                gotPixel = true;
                handled = true;
              }
            }

            break;
        }

        if (!handled) {
          nwNeW = 1;
          oldWWneW = wwNeW;
          if (!this._OnStack(n))
            this._Push(n);
        }
      }

      ++run;
      if (x + run >= tileWidth - 1)
        break;

      Update(x + run);

      if (nwNeW == 0 && nw == n && n == ne) {
        var start = x + run;
        var same = buffer[above + start + 1];
        int position;
        for (position = start + 2; position < tileWidth; ++position)
          if (buffer[above + position] != same)
            break;
        var length = position - start - 1;
        var index = length <= 1 ? 0 : 32 - int.LeadingZeroCount(length - 1);
        if (this._Bit(ref this._previousRowRung[index]) != 0) {
          run += length;
        } else {
          var rle = 0;
          var flag = false;
          for (var p = index - 1; p >= 0; --p) {
            if ((1 << p) + rle < length) {
              int bit = flag ? this._Bit(ref this._runLengthOne) : this._Bit(ref this._runLengthZeroes[p]);
              if (bit != 0) {
                flag = true;
                rle |= 1 << p;
              }
            }
          }

          run += rle;
          break;
        }

        if (x + run >= tileWidth - 1)
          break;

        Load(x + run);
        wwNeW = 0;
        nwNeW = 0;
      }

      var context = wwNeW << 7 | nwNeW << 6 | F(n != ne) << 5 | F(nw != n) << 4
        | F(nww != nw) << 3 | F(nne != ne) << 2 | F(nn != n) << 1 | F(nnw != nw);
      wwNeW = this._Bit(ref this._wContextRung[context]);
    } while (wwNeW == 0);

    this._nextRunPosition = x + run;
    return gotPixel ? 1 : 0;
  }

  /// <summary><c>epic_predict_from_NW_NE</c>.</summary>
  private bool _PredictFromNorth(uint[] buffer, int row, int above, int x, int y, int run, int tileWidth, ref uint pixel) {
    if (x != 0 && y != 0) {
      var nw = buffer[above + x - 1];
      if (nw != buffer[row + x - 1] && nw != buffer[above + x] && !this._OnStack(nw)
          && this._Predict2(ref this._nwPredictionRung[nw & 0xFF], ref pixel, nw))
        return true;
    }

    var position = x + run - 1;
    if (position < tileWidth - 1 && y != 0) {
      var ne = buffer[above + position + 1];
      if (ne != buffer[above + position] && !this._OnStack(ne)
          && this._Predict2(ref this._nePredictionRung[ne & 0xFF], ref pixel, ne))
        return true;
    }

    return false;
  }

  /// <summary><c>epic_decode_from_cache</c>: the colours that have followed <paramref name="w"/> before, most recent first.</summary>
  private bool _FromCache(uint w, ref uint pixel) {
    if (!this._cache.TryGetValue(w, out var list) || list.Count == 0)
      return false;

    for (var node = list.First; node != null; node = node.Next) {
      var entry = node.Value;
      if (this._OnStack(entry.Pixel))
        continue;
      if (this._Bit(ref entry.Rung) != 0) {
        pixel = entry.Pixel;
        if (node != list.First) {
          list.Remove(node);
          list.AddFirst(node);
        }

        return true;
      }

      this._Push(entry.Pixel);
    }

    return false;
  }

  private bool _HasCacheEntries(uint pixel) => this._cache.TryGetValue(pixel, out var list) && list.Count != 0;

  /// <summary><c>epic_decode_tile</c> into <paramref name="buffer"/>, whose row <c>y</c> starts at <paramref name="origin"/> + y * <paramref name="stride"/>.</summary>
  internal void DecodeTile(uint[] buffer, int origin, int stride, int tileWidth, int tileHeight) {
    int row = origin, above = -1, above2;
    for (var y = 0; y < tileHeight; ++y, row += stride) {
      above2 = above;
      above = y == 0 ? -1 : row - stride;
      this._nextRunPosition = 0;
      for (var x = 0; x < tileWidth;) {
        if (this._els.Failed)
          throw new InvalidDataException("A Go2Meeting ePIC tile ends before its last pixel.");

        var w = buffer[row + x - 1];
        if (y >= 1 && x >= 2 && w != buffer[row + x - 2] && w != buffer[above + x - 1]
            && w != buffer[above + x - 2] && w != buffer[above + x] && !this._HasCacheEntries(w)) {
          buffer[row + x] = this._PixelPrediction(buffer, row, above, x, y);
          ++x;
          continue;
        }

        uint pixel = 0;
        int run;
        bool gotPixel;
        this._stackPosition = 0;
        if (y < 2 || x < 2 || x == tileWidth - 1) {
          run = 1;
          gotPixel = this._HandleEdges(buffer, row, above, x, y, ref pixel);
        } else {
          var result = this._RunLength(buffer, row, above, above2, x, tileWidth, ref pixel, out run);
          if (result < 0)
            throw new InvalidDataException("A Go2Meeting ePIC tile ends inside a run.");
          gotPixel = result != 0;
        }

        if (!gotPixel && !this._PredictFromNorth(buffer, row, above, x, y, run, tileWidth, ref pixel)) {
          var reference = buffer[row + x - 1];
          if (x == 0 || !this._FromCache(reference, ref pixel)) {
            pixel = this._PixelPrediction(buffer, row, above, x, y);
            if (this._OnStack(pixel))
              throw new InvalidDataException("A Go2Meeting ePIC pixel decodes to a colour it was told it is not.");
            if (x != 0) {
              if (!this._cache.TryGetValue(reference, out var list))
                this._cache[reference] = list = new();
              list.AddFirst(new CacheEntry(pixel));
            }
          }
        }

        for (; run > 0; ++x, --run)
          buffer[row + x] = pixel;
      }
    }
  }
}
