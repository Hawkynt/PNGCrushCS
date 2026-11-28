using System;
using System.IO;
using System.Numerics;
using FileFormat.Codecs.Tmv;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Decodes 8088flex TMV video: a text-mode screen, a character and an attribute byte for each
/// eight-by-eight cell, drawn with the IBM CGA font in the sixteen CGA colours.
/// </summary>
/// <remarks>
/// Converted from FFmpeg's <c>libavcodec/tmv.c</c> and the font and palette in <c>libavutil/xga_font_data.c</c>
/// and <c>libavcodec/cga_data.c</c>, LGPL-2.1-or-later; see <c>Tmv/THIRD-PARTY-NOTICE.FFmpeg.txt</c>. The
/// attribute's high nibble is the background, all sixteen colours of it, as FFmpeg draws it: blinking is
/// not applied. Pictures come out as <see cref="PixelFormat.Indexed8"/> with the CGA palette.
/// </remarks>
public sealed class TmvVideoDecoder : IVideoCodecDecoder<TmvVideoDecoder> {

  private readonly int _width;
  private readonly int _height;

  private TmvVideoDecoder(int width, int height) {
    this._width = width;
    this._height = height;
  }

  public static string CodecName => "8088flex TMV";

  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    return stream.Kind == MediaStreamKind.Video && stream.Codec.EqualsIgnoringCase(FileFormat.Tmv.TmvContainer.VideoCodec);
  }

  public static TmvVideoDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (!Accepts(stream))
      throw new NotSupportedException($"Stream {stream.Index} is not TMV.");
    if (stream.Width < 8 || stream.Height < 8)
      throw new NotSupportedException($"A TMV screen is at least one eight-pixel character; stream {stream.Index} states {stream.Width}x{stream.Height}.");
    return new(stream.Width, stream.Height);
  }

  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    var columns = this._width >> 3;
    var rows = this._height >> 3;
    var data = packet.Data.Span;
    if (data.Length < 2 * columns * rows)
      throw new InvalidDataException($"A TMV screen of {columns}x{rows} characters is {2 * columns * rows} bytes; this packet is {data.Length}.");

    var pixels = new byte[this._width * this._height];
    var font = TmvTables.CgaFont;
    var at = 0;
    for (var row = 0; row < rows; ++row)
      for (var column = 0; column < columns; ++column) {
        var character = data[at++];
        var attribute = data[at++];
        int background = attribute >> 4, foreground = attribute & 0xF;
        for (var y = 0; y < 8; ++y) {
          var bits = font[character * 8 + y];
          var line = (row * 8 + y) * this._width + column * 8;
          for (var x = 0; x < 8; ++x)
            pixels[line + x] = (byte)((bits & (0x80 >> x)) != 0 ? foreground : background);
        }
      }

    frame = new() {
      Width = this._width,
      Height = this._height,
      Format = PixelFormat.Indexed8,
      PixelData = pixels,
      Palette = TmvTables.CgaPalette.ToArray(),
      PaletteCount = 16,
    };
    return true;
  }
}

/// <summary>
/// Encodes 8088flex TMV video: every eight-by-eight cell becomes the character and the two CGA colours
/// that come closest to it.
/// </summary>
/// <remarks>
/// This package's own; FFmpeg has no TMV encoder. A cell's pixels are taken to their nearest CGA colours,
/// the two most frequent become background and foreground, and the character whose glyph best matches
/// the resulting mask — tried both ways round — is chosen. A picture drawn from the CGA font in the CGA
/// colours comes back exactly; any other is approximated, which is what TMV is. FFmpeg's decoder is the
/// oracle that reads what this writes.
/// </remarks>
[VerifiedBy(ConformanceOracle.FFmpeg)]
public sealed class TmvVideoEncoder : IVideoCodecEncoder<TmvVideoEncoder> {

  private readonly MediaStreamInfo _stream;

  private TmvVideoEncoder(MediaStreamInfo requested) => this._stream = new() {
    Index = requested.Index,
    Kind = MediaStreamKind.Video,
    Codec = FileFormat.Tmv.TmvContainer.VideoCodec,
    Handler = FileFormat.Tmv.TmvContainer.VideoCodec,
    TimeBase = requested.TimeBase,
    FrameRate = requested.FrameRate,
    DeclaredFrameCount = requested.DeclaredFrameCount,
    Width = requested.Width,
    Height = requested.Height,
    BitsPerPixel = 8,
    Language = requested.Language,
    Name = requested.Name,
  };

  public static string CodecName => TmvVideoDecoder.CodecName;
  public static CodecTag Codec => FileFormat.Tmv.TmvContainer.VideoCodec;

  public static bool Accepts(MediaStreamInfo stream) => stream.Codec.EqualsIgnoringCase(FileFormat.Tmv.TmvContainer.VideoCodec);

  public static TmvVideoEncoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.Kind != MediaStreamKind.Video)
      throw new NotSupportedException("TMV can only encode a video stream.");
    if (stream.Width % 8 != 0 || stream.Height % 8 != 0 || stream.Width / 8 is < 1 or > 255 || stream.Height / 8 is < 1 or > 255)
      throw new NotSupportedException($"A TMV screen is 1 to 255 eight-pixel characters each way; stream {stream.Index} states {stream.Width}x{stream.Height}.");
    return new(stream);
  }

  public MediaStreamInfo DescribeStream() => this._stream;

  public bool TryEncode(RawImage frame, long? presentationTimestamp, out CodedPacket packet) {
    ArgumentNullException.ThrowIfNull(frame);
    if (frame.Width != this._stream.Width || frame.Height != this._stream.Height)
      throw new InvalidDataException($"TMV geometry is fixed at {this._stream.Width}x{this._stream.Height}; received {frame.Width}x{frame.Height}.");

    int width = frame.Width, height = frame.Height;
    var rgb = frame.ToRgb24();
    var index = new byte[width * height];
    for (var i = 0; i < index.Length; ++i)
      index[i] = _Nearest(rgb[i * 3], rgb[i * 3 + 1], rgb[i * 3 + 2]);

    var columns = width / 8;
    var rows = height / 8;
    var data = new byte[columns * rows * 2];
    Span<int> counts = stackalloc int[16];
    Span<byte> cell = stackalloc byte[64];
    Span<byte> mask = stackalloc byte[8];
    for (var row = 0; row < rows; ++row)
      for (var column = 0; column < columns; ++column) {
        counts.Clear();
        for (var y = 0; y < 8; ++y)
          for (var x = 0; x < 8; ++x) {
            var c = index[(row * 8 + y) * width + column * 8 + x];
            cell[y * 8 + x] = c;
            ++counts[c];
          }

        int first = 0, second = -1;
        for (var c = 1; c < 16; ++c)
          if (counts[c] > counts[first])
            first = c;
        for (var c = 0; c < 16; ++c)
          if (c != first && counts[c] > 0 && (second < 0 || counts[c] > counts[second]))
            second = c;
        if (second < 0)
          second = first;

        // The mask of pixels nearer the second colour than the first.
        for (var y = 0; y < 8; ++y) {
          var bits = 0;
          for (var x = 0; x < 8; ++x)
            if (cell[y * 8 + x] != first && _Distance(cell[y * 8 + x], second) < _Distance(cell[y * 8 + x], first))
              bits |= 0x80 >> x;
          mask[y] = (byte)bits;
        }

        var (character, inverted) = _BestCharacter(mask);
        var foreground = inverted ? first : second;
        var background = inverted ? second : first;
        var at = (row * columns + column) * 2;
        data[at] = character;
        data[at + 1] = (byte)((background << 4) | foreground);
      }

    packet = new(this._stream.Index, data, PresentationTimestamp: presentationTimestamp,
      DecodeTimestamp: presentationTimestamp, Duration: 1, IsKeyFrame: true);
    return true;
  }

  private static (byte Character, bool Inverted) _BestCharacter(ReadOnlySpan<byte> mask) {
    var font = TmvTables.CgaFont;
    var best = 0;
    var bestCost = int.MaxValue;
    var bestInverted = false;
    for (var c = 0; c < 256; ++c) {
      int direct = 0, inverse = 0;
      for (var y = 0; y < 8; ++y) {
        direct += BitOperations.PopCount((uint)(font[c * 8 + y] ^ mask[y]));
        inverse += BitOperations.PopCount((uint)(font[c * 8 + y] ^ mask[y] ^ 0xFF));
      }
      if (direct < bestCost) {
        (best, bestCost, bestInverted) = (c, direct, false);
      }
      if (inverse < bestCost) {
        (best, bestCost, bestInverted) = (c, inverse, true);
      }
    }
    return ((byte)best, bestInverted);
  }

  private static byte _Nearest(int r, int g, int b) {
    var palette = TmvTables.CgaPalette;
    var best = 0;
    var bestDistance = int.MaxValue;
    for (var i = 0; i < 16; ++i) {
      int dr = r - palette[i * 3], dg = g - palette[i * 3 + 1], db = b - palette[i * 3 + 2];
      var distance = dr * dr + dg * dg + db * db;
      if (distance < bestDistance) {
        bestDistance = distance;
        best = i;
      }
    }
    return (byte)best;
  }

  private static int _Distance(int a, int b) {
    var palette = TmvTables.CgaPalette;
    int dr = palette[a * 3] - palette[b * 3], dg = palette[a * 3 + 1] - palette[b * 3 + 1], db = palette[a * 3 + 2] - palette[b * 3 + 2];
    return dr * dr + dg * dg + db * db;
  }
}
