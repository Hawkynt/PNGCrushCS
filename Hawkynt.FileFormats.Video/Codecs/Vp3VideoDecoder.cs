using System;
using FileFormat.Codecs.Vp3;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Decodes On2 VP3 video, the codec Theora was built from.
/// </summary>
/// <remarks>
/// VP3.0 and VP3.1 use the same block, motion, coefficient, inverse-transform and loop-filter syntax.
/// Their only decoder-visible difference is the intra-frame header: VP3.1 appends eight bits carrying
/// its bitstream version, coding type and reserved bits after the width/height codes, while VP3.0
/// stops there. The core decoder is the already measured VP3.1 path; <c>VP30</c> key frames are
/// normalized to that header before entering it. Inter frames are byte-for-byte the same syntax.
/// <para/>
/// On2 donated VP3 to Xiph.Org, who built Theora on it. The fixed VP3 tables and reconstruction
/// procedures are the ones published in the Theora specification Appendix B. The header distinction
/// above is cross-checked against the donated VP3 implementation and FFmpeg's VP3 decoder.
/// <para/>
/// Malformed input is refused rather than replaced with a plausible repeated frame. That matters for
/// VP3 because an unchanged frame is legitimate syntax, so silently repeating the previous picture on
/// decode failure would be indistinguishable from success.
/// </remarks>
public sealed class Vp3VideoDecoder : IVideoCodecDecoder<Vp3VideoDecoder> {

  /// <summary>The four-character codes containers name VP3 with.</summary>
  private static readonly CodecTag[] _Tags = [
    CodecTag.FromCharacters("VP30"),
    CodecTag.FromCharacters("VP31"),
    CodecTag.FromCharacters("VP32"),
  ];

  private static readonly CodecTag _Version30 = CodecTag.FromCharacters("VP30");

  private readonly Vp3Decoder _decoder;
  private readonly bool _isVersion30;

  public static string CodecName => "On2 VP3";

  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    if (stream.Kind != MediaStreamKind.Video)
      return false;

    foreach (var tag in _Tags)
      if (stream.Codec.EqualsIgnoringCase(tag))
        return true;

    return false;
  }

  /// <summary>Builds a decoder for one stream.</summary>
  public static Vp3VideoDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    if (stream.Width <= 0 || stream.Height <= 0)
      throw new NotSupportedException(
        $"This VP3 stream is described as {stream.Width}×{stream.Height}. VP3 carries no picture size of its "
        + "own, so the container has to state one, and this one states a picture with no area.");

    return new(stream.Width, stream.Height, stream.Codec.EqualsIgnoringCase(_Version30));
  }

  private Vp3VideoDecoder(int width, int height, bool isVersion30) {
    this._decoder = new(width, height);
    this._isVersion30 = isVersion30;
  }

  /// <summary>
  /// Decodes one packet, which for VP3 is exactly one coded frame.
  /// </summary>
  /// <returns>Always <c>true</c>; every successfully decoded VP3 packet is a displayable frame.</returns>
  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    var data = this._NormalizeVp30Intra(packet.Data);
    var picture = this._decoder.Decode(data);

    frame = new() {
      Width = this._decoder.Width,
      Height = this._decoder.Height,
      Format = PixelFormat.Rgb24,
      PixelData = Vp3ColorConversion.ToRgb24(picture, this._decoder.Width, this._decoder.Height),
    };

    return true;
  }

  /// <summary>
  /// Expands VP3.0's sixteen-bit intra header to the twenty-four-bit VP3.1 form the core reads.
  /// </summary>
  private ReadOnlyMemory<byte> _NormalizeVp30Intra(ReadOnlyMemory<byte> packet) {
    if (!this._isVersion30 || packet.Length == 0 || (packet.Span[0] & 0x80) != 0)
      return packet;

    // Two complete bytes precede the VP3.0 coefficient stream: the common frame/q byte and the
    // width/height-code byte. A shorter packet is malformed already; leaving it untouched lets the
    // bit reader produce the normal truncated-frame diagnostic instead of inventing header bytes.
    if (packet.Length < 2)
      return packet;

    var result = new byte[packet.Length + 1];
    packet.Span[..2].CopyTo(result);
    result[2] = 0x08; // version 1 in five bits, normal coding type, two reserved zero bits
    packet.Span[2..].CopyTo(result.AsSpan(3));
    return result;
  }
}
