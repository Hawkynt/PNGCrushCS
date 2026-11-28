using System;
using System.IO;

namespace FileFormat.Codecs.Mss2;

/// <summary>The pictures MS Screen 2's region coder draws into: palette indices, their RGB, and the mask.</summary>
/// <remarks>
/// Everything is in the coder's own coordinates, row nought first; FFmpeg keeps the RGB picture upside
/// down in memory for that, and the decoder turns it the right way up only when it hands a frame out.
/// </remarks>
internal sealed class Mss12Picture {

  internal Mss12Picture(int width, int height) {
    this.Width = width;
    this.Height = height;
    this.PaletteStride = (width + 15) & ~15;
    this.Indices = new byte[this.PaletteStride * height];
    this.LastIndices = new byte[this.PaletteStride * height];
    this.Mask = new byte[this.PaletteStride * height];
  }

  internal int Width { get; }

  internal int Height { get; }

  internal int PaletteStride { get; }

  internal byte[] Indices { get; set; }

  internal byte[] LastIndices { get; set; }

  internal byte[] Mask { get; }

  /// <summary>The RGB picture being drawn, three bytes a sample, rows <see cref="Width"/> * 3 apart.</summary>
  internal byte[] Rgb { get; set; } = [];

  /// <summary>The previous picture where this one was started fresh, or <c>null</c> where it is drawn over it in place.</summary>
  internal byte[]? LastRgb { get; set; }

  internal uint[] Palette { get; } = new uint[256];

  internal bool KeyFrame { get; set; }

  internal int MotionX { get; set; }

  internal int MotionY { get; set; }

  internal void SetRgb(int x, int y, uint colour) {
    var at = (y * this.Width + x) * 3;
    this.Rgb[at] = (byte)(colour >> 16);
    this.Rgb[at + 1] = (byte)(colour >> 8);
    this.Rgb[at + 2] = (byte)colour;
  }
}

/// <summary>
/// MS Screen's region coder: a rectangle split recursively into pieces that are a flat colour, coded
/// pixel by pixel against their neighbours, copied from the last picture, moved from it, or masked.
/// </summary>
/// <remarks>
/// Converted from FFmpeg's <c>libavcodec/mss12.c</c> (LGPL-2.1-or-later) for MS Screen 2, which is
/// that file's version 1: an inter-picture pixel context of three symbols seeded 1, 2, 4, and an RGB
/// picture drawn alongside the palette one, so that the mask's values are 1, 2 and 4 — code, copy,
/// move. Every model and context belongs to a slice, and the two halves of a split picture each keep
/// their own.
/// </remarks>
internal sealed class Mss12SliceContext {

  private const int _SPLIT_VERTICAL = 0;
  private const int _SPLIT_HORIZONTAL = 1;
  private const int _SPLIT_NONE = 2;

  private static readonly int[] _SecondOrderSizes = [1, 7, 6, 1];

  private readonly Mss12Model _intraRegion = new(2, Mss12Model.AdaptiveThreshold);
  private readonly Mss12Model _interRegion = new(2, Mss12Model.AdaptiveThreshold);
  private readonly Mss12Model _splitMode = new(3, Mss12Model.HighThreshold);
  private readonly Mss12Model _edgeMode = new(2, Mss12Model.HighThreshold);
  private readonly Mss12Model _pivot = new(3, Mss12Model.LowThreshold);
  private readonly PixelContext _intraPixels;
  private readonly PixelContext _interPixels;
  private readonly Mss12Picture _picture;

  internal Mss12SliceContext(Mss12Picture picture, int fullModelSymbols) {
    this._picture = picture;
    this._intraPixels = new(8, fullModelSymbols, specialInitialCache: false);
    this._interPixels = new(3, fullModelSymbols, specialInitialCache: true);
  }

  /// <summary><c>ff_mss12_slicecontext_reset</c>.</summary>
  internal void Reset() {
    this._intraRegion.Reset();
    this._interRegion.Reset();
    this._splitMode.Reset();
    this._edgeMode.Reset();
    this._pivot.Reset();
    this._intraPixels.Reset();
    this._interPixels.Reset();
  }

  /// <summary><c>ff_mss12_decode_rect</c>.</summary>
  internal void DecodeRectangle(IMss12Coder coder, int x, int y, int width, int height) {
    switch (coder.ReadSymbol(this._splitMode)) {
      case _SPLIT_VERTICAL: {
        var pivot = this._Pivot(coder, height);
        this.DecodeRectangle(coder, x, y, width, pivot);
        this.DecodeRectangle(coder, x, y + pivot, width, height - pivot);
        return;
      }
      case _SPLIT_HORIZONTAL: {
        var pivot = this._Pivot(coder, width);
        this.DecodeRectangle(coder, x, y, pivot, height);
        this.DecodeRectangle(coder, x + pivot, y, width - pivot, height);
        return;
      }
      case _SPLIT_NONE:
        if (this._picture.KeyFrame)
          this._DecodeIntraRegion(coder, x, y, width, height);
        else
          this._DecodeInterRegion(coder, x, y, width, height);
        return;
      default:
        throw new InvalidDataException("An MS Screen 2 rectangle states a split mode the format does not have.");
    }
  }

  /// <summary><c>decode_pivot</c>.</summary>
  private int _Pivot(IMss12Coder coder, int basis) {
    var inverse = coder.ReadSymbol(this._edgeMode);
    var value = coder.ReadSymbol(this._pivot) + 1;
    if (value > 2) {
      if ((basis + 1) / 2 - 2 <= 0)
        throw new InvalidDataException("An MS Screen 2 split is wider than the rectangle it splits.");
      value = coder.ReadNumber((basis + 1) / 2 - 2) + 3;
    }

    if ((uint)value >= (uint)basis)
      throw new InvalidDataException("An MS Screen 2 split lies outside the rectangle it splits.");
    var pivot = inverse != 0 ? basis - value : value;
    if (pivot < 1)
      throw new InvalidDataException("An MS Screen 2 split leaves an empty rectangle.");
    return pivot;
  }

  private void _DecodeIntraRegion(IMss12Coder coder, int x, int y, int width, int height) {
    var picture = this._picture;
    if (coder.ReadSymbol(this._intraRegion) == 0) {
      var pixel = this._intraPixels.DecodePixel(coder, [], 0, anyNeighbours: false);
      var colour = picture.Palette[pixel];
      for (var j = 0; j < height; ++j) {
        picture.Indices.AsSpan((y + j) * picture.PaletteStride + x, width).Fill((byte)pixel);
        for (var i = 0; i < width; ++i)
          picture.SetRgb(x + i, y + j, colour);
      }

      return;
    }

    this._DecodeRegion(coder, picture.Indices, x, y, width, height, this._intraPixels, drawRgb: true);
  }

  private void _DecodeInterRegion(IMss12Coder coder, int x, int y, int width, int height) {
    var picture = this._picture;
    if (coder.ReadSymbol(this._interRegion) == 0) {
      var mode = this._interPixels.DecodePixel(coder, [], 0, anyNeighbours: false);
      if (mode == 0x02)
        this._CopyFromLast(x, y, width, height);
      else if (mode == 0x04)
        this._MotionCompensate(x, y, width, height);
      else if (mode != 0x80)
        this._DecodeIntraRegion(coder, x, y, width, height);
      return;
    }

    this._DecodeRegion(coder, picture.Mask, x, y, width, height, this._interPixels, drawRgb: false);
    this._DecodeMaskedRegion(coder, x, y, width, height);
  }

  /// <summary><c>decode_region</c>: every pixel coded, in raster order, against its neighbours.</summary>
  private void _DecodeRegion(IMss12Coder coder, byte[] plane, int x, int y, int width, int height, PixelContext context, bool drawRgb) {
    var stride = this._picture.PaletteStride;
    for (var j = 0; j < height; ++j)
      for (var i = 0; i < width; ++i) {
        var at = (y + j) * stride + x + i;
        var pixel = i == 0 && j == 0
          ? context.DecodePixel(coder, [], 0, anyNeighbours: false)
          : context.DecodePixelInContext(coder, plane, at, stride, i, j, width - i - 1);
        plane[at] = (byte)pixel;
        if (drawRgb)
          this._picture.SetRgb(x + i, y + j, this._picture.Palette[pixel]);
      }
  }

  /// <summary><c>decode_region_masked</c>: the mask says per pixel whether it is coded, copied or moved.</summary>
  private void _DecodeMaskedRegion(IMss12Coder coder, int x, int y, int width, int height) {
    var picture = this._picture;
    var stride = picture.PaletteStride;
    for (var j = 0; j < height; ++j)
      for (var i = 0; i < width; ++i) {
        var at = (y + j) * stride + x + i;
        var mask = picture.Mask[at];
        if (mask == 0x02) {
          this._CopyFromLast(x + i, y + j, 1, 1);
        } else if (mask == 0x04) {
          this._MotionCompensate(x + i, y + j, 1, 1);
        } else if (mask != 0x80) {
          var pixel = i == 0 && j == 0
            ? this._intraPixels.DecodePixel(coder, [], 0, anyNeighbours: false)
            : this._intraPixels.DecodePixelInContext(coder, picture.Indices, at, stride, i, j, width - i - 1);
          picture.Indices[at] = (byte)pixel;
          picture.SetRgb(x + i, y + j, picture.Palette[pixel]);
        }
      }
  }

  /// <summary><c>copy_rectangles</c>: a no-op where the picture is drawn over the last one in place.</summary>
  private void _CopyFromLast(int x, int y, int width, int height) {
    var picture = this._picture;
    if (picture.LastRgb == null)
      return;
    for (var j = y; j < y + height; ++j) {
      Array.Copy(picture.LastRgb, (j * picture.Width + x) * 3, picture.Rgb, (j * picture.Width + x) * 3, width * 3);
      Array.Copy(picture.LastIndices, j * picture.PaletteStride + x, picture.Indices, j * picture.PaletteStride + x, width);
    }
  }

  /// <summary><c>motion_compensation</c>: the rectangle moved by the picture's one vector, row by row as <c>memmove</c> does it.</summary>
  private void _MotionCompensate(int x, int y, int width, int height) {
    var picture = this._picture;
    var mx = picture.MotionX;
    var my = picture.MotionY;
    if (x + mx < 0 || x + mx + width > picture.Width || y + my < 0 || y + my + height > picture.Height)
      throw new InvalidDataException("An MS Screen 2 motion vector reaches outside the picture.");

    var sourceIndices = picture.LastRgb != null ? picture.LastIndices : picture.Indices;
    var sourceRgb = picture.LastRgb ?? picture.Rgb;
    for (var j = 0; j < height; ++j) {
      Array.Copy(sourceIndices, (y + my + j) * picture.PaletteStride + x + mx, picture.Indices, (y + j) * picture.PaletteStride + x, width);
      Array.Copy(sourceRgb, ((y + my + j) * picture.Width + x + mx) * 3, picture.Rgb, ((y + j) * picture.Width + x) * 3, width * 3);
    }
  }

  /// <summary>FFmpeg's <c>PixContext</c>: a move-to-front cache of recent colours and the models that code pixels.</summary>
  private sealed class PixelContext {

    private const int _TOP_LEFT = 0;
    private const int _TOP = 1;
    private const int _TOP_RIGHT = 2;
    private const int _LEFT = 3;

    private readonly int _cacheSize;
    private readonly int _symbolCount;
    private readonly bool _specialInitialCache;
    private readonly byte[] _cache = new byte[12];
    private readonly Mss12Model _cacheModel;
    private readonly Mss12Model _fullModel;
    private readonly Mss12Model[,] _secondary = new Mss12Model[15, 4];

    internal PixelContext(int cacheSize, int fullModelSymbols, bool specialInitialCache) {
      this._cacheSize = cacheSize + 4;
      this._symbolCount = cacheSize;
      this._specialInitialCache = specialInitialCache;
      this._cacheModel = new(this._symbolCount + 1, Mss12Model.LowThreshold);
      this._fullModel = new(fullModelSymbols, Mss12Model.HighThreshold);
      for (int i = 0, index = 0; i < 4; ++i)
        for (var j = 0; j < _SecondOrderSizes[i]; ++j, ++index)
          for (var k = 0; k < 4; ++k)
            this._secondary[index, k] = new(2 + i, i != 0 ? Mss12Model.LowThreshold : Mss12Model.AdaptiveThreshold);
    }

    internal void Reset() {
      if (!this._specialInitialCache) {
        for (var i = 0; i < this._cacheSize; ++i)
          this._cache[i] = (byte)i;
      } else {
        this._cache[0] = 1;
        this._cache[1] = 2;
        this._cache[2] = 4;
      }

      this._cacheModel.Reset();
      this._fullModel.Reset();
      foreach (var model in this._secondary)
        model.Reset();
    }

    /// <summary><c>decode_pixel</c>.</summary>
    internal int DecodePixel(IMss12Coder coder, ReadOnlySpan<byte> neighbours, int neighbourCount, bool anyNeighbours) {
      var value = coder.ReadSymbol(this._cacheModel);
      int pixel;
      if (value < this._symbolCount) {
        if (anyNeighbours) {
          var index = 0;
          int i;
          for (i = 0; i < this._cacheSize; ++i) {
            int j;
            for (j = 0; j < neighbourCount; ++j)
              if (this._cache[i] == neighbours[j])
                break;
            if (j == neighbourCount) {
              if (index == value)
                break;
              ++index;
            }
          }

          value = Math.Min(i, this._cacheSize - 1);
        }

        pixel = this._cache[value];
      } else {
        pixel = coder.ReadSymbol(this._fullModel);
        int i;
        for (i = 0; i < this._cacheSize - 1; ++i)
          if (this._cache[i] == pixel)
            break;
        value = i;
      }

      if (value != 0) {
        for (var i = value; i > 0; --i)
          this._cache[i] = this._cache[i - 1];
        this._cache[0] = (byte)pixel;
      }

      return pixel;
    }

    /// <summary><c>decode_pixel_in_context</c>.</summary>
    internal int DecodePixelInContext(IMss12Coder coder, byte[] plane, int at, int stride, int x, int y, int hasRight) {
      Span<byte> neighbours = stackalloc byte[4];
      if (y == 0) {
        neighbours.Fill(plane[at - 1]);
      } else {
        neighbours[_TOP] = plane[at - stride];
        if (x == 0) {
          neighbours[_TOP_LEFT] = neighbours[_LEFT] = neighbours[_TOP];
        } else {
          neighbours[_TOP_LEFT] = plane[at - stride - 1];
          neighbours[_LEFT] = plane[at - 1];
        }

        neighbours[_TOP_RIGHT] = hasRight != 0 ? plane[at - stride + 1] : neighbours[_TOP];
      }

      var sub = 0;
      if (x >= 2 && plane[at - 2] == neighbours[_LEFT])
        sub = 1;
      if (y >= 2 && plane[at - 2 * stride] == neighbours[_TOP])
        sub |= 2;

      Span<byte> reference = stackalloc byte[4];
      var count = 1;
      reference[0] = neighbours[0];
      for (var i = 1; i < 4; ++i) {
        int j;
        for (j = 0; j < count; ++j)
          if (reference[j] == neighbours[i])
            break;
        if (j == count)
          reference[count++] = neighbours[i];
      }

      var layer = count switch {
        1 => 0,
        2 => neighbours[_TOP] == neighbours[_TOP_LEFT]
          ? neighbours[_TOP_RIGHT] == neighbours[_TOP_LEFT] ? 1 : neighbours[_LEFT] == neighbours[_TOP_LEFT] ? 2 : 3
          : neighbours[_TOP_RIGHT] == neighbours[_TOP_LEFT]
            ? neighbours[_LEFT] == neighbours[_TOP_LEFT] ? 4 : 5
            : neighbours[_LEFT] == neighbours[_TOP_LEFT] ? 6 : 7,
        3 => neighbours[_TOP] == neighbours[_TOP_LEFT] ? 8
          : neighbours[_TOP_RIGHT] == neighbours[_TOP_LEFT] ? 9
          : neighbours[_LEFT] == neighbours[_TOP_LEFT] ? 10
          : neighbours[_TOP_RIGHT] == neighbours[_TOP] ? 11
          : neighbours[_TOP] == neighbours[_LEFT] ? 12
          : 13,
        _ => 14,
      };

      var pixel = coder.ReadSymbol(this._secondary[layer, sub]);
      return pixel < count ? reference[pixel] : this.DecodePixel(coder, reference, count, anyNeighbours: true);
    }
  }
}
