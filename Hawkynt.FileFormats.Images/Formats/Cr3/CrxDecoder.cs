using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.Cr3;

/// <summary>The Bayer mosaic a CR3's CRX track decodes to, as the sensor delivered it.</summary>
/// <param name="Width">Columns of the mosaic.</param>
/// <param name="Height">Rows of the mosaic.</param>
/// <param name="BitsPerSample">The sample depth the track states, usually 14.</param>
/// <param name="CfaLayout">The colour of the top-left 2x2: 0 RGGB, 1 GRBG, 2 GBRG, 3 BGGR.</param>
/// <param name="Samples">One sample a site, rows from the top, uncorrected.</param>
public sealed record Cr3Sensor(int Width, int Height, int BitsPerSample, int CfaLayout, ushort[] Samples);

/// <summary>
/// The image header a CRX track states in its <c>CMP1</c> box: size, tiling, planes, depth,
/// encoding and the number of wavelet levels.
/// </summary>
internal readonly record struct CrxHeader(
  int Version, int Width, int Height, int TileWidth, int TileHeight, int Bits, int Planes, int CfaLayout,
  int EncodingType, int Levels, int MdatHeaderSize, int MedianBits) {

  /// <summary>Reads a <c>CMP1</c> box's body (what follows its size and type).</summary>
  internal static CrxHeader Parse(ReadOnlySpan<byte> body) {
    if (body.Length < 32)
      throw new InvalidDataException("A CR3 CMP1 box is shorter than its fixed fields.");
    var version = BinaryPrimitives.ReadUInt16BigEndian(body[4..]);
    var width = (int)BinaryPrimitives.ReadUInt32BigEndian(body[8..]);
    var height = (int)BinaryPrimitives.ReadUInt32BigEndian(body[12..]);
    var tileWidth = (int)BinaryPrimitives.ReadUInt32BigEndian(body[16..]);
    var tileHeight = (int)BinaryPrimitives.ReadUInt32BigEndian(body[20..]);
    int bits = body[24], planes = body[25] >> 4, cfa = body[25] & 15, encoding = body[26] >> 4, levels = body[26] & 15;
    var mdatHeader = (int)BinaryPrimitives.ReadUInt32BigEndian(body[28..]);
    var median = bits;

    // An extended header may state a different depth for the median the planes are centred on.
    var boxSize = body.Length + 8;
    if (body.Length >= 36 && (BinaryPrimitives.ReadUInt32BigEndian(body[32..]) & 0x80000000) != 0 && planes == 4 && boxSize >= 56 && body.Length >= 60) {
      var useMedian = (BinaryPrimitives.ReadUInt32BigEndian(body[56..]) & 0x40000000) != 0;
      if (useMedian && boxSize > 84 && body.Length > 84)
        median = body[84];
    }

    if (width <= 0 || height <= 0 || tileWidth <= 0 || tileHeight <= 0)
      throw new InvalidDataException($"A CR3 CMP1 box states {width}x{height} in tiles of {tileWidth}x{tileHeight}.");
    return new(version, width, height, tileWidth, tileHeight, bits, planes, cfa, encoding, levels, mdatHeader, median);
  }
}

/// <summary>
/// Canon's CRX codec, written from a description of it — Laurent Clévy's <c>canon_cr3</c> notes
/// and the behaviour of LibRaw's and DNGLab's decoders, both LGPL-2.1-only and read rather than
/// converted — and checked against LibRaw's output sample for sample.
/// </summary>
/// <remarks>
/// A track's data opens with a header of nested records: each tile (<c>FF01</c>/<c>FF11</c>), each
/// of its planes (<c>FF02</c>/<c>FF12</c>) and each plane's subbands (<c>FF03</c>/<c>FF13</c>), each
/// stating its size. A Bayer image has four planes, one per site of the 2x2 pattern, each at half
/// the tile's size. Without wavelet levels a plane is one band of samples coded line by line in the
/// manner of JPEG-LS: a run of repeats where the neighbours agree, otherwise a difference from the
/// median edge predictor, both through an adaptive Golomb-Rice code. Samples are differences from
/// the middle of the range and are clipped into it.
/// </remarks>
internal static class CrxDecoder {

  private const int _ESCAPE = 41;
  private const int _ESCAPE_BITS = 21;
  private const int _K_MAX = 15;

  // The run-length code's steps, as JPEG-LS has them.
  private static ReadOnlySpan<byte> _RunBits => [0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 2, 2, 3, 3, 3, 3, 4, 4, 5, 5, 6, 6, 7, 7, 8, 9, 10, 11, 12, 13, 14, 15];

  internal static Cr3Sensor Decode(CrxHeader header, ReadOnlySpan<byte> track) {
    if (header.Planes != 4)
      throw new NotSupportedException($"This CR3 track has {header.Planes} planes; only the four of a Bayer image are decoded.");
    if (header.EncodingType != 0)
      throw new NotSupportedException($"This CR3 track is of encoding type {header.EncodingType}; only the Bayer encoding is decoded.");
    if (header.Levels != 0)
      return CrxWavelet.Decode(header, track);
    if (header.MdatHeaderSize > track.Length)
      throw new InvalidDataException("A CR3 track is shorter than its own header.");

    var tiles = CrxLayout.Parse(header, track[..header.MdatHeaderSize]);
    var data = track[header.MdatHeaderSize..];
    var image = new ushort[header.Width * header.Height];
    var median = 1 << (header.MedianBits - 1);
    var maximum = (1 << header.MedianBits) - 1;
    foreach (var tile in tiles) {
      var planes = new int[4][];
      for (var p = 0; p < 4; ++p) {
        var plane = tile.Planes[p];
        if (plane.Bands.Count != 1)
          throw new InvalidDataException("A CR3 plane without wavelet levels states more than one band.");
        var band = plane.Bands[0];
        var at = tile.Offset + tile.ExtraSize + plane.Offset + band.Offset;
        if (at < 0 || at + band.DataSize > data.Length)
          throw new InvalidDataException("A CR3 band lies outside its track.");
        planes[p] = DecodeBand(data.Slice(at, band.DataSize), tile.PlaneWidth, tile.PlaneHeight, plane.SupportsPartial, plane.RoundedBitsMask);
      }

      for (var row = 0; row < tile.Height && tile.Top + row < header.Height; ++row) {
        var line = row >> 1;
        int[] left = planes[(row & 1) * 2], right = planes[(row & 1) * 2 + 1];
        var target = (tile.Top + row) * header.Width + tile.Left;
        for (var x = 0; x < tile.PlaneWidth && tile.Left + 2 * x + 1 < header.Width + 1; ++x) {
          image[target + 2 * x] = (ushort)Math.Clamp(median + left[line * tile.PlaneWidth + x], 0, maximum);
          if (tile.Left + 2 * x + 1 < header.Width)
            image[target + 2 * x + 1] = (ushort)Math.Clamp(median + right[line * tile.PlaneWidth + x], 0, maximum);
        }
      }
    }

    return new(header.Width, header.Height, header.Bits, header.CfaLayout, image);
  }

  /// <summary>One band, line by line; the result is <paramref name="width"/> by <paramref name="height"/>.</summary>
  internal static int[] DecodeBand(ReadOnlySpan<byte> data, int width, int height, bool partial, int roundedMask) {
    var band = new BandState(width, roundedMask);
    var bits = new CrxBits(data);
    var result = new int[width * height];
    for (var row = 0; row < height; ++row) {
      band.Line(ref bits, row, partial);
      band.Current.AsSpan(1, width).CopyTo(result.AsSpan(row * width));
    }

    return result;
  }

  /// <summary>The two line buffers a band is predicted through, each a sample wider at both ends.</summary>
  internal sealed class BandState(int width, int roundedMask) {
    internal int[] Previous = new int[width + 2];
    internal int[] Current = new int[width + 2];
    internal readonly int[] LineK = new int[width + 2];
    internal int RunState;
    internal int K;
    internal readonly int Width = width;
    internal readonly int RoundedMask = roundedMask;
    internal int RoundedBits;

    internal void Line(ref CrxBits bits, int row, bool partial) {
      if (row == 0) {
        this.RunState = 0;
        this.K = 0;
        if (!partial)
          this._TopNoReference(ref bits);
        else if (this.RoundedMask <= 0)
          this._Top(ref bits);
        else {
          this.RoundedBits = 1;
          if ((this.RoundedMask & ~1) != 0)
            while (this.RoundedMask >> this.RoundedBits != 0)
              ++this.RoundedBits;
          this._TopRounded(ref bits);
        }

        return;
      }

      (this.Previous, this.Current) = (this.Current, this.Previous);
      if (!partial)
        this._NonTopNoReference(ref bits);
      else if (this.RoundedMask <= 0)
        this._NonTop(ref bits);
      else
        this._NonTopRounded(ref bits);
    }

    private int _Rice(ref CrxBits bits, bool adapt, int kMax) {
      var prefix = bits.Zeros();
      int value;
      if (prefix >= _ESCAPE)
        value = bits.Read(_ESCAPE_BITS);
      else if (this.K > 0)
        value = (prefix << this.K) | bits.Read(this.K);
      else
        value = prefix;
      if (adapt)
        this.K = _NextK(this.K, value, kMax);
      return value;
    }

    private static int _NextK(int k, int value, int kMax) {
      var next = k;
      var shifted = (uint)value >> k;
      if (shifted > 2)
        ++next;
      if (shifted > 5)
        ++next;
      if ((uint)value < (uint)((1 << k) >> 1))
        --next;
      return kMax > 0 ? Math.Min(next, kMax) : next;
    }

    private static int _Signed(int code) => -(code & 1) ^ (int)((uint)code >> 1);

    private static int _Median(int a, int b, int c) {
      if (c >= Math.Max(a, b))
        return Math.Min(a, b);
      if (c <= Math.Min(a, b))
        return Math.Max(a, b);
      return a + b - c;
    }

    /// <summary>A run of samples equal to their left neighbour (or nought), its length coded as JPEG-LS codes runs.</summary>
    private int _Run(ref CrxBits bits, int remaining) {
      var count = 1;
      while (count != remaining && bits.Read(1) == 1) {
        count += 1 << _RunBits[this.RunState];
        if (count > remaining) {
          count = remaining;
          break;
        }

        this.RunState = Math.Min(this.RunState + 1, 31);
      }

      if (count < remaining) {
        if (_RunBits[this.RunState] > 0)
          count += bits.Read(_RunBits[this.RunState]);
        this.RunState = Math.Max(this.RunState - 1, 0);
        if (count > remaining)
          throw new InvalidDataException("A CR3 run passes the end of its line.");
      }

      return count;
    }

    // The neighbourhood of the sample at position x of the current line:  c b d / a x.
    private int _A(int x) => this.Current[x - 1];
    private int _B(int x) => this.Previous[x];
    private int _C(int x) => this.Previous[x - 1];
    private int _D(int x) => this.Previous[x + 1];

    private void _Top(ref CrxBits bits) {
      var x = 1;
      var remaining = this.Width;
      this.Current[0] = 0;
      while (remaining > 1) {
        if (this._A(x) != 0) {
          this.Current[x] = this._A(x);
        } else {
          if (bits.Read(1) == 1) {
            var n = this._Run(ref bits, remaining);
            remaining -= n;
            for (var i = 0; i < n; ++i, ++x)
              this.Current[x] = this._A(x);
            if (remaining == 0)
              break;
          }

          this.Current[x] = 0;
        }

        this.Current[x] += _Signed(this._Rice(ref bits, true, _K_MAX));
        ++x;
        --remaining;
      }

      if (remaining == 1) {
        var predicted = this._A(x);
        this.Current[x] = predicted + _Signed(this._Rice(ref bits, true, _K_MAX));
        ++x;
      }

      this.Current[x] = this._A(x) + 1;
    }

    private void _NonTop(ref CrxBits bits) {
      var x = 1;
      var remaining = this.Width;
      this.Current[0] = this._B(1);
      while (remaining > 1) {
        var predicted = 0;
        if (this._A(x) == this._B(x) && this._A(x) == this._D(x)) {
          if (bits.Read(1) == 1) {
            var n = this._Run(ref bits, remaining);
            remaining -= n;
            for (var i = 0; i < n; ++i, ++x)
              this.Current[x] = this._A(x);
          }

          if (remaining > 0)
            predicted = this._B(x);
        } else {
          predicted = _Median(this._A(x), this._B(x), this._C(x));
        }

        if (remaining > 0) {
          var code = this._Rice(ref bits, false, _K_MAX);
          this.Current[x] = predicted + _Signed(code);
          if (remaining > 1) {
            var delta = (this._D(x) - this._B(x)) << 1;
            code = (int)(((uint)code + (uint)Math.Abs(delta)) >> 1);
          }

          this.K = _NextK(this.K, code, _K_MAX);
          ++x;
        }

        remaining = Math.Max(remaining - 1, 0);
      }

      if (remaining == 1) {
        var predicted = _Median(this._A(x), this._B(x), this._C(x));
        this.Current[x] = predicted + _Signed(this._Rice(ref bits, true, _K_MAX));
        ++x;
      }

      this.Current[x] = this._A(x) + 1;
    }

    private void _TopNoReference(ref CrxBits bits) {
      var x = 1;
      var remaining = this.Width;
      this.Previous[0] = 0;
      this.Current[0] = 0;
      while (remaining > 1) {
        if (this._A(x) != 0) {
          this.Current[x] = _Signed(this._Rice(ref bits, true, _K_MAX));
        } else {
          if (bits.Read(1) == 1) {
            var n = this._Run(ref bits, remaining);
            remaining -= n;
            for (var i = 0; i < n; ++i, ++x) {
              this.Current[x] = 0;
              this.LineK[x - 1] = 0;
            }

            if (remaining == 0)
              break;
          }

          this.Current[x] = _Signed(this._Rice(ref bits, true, _K_MAX) + 1);
        }

        this.LineK[x - 1] = this.K;
        ++x;
        --remaining;
      }

      if (remaining == 1) {
        this.Current[x] = _Signed(this._Rice(ref bits, true, _K_MAX));
        this.LineK[x - 1] = this.K;
        ++x;
      }

      this.Current[x] = 0;
    }

    private void _NonTopNoReference(ref CrxBits bits) {
      var x = 1;
      var remaining = this.Width;
      while (remaining > 1) {
        if ((this._D(x) | this._B(x) | this._A(x)) != 0) {
          this.Current[x] = _Signed(this._Rice(ref bits, true, 0));
          this._AdjustK(x);
        } else {
          if (bits.Read(1) == 1) {
            var n = this._Run(ref bits, remaining);
            remaining -= n;
            for (var i = 0; i < n; ++i, ++x) {
              this.Current[x] = 0;
              this.LineK[x - 1] = 0;
            }
          }

          if (remaining <= 1) {
            if (remaining == 1) {
              this.Current[x] = _Signed(this._Rice(ref bits, true, _K_MAX) + 1);
              this.LineK[x - 1] = this.K;
              ++x;
              remaining = 0;
            }

            break;
          }

          this.Current[x] = _Signed(this._Rice(ref bits, true, 0) + 1);
          this._AdjustK(x);
        }

        this.LineK[x - 1] = this.K;
        ++x;
        --remaining;
      }

      if (remaining == 1) {
        this.Current[x] = _Signed(this._Rice(ref bits, true, _K_MAX));
        this.LineK[x - 1] = this.K;
        ++x;
      }
    }

    /// <summary>The high bands' K follows the line above: it rises where the line above's was higher, and stops at fifteen.</summary>
    private void _AdjustK(int x) {
      if (Math.Max(this.LineK[x] - this.K, 0) <= 1) {
        if (this.K >= 15)
          this.K = 15;
      } else {
        ++this.K;
      }
    }

    private void _RoundedSymbol(ref CrxBits bits, int x, bool median, bool notEnd) {
      var predicted = median ? _Median(this._A(x), this._B(x), this._C(x)) : this._B(x);
      var code = this._Rice(ref bits, false, _K_MAX);
      var signed = _Signed(code);
      this.Current[x] = this.RoundedMask * 2 * signed + (signed >> 31) + predicted;
      if (notEnd) {
        int step;
        if (this._D(x) > this._B(x))
          step = (this._D(x) - this._B(x) + this.RoundedMask - 1) >> this.RoundedBits;
        else
          step = -((this._B(x) - this._D(x) + this.RoundedMask) >> this.RoundedBits);
        this.K = _NextK(this.K, (int)(((uint)code + 2 * (uint)Math.Abs(step)) >> 1), _K_MAX);
      } else {
        this.K = _NextK(this.K, code, _K_MAX);
      }
    }

    private void _TopRounded(ref CrxBits bits) {
      var x = 1;
      var remaining = this.Width;
      this.Current[0] = 0;
      while (remaining > 1) {
        if (Math.Abs(this._A(x)) > this.RoundedMask) {
          this.Current[x] = this._A(x);
        } else {
          if (bits.Read(1) == 1) {
            var n = this._Run(ref bits, remaining);
            remaining -= n;
            for (var i = 0; i < n; ++i, ++x)
              this.Current[x] = this._A(x);
            if (remaining == 0)
              break;
          }

          this.Current[x] = 0;
        }

        var signed = _Signed(this._Rice(ref bits, true, _K_MAX));
        this.Current[x] += this.RoundedMask * 2 * signed + (signed >> 31);
        ++x;
        --remaining;
      }

      if (remaining == 1) {
        var signed = _Signed(this._Rice(ref bits, true, _K_MAX));
        this.Current[x] += this.RoundedMask * 2 * signed + (signed >> 31);
        ++x;
      }

      this.Current[x] = this._A(x) + 1;
    }

    private void _NonTopRounded(ref CrxBits bits) {
      var x = 1;
      var remaining = this.Width;
      var reached = false;
      this.Previous[0] = this._B(1);
      this.Current[0] = this._B(1);
      while (remaining > 1) {
        if (Math.Abs(this._D(x) - this._B(x)) > this.RoundedMask) {
          this._RoundedSymbol(ref bits, x, true, true);
          ++x;
          reached = true;
        } else if (reached || Math.Abs(this._C(x) - this._A(x)) > this.RoundedMask) {
          this._RoundedSymbol(ref bits, x, true, true);
          ++x;
          reached = false;
        } else {
          if (bits.Read(1) == 1) {
            var n = this._Run(ref bits, remaining);
            remaining -= n;
            for (var i = 0; i < n; ++i, ++x)
              this.Current[x] = this._A(x);
          }

          if (remaining > 1) {
            this._RoundedSymbol(ref bits, x, false, true);
            ++x;
            reached = Math.Abs(this._B(x) - this._C(x)) > this.RoundedMask;
          } else if (remaining == 1) {
            this._RoundedSymbol(ref bits, x, false, false);
            ++x;
          }
        }

        remaining = Math.Max(remaining - 1, 0);
      }

      if (remaining == 1) {
        this._RoundedSymbol(ref bits, x, true, false);
        ++x;
      }

      this.Current[x] = this._A(x) + 1;
    }
  }
}

/// <summary>Bits most significant first; past the end of the band, noughts.</summary>
internal ref struct CrxBits(ReadOnlySpan<byte> data) {
  private readonly ReadOnlySpan<byte> _data = data;
  private int _at;
  private ulong _buffer;
  private int _count;

  private void _Fill() {
    while (this._count <= 56) {
      ulong next = this._at < this._data.Length ? this._data[this._at] : 0UL;
      ++this._at;
      this._buffer |= next << (56 - this._count);
      this._count += 8;
    }
  }

  internal int Read(int n) {
    if (n == 0)
      return 0;
    this._Fill();
    var value = (int)(this._buffer >> (64 - n));
    this._buffer <<= n;
    this._count -= n;
    return value;
  }

  /// <summary>Counts nought bits up to the next one, and consumes that one too.</summary>
  internal int Zeros() {
    var zeros = 0;
    while (true) {
      this._Fill();
      if (this._buffer == 0) {
        zeros += this._count;
        this._buffer = 0;
        this._count = 0;
        if (this._at > this._data.Length + 64)
          throw new InvalidDataException("A CR3 band runs out of data inside a code.");
        continue;
      }

      var lead = System.Numerics.BitOperations.LeadingZeroCount(this._buffer);
      zeros += lead;
      this._buffer <<= lead + 1;
      this._count -= lead + 1;
      return zeros;
    }
  }
}

/// <summary>A tile of a CRX track, its planes, and their bands, as its header states them.</summary>
internal sealed class CrxTile {
  internal int Size;
  internal int Offset;
  internal int ExtraSize;
  internal int QpDataSize;
  internal int Left, Top, Width, Height, PlaneWidth, PlaneHeight;
  internal bool TilesLeft, TilesRight, TilesTop, TilesBottom;
  internal readonly List<CrxPlane> Planes = [];
}

internal sealed class CrxPlane {
  internal int Size;
  internal int Offset;
  internal bool SupportsPartial;
  internal int RoundedBitsMask;
  internal readonly List<CrxBand> Bands = [];
}

internal sealed class CrxBand {
  internal int Size;
  internal int Offset;
  internal int DataSize;
  internal int QParam;
  internal int QStepBase;
  internal int QStepMultiplier;
  internal bool NewQuantisation;
  internal int Width, Height;
  internal int RowStartAddon, RowEndAddon, ColStartAddon, ColEndAddon, LevelShift;
}

internal static class CrxLayout {

  internal static List<CrxTile> Parse(CrxHeader header, ReadOnlySpan<byte> records) {
    var tiles = new List<CrxTile>();
    var at = 0;
    int Indicator(ReadOnlySpan<byte> r, ref int p) {
      if (p + 2 > r.Length)
        return 0;
      var v = BinaryPrimitives.ReadUInt16BigEndian(r[p..]);
      p += 2;
      return v;
    }

    var tileOffset = 0;
    var ind = Indicator(records, ref at);
    while (ind is 0xFF01 or 0xFF11) {
      var tile = new CrxTile { Offset = tileOffset };
      var size = BinaryPrimitives.ReadUInt16BigEndian(records[at..]);
      tile.Size = (int)BinaryPrimitives.ReadUInt32BigEndian(records[(at + 2)..]);
      at += 10;
      if (size == 16) {
        tile.QpDataSize = (int)BinaryPrimitives.ReadUInt32BigEndian(records[at..]);
        tile.ExtraSize = tile.QpDataSize + BinaryPrimitives.ReadUInt16BigEndian(records[(at + 4)..]);
        at += 8;
      } else if (size != 8) {
        throw new InvalidDataException($"A CR3 tile record states {size} bytes.");
      }

      var planeOffset = 0;
      ind = Indicator(records, ref at);
      while (ind is 0xFF02 or 0xFF12) {
        var plane = new CrxPlane { Offset = planeOffset };
        at += 2;
        plane.Size = (int)BinaryPrimitives.ReadUInt32BigEndian(records[at..]);
        var flags = BinaryPrimitives.ReadUInt32BigEndian(records[(at + 4)..]);
        at += 8;
        plane.SupportsPartial = (flags & 0x8000000) != 0;
        var rounded = (int)((flags >> 25) & 3);
        plane.RoundedBitsMask = rounded != 0 ? 1 << (rounded - 1) : 0;

        var bandOffset = 0;
        ind = Indicator(records, ref at);
        while (ind is 0xFF03 or 0xFF13) {
          var band = new CrxBand { Offset = bandOffset };
          var bandRecord = BinaryPrimitives.ReadUInt16BigEndian(records[at..]);
          band.Size = (int)BinaryPrimitives.ReadUInt32BigEndian(records[(at + 2)..]);
          at += 6;
          if (ind == 0xFF03) {
            if (bandRecord != 8)
              throw new InvalidDataException($"A CR3 band record states {bandRecord} bytes.");
            var bandFlags = BinaryPrimitives.ReadUInt32BigEndian(records[at..]);
            at += 4;
            band.QParam = (int)((bandFlags >> 19) & 0xFF);
            band.DataSize = band.Size - (int)(bandFlags & 0x7FFFF);
          } else {
            if (bandRecord != 16)
              throw new InvalidDataException($"A CR3 band record states {bandRecord} bytes.");
            band.QStepMultiplier = BinaryPrimitives.ReadUInt16BigEndian(records[(at + 2)..]);
            band.QStepBase = BinaryPrimitives.ReadInt32BigEndian(records[(at + 4)..]);
            band.DataSize = band.Size - BinaryPrimitives.ReadUInt16BigEndian(records[(at + 8)..]);
            band.NewQuantisation = true;
            at += 12;
          }

          bandOffset += band.Size;
          plane.Bands.Add(band);
          ind = Indicator(records, ref at);
        }

        planeOffset += plane.Size;
        tile.Planes.Add(plane);
      }

      tileOffset += tile.Size;
      tiles.Add(tile);
    }

    if (tiles.Count == 0)
      throw new InvalidDataException("A CR3 track states no tile.");

    // Where each tile sits, and how big it is: every tile but the last in a row or column is the
    // stated tile size, the last takes what is left.
    var columns = (header.Width + header.TileWidth - 1) / header.TileWidth;
    var rows = (header.Height + header.TileHeight - 1) / header.TileHeight;
    if (columns * rows != tiles.Count)
      throw new InvalidDataException($"A CR3 track of {columns}x{rows} tiles states {tiles.Count}.");
    for (var i = 0; i < tiles.Count; ++i) {
      var tile = tiles[i];
      int column = i % columns, row = i / columns;
      tile.Left = column * header.TileWidth;
      tile.Top = row * header.TileHeight;
      tile.Width = column < columns - 1 ? header.TileWidth : header.Width - header.TileWidth * (columns - 1);
      tile.Height = row < rows - 1 ? header.TileHeight : header.Height - header.TileHeight * (rows - 1);
      tile.PlaneWidth = tile.Width >> 1;
      tile.PlaneHeight = tile.Height >> 1;
      tile.TilesLeft = column > 0;
      tile.TilesRight = column < columns - 1;
      tile.TilesTop = row > 0;
      tile.TilesBottom = row < rows - 1;
      if (tile.Planes.Count != header.Planes)
        throw new InvalidDataException($"A CR3 tile states {tile.Planes.Count} planes for a track of {header.Planes}.");
      foreach (var plane in tile.Planes) {
        if (header.Levels == 0) {
          plane.Bands[0].Width = tile.PlaneWidth;
          plane.Bands[0].Height = tile.PlaneHeight;
        }
      }
    }

    return tiles;
  }
}

/// <summary>
/// The wavelet-coded (C-RAW) tracks: each plane is up to three levels of a reversible LeGall 5/3
/// wavelet, its bands quantised and coded as the lossless band is.
/// </summary>
/// <remarks>
/// Every band is a bit stream of its own, so each is decoded whole and the inverse transform then
/// runs over whole arrays, level by level, rows then columns. Where a tile has a neighbour, its
/// bands carry extra coefficients from across the border — how many is a fixed table — and the
/// transform uses them in place of the symmetric extension it uses at the picture's own edges.
/// </remarks>
internal static class CrxWavelet {

  // How many extra coefficients a band carries towards a neighbouring tile, by level and by the
  // plane size's remainder modulo eight: copied exactly.
  private static ReadOnlySpan<byte> _ExtraCoefficients => [
    1, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 1, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0,
    1, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 1, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0,
    1, 1, 1, 1, 0, 0, 1, 0, 1, 0, 0, 0, 1, 2, 2, 1, 0, 0, 1, 1, 1, 1, 0, 0,
    1, 1, 1, 1, 0, 0, 1, 0, 1, 0, 0, 0, 1, 2, 2, 1, 0, 0, 1, 1, 1, 1, 0, 0,
    1, 1, 1, 1, 1, 1, 1, 0, 1, 0, 1, 0, 1, 2, 2, 2, 2, 1, 1, 1, 1, 2, 2, 1,
    1, 1, 1, 2, 2, 1, 1, 0, 1, 1, 1, 1, 1, 2, 2, 1, 1, 1, 1, 1, 1, 1, 1, 1,
  ];

  // The quantiser steps of one sixth of an octave, copied exactly.
  private static ReadOnlySpan<uint> _QuantSteps => [0x28, 0x2D, 0x33, 0x39, 0x40, 0x48];

  internal static Cr3Sensor Decode(CrxHeader header, ReadOnlySpan<byte> track) {
    if (header.Levels > 3)
      throw new NotSupportedException($"This CR3 track states {header.Levels} wavelet levels; at most three exist.");
    if (header.MdatHeaderSize > track.Length)
      throw new InvalidDataException("A CR3 track is shorter than its own header.");
    var tiles = CrxLayout.Parse(header, track[..header.MdatHeaderSize]);
    var data = track[header.MdatHeaderSize..];
    var image = new ushort[header.Width * header.Height];
    var median = 1 << (header.MedianBits - 1);
    var maximum = (1 << header.MedianBits) - 1;
    var levels = header.Levels;

    foreach (var tile in tiles) {
      var qSteps = tile.QpDataSize > 0 ? _QStepTables(tile, levels, data.Slice(tile.Offset, tile.QpDataSize)) : null;
      var planes = new int[4][];
      for (var p = 0; p < 4; ++p) {
        var plane = tile.Planes[p];
        if (plane.Bands.Count != 3 * levels + 1)
          throw new InvalidDataException($"A CR3 plane of {levels} levels states {plane.Bands.Count} bands.");
        _SizeBands(header, tile, plane);
        var bands = new int[plane.Bands.Count][];
        for (var b = 0; b < plane.Bands.Count; ++b) {
          var band = plane.Bands[b];
          var at = tile.Offset + tile.ExtraSize + plane.Offset + band.Offset;
          if (band.DataSize == 0) {
            bands[b] = new int[band.Width * band.Height];
            continue;
          }

          if (at < 0 || at + band.DataSize > data.Length)
            throw new InvalidDataException("A CR3 band lies outside its track.");
          var partial = plane.SupportsPartial && b == 0;
          bands[b] = CrxDecoder.DecodeBand(data.Slice(at, band.DataSize), band.Width, band.Height, partial, partial ? plane.RoundedBitsMask : 0);
          var level = Math.Max(b - 1, 0) / 3;
          _Dequantise(bands[b], band, qSteps?[level]);
        }

        planes[p] = _Inverse(header, tile, plane, bands);
      }

      for (var row = 0; row < tile.Height && tile.Top + row < header.Height; ++row) {
        var line = row >> 1;
        int[] left = planes[(row & 1) * 2], right = planes[(row & 1) * 2 + 1];
        var target = (tile.Top + row) * header.Width + tile.Left;
        for (var x = 0; x < tile.PlaneWidth; ++x) {
          image[target + 2 * x] = (ushort)Math.Clamp(median + left[line * tile.PlaneWidth + x], 0, maximum);
          if (tile.Left + 2 * x + 1 < header.Width)
            image[target + 2 * x + 1] = (ushort)Math.Clamp(median + right[line * tile.PlaneWidth + x], 0, maximum);
        }
      }
    }

    return new(header.Width, header.Height, header.Bits, header.CfaLayout, image);
  }

  /// <summary>Each band's size: half the level above, plus the coefficients it carries towards neighbouring tiles.</summary>
  private static void _SizeBands(CrxHeader header, CrxTile tile, CrxPlane plane) {
    var levels = header.Levels;
    int bandWidth = tile.PlaneWidth, bandHeight = tile.PlaneHeight;
    var table = _ExtraCoefficients;
    var rowBase = 0x30 * (levels - 1) + 6 * (tile.PlaneWidth & 7);
    var columnBase = 0x30 * (levels - 1) + 6 * (tile.PlaneHeight & 7);
    for (var level = 0; level < levels; ++level) {
      int oddWidth = bandWidth & 1, oddHeight = bandHeight & 1;
      bandWidth = (bandWidth + oddWidth) >> 1;
      bandHeight = (bandHeight + oddHeight) >> 1;
      int widthExtra0 = 0, widthExtra1 = 0, heightExtra0 = 0, heightExtra1 = 0, columnStart = 0, rowStart = 0;
      if (tile.TilesRight) {
        widthExtra0 = table[rowBase + 2 * level];
        widthExtra1 = table[rowBase + 2 * level + 1];
      }

      if (tile.TilesLeft) {
        ++widthExtra0;
        columnStart = 1;
      }

      if (tile.TilesBottom) {
        heightExtra0 = table[columnBase + 2 * level];
        heightExtra1 = table[columnBase + 2 * level + 1];
      }

      if (tile.TilesTop) {
        ++heightExtra0;
        rowStart = 1;
      }

      var i = (levels - level) * 3;
      _Set(plane.Bands[i], bandWidth + widthExtra0 - oddWidth, bandHeight + heightExtra0 - oddHeight, header.Version, level + 1, columnStart, widthExtra0 - columnStart, rowStart, heightExtra0 - rowStart);
      _Set(plane.Bands[i - 1], bandWidth + widthExtra1, bandHeight + heightExtra0 - oddHeight, header.Version, level + 1, 0, widthExtra1, rowStart, heightExtra0 - rowStart);
      _Set(plane.Bands[i - 2], bandWidth + widthExtra0 - oddWidth, bandHeight + heightExtra1, header.Version, level + 1, columnStart, widthExtra0 - columnStart, 0, heightExtra1);
    }

    int lowWidthExtra = tile.TilesRight ? table[rowBase + 2 * levels - 1] : 0;
    int lowHeightExtra = tile.TilesBottom ? table[columnBase + 2 * levels - 1] : 0;
    _Set(plane.Bands[0], bandWidth + lowWidthExtra, bandHeight + lowHeightExtra, header.Version, levels, 0, lowWidthExtra, 0, lowHeightExtra);
  }

  private static void _Set(CrxBand band, int width, int height, int version, int level, int columnStart, int columnEnd, int rowStart, int rowEnd) {
    band.Width = width;
    band.Height = height;
    if (version == 0x200) {
      band.RowStartAddon = rowStart;
      band.RowEndAddon = rowEnd;
      band.ColStartAddon = columnStart;
      band.ColEndAddon = columnEnd;
      band.LevelShift = 3 - level;
    }
  }

  private static uint _Step(int quant) {
    unchecked {
      var steps = _QuantSteps;
      return quant / 6 >= 6
        ? steps[quant % 6] << (quant / 6 + 26)
        : steps[quant % 6] >> (6 - quant / 6);
    }
  }

  /// <summary>Multiplies a band's coefficients by its quantiser step: one per band, or one per area from the tile's table.</summary>
  private static void _Dequantise(int[] values, CrxBand band, QStepTable? table) {
    if (table == null) {
      var scale = _Step(band.QParam);
      if (scale != 1)
        for (var i = 0; i < values.Length; ++i)
          values[i] = unchecked(values[i] * (int)scale);
      return;
    }

    int Scale(uint q) => Math.Clamp(band.QStepBase + (int)((q * (uint)band.QStepMultiplier) >> 3), 1, 0x168000);
    for (var row = 0; row < band.Height; ++row) {
      int tableRow;
      if (row < band.RowStartAddon)
        tableRow = 0;
      else if (row < band.Height - band.RowEndAddon)
        tableRow = row - band.RowEndAddon;
      else
        tableRow = band.Height - band.RowEndAddon - band.RowStartAddon - 1;
      var q = table.Steps.AsSpan(table.Width * tableRow);
      var line = values.AsSpan(row * band.Width, band.Width);
      var middle = band.Width - band.ColEndAddon - band.ColStartAddon;
      for (var x = 0; x < band.ColStartAddon; ++x)
        line[x] *= Scale(q[0]);
      var columns = 1 << band.LevelShift;
      for (var x = 0; x < middle; ++x) {
        var qi = x / columns;
        if (qi >= table.Width)
          break;
        line[band.ColStartAddon + x] *= Scale(q[qi]);
      }

      for (var x = band.ColStartAddon + middle; x < band.Width; ++x)
        line[x] *= Scale(q[(middle - 1) >> band.LevelShift]);
    }
  }

  internal sealed class QStepTable(int width, uint[] steps) {
    internal readonly int Width = width;
    internal readonly uint[] Steps = steps;
  }

  /// <summary>A tile's quantiser table: a grid of quantiser values, coded as the bands are, averaged down per level.</summary>
  private static QStepTable[] _QStepTables(CrxTile tile, int levels, ReadOnlySpan<byte> data) {
    var width = (tile.PlaneWidth >> 3) + ((tile.PlaneWidth & 7) != 0 ? 1 : 0);
    var height = (tile.PlaneHeight >> 1) + (tile.PlaneHeight & 1);
    var grid = new int[width * height];
    var bits = new CrxBits(data);
    var previous = new int[width + 2];
    var current = new int[width + 2];
    var k = 0;
    int Rice(ref CrxBits b, bool adapt, int kMax) {
      var prefix = b.Zeros();
      var value = prefix >= 23 ? b.Read(8) : k > 0 ? (prefix << k) | b.Read(k) : prefix;
      if (adapt)
        k = _NextK(k, value, kMax);
      return value;
    }

    static int Signed(int code) => -(code & 1) ^ (int)((uint)code >> 1);
    for (var row = 0; row < height; ++row) {
      var x = 1;
      if (row == 0) {
        current[0] = 0;
        for (var i = 0; i < width; ++i, ++x)
          current[x] = current[x - 1] + Signed(Rice(ref bits, true, 7));
      } else {
        current[0] = previous[1];
        var deltaH = previous[1] - previous[0];
        for (var remaining = width - 1; remaining >= 0; --remaining, ++x) {
          int a = current[x - 1], b = previous[x], c = previous[x - 1], d = previous[x + 1];
          var deltaV = c - a;
          int predicted;
          if (((deltaV < 0) ^ (deltaH < 0)) == false)
            predicted = a + deltaH;
          else if (((a < b) ^ (deltaH < 0)) == false)
            predicted = a;
          else
            predicted = b;
          var code = Rice(ref bits, false, 0);
          current[x] = predicted + Signed(code);
          if (remaining > 0) {
            deltaH = d - b;
            k = _NextK(k, (int)(((uint)code + 2 * (uint)Math.Abs(deltaH)) >> 1), 7);
          } else {
            k = _NextK(k, code, 7);
          }
        }
      }

      current[x] = current[x - 1] + 1;
      for (var col = 0; col < width; ++col)
        grid[row * width + col] = current[col + 1] + 4;
      (previous, current) = (current, previous);
    }

    // Level three averages four grid rows, level two two, level one takes them as they are.
    int height4 = (tile.PlaneHeight >> 2) + ((tile.PlaneHeight & 3) > 0 ? 1 : 0);
    int height8 = (tile.PlaneHeight >> 3) + ((tile.PlaneHeight & 7) > 0 ? 1 : 0);
    var tables = new List<QStepTable>();
    for (var level = levels; level >= 1; --level) {
      int rows = level switch { 3 => height8, 2 => height4, _ => height };
      var steps = new uint[width * rows];
      for (var r = 0; r < rows; ++r)
        for (var c = 0; c < width; ++c) {
          int quant;
          if (level == 3) {
            var sum = 0;
            for (var j = 0; j < 4; ++j)
              sum += grid[width * Math.Min(4 * r + j, height - 1) + c];
            quant = sum < 0 ? (sum + 3) / 4 : sum / 4;
          } else if (level == 2) {
            quant = (grid[width * Math.Min(2 * r, height - 1) + c] + grid[width * Math.Min(2 * r + 1, height - 1) + c]) / 2;
          } else {
            quant = grid[r * width + c];
          }

          steps[r * width + c] = _Step(quant);
        }

      tables.Add(new(width, steps));
    }

    return [.. tables];
  }

  private static int _NextK(int k, int value, int kMax) {
    var next = k;
    var shifted = (uint)value >> k;
    if (shifted > 2)
      ++next;
    if (shifted > 5)
      ++next;
    if ((uint)value < (uint)((1 << k) >> 1))
      --next;
    return kMax > 0 ? Math.Min(next, kMax) : next;
  }

  /// <summary>The inverse transform of one plane, from its coarsest level to the plane itself.</summary>
  private static int[] _Inverse(CrxHeader header, CrxTile tile, CrxPlane plane, int[][] bands) {
    var levels = header.Levels;
    var low = bands[0];
    int lowWidth = plane.Bands[0].Width, lowHeight = plane.Bands[0].Height;
    for (var level = 0; level < levels; ++level) {
      int outWidth, outHeight;
      if (level == levels - 1) {
        outWidth = tile.PlaneWidth;
        outHeight = tile.PlaneHeight;
      } else {
        outHeight = plane.Bands[3 * level + 4].Height;
        outWidth = plane.Bands[3 * level + 5].Width;
      }

      CrxBand hl = plane.Bands[3 * level + 1], lh = plane.Bands[3 * level + 2], hh = plane.Bands[3 * level + 3];
      int[] h1 = bands[3 * level + 1], l2 = bands[3 * level + 2], h3 = bands[3 * level + 3];

      // Rows: the low rows from LL and HL, the high rows from LH and HH.
      var lowRows = new int[lowHeight * outWidth];
      for (var r = 0; r < lowHeight; ++r)
        _Line(lowRows.AsSpan(r * outWidth, outWidth), low.AsSpan(r * lowWidth, lowWidth), h1.AsSpan(r * hl.Width, hl.Width), 1, 1, tile.TilesLeft, tile.TilesRight);
      var highRows = new int[lh.Height * outWidth];
      for (var r = 0; r < lh.Height; ++r)
        _Line(highRows.AsSpan(r * outWidth, outWidth), l2.AsSpan(r * lh.Width, lh.Width), h3.AsSpan(r * hh.Width, hh.Width), 1, 1, tile.TilesLeft, tile.TilesRight);

      // Columns.
      var result = new int[outWidth * outHeight];
      var lowColumn = new int[lowHeight];
      var highColumn = new int[lh.Height];
      var column = new int[outHeight];
      for (var c = 0; c < outWidth; ++c) {
        for (var r = 0; r < lowHeight; ++r)
          lowColumn[r] = lowRows[r * outWidth + c];
        for (var r = 0; r < lh.Height; ++r)
          highColumn[r] = highRows[r * outWidth + c];
        _Line(column, lowColumn, highColumn, 1, 1, tile.TilesTop, tile.TilesBottom);
        for (var r = 0; r < outHeight; ++r)
          result[r * outWidth + c] = column[r];
      }

      low = result;
      lowWidth = outWidth;
      lowHeight = outHeight;
    }

    return low;
  }

  /// <summary>
  /// The inverse 5/3 lifting of one line: even samples are the low ones less a quarter of their
  /// two high neighbours, odd samples the high ones plus half their two even neighbours; at an edge
  /// with no neighbouring tile the missing neighbour mirrors the present one.
  /// </summary>
  private static void _Line(Span<int> output, ReadOnlySpan<int> low, ReadOnlySpan<int> high, int lowStride, int highStride, bool extraBefore, bool extraAfter) {
    var n = output.Length;
    if (n <= 1) {
      output[0] = low[0];
      return;
    }

    int first;
    if (extraBefore) {
      first = low[0] - ((high[0] + high[1] + 2) >> 2);
      high = high[1..];
    } else {
      first = low[0] - ((high[0] + 1) >> 1);
    }

    output[0] = first;
    var pairs = (n - 2) / 2;
    var previous = first;
    for (var k = 0; k < pairs; ++k) {
      var even = low[1 + k] - ((high[k] + high[k + 1] + 2) >> 2);
      output[1 + 2 * k] = high[k] + ((previous + even) >> 1);
      output[2 + 2 * k] = even;
      previous = even;
    }

    var h = high[pairs];
    var at = 1 + 2 * pairs;
    if (extraAfter) {
      var even = low[1 + pairs] - ((h + high[pairs + 1] + 2) >> 2);
      output[at] = h + ((previous + even) >> 1);
      if ((n & 1) == 1)
        output[at + 1] = even;
    } else if ((n & 1) == 1) {
      var even = low[1 + pairs] - ((h + 1) >> 1);
      output[at] = h + ((previous + even) >> 1);
      output[at + 1] = even;
    } else {
      output[at] = h + previous;
    }
  }
}
