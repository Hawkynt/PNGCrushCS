using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Matroska;

/// <summary>A Matroska/WebM document split into declared tracks and coded packets.</summary>
[FormatMimeType("video/x-matroska", "audio/x-matroska", "video/webm", "audio/webm")]
public sealed class MatroskaContainer : IVideoContainerReader<MatroskaContainer> {

  public required string? DocType { get; init; }
  public required ReadOnlyMemory<byte> File { get; init; }
  internal int SegmentStart { get; init; }
  internal int SegmentEnd { get; init; }
  public required long TimestampScale { get; init; }
  internal IReadOnlyList<MatroskaTrack> TrackEntries { get; init; } = [];
  public required VideoMetadata FileMetadata { get; init; }

  private Dictionary<ulong, int>? _byTrackNumber;

  private Dictionary<ulong, int> _TrackIndex {
    get {
      if (this._byTrackNumber != null)
        return this._byTrackNumber;

      var map = new Dictionary<ulong, int>(this.TrackEntries.Count);
      for (var i = 0; i < this.TrackEntries.Count; ++i)
        map.TryAdd(this.TrackEntries[i].Number, i);
      return this._byTrackNumber = map;
    }
  }

  public static string PrimaryExtension => ".mkv";
  public static string[] FileExtensions => [".mkv", ".mka", ".mks", ".mk3d", ".webm"];

  public static bool? MatchesSignature(ReadOnlySpan<byte> header)
    => header.Length >= 4 && header[0] == 0x1A && header[1] == 0x45 && header[2] == 0xDF && header[3] == 0xA3
      ? true
      : null;

  public static MatroskaContainer FromSpan(ReadOnlySpan<byte> data) => MatroskaReader.FromSpan(data);
  public static MatroskaContainer FromBytes(byte[] data) => MatroskaReader.FromBytes(data);

  public static MatroskaContainer FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Matroska file not found.", file.FullName);
    return MatroskaReader.FromBytes(System.IO.File.ReadAllBytes(file.FullName));
  }

  /// <summary>
  /// Returns stream declarations including Matroska's container-level Audio geometry. The parser keeps
  /// codec-private bytes opaque, but SamplingFrequency/Channels/BitDepth are not codec data: they are
  /// fields of TrackEntry/Audio and are needed by a muxer to recreate that element.
  /// </summary>
  public static IReadOnlyList<MediaStreamInfo> Streams(MatroskaContainer container) {
    ArgumentNullException.ThrowIfNull(container);

    var geometry = _ReadAudioGeometry(container);
    var result = new MediaStreamInfo[container.TrackEntries.Count];
    for (var i = 0; i < result.Length; ++i) {
      var info = container.TrackEntries[i].Info;
      var (sampleRate, channels, bitsPerSample) = geometry[i];
      result[i] = info.Kind == MediaStreamKind.Audio && (sampleRate != 0 || channels != 0 || bitsPerSample != 0)
        ? _WithAudioGeometry(info, sampleRate, channels, bitsPerSample)
        : info;
    }

    return result;
  }

  private static (int SampleRate, int Channels, int BitsPerSample)[] _ReadAudioGeometry(MatroskaContainer container) {
    var result = new (int SampleRate, int Channels, int BitsPerSample)[container.TrackEntries.Count];
    EbmlElement? tracks = null;
    foreach (var element in EbmlScanner.Walk(
               container.File,
               container.SegmentStart,
               container.SegmentEnd,
               MatroskaElementId.IsSegmentLevel))
      if (element.Id == MatroskaElementId.TRACKS) {
        tracks = element;
        break;
      }

    if (tracks == null)
      return result;

    var index = 0;
    foreach (var entry in EbmlScanner.Children(container.File, tracks.Value)) {
      if (entry.Id != MatroskaElementId.TRACK_ENTRY)
        continue;
      if (index >= result.Length)
        break;

      foreach (var child in EbmlScanner.Children(container.File, entry)) {
        if (child.Id != MatroskaElementId.AUDIO)
          continue;

        // Matroska's schema defaults these two values when the elements are omitted.
        var sampleRate = 8000;
        var channels = 1;
        var bitsPerSample = 0;

        foreach (var field in EbmlScanner.Children(container.File, child))
          switch (field.Id) {
            case MatroskaElementId.SAMPLING_FREQUENCY: {
              // An element that is present but unreadable is not the same as one that is absent:
              // the schema default only stands for the absent case, so an unreadable one reads as
              // no stated rate rather than as 8000 Hz.
              sampleRate = 0;
              if (field.FloatValue() is { } value) {
                var rounded = Math.Round(value);
                sampleRate = double.IsFinite(value) && value > 0 && rounded <= int.MaxValue && Math.Abs(value - rounded) < 1e-9
                  ? checked((int)rounded)
                  : 0;
              }

              break;
            }
            case MatroskaElementId.CHANNELS:
              channels = checked((int)Math.Min(field.UnsignedValue(), int.MaxValue));
              break;
            case MatroskaElementId.BIT_DEPTH:
              bitsPerSample = checked((int)Math.Min(field.UnsignedValue(), int.MaxValue));
              break;
          }

        result[index] = (sampleRate, channels, bitsPerSample);
        break;
      }

      ++index;
    }

    return result;
  }

  private static MediaStreamInfo _WithAudioGeometry(MediaStreamInfo source, int sampleRate, int channels, int bitsPerSample)
    => new() {
      Index = source.Index,
      Kind = source.Kind,
      Codec = source.Codec,
      Handler = source.Handler,
      CodecId = source.CodecId,
      TimeBase = source.TimeBase,
      FrameRate = source.FrameRate,
      DeclaredFrameCount = source.DeclaredFrameCount,
      Width = source.Width,
      Height = source.Height,
      BitsPerPixel = source.BitsPerPixel,
      SampleRate = sampleRate,
      Channels = channels,
      BitsPerSample = bitsPerSample,
      CodecPrivateData = source.CodecPrivateData,
      Language = source.Language,
      Name = source.Name,
    };

  public static VideoMetadata Metadata(MatroskaContainer container) {
    ArgumentNullException.ThrowIfNull(container);
    return container.FileMetadata;
  }

  public static IEnumerable<CodedPacket> ReadPackets(MatroskaContainer container) {
    ArgumentNullException.ThrowIfNull(container);
    return _Walk(container, null);
  }

  public static IEnumerable<CodedPacket> ReadPackets(MatroskaContainer container, int streamIndex) {
    ArgumentNullException.ThrowIfNull(container);
    if ((uint)streamIndex >= (uint)container.TrackEntries.Count)
      return [];
    return _Walk(container, streamIndex);
  }

  private static IEnumerable<CodedPacket> _Walk(MatroskaContainer container, int? onlyStream) {
    var frames = new List<(int Offset, int Length)>();
    var packets = new List<CodedPacket>();

    foreach (var level1 in EbmlScanner.Walk(
               container.File,
               container.SegmentStart,
               container.SegmentEnd,
               MatroskaElementId.IsSegmentLevel)) {
      if (level1.Id != MatroskaElementId.CLUSTER)
        continue;

      var clusterTimestamp = 0L;
      foreach (var child in EbmlScanner.Children(container.File, level1)) {
        switch (child.Id) {
          case MatroskaElementId.CLUSTER_TIMESTAMP:
            clusterTimestamp = (long)child.UnsignedValue();
            continue;

          case MatroskaElementId.SIMPLE_BLOCK:
            _ReadBlock(container, child, clusterTimestamp, null, null, frames, onlyStream, packets);
            break;

          case MatroskaElementId.BLOCK_GROUP: {
            EbmlElement? block = null;
            long? duration = null;
            var referenced = false;

            foreach (var member in EbmlScanner.Children(container.File, child))
              switch (member.Id) {
                case MatroskaElementId.BLOCK:
                  block ??= member;
                  break;
                case MatroskaElementId.BLOCK_DURATION:
                  duration ??= (long)member.UnsignedValue();
                  break;
                case MatroskaElementId.REFERENCE_BLOCK:
                  referenced = true;
                  break;
              }

            if (block == null)
              continue;

            _ReadBlock(container, block.Value, clusterTimestamp, duration, !referenced, frames, onlyStream, packets);
            break;
          }

          default:
            continue;
        }

        foreach (var packet in packets)
          yield return packet;
      }
    }
  }

  private static void _ReadBlock(
    MatroskaContainer container,
    EbmlElement element,
    long clusterTimestamp,
    long? statedDuration,
    bool? keyFrame,
    List<(int Offset, int Length)> frames,
    int? onlyStream,
    List<CodedPacket> packets) {
    packets.Clear();

    if (element.IsTruncated)
      throw new InvalidDataException(
        $"The file ends inside the block at offset {element.Offset}, which states more bytes than are there — {element.Body.Length} of them were written.");

    var block = element.Body;
    if (!MatroskaBlock.TryReadHeader(block.Span, out var header))
      throw new InvalidDataException(
        $"A block of {block.Length} bytes at offset {element.Offset} is too short to state a track, a timestamp and its flags.");

    if (!container._TrackIndex.TryGetValue(header.TrackNumber, out var streamIndex))
      return;
    if (onlyStream != null && streamIndex != onlyStream)
      return;

    var track = container.TrackEntries[streamIndex];
    MatroskaBlock.ReadFrames(block.Span, header, frames);

    var timestamp = clusterTimestamp + header.RelativeTimestamp - track.CodecDelayTicks;
    var laces = frames.Count;
    var duration = statedDuration
                   ?? (track.DefaultDurationNanoseconds > 0
                     ? track.DefaultDurationNanoseconds * laces / container.TimestampScale
                     : 0);

    for (var i = 0; i < laces; ++i) {
      var (offset, length) = frames[i];
      var start = duration * i / laces;
      var next = duration * (i + 1) / laces;
      packets.Add(new(
        streamIndex,
        block.Slice(offset, length),
        timestamp + start,
        timestamp + start,
        duration == 0 ? null : next - start,
        keyFrame ?? header.IsKeyFrame));
    }
  }
}
