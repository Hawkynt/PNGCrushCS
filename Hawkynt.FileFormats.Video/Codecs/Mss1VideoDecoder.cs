using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Bmp;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>Decodes Microsoft Screen 1 / Windows Media Video V7 Screen (<c>MSS1</c>).</summary>
/// <remarks>
/// Adapted from FFmpeg's <c>libavcodec/mss1.c</c>, <c>mss12.c</c> and <c>mss12.h</c>, copyright
/// (c) 2012 Konstantin Shishkov, distributed there under LGPL-2.1-or-later. This adaptation is
/// distributed with PNGCrushCS under LGPL-3.0-or-later.
/// <para/>
/// MSS1 is an adaptive arithmetic-coded paletted screen codec. Rectangles recursively split, then
/// either fill from a move-to-front pixel cache, decode pixels with four-neighbour contexts, or keep
/// pixels from the persistent previous picture. The stream header carries the initial 256-entry
/// palette and says how many trailing palette entries keyframes may replace.
/// </remarks>
public sealed class Mss1VideoDecoder : IVideoCodecDecoder<Mss1VideoDecoder> {

  private const int _AdaptiveThreshold = -1;
  private const int _LowThreshold = 15;
  private const int _HighThreshold = 50;
  private const int _MaximumOverread = 16;

  private static readonly CodecTag _Tag = CodecTag.FromCharacters("MSS1");
  private static readonly int[] _SecondaryOrderSizes = [1, 7, 6, 1];

  private readonly int _width;
  private readonly int _height;
  private readonly int _freeColours;
  private readonly int _streamIndex;
  private readonly byte[] _palette;
  private readonly byte[] _picture;
  private readonly byte[] _mask;
  private readonly SliceContext _slice;
  private bool _corrupted = true;

  private Mss1VideoDecoder(
    int width,
    int height,
    int freeColours,
    byte[] palette,
    int streamIndex
  ) {
    this._width = width;
    this._height = height;
    this._freeColours = freeColours;
    this._palette = palette;
    this._streamIndex = streamIndex;
    this._picture = new byte[checked(width * height)];
    this._mask = new byte[this._picture.Length];
    this._slice = new SliceContext(fullModelSymbols: 256);
  }

  /// <summary>Gets the codec name.</summary>
  public static string CodecName => "MS Screen 1";

  /// <summary>Determines whether the specified media stream is supported.</summary>
  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    return stream.Kind == MediaStreamKind.Video && stream.Codec.EqualsIgnoringCase(_Tag);
  }

  /// <summary>Creates a decoder for the specified media stream.</summary>
  public static Mss1VideoDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.Width <= 0 || stream.Height <= 0 || stream.Width > 4096 || stream.Height > 4096)
      throw new InvalidDataException(
        $"MSS1 stream {stream.Index} states an invalid {stream.Width}x{stream.Height} display surface.");
    if ((long)stream.Width * stream.Height > int.MaxValue)
      throw new InvalidDataException($"MSS1 stream {stream.Index} is too large to hold in one managed frame.");

    var format = stream.CodecPrivateData.Span;
    var offset = BitmapInfoHeader.StructSize;
    const int minimumExtra = 52 + 256 * 3;
    if (format.Length < offset + minimumExtra)
      throw new InvalidDataException(
        $"MSS1 stream {stream.Index} carries {Math.Max(0, format.Length - offset)} codec-private byte(s), where at least {minimumExtra} are required.");

    var extra = format[offset..];
    var declaredSize = BinaryPrimitives.ReadUInt32BigEndian(extra);
    if (declaredSize < extra.Length)
      throw new InvalidDataException(
        $"MSS1 stream {stream.Index} declares {declaredSize} header byte(s), fewer than the {extra.Length} actually carried.");

    var headerVersion = BinaryPrimitives.ReadUInt32BigEndian(extra[4..]);
    if (headerVersion > 1)
      throw new InvalidDataException(
        $"MSS1 stream {stream.Index} carries an MSS2-era header version {headerVersion}.");

    var codedWidth = Math.Max(checked((int)BinaryPrimitives.ReadUInt32BigEndian(extra[20..])), stream.Width);
    var codedHeight = Math.Max(checked((int)BinaryPrimitives.ReadUInt32BigEndian(extra[24..])), stream.Height);
    if (codedWidth is < 1 or > 4096 || codedHeight is < 1 or > 4096)
      throw new InvalidDataException(
        $"MSS1 stream {stream.Index} states invalid coded dimensions {codedWidth}x{codedHeight}.");

    var freeColours = checked((int)BinaryPrimitives.ReadUInt32BigEndian(extra[48..]));
    if ((uint)freeColours > 256)
      throw new InvalidDataException(
        $"MSS1 stream {stream.Index} states {freeColours} changeable palette entries; the palette contains 256.");

    var palette = new byte[256 * 3];
    extra.Slice(52, palette.Length).CopyTo(palette);
    return new(stream.Width, stream.Height, freeColours, palette, stream.Index);
  }

  /// <summary>Attempts to decode the specified coded packet into a raw image frame.</summary>
  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    if (packet.Data.Length < 2)
      throw new InvalidDataException($"MSS1 stream {this._streamIndex} supplied fewer than 16 arithmetic seed bits.");

    var coder = new ArithmeticCoder(packet.Data.Span);
    var keyframe = coder.GetBit() == 0;
    if (keyframe) {
      this._corrupted = false;
      this._slice.Reset();
      this._DecodePalette(coder);
    } else if (this._corrupted) {
      throw new InvalidDataException("An MSS1 interframe arrived before a valid keyframe.");
    }

    try {
      this._DecodeRectangle(coder, this._slice, 0, 0, this._width, this._height, keyframe);
    } catch {
      this._corrupted = true;
      throw;
    }

    frame = new() {
      Width = this._width,
      Height = this._height,
      Format = PixelFormat.Rgb24,
      PixelData = this._ExpandRgb(),
    };
    return true;
  }

  private void _DecodePalette(ArithmeticCoder coder) {
    if (this._freeColours == 0)
      return;

    var count = coder.GetNumber(this._freeColours + 1);
    var paletteAt = (256 - this._freeColours) * 3;
    for (var i = 0; i < count; ++i) {
      this._palette[paletteAt++] = checked((byte)coder.GetBits(8));
      this._palette[paletteAt++] = checked((byte)coder.GetBits(8));
      this._palette[paletteAt++] = checked((byte)coder.GetBits(8));
    }
  }

  private void _DecodeRectangle(
    ArithmeticCoder coder,
    SliceContext slice,
    int x,
    int y,
    int width,
    int height,
    bool keyframe
  ) {
    if (coder.Overread > _MaximumOverread)
      throw new InvalidDataException("MSS1 arithmetic input overran the coded packet by more than sixteen padding bits.");

    var split = coder.GetModelSymbol(slice.SplitMode);
    switch (split) {
      case 0: {
        var pivot = _DecodePivot(coder, slice, height);
        if (pivot < 1)
          throw new InvalidDataException("MSS1 selected an invalid vertical rectangle pivot.");
        this._DecodeRectangle(coder, slice, x, y, width, pivot, keyframe);
        this._DecodeRectangle(coder, slice, x, y + pivot, width, height - pivot, keyframe);
        return;
      }
      case 1: {
        var pivot = _DecodePivot(coder, slice, width);
        if (pivot < 1)
          throw new InvalidDataException("MSS1 selected an invalid horizontal rectangle pivot.");
        this._DecodeRectangle(coder, slice, x, y, pivot, height, keyframe);
        this._DecodeRectangle(coder, slice, x + pivot, y, width - pivot, height, keyframe);
        return;
      }
      case 2:
        if (keyframe)
          this._DecodeIntraRegion(coder, slice, x, y, width, height);
        else
          this._DecodeInterRegion(coder, slice, x, y, width, height);
        return;
      default:
        throw new InvalidDataException($"MSS1 rectangle split symbol {split} is invalid.");
    }
  }

  private static int _DecodePivot(ArithmeticCoder coder, SliceContext slice, int basis) {
    var inverse = coder.GetModelSymbol(slice.EdgeMode);
    var value = coder.GetModelSymbol(slice.Pivot) + 1;
    if (value > 2) {
      var choices = (basis + 1) / 2 - 2;
      if (choices <= 0)
        return -1;
      value = coder.GetNumber(choices) + 3;
    }
    if ((uint)value >= basis)
      return -1;
    return inverse != 0 ? basis - value : value;
  }

  private void _DecodeIntraRegion(
    ArithmeticCoder coder,
    SliceContext slice,
    int x,
    int y,
    int width,
    int height
  ) {
    var mode = coder.GetModelSymbol(slice.IntraRegion);
    if (mode == 0) {
      var pixel = _DecodePixel(coder, slice.IntraPixels, ReadOnlySpan<byte>.Empty);
      for (var row = y; row < y + height; ++row)
        this._picture.AsSpan(row * this._width + x, width).Fill(pixel);
      return;
    }

    this._DecodeRegion(coder, this._picture, slice.IntraPixels, x, y, width, height);
  }

  private void _DecodeInterRegion(
    ArithmeticCoder coder,
    SliceContext slice,
    int x,
    int y,
    int width,
    int height
  ) {
    var mode = coder.GetModelSymbol(slice.InterRegion);
    if (mode == 0) {
      var action = _DecodePixel(coder, slice.InterPixels, ReadOnlySpan<byte>.Empty);
      // MSS1 decodes into a persistent picture. 0x02 and 0x80 therefore both mean that the existing
      // pixels survive unchanged. 0x04 is MSS2 motion compensation and has no valid MSS1 palette-only
      // implementation in the shared reference path.
      if (action is 0x02 or 0x80)
        return;
      if (action == 0x04)
        throw new InvalidDataException("MSS1 selected the MSS2-only motion-compensation action.");
      this._DecodeIntraRegion(coder, slice, x, y, width, height);
      return;
    }

    this._DecodeRegion(coder, this._mask, slice.InterPixels, x, y, width, height);
    this._DecodeMaskedRegion(coder, slice, x, y, width, height);
  }

  private void _DecodeRegion(
    ArithmeticCoder coder,
    byte[] destination,
    PixelContext pixels,
    int x,
    int y,
    int width,
    int height
  ) {
    for (var localY = 0; localY < height; ++localY)
      for (var localX = 0; localX < width; ++localX) {
        var pixel = localX == 0 && localY == 0
          ? _DecodePixel(coder, pixels, ReadOnlySpan<byte>.Empty)
          : this._DecodePixelInContext(coder, pixels, destination, x + localX, y + localY, x, y,
            localX, localY, localX + 1 < width);
        destination[(y + localY) * this._width + x + localX] = pixel;
      }
  }

  private void _DecodeMaskedRegion(
    ArithmeticCoder coder,
    SliceContext slice,
    int x,
    int y,
    int width,
    int height
  ) {
    for (var localY = 0; localY < height; ++localY)
      for (var localX = 0; localX < width; ++localX) {
        var absoluteX = x + localX;
        var absoluteY = y + localY;
        var at = absoluteY * this._width + absoluteX;
        var action = this._mask[at];
        if (action is 0x02 or 0x80)
          continue;
        if (action == 0x04)
          throw new InvalidDataException("MSS1 mask selected the MSS2-only motion-compensation action.");

        var pixel = localX == 0 && localY == 0
          ? _DecodePixel(coder, slice.IntraPixels, ReadOnlySpan<byte>.Empty)
          : this._DecodePixelInContext(coder, slice.IntraPixels, this._picture, absoluteX, absoluteY,
            x, y, localX, localY, localX + 1 < width);
        this._picture[at] = pixel;
      }
  }

  private byte _DecodePixelInContext(
    ArithmeticCoder coder,
    PixelContext context,
    byte[] source,
    int absoluteX,
    int absoluteY,
    int regionX,
    int regionY,
    int localX,
    int localY,
    bool hasRight
  ) {
    Span<byte> neighbours = stackalloc byte[4];
    if (localY == 0) {
      var left = source[absoluteY * this._width + absoluteX - 1];
      neighbours.Fill(left);
    } else {
      var top = source[(absoluteY - 1) * this._width + absoluteX];
      neighbours[1] = top;
      if (localX == 0) {
        neighbours[0] = top;
        neighbours[3] = top;
      } else {
        neighbours[0] = source[(absoluteY - 1) * this._width + absoluteX - 1];
        neighbours[3] = source[absoluteY * this._width + absoluteX - 1];
      }
      neighbours[2] = hasRight
        ? source[(absoluteY - 1) * this._width + absoluteX + 1]
        : top;
    }

    var sub = 0;
    if (localX >= 2 && source[absoluteY * this._width + absoluteX - 2] == neighbours[3])
      sub = 1;
    if (localY >= 2 && source[(absoluteY - 2) * this._width + absoluteX] == neighbours[1])
      sub |= 2;

    Span<byte> references = stackalloc byte[4];
    var referenceCount = 1;
    references[0] = neighbours[0];
    for (var i = 1; i < 4; ++i) {
      var seen = false;
      for (var j = 0; j < referenceCount; ++j)
        if (references[j] == neighbours[i]) {
          seen = true;
          break;
        }
      if (!seen)
        references[referenceCount++] = neighbours[i];
    }

    var layer = referenceCount switch {
      1 => 0,
      2 => _TwoNeighbourLayer(neighbours),
      3 => _ThreeNeighbourLayer(neighbours),
      4 => 14,
      _ => throw new InvalidDataException("MSS1 neighbour classification produced an impossible reference count."),
    };

    var symbol = coder.GetModelSymbol(context.Secondary[layer, sub]);
    return symbol < referenceCount
      ? references[symbol]
      : _DecodePixel(coder, context, references[..referenceCount]);
  }

  private static int _TwoNeighbourLayer(ReadOnlySpan<byte> neighbours) {
    if (neighbours[1] == neighbours[0]) {
      if (neighbours[2] == neighbours[0])
        return 1;
      return neighbours[3] == neighbours[0] ? 2 : 3;
    }
    if (neighbours[2] == neighbours[0])
      return neighbours[3] == neighbours[0] ? 4 : 5;
    return neighbours[3] == neighbours[0] ? 6 : 7;
  }

  private static int _ThreeNeighbourLayer(ReadOnlySpan<byte> neighbours) {
    if (neighbours[1] == neighbours[0])
      return 8;
    if (neighbours[2] == neighbours[0])
      return 9;
    if (neighbours[3] == neighbours[0])
      return 10;
    if (neighbours[2] == neighbours[1])
      return 11;
    return neighbours[1] == neighbours[3] ? 12 : 13;
  }

  private static byte _DecodePixel(ArithmeticCoder coder, PixelContext context, ReadOnlySpan<byte> neighbours) {
    if (coder.Overread > _MaximumOverread)
      throw new InvalidDataException("MSS1 arithmetic input overran the coded packet by more than sixteen padding bits.");

    var value = coder.GetModelSymbol(context.CacheModel);
    int cacheIndex;
    byte pixel;
    if (value < context.SymbolCount) {
      cacheIndex = value;
      if (!neighbours.IsEmpty) {
        var wanted = value;
        cacheIndex = 0;
        for (; cacheIndex < context.CacheSize; ++cacheIndex) {
          var excluded = false;
          foreach (var neighbour in neighbours)
            if (context.Cache[cacheIndex] == neighbour) {
              excluded = true;
              break;
            }
          if (excluded)
            continue;
          if (wanted-- == 0)
            break;
        }
        cacheIndex = Math.Min(cacheIndex, context.CacheSize - 1);
      }
      pixel = context.Cache[cacheIndex];
    } else {
      pixel = checked((byte)coder.GetModelSymbol(context.FullModel));
      cacheIndex = 0;
      while (cacheIndex < context.CacheSize - 1 && context.Cache[cacheIndex] != pixel)
        ++cacheIndex;
    }

    if (cacheIndex != 0) {
      for (var i = cacheIndex; i > 0; --i)
        context.Cache[i] = context.Cache[i - 1];
      context.Cache[0] = pixel;
    }
    return pixel;
  }

  private byte[] _ExpandRgb() {
    var output = new byte[checked(this._width * this._height * 3)];
    var at = 0;
    for (var displayY = 0; displayY < this._height; ++displayY) {
      var codedY = this._height - 1 - displayY;
      for (var x = 0; x < this._width; ++x) {
        var paletteAt = this._picture[codedY * this._width + x] * 3;
        output[at++] = this._palette[paletteAt];
        output[at++] = this._palette[paletteAt + 1];
        output[at++] = this._palette[paletteAt + 2];
      }
    }
    return output;
  }

  private sealed class SliceContext {
    internal readonly Model IntraRegion = new(2, _AdaptiveThreshold);
    internal readonly Model InterRegion = new(2, _AdaptiveThreshold);
    internal readonly Model Pivot = new(3, _LowThreshold);
    internal readonly Model EdgeMode = new(2, _HighThreshold);
    internal readonly Model SplitMode = new(3, _HighThreshold);
    internal readonly PixelContext IntraPixels;
    internal readonly PixelContext InterPixels;

    internal SliceContext(int fullModelSymbols) {
      this.IntraPixels = new(8, fullModelSymbols);
      this.InterPixels = new(2, fullModelSymbols);
      this.Reset();
    }

    internal void Reset() {
      this.IntraRegion.Reset();
      this.InterRegion.Reset();
      this.Pivot.Reset();
      this.EdgeMode.Reset();
      this.SplitMode.Reset();
      this.IntraPixels.Reset();
      this.InterPixels.Reset();
    }
  }

  private sealed class PixelContext {
    internal readonly int CacheSize;
    internal readonly int SymbolCount;
    internal readonly byte[] Cache = new byte[12];
    internal readonly Model CacheModel;
    internal readonly Model FullModel;
    internal readonly Model[,] Secondary = new Model[15, 4];

    internal PixelContext(int symbolCount, int fullModelSymbols) {
      this.CacheSize = symbolCount + 4;
      this.SymbolCount = symbolCount;
      this.CacheModel = new(symbolCount + 1, _LowThreshold);
      this.FullModel = new(fullModelSymbols, _HighThreshold);

      var index = 0;
      for (var order = 0; order < 4; ++order)
        for (var j = 0; j < _SecondaryOrderSizes[order]; ++j, ++index)
          for (var sub = 0; sub < 4; ++sub)
            this.Secondary[index, sub] = new(2 + order, order == 0 ? _AdaptiveThreshold : _LowThreshold);
    }

    internal void Reset() {
      for (var i = 0; i < this.CacheSize; ++i)
        this.Cache[i] = checked((byte)i);
      this.CacheModel.Reset();
      this.FullModel.Reset();
      for (var layer = 0; layer < 15; ++layer)
        for (var sub = 0; sub < 4; ++sub)
          this.Secondary[layer, sub].Reset();
    }
  }

  private sealed class Model {
    internal readonly int[] Cumulative = new int[257];
    internal readonly int[] Weights = new int[257];
    internal readonly byte[] IndexToSymbol = new byte[257];
    internal readonly int SymbolCount;
    private readonly int _thresholdWeight;
    private int _threshold;

    internal Model(int symbolCount, int thresholdWeight) {
      if (symbolCount is < 2 or > 256)
        throw new ArgumentOutOfRangeException(nameof(symbolCount));
      this.SymbolCount = symbolCount;
      this._thresholdWeight = thresholdWeight;
      this._threshold = symbolCount * thresholdWeight;
    }

    internal void Reset() {
      for (var i = 0; i <= this.SymbolCount; ++i) {
        this.Weights[i] = 1;
        this.Cumulative[i] = this.SymbolCount - i;
      }
      this.Weights[0] = 0;
      for (var i = 0; i < this.SymbolCount; ++i)
        this.IndexToSymbol[i + 1] = checked((byte)i);
    }

    internal void Update(int value) {
      if (this.Weights[value] == this.Weights[value - 1]) {
        var first = value;
        while (this.Weights[first - 1] == this.Weights[value])
          --first;
        if (first != value) {
          (this.IndexToSymbol[value], this.IndexToSymbol[first]) =
            (this.IndexToSymbol[first], this.IndexToSymbol[value]);
          value = first;
        }
      }

      ++this.Weights[value];
      for (var i = value - 1; i >= 0; --i)
        ++this.Cumulative[i];
      this._Rescale();
    }

    private void _Rescale() {
      if (this._thresholdWeight == _AdaptiveThreshold) {
        var denominator = 2 * this.Weights[this.SymbolCount] - 1;
        var threshold = (((denominator >> 1) + 4 * this.Cumulative[0]) / denominator);
        this._threshold = Math.Min(threshold, 0x3FFF);
      }

      while (this.Cumulative[0] > this._threshold) {
        var cumulative = 0;
        for (var i = this.SymbolCount; i >= 0; --i) {
          this.Cumulative[i] = cumulative;
          this.Weights[i] = (this.Weights[i] + 1) >> 1;
          cumulative += this.Weights[i];
        }
      }
    }
  }

  private sealed class ArithmeticCoder {
    private readonly MsbBitReader _bits;
    private int _low;
    private int _high = 0xFFFF;
    private int _value;

    internal ArithmeticCoder(ReadOnlySpan<byte> source) {
      this._bits = new(source);
      this._value = checked((int)this._bits.ReadBits(16));
    }

    internal int Overread { get; private set; }

    internal int GetBit() {
      var range = this._high - this._low + 1;
      var bit = 2 * this._value - this._low >= this._high ? 1 : 0;
      if (bit != 0)
        this._low += range >> 1;
      else
        this._high = this._low + (range >> 1) - 1;
      this._Normalise();
      return bit;
    }

    internal int GetBits(int count) {
      var range = this._high - this._low + 1;
      var value = (((this._value - this._low + 1) << count) - 1) / range;
      var probability = range * value;
      this._high = ((probability + range) >> count) + this._low - 1;
      this._low += probability >> count;
      this._Normalise();
      return value;
    }

    internal int GetNumber(int modulus) {
      if (modulus <= 0)
        throw new InvalidDataException("MSS1 arithmetic decoder received a non-positive modulus.");
      var range = this._high - this._low + 1;
      var value = ((this._value - this._low + 1) * modulus - 1) / range;
      var probability = range * value;
      this._high = (probability + range) / modulus + this._low - 1;
      this._low += probability / modulus;
      this._Normalise();
      return value;
    }

    internal int GetModelSymbol(Model model) {
      var range = this._high - this._low + 1;
      var total = model.Cumulative[0];
      var scaled = ((this._value - this._low + 1) * total - 1) / range;
      var index = 1;
      while (index <= model.SymbolCount && model.Cumulative[index] > scaled)
        ++index;
      if (index > model.SymbolCount)
        throw new InvalidDataException("MSS1 arithmetic probability fell outside its adaptive model.");

      this._high = range * model.Cumulative[index - 1] / total + this._low - 1;
      this._low += range * model.Cumulative[index] / total;
      var symbol = model.IndexToSymbol[index];
      model.Update(index);
      this._Normalise();
      return symbol;
    }

    private void _Normalise() {
      while (true) {
        if (this._high >= 0x8000) {
          if (this._low < 0x8000) {
            if (this._low >= 0x4000 && this._high < 0xC000) {
              this._value -= 0x4000;
              this._low -= 0x4000;
              this._high -= 0x4000;
            } else {
              return;
            }
          } else {
            this._value -= 0x8000;
            this._low -= 0x8000;
            this._high -= 0x8000;
          }
        }

        this._value <<= 1;
        this._low <<= 1;
        this._high = (this._high << 1) | 1;
        if (this._bits.RemainingBits < 1) {
          ++this.Overread;
        } else {
          this._value |= this._bits.ReadBit();
        }
      }
    }
  }

  private sealed class MsbBitReader {
    private readonly byte[] _source;
    private int _position;

    internal MsbBitReader(ReadOnlySpan<byte> source) => this._source = source.ToArray();

    internal int RemainingBits => this._source.Length * 8 - this._position;

    internal int ReadBit() {
      if (this.RemainingBits < 1)
        throw new InvalidDataException("MSS1 arithmetic seed is truncated.");
      var absolute = this._position++;
      return (this._source[absolute >> 3] >> (7 - (absolute & 7))) & 1;
    }

    internal uint ReadBits(int count) {
      if (count < 0 || count > 32 || this.RemainingBits < count)
        throw new InvalidDataException($"MSS1 bitstream ends while reading {count} bit(s).");
      uint value = 0;
      for (var i = 0; i < count; ++i)
        value = (value << 1) | (uint)this.ReadBit();
      return value;
    }
  }
}
