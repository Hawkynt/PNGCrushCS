using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;

namespace FileFormat.InterplayMve;

/// <summary>
/// An Interplay MVE file (<c>.mve</c>) taken apart into the streams it declares and the opcodes it
/// holds, and nothing else.
/// </summary>
/// <remarks>
/// Like RoQ, MVE is its own container: there is no wrapper naming a codec the way an AVI names one,
/// because a file only ever holds Interplay's own video and DPCM audio. What still makes it honest to
/// split into demux and decode is that "where the opcodes are" and "what an 8x8 block encoding or a
/// palette byte means" remain two different questions — see <see cref="MveReader"/> for the chunk and
/// opcode walk that answers only the first of them. <see cref="Codecs.MveVideoDecoder"/> is the only
/// thing here that reads a block encoding.
/// <para/>
/// The picture size lives in an <c>INIT_VIDEO_BUFFERS</c> opcode near the start of the file, stated in
/// 8-pixel macroblocks rather than pixels — the format's own published description states it the other
/// way round, and every sample measured against this reader contradicts that: multiplying by eight is
/// what reproduces ffmpeg's own reported picture size, not reading the field directly.
/// </remarks>
[FormatMimeType("video/x-interplay-mve")]
[FormatMagicBytes([0x49, 0x6E, 0x74, 0x65, 0x72, 0x70, 0x6C, 0x61, 0x79, 0x20, 0x4D, 0x56, 0x45, 0x20, 0x46, 0x69, 0x6C, 0x65, 0x1A, 0x00])]
public sealed class MveContainer : IVideoContainerReader<MveContainer> {

  /// <summary>The whole file, which every packet is a window onto.</summary>
  public required ReadOnlyMemory<byte> Data { get; init; }

  /// <summary>Picture width in pixels, eight times what <c>INIT_VIDEO_BUFFERS</c> states.</summary>
  public required int Width { get; init; }

  /// <summary>Picture height in pixels, eight times what <c>INIT_VIDEO_BUFFERS</c> states.</summary>
  public required int Height { get; init; }

  /// <summary>How many <c>VIDEO_DATA</c> opcodes the file holds, counted by walking it once.</summary>
  public required int VideoFrameCount { get; init; }

  /// <summary>Whether the file carries any sound at all.</summary>
  public required bool HasAudio { get; init; }

  /// <summary>Whether the sound this file carries is two channels rather than one.</summary>
  public required bool AudioIsStereo { get; init; }

  /// <summary>Whether the sound this file carries is sixteen bits a sample rather than eight.</summary>
  public required bool AudioIs16Bit { get; init; }

  /// <summary>The sample rate <c>INIT_AUDIO_BUFFERS</c> states, in hertz.</summary>
  public required int AudioSampleRate { get; init; }

  /// <summary>How long one picture is shown, in microseconds — <c>CREATE_TIMER</c>'s rate multiplied
  /// by its subdivision, which is what reproduces ffmpeg's own reported frame rate.</summary>
  public required long FrameDurationMicroseconds { get; init; }

  private static readonly Rational _VIDEO_TIME_BASE = new(1, 1_000_000);
  private static readonly Rational _AUDIO_TIME_BASE_UNIT = new(1, 1);

  // -------- Format identity --------

  public static string PrimaryExtension => ".mve";

  public static string[] FileExtensions => [".mve"];

  // -------- Demux --------

  public static MveContainer FromSpan(ReadOnlySpan<byte> data) => MveReader.Open(data.ToArray());

  /// <summary>Opens a file over the caller's array, keeping it rather than copying it.</summary>
  public static MveContainer FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);

    return MveReader.Open(data);
  }

  public static MveContainer FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Interplay MVE file not found.", file.FullName);

    return MveReader.Open(File.ReadAllBytes(file.FullName));
  }

  public static IReadOnlyList<MediaStreamInfo> Streams(MveContainer container) {
    ArgumentNullException.ThrowIfNull(container);

    var frameRate = container.FrameDurationMicroseconds > 0
      ? new Rational(1_000_000, container.FrameDurationMicroseconds)
      : Rational.Unknown;

    var video = new MediaStreamInfo {
      Index = 0,
      Kind = MediaStreamKind.Video,
      Codec = CodecTag.FromCharacters("IMVE"),
      Width = container.Width,
      Height = container.Height,
      TimeBase = _VIDEO_TIME_BASE,
      FrameRate = frameRate,
      DeclaredFrameCount = container.VideoFrameCount,
    };

    if (!container.HasAudio)
      return [video];

    var audio = new MediaStreamInfo {
      Index = 1,
      Kind = MediaStreamKind.Audio,
      Codec = CodecTag.FromCharacters(container.AudioIsStereo ? "IMVS" : "IMVM"),
      TimeBase = container.AudioSampleRate > 0 ? new Rational(1, container.AudioSampleRate) : _AUDIO_TIME_BASE_UNIT,
    };

    return [video, audio];
  }

  public static IEnumerable<CodedPacket> ReadPackets(MveContainer container) {
    ArgumentNullException.ThrowIfNull(container);

    return MveReader.ReadPackets(container);
  }

  /// <summary>Nothing beyond the streams themselves. An MVE file has no field for a title, an author
  /// or a creation date.</summary>
  public static VideoMetadata Metadata(MveContainer container) {
    ArgumentNullException.ThrowIfNull(container);

    var streams = Streams(container);
    var declared = new MediaStreamMetadata[streams.Count];
    for (var i = 0; i < streams.Count; ++i)
      declared[i] = new(streams[i].Index, streams[i].Kind, streams[i].Codec);

    return new() { Streams = declared };
  }
}
