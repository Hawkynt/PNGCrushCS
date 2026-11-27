using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Codecs.Dfa;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Decodes Chronomaster DFA video: an eight-bit picture that each frame's chunks rewrite — raw, LZ77,
/// line and word deltas, double-size pixels — under a 6-bit palette.
/// </summary>
/// <remarks>
/// Converted from FFmpeg's <c>libavcodec/dfa.c</c>, LGPL-2.1-or-later; see <c>Dfa/THIRD-PARTY-NOTICE.FFmpeg.txt</c>.
/// Version 0x100 films store the picture as four interleaved quarter-width columns of lines, and are drawn
/// in display order. FFmpeg refuses every DDS1 chunk through a comparison written the wrong way round; this
/// decoder draws them, and agrees with FFmpeg everywhere else. A damaged chunk is refused by name, and the
/// picture keeps whatever it had drawn, as FFmpeg's does, so later frames draw over the same picture.
/// </remarks>
public sealed class DfaVideoDecoder : IVideoCodecDecoder<DfaVideoDecoder> {

  private readonly DfaFrameDecoder _decoder;

  private DfaVideoDecoder(DfaFrameDecoder decoder) => this._decoder = decoder;

  public static string CodecName => "Chronomaster DFA";

  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    return stream.Kind == MediaStreamKind.Video && stream.Codec.EqualsIgnoringCase(FileFormat.Dfa.DfaContainer.VideoCodec);
  }

  public static DfaVideoDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (!Accepts(stream))
      throw new NotSupportedException($"Stream {stream.Index} is not Chronomaster DFA.");
    if (stream.Width is <= 0 or >= 65536 || stream.Height is <= 0 or >= 65536)
      throw new NotSupportedException($"A DFA picture is 1 to 65535 pixels each way; stream {stream.Index} states {stream.Width}x{stream.Height}.");
    var version = stream.CodecPrivateData.Length == 2 ? BinaryPrimitives.ReadUInt16LittleEndian(stream.CodecPrivateData.Span) : 0;
    if (version == DfaChunk.InterleavedVersion && (stream.Width % 4 != 0 || stream.Height % 4 != 0))
      throw new NotSupportedException(
        $"A version 0x100 DFA picture interleaves four columns of lines, so its sides are multiples of four; stream {stream.Index} states {stream.Width}x{stream.Height}.");
    return new(new(stream.Width, stream.Height, version));
  }

  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    if (!this._decoder.Decode(packet.Data.Span, out var damaged))
      throw new InvalidDataException($"This DFA frame's {damaged} chunk is damaged.");

    var palette = new byte[256 * 3];
    for (var i = 0; i < 256; ++i) {
      var colour = this._decoder.Palette[i];
      palette[i * 3] = (byte)(colour >> 16);
      palette[i * 3 + 1] = (byte)(colour >> 8);
      palette[i * 3 + 2] = (byte)colour;
    }
    frame = new() {
      Width = this._decoder.Width,
      Height = this._decoder.Height,
      Format = PixelFormat.Indexed8,
      PixelData = this._decoder.Picture(),
      Palette = palette,
      PaletteCount = 256,
    };
    return true;
  }
}

/// <summary>
/// Encodes Chronomaster DFA video: each frame the smallest of a raw picture, a clear, an LZ77 picture
/// and a delta that skips what has not changed, under a 6-bit palette.
/// </summary>
/// <remarks>
/// This package's own; FFmpeg has no DFA encoder. An <see cref="PixelFormat.Indexed8"/> picture keeps
/// its own indices, its palette taken to the six bits a component DFA stores; any other picture is taken
/// to a fixed palette of six reds, seven greens and six blues. Only the first picture is written whole:
/// DFA has no index to seek by, so a later whole picture is written only where it is the smallest choice
/// anyway. A picture whose palette DFA can hold comes back exactly. FFmpeg's decoder is the oracle that
/// reads what this writes.
/// </remarks>
[VerifiedBy(ConformanceOracle.FFmpeg)]
public sealed class DfaVideoEncoder : IVideoCodecEncoder<DfaVideoEncoder> {

  private static readonly byte[] _Cube = _BuildCube();

  private readonly MediaStreamInfo _stream;
  private readonly DfaEncoder _encoder;
  private int _pictureNumber;

  private DfaVideoEncoder(MediaStreamInfo requested) {
    this._stream = new() {
      Index = requested.Index,
      Kind = MediaStreamKind.Video,
      Codec = FileFormat.Dfa.DfaContainer.VideoCodec,
      Handler = FileFormat.Dfa.DfaContainer.VideoCodec,
      TimeBase = requested.TimeBase,
      FrameRate = requested.FrameRate,
      DeclaredFrameCount = requested.DeclaredFrameCount,
      Width = requested.Width,
      Height = requested.Height,
      BitsPerPixel = 8,
      CodecPrivateData = new byte[2],
      Language = requested.Language,
      Name = requested.Name,
    };
    this._encoder = new(requested.Width, requested.Height);
  }

  public static string CodecName => DfaVideoDecoder.CodecName;
  public static CodecTag Codec => FileFormat.Dfa.DfaContainer.VideoCodec;

  public static bool Accepts(MediaStreamInfo stream) => stream.Codec.EqualsIgnoringCase(FileFormat.Dfa.DfaContainer.VideoCodec);

  public static DfaVideoEncoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.Kind != MediaStreamKind.Video)
      throw new NotSupportedException("DFA can only encode a video stream.");
    if (stream.Width is <= 0 or >= 65536 || stream.Height is <= 0 or >= 65536)
      throw new NotSupportedException($"A DFA picture is 1 to 65535 pixels each way; stream {stream.Index} states {stream.Width}x{stream.Height}.");
    return new(stream);
  }

  public MediaStreamInfo DescribeStream() => this._stream;

  private static byte[] _BuildCube() {
    var palette = new byte[256 * 3];
    var i = 0;
    for (var r = 0; r < 6; ++r)
      for (var g = 0; g < 7; ++g)
        for (var b = 0; b < 6; ++b, ++i) {
          palette[i * 3] = (byte)DfaEncoder.Expand(r * 63 / 5);
          palette[i * 3 + 1] = (byte)DfaEncoder.Expand(g * 63 / 6);
          palette[i * 3 + 2] = (byte)DfaEncoder.Expand(b * 63 / 5);
        }
    return palette;
  }

  public bool TryEncode(RawImage frame, long? presentationTimestamp, out CodedPacket packet) {
    ArgumentNullException.ThrowIfNull(frame);
    if (frame.Width != this._stream.Width || frame.Height != this._stream.Height)
      throw new InvalidDataException($"DFA geometry is fixed at {this._stream.Width}x{this._stream.Height}; received {frame.Width}x{frame.Height}.");

    byte[] indices, palette;
    if (frame.Format == PixelFormat.Indexed8 && frame.Palette is { Length: >= 3 } given) {
      indices = frame.PixelData;
      palette = new byte[256 * 3];
      given.AsSpan(0, Math.Min(given.Length, palette.Length)).CopyTo(palette);
    } else {
      var rgb = frame.ToRgb24();
      indices = new byte[frame.Width * frame.Height];
      for (var p = 0; p < indices.Length; ++p) {
        var r = (rgb[p * 3] * 5 + 127) / 255;
        var g = (rgb[p * 3 + 1] * 6 + 127) / 255;
        var b = (rgb[p * 3 + 2] * 5 + 127) / 255;
        indices[p] = (byte)((r * 7 + g) * 6 + b);
      }
      palette = _Cube;
    }

    var key = this._pictureNumber++ == 0;
    packet = new(this._stream.Index, this._encoder.Encode(indices, palette, key), PresentationTimestamp: presentationTimestamp,
      DecodeTimestamp: presentationTimestamp, Duration: 1, IsKeyFrame: key);
    return true;
  }
}
