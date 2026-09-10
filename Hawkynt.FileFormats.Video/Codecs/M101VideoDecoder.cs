using System;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>Decodes Matrox Uncompressed SD (<c>M101</c>) in its eight- and ten-bit 4:2:2 layouts.</summary>
/// <remarks>
/// The byte layout is shared with Matrox Uncompressed HD (<c>M102</c>); the common implementation and
/// its FFmpeg LGPL provenance are kept in <see cref="MatroxUncompressedVideoDecoderCore"/>.
/// </remarks>
public sealed class M101VideoDecoder : IVideoCodecDecoder<M101VideoDecoder> {

  private static readonly CodecTag _Tag = CodecTag.FromCharacters("M101");

  private readonly MatroxUncompressedVideoDecoderCore _core;

  private M101VideoDecoder(MatroxUncompressedVideoDecoderCore core) => this._core = core;

  public static string CodecName => "Matrox Uncompressed SD";

  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    return stream.Kind == MediaStreamKind.Video && stream.Codec.EqualsIgnoringCase(_Tag);
  }

  public static M101VideoDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    return new(MatroxUncompressedVideoDecoderCore.Create(stream, CodecName));
  }

  public bool TryDecode(CodedPacket packet, out RawImage frame) => this._core.TryDecode(packet, out frame);
}
