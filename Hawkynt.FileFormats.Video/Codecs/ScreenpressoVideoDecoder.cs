using System;
using System.IO;
using System.IO.Compression;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>Decodes Screenpresso SPV1 screen-capture video.</summary>
/// <remarks>
/// Adapted from FFmpeg's <c>libavcodec/screenpresso.c</c>, copyright (C) 2015 Vittorio Giovara,
/// distributed there under LGPL-2.1-or-later. This adaptation is distributed with PNGCrushCS under
/// LGPL-3.0-or-later.
/// <para/>
/// Each packet has a two-byte header followed by zlib data. The low bit of byte zero selects a full
/// frame versus an additive delta; bits 2..3 of byte one encode the native pixel size. Rows in the
/// compressed representation are bottom-up and padded to a four-byte boundary. Deltas are added byte
/// for byte to the currently reconstructed native frame, not to a separate reference picture.
/// </remarks>
public sealed class ScreenpressoVideoDecoder : IVideoCodecDecoder<ScreenpressoVideoDecoder> {

  private static readonly CodecTag _Tag = CodecTag.FromCharacters("SPV1");

  private readonly int _width;
  private readonly int _height;
  private readonly int _streamIndex;
  private byte[]? _current;
  private int _componentSize;

  private ScreenpressoVideoDecoder(int width, int height, int streamIndex) {
    this._width = width;
    this._height = height;
    this._streamIndex = streamIndex;
  }

  /// <summary>Gets the codec name.</summary>
  public static string CodecName => "Screenpresso";

  /// <summary>Determines whether the specified media stream is supported.</summary>
  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    return stream.Kind == MediaStreamKind.Video && stream.Codec.EqualsIgnoringCase(_Tag);
  }

  /// <summary>Creates a decoder for the specified media stream.</summary>
  public static ScreenpressoVideoDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.Width <= 0 || stream.Height <= 0)
      throw new InvalidDataException(
        $"Screenpresso stream {stream.Index} states a picture of {stream.Width}x{stream.Height}, which has no pixels.");
    if ((long)stream.Width * stream.Height * 4 > int.MaxValue)
      throw new InvalidDataException($"Screenpresso stream {stream.Index}'s frame is too large to hold in memory.");
    return new(stream.Width, stream.Height, stream.Index);
  }

  /// <summary>Attempts to decode the specified coded packet into a raw image frame.</summary>
  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    var data = packet.Data.Span;
    if (data.Length < 3)
      throw new InvalidDataException(
        $"Screenpresso stream {this._streamIndex} carries a packet shorter than its two-byte header and zlib payload.");

    var keyframe = (data[0] & 1) != 0;
    var componentSize = ((data[1] >> 2) & 3) + 1;
    if (componentSize is not (2 or 3 or 4))
      throw new InvalidDataException(
        $"Screenpresso stream {this._streamIndex} states an unsupported {componentSize}-byte pixel layout.");

    if (this._current is null || this._componentSize != componentSize) {
      if (!keyframe && this._current is not null)
        throw new InvalidDataException(
          $"Screenpresso stream {this._streamIndex} changes native pixel size on a delta frame.");
      this._componentSize = componentSize;
      this._current = new byte[checked(this._width * this._height * componentSize)];
    }

    var sourceStride = checked((this._width * componentSize + 3) & ~3);
    var inflated = _Inflate(data[2..], checked(sourceStride * this._height), this._streamIndex);
    var rowBytes = this._width * componentSize;

    for (var outputRow = 0; outputRow < this._height; ++outputRow) {
      var sourceRow = this._height - 1 - outputRow;
      var source = inflated.AsSpan(sourceRow * sourceStride, rowBytes);
      var destination = this._current.AsSpan(outputRow * rowBytes, rowBytes);
      if (keyframe)
        source.CopyTo(destination);
      else
        for (var i = 0; i < rowBytes; ++i)
          destination[i] = unchecked((byte)(destination[i] + source[i]));
    }

    frame = this._ToRawImage();
    return true;
  }

  private RawImage _ToRawImage() {
    var native = this._current!;
    return this._componentSize switch {
      3 => new RawImage {
        Width = this._width,
        Height = this._height,
        Format = PixelFormat.Bgr24,
        PixelData = (byte[])native.Clone(),
      },
      4 => new RawImage {
        Width = this._width,
        Height = this._height,
        Format = PixelFormat.Bgra32,
        PixelData = _Bgr0ToBgra(native),
      },
      2 => new RawImage {
        Width = this._width,
        Height = this._height,
        Format = PixelFormat.Rgb24,
        PixelData = _Rgb555ToRgb24(native),
      },
      _ => throw new InvalidOperationException(),
    };
  }

  private static byte[] _Inflate(ReadOnlySpan<byte> compressed, int required, int streamIndex) {
    try {
      using var input = new MemoryStream(compressed.ToArray(), writable: false);
      using var zlib = new ZLibStream(input, CompressionMode.Decompress);
      var result = new byte[required];
      var offset = 0;
      while (offset < result.Length) {
        var read = zlib.Read(result, offset, result.Length - offset);
        if (read == 0)
          break;
        offset += read;
      }
      if (offset < required)
        throw new InvalidDataException(
          $"Screenpresso stream {streamIndex} inflates to {offset} byte(s), where the picture requires {required}.");
      return result;
    } catch (InvalidDataException) {
      throw;
    } catch (Exception ex) when (ex is IOException or NotSupportedException) {
      throw new InvalidDataException($"Screenpresso stream {streamIndex} carries invalid zlib data.", ex);
    }
  }

  private static byte[] _Bgr0ToBgra(ReadOnlySpan<byte> source) {
    var result = source.ToArray();
    for (var i = 3; i < result.Length; i += 4)
      result[i] = 255;
    return result;
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
