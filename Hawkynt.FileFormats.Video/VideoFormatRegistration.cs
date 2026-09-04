using FileFormat.Core;

namespace Hawkynt.FileFormats.Video;

/// <summary>
/// Hand-written partial of the source-generated <c>VideoFormatRegistration.g.cs</c>. Hosts the typed
/// registration methods the generated <c>RegisterAll()</c> calls — these use static-interface
/// dispatch to reach each container's and each codec's own members without any runtime reflection.
/// </summary>
/// <remarks>
/// Note that a container is registered without being told which codecs exist and a codec without
/// being told which containers exist. Adding either is one type in one file and nothing else
/// recompiled to mention it, which is the practical form the demux/decode split takes.
/// </remarks>
internal static partial class VideoFormatRegistration {

  /// <summary>Implemented by the source generator (<c>VideoFormatRegistration.g.cs</c>).</summary>
  static partial void RegisterAll();

  internal static void Initialize() {
    RegisterAll();
    VideoFormatRegistry.BuildDetectionOrder();
  }

  private static void _RegisterContainer<T>(VideoFormat format, MagicSignature[] magic, int priority, string[] mimeTypes)
    where T : IVideoContainerReader<T> {
    var entry = new VideoFormatEntry(
      Format: format,
      Name: format.ToString(),
      PrimaryExtension: T.PrimaryExtension,
      AllExtensions: T.FileExtensions,
      MimeTypes: mimeTypes,
      MagicSignatures: magic,
      MatchesSignature: header => T.MatchesSignature(header.Span),
      DetectionPriority: priority,
      ReadStreams: data => T.Streams(VideoIO.Read<T>(data)),
      // The container is parsed when the walk is asked for and the packets come out of it one at a
      // time; nothing here materialises a film.
      ReadPackets: data => T.ReadPackets(VideoIO.Read<T>(data)),
      ReadStreamPackets: (data, index) => T.ReadPackets(VideoIO.Read<T>(data), index),
      ReadMetadata: data => T.Metadata(VideoIO.Read<T>(data)));

    VideoFormatRegistry.Register(entry);
  }

  private static void _RegisterDecoder<T>() where T : IVideoCodecDecoder<T>
    => VideoFormatRegistry.RegisterCodec(new(
      CodecName: T.CodecName,
      Accepts: static stream => T.Accepts(stream),
      CreateDecoder: static stream => T.Create(stream)));

  private static void _RegisterEncoder<T>() where T : IVideoCodecEncoder<T>
    => VideoFormatRegistry.RegisterEncoder(new(
      CodecName: T.CodecName,
      Codec: T.Codec,
      CreateEncoder: static stream => T.Create(stream)));
}
