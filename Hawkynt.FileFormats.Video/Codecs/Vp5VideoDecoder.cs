using System;
using FileFormat.Codecs.Vp5;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Decodes On2 VP5 video.
/// </summary>
/// <remarks>
/// VP5 sits between VP3 and VP6: the same 4:2:0 block structure and the same 8x8 integer transform as
/// VP3, with an arithmetic coder and a context-conditioned coefficient model in place of VP3's Huffman
/// tables. It carries its own picture size in every key frame, so unlike VP3 it does not need the
/// container to state one; an inter frame keeps whatever the last key frame established.
/// <para/>
/// Every layout the format defines is decoded, progressive and interlaced alike. Malformed input is
/// refused rather than concealed with a repeat of the previous picture — for a codec where an
/// unchanged frame is legitimate syntax, concealment is indistinguishable from success.
/// </remarks>
public sealed class Vp5VideoDecoder : IVideoCodecDecoder<Vp5VideoDecoder> {

  /// <summary>The four-character code containers name VP5 with.</summary>
  private static readonly CodecTag _Tag = CodecTag.FromCharacters("VP50");

  private readonly Vp5Decoder _decoder = new();

  public static string CodecName => "On2 VP5";

  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    return stream.Kind == MediaStreamKind.Video && stream.Codec.EqualsIgnoringCase(_Tag);
  }

  /// <summary>Builds a decoder for one stream.</summary>
  public static Vp5VideoDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    if (stream.Kind != MediaStreamKind.Video)
      throw new NotSupportedException("On2 VP5 describes pictures, so it can only decode a video stream.");

    return new();
  }

  /// <summary>Decodes one packet, which for VP5 is exactly one coded picture.</summary>
  /// <returns>Always <c>true</c>; VP5 has no bidirectional pictures, so nothing is ever held back.</returns>
  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    var picture = this._decoder.Decode(packet.Data);
    var width = this._decoder.Width;
    var height = this._decoder.Height;

    frame = new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Rgb24,
      PixelData = Vp5ColorConversion.ToRgb24(picture, width, height),
    };

    return true;
  }
}
