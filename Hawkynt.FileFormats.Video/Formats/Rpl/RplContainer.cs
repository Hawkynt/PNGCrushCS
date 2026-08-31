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
[FormatMimeType("video/x-armovie")]
[FormatMagicBytes([(byte)'A', (byte)'R', (byte)'M', (byte)'o', (byte)'v', (byte)'i', (byte)'e'])]
public sealed class RplContainer : IVideoContainerReader<RplContainer> {
  /// <summary>Initializes a new instance of this type.</summary>
  public RplContainer() { }

  /// <summary>Gets the data.</summary>
  public required ReadOnlyMemory<byte> Data { get; init; }
  /// <summary>Gets the header.</summary>
  public required RplHeader Header { get; init; }
  /// <summary>Gets the chunks.</summary>
  public required IReadOnlyList<RplChunkEntry> Chunks { get; init; }
  /// <summary>Gets a value indicating whether this instance has audio.</summary>
  public bool HasAudio => this.Header.SoundCompressionFormat != 0;

  /// <summary>Gets the primary file extension for this format.</summary>
  public static string PrimaryExtension => ".rpl";
  /// <summary>Gets the file extensions supported by this format.</summary>
  public static string[] FileExtensions => [".rpl"];

  /// <summary>Reads an instance from the specified byte span.</summary>
  public static RplContainer FromSpan(ReadOnlySpan<byte> data) => RplReader.Open(data.ToArray());

  /// <summary>Reads an instance from the specified byte array.</summary>
  public static RplContainer FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return RplReader.Open(data);
  }

  /// <summary>Reads an instance from the specified file.</summary>
  public static RplContainer FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("ARMovie/RPL file not found.", file.FullName);
    return RplReader.Open(File.ReadAllBytes(file.FullName));
  }

  /// <summary>Gets the media streams declared by the specified container.</summary>
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
        SampleRate = header.SampleRate,
        Channels = header.ChannelCount,
        BitsPerSample = header.SamplePrecision,
      });

    return streams;
  }

  /// <summary>Enumerates coded packets from the specified container.</summary>
  public static IEnumerable<CodedPacket> ReadPackets(RplContainer container) {
    ArgumentNullException.ThrowIfNull(container);
    return RplReader.ReadPackets(container);
  }

  /// <summary>Gets the metadata exposed by the specified container.</summary>
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
