using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Vqa;

/// <summary>
/// A Westwood VQA file (<c>.vqa</c>) taken apart into its header fields and the chunks it holds — and
/// nothing else.
/// </summary>
/// <remarks>
/// Like RoQ, Interplay MVE and id Cinematic, this is its own container: nothing wraps it, because a
/// file only ever holds this one video codec and Westwood's own audio. Its chunk layout is IFF-like —
/// a four-character ID and a big-endian size — with <c>FORM/WVQA</c> wrapping the stream.
/// <para/>
/// The coded video may be version-1/version-2 palettised VQA or version-2/version-3 HiColor VQA. The
/// container deliberately does not interpret codebooks or pointer commands; the forty-two-byte VQHD
/// payload is carried verbatim as codec private data so <see cref="Codecs.VqaVideoDecoder"/> owns that
/// syntax. VQHD's frame-rate and colour-count fields are container-visible stream metadata, however,
/// and are surfaced here rather than being hard-coded to the common 15 fps / 8-bit case.
/// </remarks>
[FormatMimeType("video/x-vqa")]
public sealed class VqaContainer : IVideoContainerReader<VqaContainer> {

  public required ReadOnlyMemory<byte> Data { get; init; }
  public required int Width { get; init; }
  public required int Height { get; init; }
  public required int BlockWidth { get; init; }
  public required int BlockHeight { get; init; }
  public required int VideoFrameCount { get; init; }
  public required int AudioSampleRate { get; init; }
  public required int AudioChannels { get; init; }
  public required ReadOnlyMemory<byte> HeaderPayload { get; init; }

  private static readonly CodecTag _VIDEO_CODEC = CodecTag.FromCharacters("WSVQ");
  private static readonly CodecTag _AUDIO_CODEC = CodecTag.FromCharacters("WSAD");

  public static string PrimaryExtension => ".vqa";
  public static string[] FileExtensions => [".vqa"];

  public static bool? MatchesSignature(ReadOnlySpan<byte> header)
    => header.Length >= 12 && header[..4].SequenceEqual("FORM"u8) && header.Slice(8, 4).SequenceEqual("WVQA"u8)
      ? true
      : null;

  public static VqaContainer FromSpan(ReadOnlySpan<byte> data) => VqaReader.Open(data.ToArray());

  public static VqaContainer FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return VqaReader.Open(data);
  }

  public static VqaContainer FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Westwood VQA file not found.", file.FullName);
    return VqaReader.Open(File.ReadAllBytes(file.FullName));
  }

  public static IReadOnlyList<MediaStreamInfo> Streams(VqaContainer container) {
    ArgumentNullException.ThrowIfNull(container);

    var header = container.HeaderPayload.Span;
    var version = BinaryPrimitives.ReadUInt16LittleEndian(header);
    var statedRate = header[12];
    var framesPerSecond = statedRate != 0 ? statedRate : version == 1 ? 10 : 15;
    var colours = BinaryPrimitives.ReadUInt16LittleEndian(header[14..]);

    var video = new MediaStreamInfo {
      Index = 0,
      Kind = MediaStreamKind.Video,
      Codec = _VIDEO_CODEC,
      Width = container.Width,
      Height = container.Height,
      BitsPerPixel = colours == 0 ? 15 : 8,
      TimeBase = new(1, framesPerSecond),
      FrameRate = new(framesPerSecond, 1),
      DeclaredFrameCount = container.VideoFrameCount,
      CodecPrivateData = container.HeaderPayload,
    };

    if (container.AudioSampleRate == 0 || container.AudioChannels == 0)
      return [video];

    var audio = new MediaStreamInfo {
      Index = 1,
      Kind = MediaStreamKind.Audio,
      Codec = _AUDIO_CODEC,
      TimeBase = new(1, container.AudioSampleRate),
      SampleRate = container.AudioSampleRate,
      Channels = container.AudioChannels,
      BitsPerSample = header[27] == 0 && version == 1 ? 8 : header[27],
    };

    return [video, audio];
  }

  public static IEnumerable<CodedPacket> ReadPackets(VqaContainer container) {
    ArgumentNullException.ThrowIfNull(container);
    return VqaReader.ReadPackets(container);
  }

  public static VideoMetadata Metadata(VqaContainer container) {
    ArgumentNullException.ThrowIfNull(container);

    var streams = Streams(container);
    var declared = new MediaStreamMetadata[streams.Count];
    for (var i = 0; i < streams.Count; ++i)
      declared[i] = new(streams[i].Index, streams[i].Kind, streams[i].Codec);

    return new() { Streams = declared };
  }
}
