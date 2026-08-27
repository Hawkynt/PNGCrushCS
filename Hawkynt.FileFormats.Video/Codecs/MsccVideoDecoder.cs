using System;
using System.IO;
using System.IO.Compression;
using FileFormat.Bmp;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>Decodes Mandsoft Screen Capture Codec (<c>MSCC</c>) and its SRGC sibling.</summary>
/// <remarks>
/// Adapted from FFmpeg's <c>libavcodec/mscc.c</c>, copyright (c) 2017 Paul B Mahol, distributed
/// there under LGPL-2.1-or-later. This adaptation is distributed with PNGCrushCS under
/// LGPL-3.0-or-later.
/// <para/>
/// The outer stream is zlib. MSCC stores the zlib stream's first byte obfuscated across the first
/// three packet bytes; SRGC uses an ordinary zlib stream. Inflated data is a Microsoft-RLE-shaped
/// walk generalized from indexed bytes to 16-, 24- and 32-bit pixels, including end-of-line,
/// end-of-frame, delta and literal commands.
/// </remarks>
public sealed class MsccVideoDecoder : IVideoCodecDecoder<MsccVideoDecoder> {

  private static readonly CodecTag _MsccTag = CodecTag.FromCharacters("MSCC");
  private static readonly CodecTag _SrgcTag = CodecTag.FromCharacters("SRGC");

  private readonly int _width;
  private readonly int _height;
  private readonly int _bpp;
  private readonly int _bits;
  private readonly int _streamIndex;
  private readonly bool _obfuscatedZlibHeader;
  private readonly byte[]? _palette;

  private MsccVideoDecoder(
    int width,
    int height,
    int bits,
    int streamIndex,
    bool obfuscatedZlibHeader,
    byte[]? palette
  ) {
    this._width = width;
    this._height = height;
    this._bits = bits;
    this._bpp = bits >> 3;
    this._streamIndex = streamIndex;
    this._obfuscatedZlibHeader = obfuscatedZlibHeader;
    this._palette = palette;
  }

  public static string CodecName => "Mandsoft / Screen Recorder Gold";

  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    return stream.Kind == MediaStreamKind.Video
      && (stream.Codec.EqualsIgnoringCase(_MsccTag) || stream.Codec.EqualsIgnoringCase(_SrgcTag));
  }

  public static MsccVideoDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.Width <= 0 || stream.Height <= 0)
      throw new InvalidDataException(
        $"MSCC stream {stream.Index} states a picture of {stream.Width}x{stream.Height}, which has no pixels.");
    if (stream.BitsPerPixel is not (8 or 16 or 24 or 32))
      throw new NotSupportedException(
        $"MSCC stream {stream.Index} states {stream.BitsPerPixel} bits per pixel; defined layouts are 8, 16, 24 and 32.");

    var bpp = stream.BitsPerPixel >> 3;
    if ((long)stream.Width * stream.Height * bpp > int.MaxValue)
      throw new InvalidDataException($"MSCC stream {stream.Index}'s frame is too large to hold in memory.");

    byte[]? palette = null;
    if (stream.BitsPerPixel == 8) {
      var format = stream.CodecPrivateData.Span;
      var paletteBytes = Math.Max(0, format.Length - BitmapInfoHeader.StructSize);
      var entries = Math.Min(256, paletteBytes / 4);
      if (entries == 0)
        throw new NotSupportedException(
          $"MSCC stream {stream.Index} is indexed but its AVI stream format carries no palette.");
      palette = new byte[entries * 3];
      var source = format[BitmapInfoHeader.StructSize..];
      for (var i = 0; i < entries; ++i) {
        palette[i * 3] = source[i * 4 + 2];
        palette[i * 3 + 1] = source[i * 4 + 1];
        palette[i * 3 + 2] = source[i * 4];
      }
    }

    return new(
      stream.Width,
      stream.Height,
      stream.BitsPerPixel,
      stream.Index,
      stream.Codec.EqualsIgnoringCase(_MsccTag),
      palette
    );
  }

  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    var compressed = this._BuildZlibStream(packet.Data.Span);
    var inflated = _Inflate(compressed, checked(this._width * this._height * this._bpp * 2), this._streamIndex);
    var coded = this._DecodeRle(inflated);
    var topDown = this._FlipRows(coded);

    frame = this._bits switch {
      8 => new RawImage {
        Width = this._width,
        Height = this._height,
        Format = PixelFormat.Indexed8,
        PixelData = topDown,
        Palette = (byte[])this._palette!.Clone(),
        PaletteCount = this._palette.Length / 3,
      },
      16 => new RawImage {
        Width = this._width,
        Height = this._height,
        Format = PixelFormat.Rgb24,
        PixelData = _Rgb555ToRgb24(topDown),
      },
      24 => new RawImage {
        Width = this._width,
        Height = this._height,
        Format = PixelFormat.Bgr24,
        PixelData = topDown,
      },
      32 => new RawImage {
        Width = this._width,
        Height = this._height,
        Format = PixelFormat.Bgra32,
        PixelData = topDown,
      },
      _ => throw new InvalidOperationException(),
    };
    return true;
  }

  private byte[] _BuildZlibStream(ReadOnlySpan<byte> packet) {
    if (!this._obfuscatedZlibHeader)
      return packet.ToArray();

    if (packet.Length < 3)
      throw new InvalidDataException(
        $"MSCC stream {this._streamIndex} carries fewer than three bytes, so its zlib header cannot be reconstructed.");
    var result = new byte[packet.Length - 2];
    result[0] = (byte)(packet[2] ^ packet[0]);
    packet[3..].CopyTo(result.AsSpan(1));
    return result;
  }

  private byte[] _DecodeRle(ReadOnlySpan<byte> data) {
    var result = new byte[checked(this._width * this._height * this._bpp)];
    var input = 0;
    var x = 0;
    var y = 0;
    var ended = false;

    while (input < data.Length && !ended) {
      var run = data[input++];
      if (run != 0) {
        var pixel = this._ReadPixel(data, ref input);
        this._RequirePosition(x, y, run);
        for (var i = 0; i < run; ++i)
          this._WritePixel(result, y * this._width + x + i, pixel);
        x += run;
        continue;
      }

      if (input >= data.Length)
        throw new InvalidDataException($"MSCC stream {this._streamIndex} ends after an escape marker.");
      var command = data[input++];
      switch (command) {
        case 0:
          x = 0;
          ++y;
          if (y > this._height)
            throw new InvalidDataException($"MSCC stream {this._streamIndex} advances past the last row.");
          break;
        case 1:
          ended = true;
          break;
        case 2:
          if (data.Length - input < 2)
            throw new InvalidDataException($"MSCC stream {this._streamIndex} truncates a delta command.");
          x += data[input++];
          y += data[input++];
          if (x > this._width || y >= this._height)
            throw new InvalidDataException($"MSCC stream {this._streamIndex} moves its RLE cursor outside the frame.");
          break;
        default: {
          var count = command;
          this._RequirePosition(x, y, count);
          for (var i = 0; i < count; ++i)
            this._WritePixel(result, y * this._width + x + i, this._ReadPixel(data, ref input));
          x += count;
          if (this._bpp == 1 && (count & 1) != 0) {
            if (input >= data.Length)
              throw new InvalidDataException($"MSCC stream {this._streamIndex} omits literal-run padding.");
            ++input;
          }
          break;
        }
      }
    }

    if (!ended)
      throw new InvalidDataException($"MSCC stream {this._streamIndex} reaches the end of its RLE stream without an end marker.");
    return result;
  }

  private uint _ReadPixel(ReadOnlySpan<byte> data, ref int input) {
    if (data.Length - input < this._bpp)
      throw new InvalidDataException($"MSCC stream {this._streamIndex} truncates a pixel value.");
    uint value = 0;
    for (var i = 0; i < this._bpp; ++i)
      value |= (uint)data[input++] << (8 * i);
    return value;
  }

  private void _WritePixel(Span<byte> destination, int pixelIndex, uint value) {
    var at = pixelIndex * this._bpp;
    for (var i = 0; i < this._bpp; ++i)
      destination[at + i] = (byte)(value >> (8 * i));
  }

  private void _RequirePosition(int x, int y, int count) {
    if (y < 0 || y >= this._height || x < 0 || count < 0 || x + count > this._width)
      throw new InvalidDataException(
        $"MSCC stream {this._streamIndex} writes {count} pixel(s) at ({x},{y}) outside its {this._width}x{this._height} frame.");
  }

  private byte[] _FlipRows(ReadOnlySpan<byte> source) {
    var rowBytes = this._width * this._bpp;
    var result = new byte[source.Length];
    for (var row = 0; row < this._height; ++row)
      source.Slice(row * rowBytes, rowBytes).CopyTo(result.AsSpan((this._height - 1 - row) * rowBytes, rowBytes));
    return result;
  }

  private static byte[] _Inflate(ReadOnlySpan<byte> compressed, int maximum, int streamIndex) {
    try {
      using var input = new MemoryStream(compressed.ToArray(), writable: false);
      using var zlib = new ZLibStream(input, CompressionMode.Decompress);
      using var output = new MemoryStream();
      var buffer = new byte[8192];
      while (true) {
        var read = zlib.Read(buffer, 0, buffer.Length);
        if (read == 0)
          break;
        if (output.Length + read > maximum)
          throw new InvalidDataException($"MSCC stream {streamIndex} inflates beyond its {maximum}-byte safety bound.");
        output.Write(buffer, 0, read);
      }
      return output.ToArray();
    } catch (InvalidDataException) {
      throw;
    } catch (Exception ex) when (ex is IOException or NotSupportedException) {
      throw new InvalidDataException($"MSCC stream {streamIndex} carries invalid zlib data.", ex);
    }
  }

  private static byte[] _Rgb555ToRgb24(ReadOnlySpan<byte> source) {
    var result = new byte[source.Length / 2 * 3];
    var at = 0;
    for (var i = 0; i < source.Length; i += 2) {
      var value = source[i] | source[i + 1] << 8;
      var r = (value >> 10) & 31;
      var g = (value >> 5) & 31;
      var b = value & 31;
      result[at++] = (byte)((r << 3) | (r >> 2));
      result[at++] = (byte)((g << 3) | (g >> 2));
      result[at++] = (byte)((b << 3) | (b >> 2));
    }
    return result;
  }
}
