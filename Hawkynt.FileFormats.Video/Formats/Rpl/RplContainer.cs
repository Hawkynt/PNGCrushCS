using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Rpl;

/// <summary>
/// An ARMovie/RPL file (<c>.rpl</c>) — the container early PC Tomb Raider games and Eidos' other
/// Escape-codec titles use — taken apart into its header fields and the packets its chunk catalogue
/// describes, and nothing else.
/// </summary>
/// <remarks>
/// ARMovie names its video and sound codecs by a plain decimal number rather than by a four-character
/// code — <c>130</c> for Escape 130, <c>124</c> for Escape 124 — so <see cref="CodecTag"/> here carries
/// that number directly rather than a synthetic four-letter spelling: a codec riding this container
/// checks <see cref="CodecTag.Value"/> against the number its own name is. See <see cref="RplReader"/>
/// for the header and chunk catalogue walk this hands off to.
/// </remarks>
[FormatMimeType("video/x-armovie")]
[FormatMagicBytes([(byte)'A', (byte)'R', (byte)'M', (byte)'o', (byte)'v', (byte)'i', (byte)'e'])]
public sealed class RplContainer : IVideoContainerReader<RplContainer> {

  /// <summary>The whole file, which every packet is a window onto.</summary>
  public required ReadOnlyMemory<byte> Data { get; init; }

  /// <summary>The twenty-one-field text header this file opens with, parsed.</summary>
  public required RplHeader Header { get; init; }

  /// <summary>Every chunk the catalogue names, in file order.</summary>
  public required IReadOnlyList<RplChunkEntry> Chunks { get; init; }

  /// <summary>Whether the header names a sound codec at all.</summary>
  public bool HasAudio => this.Header.SoundCompressionFormat != 0;

  // -------- Format identity --------

  public static string PrimaryExtension => ".rpl";

  public static string[] FileExtensions => [".rpl"];

  // -------- Demux --------

  public static RplContainer FromSpan(ReadOnlySpan<byte> data) => RplReader.Open(data.ToArray());

  /// <summary>Opens a file over the caller's array, keeping it rather than copying it.</summary>
  public static RplContainer FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);

    return RplReader.Open(data);
  }

  public static RplContainer FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("ARMovie/RPL file not found.", file.FullName);

    return RplReader.Open(File.ReadAllBytes(file.FullName));
  }

  public static IReadOnlyList<MediaStreamInfo> Streams(RplContainer container) {
    ArgumentNullException.ThrowIfNull(container);

    var header = container.Header;
    var streams = new List<MediaStreamInfo>(2);

    if (header.VideoCompressionFormat != 0 && header.Width > 0 && header.Height > 0) {
      var timeBase = header.FrameRate.IsKnown
        ? new Rational(header.FrameRate.Denominator, header.FrameRate.Numerator)
        : Rational.Unknown;

      streams.Add(new() {
        Index = 0,
        Kind = MediaStreamKind.Video,
        Codec = new((uint)header.VideoCompressionFormat),
        Width = header.Width,
        Height = header.Height,
        BitsPerPixel = header.PixelDepth,
        TimeBase = timeBase,
        FrameRate = header.FrameRate,
        DeclaredFrameCount = container.Chunks.Count,
      });
    }

    if (container.HasAudio)
      streams.Add(new() {
        Index = streams.Count,
        Kind = MediaStreamKind.Audio,
        Codec = new((uint)header.SoundCompressionFormat),
        TimeBase = header.SampleRate > 0 ? new Rational(1, header.SampleRate) : Rational.Unknown,
      });

    return streams;
  }

  public static IEnumerable<CodedPacket> ReadPackets(RplContainer container) {
    ArgumentNullException.ThrowIfNull(container);

    return RplReader.ReadPackets(container);
  }

  /// <summary>The three free-text lines the header carries: the movie's own name (usually the tool's
  /// original output path), a copyright line and the authoring tool's name and version.</summary>
  public static VideoMetadata Metadata(RplContainer container) {
    ArgumentNullException.ThrowIfNull(container);

    var streams = Streams(container);
    var declared = new MediaStreamMetadata[streams.Count];
    for (var i = 0; i < streams.Count; ++i)
      declared[i] = new(streams[i].Index, streams[i].Kind, streams[i].Codec);

    return new() {
      Streams = declared,
      Title = container.Header.MovieName,
      EncodedBy = container.Header.AuthorTool,
    };
  }
}
