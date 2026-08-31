using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;

namespace FileFormat.RoqVideo;

/// <summary>
/// A RoQ file (<c>.roq</c>) — the FMV format Graeme Devine wrote for The 11th Hour and id Software
/// carried on into Quake III and its Return to Castle Wolfenstein-era engine — taken apart into the
/// streams it declares and the chunks it holds, and nothing else.
/// </summary>
/// <remarks>
/// RoQ is its own container the way FLIC is: there is no wrapper naming a video codec the way an AVI
/// names Cinepak, because a RoQ file only ever holds one thing. What still makes it honest to split
/// into demux and decode is that "where the chunks are" and "what a quadtree opcode or a codebook byte
/// means" remain two different questions — see <see cref="RoqReader"/> for the chunk walk that answers
/// only the first of them. <see cref="FileFormat.Codecs.RoqVideoDecoder"/> is the only thing here that
/// reads a codebook entry or a motion byte.
/// <para/>
/// A RoQ file states no frame count and no duration anywhere; both are counted by walking the file
/// once on open. Nor does it state a frame rate — every source this was built against, and every
/// sample measured, runs at a fixed 30 pictures a second, which this reader takes as the format's own
/// constant rather than something a file could override.
/// </remarks>
[FormatMimeType("video/x-roq")]
[FormatMagicBytes([0x84, 0x10, 0xFF, 0xFF, 0xFF, 0xFF, 0x1E, 0x00])]
public sealed class RoqContainer : IVideoContainerReader<RoqContainer> {
  /// <summary>Initializes a new instance of this type.</summary>
  public RoqContainer() { }

  /// <summary>The whole file, which every packet is a window onto.</summary>
  public required ReadOnlyMemory<byte> Data { get; init; }

  /// <summary>Picture width in pixels, as the file's <c>RoQ_INFO</c> chunk states it.</summary>
  public required int Width { get; init; }

  /// <summary>Picture height in pixels, as the file's <c>RoQ_INFO</c> chunk states it.</summary>
  public required int Height { get; init; }

  /// <summary>How many <c>QUAD_VQ</c> chunks the file holds, counted by walking it once.</summary>
  public required int VideoFrameCount { get; init; }

  /// <summary>Whether the file carries any sound chunk at all.</summary>
  public required bool HasAudio { get; init; }

  /// <summary>Whether the sound this file carries is two channels rather than one.</summary>
  public required bool AudioIsStereo { get; init; }

  /// <summary>Every RoQ file measured against this reader runs at this fixed rate; nothing in the
  /// format states one of its own.</summary>
  private static readonly Rational _VIDEO_TIME_BASE = new(1, 30);

  /// <summary>id RoQ DPCM sound is fixed at this rate, stated nowhere in the file either.</summary>
  private static readonly Rational _AUDIO_TIME_BASE = new(1, 22050);

  // -------- Format identity --------

  /// <summary>Gets the primary file extension for this format.</summary>
  public static string PrimaryExtension => ".roq";

  /// <summary>Gets the file extensions supported by this format.</summary>
  public static string[] FileExtensions => [".roq"];

  // -------- Demux --------

  /// <summary>Reads an instance from the specified byte span.</summary>
  public static RoqContainer FromSpan(ReadOnlySpan<byte> data) => RoqReader.Open(data.ToArray());

  /// <summary>Opens a file over the caller's array, keeping it rather than copying it.</summary>
  public static RoqContainer FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);

    return RoqReader.Open(data);
  }

  /// <summary>Reads an instance from the specified file.</summary>
  public static RoqContainer FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("RoQ file not found.", file.FullName);

    return RoqReader.Open(File.ReadAllBytes(file.FullName));
  }

  /// <summary>Gets the media streams declared by the specified container.</summary>
  public static IReadOnlyList<MediaStreamInfo> Streams(RoqContainer container) {
    ArgumentNullException.ThrowIfNull(container);

    var video = new MediaStreamInfo {
      Index = 0,
      Kind = MediaStreamKind.Video,
      Codec = CodecTag.FromCharacters("RoQV"),
      Width = container.Width,
      Height = container.Height,
      TimeBase = _VIDEO_TIME_BASE,
      FrameRate = new Rational(30, 1),
      DeclaredFrameCount = container.VideoFrameCount,
    };

    if (!container.HasAudio)
      return [video];

    var audio = new MediaStreamInfo {
      Index = 1,
      Kind = MediaStreamKind.Audio,
      Codec = CodecTag.FromCharacters(container.AudioIsStereo ? "RoQS" : "RoQM"),
      TimeBase = _AUDIO_TIME_BASE,
    };

    return [video, audio];
  }

  /// <summary>Enumerates coded packets from the specified container.</summary>
  public static IEnumerable<CodedPacket> ReadPackets(RoqContainer container) {
    ArgumentNullException.ThrowIfNull(container);

    return RoqReader.ReadPackets(container);
  }

  /// <summary>Nothing beyond the streams themselves. A RoQ file has no field for a title, an author
  /// or a creation date — nothing here carries anything but pictures, chunks and sound.</summary>
  public static VideoMetadata Metadata(RoqContainer container) {
    ArgumentNullException.ThrowIfNull(container);

    var streams = Streams(container);
    var declared = new MediaStreamMetadata[streams.Count];
    for (var i = 0; i < streams.Count; ++i)
      declared[i] = new(streams[i].Index, streams[i].Kind, streams[i].Codec);

    return new() { Streams = declared };
  }
}
