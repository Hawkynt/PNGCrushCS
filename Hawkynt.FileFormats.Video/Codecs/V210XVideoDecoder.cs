using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Decodes FFmpeg's v210x/YUV10 packing: uncompressed 10-bit 4:2:2 YUV in big-endian 32-bit words.
/// </summary>
/// <remarks>
/// v210x is not v210 with its bytes swapped. Both formats carry the same 4:2:2 samples, but v210x
/// lays the raster out as a continuous component stream <c>U, Y0, V, Y1, U, Y2, V, Y3, ...</c> and
/// packs each three consecutive ten-bit components into bits 31-22, 21-12 and 11-2 of one big-endian
/// word. The low two bits of every word are unused. Word boundaries therefore do not restart at a
/// scanline: with a width that is two or four modulo six, a word straddles the logical grouping that
/// would otherwise look like a row boundary.
/// <para/>
/// FFmpeg's reference decoder rejects odd widths, exposes the result as 4:2:2 planar sixteen-bit
/// samples with the ten meaningful bits left-aligned, and consumes the packed words continuously
/// until the raster is complete. This implementation keeps the same ten meaningful bits
/// right-aligned in <see cref="PixelFormat.Yuv422P10"/>'s sixteen-bit slots, as the rest of this
/// package does. Extra bytes after the active raster are tolerated because the raw <c>.yuv10</c>
/// demuxer pads each frame to the size obtained by rounding the width to 48 pixels.
/// <para/>
/// There is no inter-frame prediction, no B/P pictures, and no forward or backward reference state:
/// one packet is one independent picture.
/// <para/>
/// The packing was derived clean-room from the public behavior of FFmpeg's LGPL-2.1-or-later
/// <c>v210x</c> decoder and raw demuxer. No implementation code is copied here.
/// </remarks>
public sealed class V210XVideoDecoder : IVideoCodecDecoder<V210XVideoDecoder> {

  internal const string CodecId = "v210x";

  private readonly int _width;
  private readonly int _height;
  private readonly int _streamIndex;
  private readonly int _lumaSamples;
  private readonly int _chromaSamples;
  private readonly int _componentCount;
  private readonly int _activeByteCount;

  private V210XVideoDecoder(int width, int height, int streamIndex) {
    this._width = width;
    this._height = height;
    this._streamIndex = streamIndex;
    this._lumaSamples = checked(width * height);
    this._chromaSamples = this._lumaSamples / 2;
    this._componentCount = checked(this._lumaSamples * 2);
    this._activeByteCount = checked((int)(((long)this._componentCount + 2) / 3 * 4));
  }

  public static string CodecName => "Uncompressed 4:2:2 10-bit (v210x)";

  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    return stream.Kind == MediaStreamKind.Video
           && stream.Codec == CodecTag.None
           && string.Equals(stream.CodecId, CodecId, StringComparison.OrdinalIgnoreCase);
  }

  public static V210XVideoDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    if (stream.Width <= 0 || stream.Height <= 0)
      throw new InvalidDataException(
        $"Video stream {stream.Index} states a picture size of {stream.Width}x{stream.Height}, which no frame can be decoded into.");
    if ((stream.Width & 1) != 0)
      throw new InvalidDataException(
        $"Video stream {stream.Index} states an odd v210x width of {stream.Width}; 4:2:2 chroma needs whole two-pixel pairs.");

    try {
      return new(stream.Width, stream.Height, stream.Index);
    } catch (OverflowException error) {
      throw new InvalidDataException(
        $"Video stream {stream.Index} states a v210x picture size of {stream.Width}x{stream.Height}, which is too large to address.", error);
    }
  }

  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    var (luma, cb, cr) = this.DecodePlanes(packet.Data.Span);

    frame = new() {
      Width = this._width,
      Height = this._height,
      Format = PixelFormat.Rgb24,
      PixelData = this._ToRgb24(luma, cb, cr),
    };

    return true;
  }

  /// <summary>
  /// Unpacks the active raster into right-aligned ten-bit Y, Cb and Cr samples. Any trailing frame
  /// padding is ignored.
  /// </summary>
  internal (ushort[] Luma, ushort[] Cb, ushort[] Cr) DecodePlanes(ReadOnlySpan<byte> data) {
    if (data.Length < this._activeByteCount)
      throw new InvalidDataException(
        $"Video stream {this._streamIndex} carries a v210x packet of {data.Length} byte(s), where a "
        + $"{this._width}x{this._height} raster needs at least {this._activeByteCount} whole packed bytes.");

    var luma = new ushort[this._lumaSamples];
    var cb = new ushort[this._chromaSamples];
    var cr = new ushort[this._chromaSamples];
    Span<ushort> samples = stackalloc ushort[3];

    var component = 0;
    for (var offset = 0; component < this._componentCount; offset += 4) {
      var word = BinaryPrimitives.ReadUInt32BigEndian(data[offset..]);
      samples[0] = (ushort)((word >> 22) & 0x3FF);
      samples[1] = (ushort)((word >> 12) & 0x3FF);
      samples[2] = (ushort)((word >> 2) & 0x3FF);

      for (var slot = 0; slot < 3 && component < this._componentCount; ++slot, ++component) {
        var pair = component >> 2;
        switch (component & 3) {
          case 0:
            cb[pair] = samples[slot];
            break;
          case 1:
            luma[pair << 1] = samples[slot];
            break;
          case 2:
            cr[pair] = samples[slot];
            break;
          default:
            luma[(pair << 1) + 1] = samples[slot];
            break;
        }
      }
    }

    return (luma, cb, cr);
  }

  /// <summary>ITU-R BT.601 studio swing, with each 4:2:2 chroma sample shared by its two luma pixels.</summary>
  private byte[] _ToRgb24(ushort[] luma, ushort[] cb, ushort[] cr) {
    var rgb = new byte[checked(this._lumaSamples * 3)];

    for (var pixel = 0; pixel < this._lumaSamples; ++pixel) {
      var luma8 = luma[pixel] >> 2;
      var cb8 = cb[pixel >> 1] >> 2;
      var cr8 = cr[pixel >> 1] >> 2;
      var scaledLuma = 298 * (luma8 - 16);
      var blueDifference = cb8 - 128;
      var redDifference = cr8 - 128;
      var target = pixel * 3;

      rgb[target] = _Clamp(scaledLuma + 409 * redDifference + 128);
      rgb[target + 1] = _Clamp(scaledLuma - 100 * blueDifference - 208 * redDifference + 128);
      rgb[target + 2] = _Clamp(scaledLuma + 516 * blueDifference + 128);
    }

    return rgb;
  }

  private static byte _Clamp(int scaled) {
    var value = scaled >> 8;
    return (byte)(value < 0 ? 0 : value > 255 ? 255 : value);
  }
}
