using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Bmp;
using FileFormat.Codecs.Mpeg4;
using FileFormat.Codecs.Mss2;
using FileFormat.Codecs.MsMpeg4;
using FileFormat.Codecs.Vc1;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Decodes MS Screen 2 — Windows Media Video 9 Screen (<c>MSS2</c>): palette pictures coded by MS
/// Screen's region coder or by run lengths, 15-bit pictures, and the WMV9 rectangles laid over either.
/// </summary>
/// <remarks>
/// Converted from FFmpeg's <c>libavcodec/mss2.c</c>, <c>mss12.c</c> and <c>mss2dsp.c</c>
/// (LGPL-2.1-or-later); see <c>Mss2/THIRD-PARTY-NOTICE.FFmpeg.txt</c>. A frame states a picture-wide
/// motion vector, a split into two slices, and a list of rectangles each of which is a Windows Media
/// Video 9 intra picture — the package's own VC-1 decoder reads them, as a Main profile picture with
/// the loop filter on — blended over the palette picture through an optional mask colour. Hands out
/// <see cref="PixelFormat.Rgb24"/>; a 15-bit stream's samples are widened by repeating their top bits,
/// as FFmpeg's scaler widens them.
/// </remarks>
public sealed class Mss2VideoDecoder : IVideoCodecDecoder<Mss2VideoDecoder> {

  private static readonly CodecTag _Tag = CodecTag.FromCharacters("MSS2");

  private const int _MAX_RECTANGLES = 20;

  private readonly int _width;
  private readonly int _height;
  private readonly int _freeColours;
  private readonly int _sliceSplit;
  private readonly Mss12Picture _picture;
  private readonly Mss12SliceContext[] _slices;
  private readonly bool _is555;
  private byte[]? _last;
  private ushort[]? _last555;
  private int _splitPosition;
  private bool _corrupted = true;

  private Mss2VideoDecoder(int width, int height, ReadOnlySpan<byte> extra) {
    this._width = width;
    this._height = height;
    this._freeColours = (int)BinaryPrimitives.ReadUInt32BigEndian(extra[48..]);
    if ((uint)this._freeColours > 256)
      throw new InvalidDataException($"This MS Screen 2 stream states {this._freeColours} changeable palette entries; there are 256.");
    this._sliceSplit = BinaryPrimitives.ReadInt32BigEndian(extra[52..]);
    var fullModelSymbols = (int)BinaryPrimitives.ReadUInt32BigEndian(extra[56..]);
    if (fullModelSymbols is < 2 or > 256)
      throw new InvalidDataException($"This MS Screen 2 stream states {fullModelSymbols} colours in use; the coder allows 2 to 256.");

    this._picture = new(width, height);
    for (var i = 0; i < 256; ++i)
      this._picture.Palette[i] = 0xFF000000u | (uint)(extra[60 + i * 3] << 16 | extra[61 + i * 3] << 8 | extra[62 + i * 3]);
    this._slices = [new(this._picture, fullModelSymbols), new(this._picture, fullModelSymbols)];
    this._is555 = this._freeColours == 127;
  }

  public static string CodecName => "MS Screen 2";

  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    return stream.Kind == MediaStreamKind.Video && stream.Codec.EqualsIgnoringCase(_Tag);
  }

  public static Mss2VideoDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (!Accepts(stream))
      throw new NotSupportedException($"Stream {stream.Index} is not MS Screen 2.");
    if (stream.Width <= 0 || stream.Height <= 0 || stream.Width > 4096 || stream.Height > 4096)
      throw new InvalidDataException($"MS Screen 2 stream {stream.Index} states an invalid {stream.Width}x{stream.Height} picture.");

    var format = stream.CodecPrivateData.Span;
    var extra = format.Length >= BitmapInfoHeader.StructSize && BinaryPrimitives.ReadUInt32LittleEndian(format) >= BitmapInfoHeader.StructSize
      ? format[BitmapInfoHeader.StructSize..]
      : format;
    if (extra.Length < 60 + 256 * 3)
      throw new InvalidDataException($"MS Screen 2 stream {stream.Index} carries {extra.Length} bytes of extradata, where {60 + 256 * 3} are needed.");
    if (BinaryPrimitives.ReadUInt32BigEndian(extra) < extra.Length)
      throw new InvalidDataException($"MS Screen 2 stream {stream.Index} states less extradata than it carries.");
    if (BinaryPrimitives.ReadUInt32BigEndian(extra[4..]) <= 1)
      throw new InvalidDataException($"Stream {stream.Index} carries an MS Screen 1 header under the MSS2 code.");
    return new(stream.Width, stream.Height, extra);
  }

  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    if (packet.Data.Length == 0) {
      frame = null!;
      return false;
    }

    var data = packet.Data.ToArray();
    var reader = new Mpeg4BitReader(data);
    var keyFrame = reader.ReadBit() == 1;
    if (keyFrame)
      reader.Skip(7);
    var hasWmv9 = reader.ReadBit() == 1;
    var hasMotion = !keyFrame && reader.ReadBit() == 1;
    var isRle = reader.ReadBit() == 1;
    var is555 = isRle && reader.ReadBit() == 1;

    if (this._sliceSplit > 0) {
      this._splitPosition = this._sliceSplit;
    } else if (this._sliceSplit < 0) {
      if (reader.ReadBit() == 1) {
        if (reader.ReadBit() == 1)
          this._splitPosition = reader.ReadBit() == 1 ? reader.ReadBits(16) : reader.ReadBits(12);
        else
          this._splitPosition = reader.ReadBits(8) << 4;
      } else if (keyFrame) {
        this._splitPosition = this._height / 2;
      }
    } else {
      this._splitPosition = this._height;
    }

    if (this._sliceSplit != 0 && (this._splitPosition < 1 - (is555 ? 1 : 0) || this._splitPosition > this._height - 1))
      throw new InvalidDataException($"This MS Screen 2 frame splits its {this._height} rows at {this._splitPosition}.");

    var at = (reader.BitPosition + 7) >> 3;
    if (data.Length - at < 1)
      throw new InvalidDataException("This MS Screen 2 frame ends inside its header.");
    if (is555 && (hasWmv9 || hasMotion || (this._sliceSplit != 0 && this._splitPosition != 0)))
      throw new InvalidDataException("This MS Screen 2 15-bit frame states tools a 15-bit frame cannot have.");

    if (is555 != this._is555 && keyFrame)
      throw new NotSupportedException("This MS Screen 2 stream changes between 15-bit and palette pictures, which this decoder does not follow.");

    if (is555) {
      frame = this._Decode555(data, at, keyFrame);
      return true;
    }

    // The WMV9 rectangle list, in its own arithmetic-coded block.
    Span<(bool Coded, int X, int Y, int W, int H)> rectangles = stackalloc (bool, int, int, int, int)[_MAX_RECTANGLES];
    var used = 0;
    var implicitRectangle = false;
    var mask = -1;
    if (hasWmv9) {
      var coder = new Mss2ArithmeticCoder(data, at);
      implicitRectangle = coder.ReadBit() == 0;
      while (coder.ReadBit() == 1) {
        if (used == _MAX_RECTANGLES)
          throw new InvalidDataException("This MS Screen 2 frame states more than twenty WMV9 rectangles.");
        var x = used == 0 ? coder.ReadNumber(this._width) : coder.ReadNumber(this._width - rectangles[used - 1].X) + rectangles[used - 1].X;
        var y = coder.ReadNumber(this._height);
        var w = coder.ReadNumber(this._width - x) + 1;
        var h = coder.ReadNumber(this._height - y) + 1;
        rectangles[used++] = (false, x, y, w, h);
      }

      if (implicitRectangle && used != 0)
        throw new InvalidDataException("This MS Screen 2 frame states both the implicit WMV9 rectangle and a list of them.");
      if (implicitRectangle)
        rectangles[used++] = (false, 0, 0, this._width, this._height);

      for (var i = 0; i < used; ++i) {
        if (!implicitRectangle && coder.ReadBit() == 1)
          throw new InvalidDataException("This MS Screen 2 WMV9 rectangle states rectangles of its own.");
        if (i == 0) {
          mask = coder.ReadBit() - 1;
          if (mask == 0)
            mask = coder.ReadNumber(256);
        }

        rectangles[i].Coded = coder.ReadNumber(2) != 0;
      }

      at += coder.ConsumedBytes;
      if (data.Length - at < 1)
        throw new InvalidDataException("This MS Screen 2 frame ends after its rectangle list.");
    }

    var picture = this._picture;
    picture.MotionX = 0;
    picture.MotionY = 0;
    if (keyFrame) {
      at += this._DecodePalette(data, at);
    } else if (hasMotion) {
      at += 4;
      if (data.Length - at < 1)
        throw new InvalidDataException("This MS Screen 2 frame ends after its motion vector.");
      picture.MotionX = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(at - 4)) - this._width;
      picture.MotionY = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(at - 2)) - this._height;
    }

    if (picture.MotionX < 0 || picture.MotionY < 0) {
      if (this._last == null)
        throw new InvalidDataException("This MS Screen 2 stream begins with a frame that moves a picture there is none of.");
      (picture.Indices, picture.LastIndices) = (picture.LastIndices, picture.Indices);
      picture.LastRgb = this._last;
      picture.Rgb = new byte[this._width * this._height * 3];
    } else {
      if (this._last == null && !keyFrame)
        throw new InvalidDataException("This MS Screen 2 stream begins with an inter frame.");
      picture.Rgb = this._last ?? new byte[this._width * this._height * 3];
      picture.LastRgb = null;
    }

    if (keyFrame) {
      this._corrupted = false;
      this._slices[0].Reset();
      if (this._sliceSplit != 0)
        this._slices[1].Reset();
    }

    if (isRle) {
      var bits = new Mpeg4BitReader(data.AsSpan(at));
      this._DecodeRle(ref bits, keyFrame, 0);
      bits.AlignToByte();
      if (this._sliceSplit != 0)
        this._DecodeRle(ref bits, keyFrame, 1);
      bits.AlignToByte();
      at += bits.BitPosition >> 3;
    } else if (!implicitRectangle || mask != -1) {
      if (this._corrupted)
        throw new InvalidDataException("This MS Screen 2 inter frame follows a frame that did not decode.");
      picture.KeyFrame = keyFrame;
      this._corrupted = true;

      int left = 0, top = 0, right = this._width, bottom = this._height;
      if (mask == -1)
        for (var i = 0; i < used; ++i)
          _CalculateDrawRegion(ref left, ref top, ref right, ref bottom,
            rectangles[i].X, rectangles[i].Y, rectangles[i].X + rectangles[i].W, rectangles[i].Y + rectangles[i].H);

      if (left >= this._width || right > this._width || top >= this._height || bottom > this._height)
        throw new InvalidDataException("This MS Screen 2 frame's region to draw lies outside the picture.");

      var coder = new Mss2ArithmeticCoder(data, at);
      if (this._sliceSplit != 0 && bottom - top >= 10) {
        this._splitPosition = _SplitPosition(this._splitPosition, top, bottom, this._height);
        this._slices[0].DecodeRectangle(coder, 0, top, this._width, this._splitPosition - top);
        at += coder.ConsumedBytes;
        if (data.Length - at < 1)
          throw new InvalidDataException("This MS Screen 2 frame ends before its second slice.");
        coder = new(data, at);
        this._slices[1].DecodeRectangle(coder, 0, this._splitPosition, this._width, bottom - this._splitPosition);
      } else {
        this._slices[0].DecodeRectangle(coder, left, top, right - left, bottom - top);
      }

      at += coder.ConsumedBytes;
      this._corrupted = false;
    } else {
      Array.Clear(picture.Indices);
    }

    for (var i = 0; i < used; ++i) {
      var (coded, x, y, w, h) = rectangles[i];
      if (coded) {
        if (data.Length - at < 4)
          throw new InvalidDataException("This MS Screen 2 frame ends before one of its WMV9 rectangles.");
        var size = data[at] | data[at + 1] << 8 | data[at + 2] << 16;
        if (size == 0)
          throw new InvalidDataException("This MS Screen 2 WMV9 rectangle states a size of nought.");
        this._DecodeWmv9(data.AsSpan(at + 3), x, y, w, h, mask);
        at += size + 3;
      } else {
        for (var r = 0; r < h; ++r)
          for (var c = 0; c < w; ++c)
            if (mask == -1 || picture.Indices[(y + r) * picture.PaletteStride + x + c] == mask)
              picture.SetRgb(x + c, y + r, 0x808080);
      }
    }

    this._last = picture.Rgb;
    frame = this._Flip(picture.Rgb);
    return true;
  }

  // ============================================================================================
  // The palette and the draw region
  // ============================================================================================

  /// <summary><c>decode_pal_v2</c>: a key frame may restate the changeable end of the palette.</summary>
  private int _DecodePalette(byte[] data, int at) {
    if (this._freeColours == 0)
      return 0;
    var count = data[at];
    if (count > this._freeColours || data.Length - at < 2 + count * 3)
      throw new InvalidDataException("This MS Screen 2 key frame restates more of the palette than may change.");
    var first = 256 - this._freeColours;
    for (var i = 0; i < count; ++i)
      this._picture.Palette[first + i] = 0xFF000000u | (uint)(data[at + 1 + 3 * i] << 16 | data[at + 2 + 3 * i] << 8 | data[at + 3 + 3 * i]);
    return 1 + count * 3;
  }

  /// <summary><c>calc_draw_region</c>: the region left to draw once a WMV9 rectangle covers one side of it.</summary>
  private static void _CalculateDrawRegion(ref int left, ref int top, ref int right, ref int bottom, int rLeft, int rTop, int rRight, int rBottom) {
    if (rTop <= top && rBottom >= bottom) {
      if (rLeft <= left && rRight >= left)
        left = Math.Min(rRight, right);
      if (rRight >= right) {
        if (rLeft >= left) {
          if (rLeft < right)
            right = rLeft;
        } else {
          right = left;
        }
      }
    }

    if (rLeft <= left && rRight >= right) {
      if (rTop <= top && rBottom >= top)
        top = Math.Min(rBottom, bottom);
      if (rBottom >= bottom) {
        if (rTop >= top) {
          if (rTop < bottom)
            bottom = rTop;
        } else {
          bottom = top;
        }
      }
    }
  }

  private static int _SplitPosition(int split, int top, int bottom, int height) {
    if (top != 0 || bottom != height)
      split = top + split * (bottom - top) / height;
    return Math.Clamp(split, top + 1, bottom - 1);
  }

  // ============================================================================================
  // Run lengths: decode_rle
  // ============================================================================================

  private void _DecodeRle(ref Mpeg4BitReader bits, bool keyFrame, int slice) {
    var picture = this._picture;
    var width = this._width;
    var height = this._height;
    int x0 = 0, y0 = 0;
    var previousAvailable = false;
    if (!keyFrame) {
      x0 = bits.ReadBits(12);
      y0 = bits.ReadBits(12);
      var clipWidth = bits.ReadBits(12) + 1;
      var clipHeight = bits.ReadBits(12) + 1;
      if (x0 + clipWidth > width || y0 + clipHeight > height)
        throw new InvalidDataException("An MS Screen 2 run-length rectangle lies outside the picture.");
      width = clipWidth;
      height = clipHeight;
      previousAvailable = y0 != 0;
    } else if (slice > 0) {
      y0 = this._splitPosition;
      previousAvailable = true;
      height -= this._splitPosition;
    } else {
      height = this._splitPosition;
    }

    var alphabet = 270 - (keyFrame ? 1 : 0);
    var lengths = new int[270];
    var codes = new int[270];
    int currentLength = 0, readCodes = 0, nextCode = 0, currentCodes = 0, remaining;
    do {
      while (currentCodes-- > 0) {
        var symbol = bits.ReadBits(8);
        if (symbol >= 204 - (keyFrame ? 1 : 0))
          symbol += 14 - (keyFrame ? 1 : 0);
        else if (symbol > 189)
          symbol = bits.ReadBit() + (symbol << 1) - 190;
        if (lengths[symbol] != 0)
          throw new InvalidDataException("An MS Screen 2 run-length table states one symbol twice.");
        lengths[symbol] = currentLength;
        codes[symbol] = nextCode++;
        ++readCodes;
      }

      ++currentLength;
      nextCode <<= 1;
      remaining = (1 << currentLength) - nextCode;
      currentCodes = bits.ReadBits(_CeilLog2(remaining + 1));
      if (currentLength > 22 || currentCodes > remaining)
        throw new InvalidDataException("An MS Screen 2 run-length table is not a code.");
    } while (currentCodes != remaining);

    remaining = alphabet - readCodes;
    int surplus;
    while ((surplus = (2 << currentLength) - (nextCode << 1) - remaining) < 0) {
      ++currentLength;
      nextCode <<= 1;
    }

    for (var i = 0; i < alphabet; ++i)
      if (lengths[i] == 0) {
        if (surplus-- == 0) {
          ++currentLength;
          nextCode <<= 1;
        }

        lengths[i] = currentLength;
        codes[i] = nextCode++;
      }

    if (nextCode != 1 << currentLength)
      throw new InvalidDataException("An MS Screen 2 run-length table does not fill its code space.");
    var table = MsMpeg4VlcTable.FromCodes("an MS Screen 2 run-length table", codes[..alphabet], lengths[..alphabet]);

    var lastSymbol = 0;
    var repeat = 0;
    for (var row = 0; row < height; ++row) {
      var y = y0 + row;
      for (var column = 0; column < width; ++column) {
        if (repeat-- < 1) {
          var b = table.Read(ref bits);
          if (b < 256) {
            lastSymbol = b;
          } else if (b < 268) {
            b -= 256;
            if (b == 11)
              b = bits.ReadBits(4) + 10;
            repeat = (b == 0 ? 0 : bits.ReadBits(b)) + (1 << b) - 1;
            if (lastSymbol == -2) {
              var skip = Math.Min(repeat, width - column);
              repeat -= skip;
              column += skip;
              if (column >= width)
                break;
            }
          } else {
            lastSymbol = 267 - b;
          }
        }

        var x = x0 + column;
        var at = y * picture.PaletteStride + x;
        if (lastSymbol >= 0) {
          picture.Indices[at] = (byte)lastSymbol;
          picture.SetRgb(x, y, picture.Palette[lastSymbol]);
        } else if (lastSymbol == -1 && previousAvailable) {
          picture.Indices[at] = picture.Indices[at - picture.PaletteStride];
          Array.Copy(picture.Rgb, ((y - 1) * this._width + x) * 3, picture.Rgb, (y * this._width + x) * 3, 3);
        }
      }

      previousAvailable = true;
    }
  }

  private static int _CeilLog2(int v) => v <= 1 ? 0 : 32 - int.LeadingZeroCount(v - 1);

  // ============================================================================================
  // 15-bit pictures: decode_555
  // ============================================================================================

  private RawImage _Decode555(byte[] data, int at, bool keyFrame) {
    if (!keyFrame && this._last555 == null)
      throw new InvalidDataException("This MS Screen 2 15-bit stream begins with an inter frame.");
    var picture = this._last555 ??= new ushort[this._width * this._height];
    var position = at;
    int Next() => position < data.Length ? data[position++] : 0;

    int x0 = 0, y0 = 0, width = this._width, height = this._height;
    var previousAvailable = false;
    if (!keyFrame) {
      var a = Next() << 4;
      var t = Next();
      x0 = a | (t >> 4);
      var endX = ((t & 0xF) << 8) | Next();
      a = Next() << 4;
      t = Next();
      y0 = a | (t >> 4);
      var endY = ((t & 0xF) << 8) | Next();
      if (endX >= width || endY >= height || x0 > endX || y0 > endY)
        throw new InvalidDataException("An MS Screen 2 15-bit rectangle lies outside the picture.");
      width = endX - x0 + 1;
      height = endY - y0 + 1;
      previousAvailable = y0 != 0;
    }

    var lastSymbol = 0;
    var repeat = 0;
    for (var row = 0; row < height; ++row) {
      var y = y0 + row;
      for (var column = 0; column < width; ++column) {
        if (repeat-- < 1) {
          var b = Next();
          if (b < 128) {
            lastSymbol = (b << 8) | Next();
          } else if (b > 129) {
            repeat = 0;
            while (b-- > 130) {
              if (repeat >= (int.MaxValue >> 8) - 1)
                throw new InvalidDataException("An MS Screen 2 15-bit run overflows.");
              repeat = (repeat << 8) + Next() + 1;
            }

            if (lastSymbol == -2) {
              var skip = Math.Min(repeat, width - column);
              repeat -= skip;
              column += skip;
              if (column >= width)
                break;
            }
          } else {
            lastSymbol = 127 - b;
          }
        }

        var at2 = y * this._width + x0 + column;
        if (lastSymbol >= 0)
          picture[at2] = (ushort)lastSymbol;
        else if (lastSymbol == -1 && previousAvailable)
          picture[at2] = picture[at2 - this._width];
      }

      previousAvailable = true;
    }

    var rgb = new byte[this._width * this._height * 3];
    for (var y = 0; y < this._height; ++y)
      for (var x = 0; x < this._width; ++x) {
        var value = picture[(this._height - 1 - y) * this._width + x];
        var target = (y * this._width + x) * 3;
        int r = (value >> 10) & 31, g = (value >> 5) & 31, b = value & 31;
        rgb[target] = (byte)((r << 3) | (r >> 2));
        rgb[target + 1] = (byte)((g << 3) | (g >> 2));
        rgb[target + 2] = (byte)((b << 3) | (b >> 2));
      }

    return new() { Width = this._width, Height = this._height, Format = PixelFormat.Rgb24, PixelData = rgb };
  }

  // ============================================================================================
  // WMV9 rectangles: decode_wmv9 and mss2dsp
  // ============================================================================================

  private void _DecodeWmv9(ReadOnlySpan<byte> data, int x, int y, int w, int h, int mask) {
    var reader = new Vc1BitReader(data);
    var header = Vc1PictureHeader.ReadMss2Intra(ref reader, out var rangeReduced);
    var resolution = header.ResolutionIndex;
    if (resolution is 1 or 2)
      throw new NotSupportedException("This MS Screen 2 WMV9 rectangle is scaled down in one direction only, which FFmpeg asks for a sample of and this decoder does not decode.");

    var macroblockWidth = (w + 15) >> 4;
    var macroblockHeight = (h + 15) >> 4;
    if (resolution == 3) {
      macroblockWidth = (macroblockWidth + 1) >> 1;
      macroblockHeight = (macroblockHeight + 1) >> 1;
    }

    var sequence = new Vc1SequenceHeader(Vc1Profile.Main, 7, 31, true, false, false, false, 1, true, false, false, false, 0, 0, false);
    var decoder = new Vc1PictureDecoder(sequence, macroblockWidth, macroblockHeight);
    var frame = new Vc1Frame(macroblockWidth, macroblockHeight);
    decoder.DecodeIntra(ref reader, header, frame, rangeReduced, loopFilter: true);

    // FFmpeg's planes are as wide as the stream; what matters is that the upsampling below reads and
    // writes only inside the rectangle, which it does.
    var lumaStride = Math.Max(frame.LumaWidth, w + 1) * (resolution == 3 ? 2 : 1);
    var chromaStride = Math.Max(frame.ChromaWidth, (w + 1) / 2 + 1) * (resolution == 3 ? 2 : 1);
    var rows = Math.Max(frame.LumaHeight, h + 1) * (resolution == 3 ? 2 : 1);
    var luma = _Plane(frame.Luma, frame.LumaWidth, frame.LumaHeight, lumaStride, rows);
    var cb = _Plane(frame.Cb, frame.ChromaWidth, frame.ChromaHeight, chromaStride, rows / 2 + 1);
    var cr = _Plane(frame.Cr, frame.ChromaWidth, frame.ChromaHeight, chromaStride, rows / 2 + 1);
    if (resolution == 3) {
      _Upsample(luma, lumaStride, w, h);
      _Upsample(cb, chromaStride, (w + 1) >> 1, (h + 1) >> 1);
      _Upsample(cr, chromaStride, (w + 1) >> 1, (h + 1) >> 1);
    }

    var picture = this._picture;
    for (var r = 0; r < h; ++r) {
      var chromaRow = (r >> 1) * chromaStride;
      for (var i = 0; i < w; ++i) {
        if (mask != -1 && picture.Indices[(y + r) * picture.PaletteStride + x + i] != mask)
          continue;
        int luminance = luma[r * lumaStride + i];
        var u = cb[chromaRow + (i >> 1)] - 128;
        var v = cr[chromaRow + (i >> 1)] - 128;
        var target = ((y + r) * this._width + x + i) * 3;
        picture.Rgb[target] = _Clip(luminance + ((91881 * v + 32768) >> 16));
        picture.Rgb[target + 1] = _Clip(luminance + ((-22554 * u - 46802 * v + 32768) >> 16));
        picture.Rgb[target + 2] = _Clip(luminance + ((116130 * u + 32768) >> 16));
      }
    }
  }

  private static byte[] _Plane(int[] samples, int width, int height, int stride, int rows) {
    var plane = new byte[stride * rows];
    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x)
        plane[y * stride + x] = (byte)samples[y * width + x];
    return plane;
  }

  /// <summary><c>upsample_plane_c</c>: a half-size picture doubled in place, from the far corner back.</summary>
  private static void _Upsample(byte[] plane, int stride, int w, int h) {
    if (w == 0 || h == 0)
      return;
    w += w & 1;
    h += h & 1;

    var j = h - 1;
    Array.Copy(plane, stride * (j >> 1), plane, stride * j, w);
    while ((j -= 2) > 0) {
      var destination1 = stride * (j + 1);
      var destination2 = stride * j;
      var source1 = stride * ((j + 1) >> 1);
      var source2 = stride * (j >> 1);
      for (var i = (w - 1) >> 1; i >= 0; --i) {
        int a = plane[source1 + i];
        int b = plane[source2 + i];
        plane[destination1 + i] = (byte)((3 * a + b + 2) >> 2);
        plane[destination2 + i] = (byte)((a + 3 * b + 2) >> 2);
      }
    }

    for (j = h - 1; j >= 0; --j) {
      var p = stride * j;
      var i = w - 1;
      plane[p + i] = plane[p + (i >> 1)];
      while ((i -= 2) > 0) {
        int a = plane[p + (i >> 1)];
        int b = plane[p + ((i + 1) >> 1)];
        plane[p + i] = (byte)((3 * a + b + 1) >> 2);
        plane[p + i + 1] = (byte)((a + 3 * b + 1) >> 2);
      }
    }
  }

  private static byte _Clip(int value) => value < 0 ? (byte)0 : value > 255 ? (byte)255 : (byte)value;

  /// <summary>The coder's picture, whose row nought is the bottom one, the right way up.</summary>
  private RawImage _Flip(byte[] rgb) {
    var output = new byte[rgb.Length];
    var row = this._width * 3;
    for (var y = 0; y < this._height; ++y)
      Array.Copy(rgb, (this._height - 1 - y) * row, output, y * row, row);
    return new() { Width = this._width, Height = this._height, Format = PixelFormat.Rgb24, PixelData = output };
  }
}
