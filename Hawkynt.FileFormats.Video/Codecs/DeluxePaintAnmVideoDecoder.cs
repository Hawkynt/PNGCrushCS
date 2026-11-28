using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Codecs.Anm;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Decodes Deluxe Paint Animation: one eight-bit picture that every record rewrites with RunSkipDump
/// operations, under the palette in the film's header.
/// </summary>
/// <remarks>
/// Converted from FFmpeg's <c>libavcodec/anm.c</c>, LGPL-2.1-or-later; see <c>Anm/THIRD-PARTY-NOTICE.FFmpeg.txt</c>.
/// An empty record changes nothing and, as in FFmpeg, yields no picture; the one before it stands. The
/// palette is the 256 four-byte entries after the colour-cycling ranges in the header, which reach the
/// decoder as codec private data. Pictures come out as <see cref="PixelFormat.Indexed8"/>.
/// </remarks>
public sealed class DeluxePaintAnmVideoDecoder : IVideoCodecDecoder<DeluxePaintAnmVideoDecoder> {

  /// <summary>The colour-cycling ranges and the palette, as the header holds them.</summary>
  internal const int PrivateDataLength = FileFormat.DeluxePaintAnm.DeluxePaintAnmContainer.PrivateDataLength;

  private readonly AnmFrameDecoder _decoder;
  private readonly byte[] _palette;
  private readonly int _width;
  private readonly int _height;

  private DeluxePaintAnmVideoDecoder(int width, int height, byte[] palette) {
    this._decoder = new(width, height);
    this._palette = palette;
    this._width = width;
    this._height = height;
  }

  public static string CodecName => "Deluxe Paint Animation";

  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    return stream.Kind == MediaStreamKind.Video && stream.Codec.EqualsIgnoringCase(FileFormat.DeluxePaintAnm.DeluxePaintAnmContainer.VideoCodec);
  }

  public static DeluxePaintAnmVideoDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (!Accepts(stream))
      throw new NotSupportedException($"Stream {stream.Index} is not Deluxe Paint Animation.");
    if (stream.Width is <= 0 or > 65535 || stream.Height is <= 0 or > 65535)
      throw new NotSupportedException($"A Deluxe Paint Animation picture is 1 to 65535 pixels each way; stream {stream.Index} states {stream.Width}x{stream.Height}.");
    if (stream.CodecPrivateData.Length < PrivateDataLength)
      throw new NotSupportedException($"A Deluxe Paint Animation stream carries its header's {PrivateDataLength} bytes of colour cycling and palette; stream {stream.Index} carries {stream.CodecPrivateData.Length}.");

    var source = stream.CodecPrivateData.Span[(16 * 8)..];
    var palette = new byte[256 * 3];
    for (var i = 0; i < 256; ++i) {
      // Each entry is a little-endian 0x00RRGGBB: blue, green, red, and a byte unused.
      palette[i * 3] = source[i * 4 + 2];
      palette[i * 3 + 1] = source[i * 4 + 1];
      palette[i * 3 + 2] = source[i * 4];
    }
    return new(stream.Width, stream.Height, palette);
  }

  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    if (!this._decoder.Decode(packet.Data.Span)) {
      frame = null!;
      return false;
    }
    frame = new() {
      Width = this._width,
      Height = this._height,
      Format = PixelFormat.Indexed8,
      PixelData = (byte[])this._decoder.Picture.Clone(),
      Palette = (byte[])this._palette.Clone(),
      PaletteCount = 256,
    };
    return true;
  }
}

/// <summary>
/// Encodes Deluxe Paint Animation: every picture a RunSkipDump record against the one before, under the
/// one palette the film's header holds.
/// </summary>
/// <remarks>
/// This package's own; FFmpeg has no Deluxe Paint Animation encoder. The film's palette is the first
/// picture's when that is <see cref="PixelFormat.Indexed8"/>, and otherwise a fixed palette of six reds,
/// seven greens and six blues; a later picture with other colours is taken to the nearest in it. The first
/// record writes every pixel; later ones skip what has not changed. A record must fit in one of the
/// format's 64 KiB pages, which bounds a picture at about 65,000 pixels — Deluxe Paint's own were
/// 320x200. The stream's description carries the palette once the first picture has been encoded. A
/// picture in the film's palette comes back exactly. FFmpeg's decoder is the oracle that reads what this
/// writes.
/// </remarks>
[VerifiedBy(ConformanceOracle.FFmpeg)]
public sealed class DeluxePaintAnmVideoEncoder : IVideoCodecEncoder<DeluxePaintAnmVideoEncoder> {

  /// <summary>The most pixels whose every-pixel record still fits a page: dumps cost three bytes a 16,383, the record seven more.</summary>
  internal const int MaximumPixels = 65_000;

  private static readonly byte[] _Cube = _BuildCube();

  private readonly MediaStreamInfo _requested;
  private readonly AnmEncoder _encoder;
  private byte[] _palette = _Cube;
  private bool _started;
  private readonly Dictionary<int, byte> _nearest = [];

  private DeluxePaintAnmVideoEncoder(MediaStreamInfo requested) {
    this._requested = requested;
    this._encoder = new(requested.Width, requested.Height);
  }

  public static string CodecName => DeluxePaintAnmVideoDecoder.CodecName;
  public static CodecTag Codec => FileFormat.DeluxePaintAnm.DeluxePaintAnmContainer.VideoCodec;

  public static bool Accepts(MediaStreamInfo stream) => stream.Codec.EqualsIgnoringCase(FileFormat.DeluxePaintAnm.DeluxePaintAnmContainer.VideoCodec);

  public static DeluxePaintAnmVideoEncoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.Kind != MediaStreamKind.Video)
      throw new NotSupportedException("Deluxe Paint Animation can only encode a video stream.");
    if (stream.Width <= 0 || stream.Height <= 0 || (long)stream.Width * stream.Height > MaximumPixels)
      throw new NotSupportedException(
        $"A Deluxe Paint Animation record must fit one 64 KiB page, about {MaximumPixels} pixels; stream {stream.Index} states {stream.Width}x{stream.Height}.");
    return new(stream);
  }

  public MediaStreamInfo DescribeStream() {
    var header = new byte[DeluxePaintAnmVideoDecoder.PrivateDataLength];
    for (var i = 0; i < 256; ++i) {
      header[16 * 8 + i * 4] = this._palette[i * 3 + 2];
      header[16 * 8 + i * 4 + 1] = this._palette[i * 3 + 1];
      header[16 * 8 + i * 4 + 2] = this._palette[i * 3];
    }
    var requested = this._requested;
    return new() {
      Index = requested.Index,
      Kind = MediaStreamKind.Video,
      Codec = Codec,
      Handler = Codec,
      TimeBase = requested.TimeBase,
      FrameRate = requested.FrameRate,
      DeclaredFrameCount = requested.DeclaredFrameCount,
      Width = requested.Width,
      Height = requested.Height,
      BitsPerPixel = 8,
      CodecPrivateData = header,
      Language = requested.Language,
      Name = requested.Name,
    };
  }

  private static byte[] _BuildCube() {
    var palette = new byte[256 * 3];
    var i = 0;
    for (var r = 0; r < 6; ++r)
      for (var g = 0; g < 7; ++g)
        for (var b = 0; b < 6; ++b, ++i) {
          palette[i * 3] = (byte)(r * 255 / 5);
          palette[i * 3 + 1] = (byte)(g * 255 / 6);
          palette[i * 3 + 2] = (byte)(b * 255 / 5);
        }
    return palette;
  }

  public bool TryEncode(RawImage frame, long? presentationTimestamp, out CodedPacket packet) {
    ArgumentNullException.ThrowIfNull(frame);
    if (frame.Width != this._requested.Width || frame.Height != this._requested.Height)
      throw new InvalidDataException(
        $"Deluxe Paint Animation geometry is fixed at {this._requested.Width}x{this._requested.Height}; received {frame.Width}x{frame.Height}.");

    var indexed = frame.Format == PixelFormat.Indexed8 && frame.Palette is { Length: >= 3 };
    if (!this._started && indexed) {
      this._palette = new byte[256 * 3];
      frame.Palette.AsSpan(0, Math.Min(frame.Palette!.Length, this._palette.Length)).CopyTo(this._palette);
    }

    byte[] indices;
    if (indexed && frame.Palette.AsSpan(0, Math.Min(frame.Palette!.Length, 768)).SequenceEqual(this._palette.AsSpan(0, Math.Min(frame.Palette.Length, 768))))
      indices = frame.PixelData;
    else {
      var rgb = frame.ToRgb24();
      indices = new byte[frame.Width * frame.Height];
      for (var p = 0; p < indices.Length; ++p)
        indices[p] = this._Nearest(rgb[p * 3], rgb[p * 3 + 1], rgb[p * 3 + 2]);
    }

    var key = !this._started;
    this._started = true;
    packet = new(this._requested.Index, this._encoder.Encode(indices), PresentationTimestamp: presentationTimestamp,
      DecodeTimestamp: presentationTimestamp, Duration: 1, IsKeyFrame: key);
    return true;
  }

  private byte _Nearest(int r, int g, int b) {
    var colour = r << 16 | g << 8 | b;
    if (this._nearest.TryGetValue(colour, out var known))
      return known;
    var best = 0;
    var bestDistance = int.MaxValue;
    for (var i = 0; i < 256; ++i) {
      int dr = r - this._palette[i * 3], dg = g - this._palette[i * 3 + 1], db = b - this._palette[i * 3 + 2];
      var distance = dr * dr + dg * dg + db * db;
      if (distance < bestDistance) {
        bestDistance = distance;
        best = i;
      }
    }
    return this._nearest[colour] = (byte)best;
  }
}
