using System;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>Decodes Matrox Uncompressed HD (<c>M102</c>) in its eight- and ten-bit 4:2:2 layouts.</summary>
/// <remarks>
/// FFmpeg maps <c>M102</c> to the same lossless decoder as <c>M101</c>. Matrox's own VFW documentation
/// distinguishes the tags by SD versus HD resolution while exposing the same bit-depth and scan-mode
/// choices for both.
/// </remarks>
public sealed class M102VideoDecoder : IVideoCodecDecoder<M102VideoDecoder> {

  private static readonly CodecTag _Tag = CodecTag.FromCharacters("M102");

  private readonly MatroxUncompressedVideoDecoderCore _core;

  private M102VideoDecoder(MatroxUncompressedVideoDecoderCore core) => this._core = core;

  public static string CodecName => "Matrox Uncompressed HD";

  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    return stream.Kind == MediaStreamKind.Video && stream.Codec.EqualsIgnoringCase(_Tag);
  }

  public static M102VideoDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    return new(MatroxUncompressedVideoDecoderCore.Create(stream, CodecName));
  }

  public bool TryDecode(CodedPacket packet, out RawImage frame) => this._core.TryDecode(packet, out frame);
}
