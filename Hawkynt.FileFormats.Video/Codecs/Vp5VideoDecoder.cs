using System;
using FileFormat.Codecs.Vp56;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>Decodes On2 VP5 video.</summary>
/// <remarks>
/// VP5 carries its coded and display dimensions in key frames and supports intra pictures plus
/// forward-predicted pictures using previous and golden references. AVI VP5 is coded bottom-up, so
/// the planar reconstruction stays in coded order and is turned right-way-up only for display.
/// </remarks>
public sealed class Vp5VideoDecoder : IVideoCodecDecoder<Vp5VideoDecoder> {
  private static readonly CodecTag _Tag = CodecTag.FromCharacters("VP50");
  private readonly Vp5Decoder _decoder = new();

  public static string CodecName => "On2 VP5";

  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    return stream.Kind == MediaStreamKind.Video && stream.Codec.EqualsIgnoringCase(_Tag);
  }

  public static Vp5VideoDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.Kind != MediaStreamKind.Video)
      throw new NotSupportedException("On2 VP5 can only decode a video stream.");
    return new();
  }

  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    var picture = this._decoder.Decode(packet.Data);
    frame = new() {
      Width = this._decoder.Width,
      Height = this._decoder.Height,
      Format = PixelFormat.Rgb24,
      PixelData = Vp56ColorConversion.ToRgb24(picture, this._decoder.Width, this._decoder.Height, flipVertically: true),
    };
    return true;
  }
}
