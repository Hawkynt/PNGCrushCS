using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using FileFormat.Core;
using Hawkynt.FileFormats.Images;
using Hawkynt.FileFormats.Video;

namespace Crush.Viewer;

/// <summary>One video file opened for viewing: its streams, its packets, and the frame last decoded.</summary>
/// <remarks>
/// Frames are decoded on demand and never all at once. Stepping forward continues the decoder that
/// produced the frame before; stepping back, or jumping, restarts it at the nearest key frame at or
/// before the one asked for, which is the only place a decoder that predicts from earlier pictures
/// can start. Frame numbers are pictures out of the decoder, not packets in, because a reordering
/// codec emits them in a different order and some packets emit none.
/// </remarks>
internal sealed class VideoDocument {

  private readonly byte[] _data;
  private readonly List<CodedPacket> _packets;
  private readonly List<(int Packet, int Frame)> _keyFrames = [(0, 0)];
  private IVideoFrameDecoder? _decoder;
  private int _nextPacket;
  private int _nextFrame;
  private readonly Queue<RawImage> _pending = new();
  private RawImage? _last;
  private int _lastIndex = -1;

  private VideoDocument(FileInfo file, byte[] data, VideoFormatEntry container, IReadOnlyList<MediaStreamInfo> streams, MediaStreamInfo video) {
    this.File = file;
    this._data = data;
    this.Container = container;
    this.Streams = streams;
    this.Video = video;
    this._packets = VideoFormatRegistry.ReadPackets(data, video.Index).Where(static p => p.Data.Length != 0).ToList();
  }

  internal FileInfo File { get; }
  internal VideoFormatEntry Container { get; }
  internal IReadOnlyList<MediaStreamInfo> Streams { get; }

  /// <summary>The first video stream a registered decoder takes.</summary>
  internal MediaStreamInfo Video { get; }

  /// <summary>The name of the codec that decodes <see cref="Video"/>.</summary>
  internal string CodecName => VideoFormatRegistry.AllCodecs.FirstOrDefault(c => c.Accepts(this.Video))?.CodecName ?? this.Video.Codec.ToString();

  /// <summary>How many pictures the stream holds: one per coded packet until decoding says otherwise.</summary>
  internal int FrameCount { get; private set; }

  /// <summary>Frames per second, or 25 where the container does not say.</summary>
  internal double FramesPerSecond
    => this.Video.FrameRate is { Numerator: > 0, Denominator: > 0 } rate ? (double)rate.Numerator / rate.Denominator : 25;

  /// <summary>Opens <paramref name="file"/> if a registered container reads it and a registered codec decodes a video stream in it.</summary>
  internal static VideoDocument? TryOpen(FileInfo file) {
    try {
      var data = System.IO.File.ReadAllBytes(file.FullName);
      var format = VideoFormatRegistry.Detect(data);
      if (format == VideoFormat.Unknown || VideoFormatRegistry.GetEntry(format) is not { } container)
        return null;

      var streams = VideoFormatRegistry.ReadStreams(data);
      var video = streams.FirstOrDefault(static s => s.Kind == MediaStreamKind.Video && VideoFormatRegistry.CanDecode(s));
      if (video == null)
        return null;

      var document = new VideoDocument(file, data, container, streams, video);
      document.FrameCount = Math.Max(1, document._packets.Count);
      return document;
    } catch (Exception) {
      return null;
    }
  }

  /// <summary>The picture at <paramref name="index"/>, or <c>null</c> past the last one decoded.</summary>
  internal RawImage? Frame(int index) {
    if (index < 0)
      return null;
    if (index == this._lastIndex)
      return this._last;

    if (this._decoder == null || index < this._nextFrame) {
      var (packet, frame) = this._keyFrames.Last(k => k.Frame <= index);
      this._Restart(packet, frame);
    }

    while (this._nextFrame <= index) {
      var picture = this._Next();
      if (picture == null) {
        this.FrameCount = this._nextFrame;
        return null;
      }

      if (this._nextFrame++ == index) {
        this._last = picture;
        this._lastIndex = index;
        return picture;
      }
    }

    return null;
  }

  /// <summary>Every picture, in order, decoded once and handed over one at a time.</summary>
  internal IEnumerable<RawImage> AllFrames(CancellationToken token = default) {
    this._Restart(0, 0);
    while (true) {
      token.ThrowIfCancellationRequested();
      var picture = this._Next();
      if (picture == null)
        yield break;
      ++this._nextFrame;
      yield return picture;
    }
  }

  /// <summary>The coded packets of every stream, for a remux that changes the container and nothing else.</summary>
  internal IEnumerable<CodedPacket> AllPackets() => VideoFormatRegistry.ReadPackets(this._data);

  private void _Restart(int packet, int frame) {
    this._decoder = VideoFormatRegistry.CreateDecoder(this.Video);
    this._nextPacket = packet;
    this._nextFrame = frame;
    this._pending.Clear();
  }

  private RawImage? _Next() {
    while (this._pending.Count == 0) {
      if (this._nextPacket >= this._packets.Count) {
        if (this._decoder == null)
          return null;
        foreach (var flushed in this._decoder.Flush())
          this._pending.Enqueue(flushed);
        this._decoder = null;
        if (this._pending.Count == 0)
          return null;
        break;
      }

      var packet = this._packets[this._nextPacket];
      if (packet.IsKeyFrame && this._pending.Count == 0 && !this._keyFrames.Any(k => k.Packet == this._nextPacket))
        this._keyFrames.Add((this._nextPacket, this._nextFrame));

      ++this._nextPacket;
      if (this._decoder!.TryDecode(packet, out var picture))
        this._pending.Enqueue(picture);
    }

    return this._pending.Dequeue();
  }
}

/// <summary>Opens video files for the viewer, and writes them back as video or as pictures.</summary>
/// <remarks>
/// The viewer pages through the frames of a video the way it pages through a multi-page picture, so
/// most of it needs nothing video-specific: a frame is a <see cref="RawImage"/> like any page.
/// Writing is where video differs, and it has two forms. Changing only the container copies the coded
/// packets of every stream across untouched — a remux, lossless by construction. Changing the codec
/// decodes the video stream and encodes it again with the codec asked for; the other streams are
/// copied across where the new container will carry them.
/// </remarks>
internal static class VideoService {

  private static readonly Lazy<HashSet<string>> _extensions = new(() =>
    VideoFormatRegistry.AllFormats.SelectMany(static e => e.AllExtensions).ToHashSet(StringComparer.OrdinalIgnoreCase));

  private static VideoDocument? _cached;

  /// <summary>Every extension a registered container claims.</summary>
  internal static IReadOnlySet<string> Extensions => _extensions.Value;

  /// <summary>Whether a registered container recognises the file's first bytes.</summary>
  internal static bool HasVideoHeader(ReadOnlySpan<byte> header)
    => header.Length >= 4 && VideoFormatRegistry.Detect(header.ToArray()) != VideoFormat.Unknown;

  /// <summary>The file as a video, or <c>null</c> when it is not one this package decodes.</summary>
  internal static VideoDocument? Open(FileInfo file) {
    var cached = _cached;
    if (cached != null && string.Equals(cached.File.FullName, file.FullName, StringComparison.OrdinalIgnoreCase)
        && cached.File.LastWriteTimeUtc == file.LastWriteTimeUtc)
      return cached;

    return _cached = VideoDocument.TryOpen(file);
  }

  /// <summary>The first picture of a video, for a thumbnail or a single-picture conversion.</summary>
  internal static RawImage? FirstFrame(FileInfo file) => Open(file)?.Frame(0);

  /// <summary>
  /// Writes <paramref name="source"/> as <paramref name="container"/>, re-encoding the video stream
  /// as <paramref name="codec"/> when one is given and it differs from the stream's own.
  /// </summary>
  internal static ConversionResult Convert(
    FileInfo source,
    VideoFormat container,
    CodecTag? codec,
    FileInfo destination,
    bool overwrite,
    Action<int, int>? progress = null,
    CancellationToken token = default) {
    destination.Refresh();
    if (!overwrite && destination.Exists)
      return new(source, destination.FullName, false, "A file of that name is already there.");

    var document = Open(source);
    if (document == null)
      return new(source, destination.FullName, false, "No registered container and codec decode a video stream in it.");

    try {
      byte[] file;
      string summary;
      if (codec is not { } wanted || wanted.EqualsIgnoringCase(document.Video.Codec)) {
        file = VideoFormatRegistry.Mux(container, document.Streams, document.AllPackets());
        summary = $"remuxed, {document.CodecName} kept";
      } else {
        (file, summary) = _Transcode(document, container, wanted, progress, token);
      }

      destination.Directory?.Create();
      var temporary = destination.FullName + ".partial";
      System.IO.File.WriteAllBytes(temporary, file);
      System.IO.File.Move(temporary, destination.FullName, overwrite: true);
      return new(source, destination.FullName, true, summary);
    } catch (OperationCanceledException) {
      return new(source, destination.FullName, false, "Cancelled; nothing was written.");
    } catch (Exception ex) {
      return new(source, destination.FullName, false, ex.Message);
    } finally {
      try { System.IO.File.Delete(destination.FullName + ".partial"); } catch { /* best effort */ }
    }
  }

  private static (byte[] File, string Summary) _Transcode(
    VideoDocument document, VideoFormat container, CodecTag codec, Action<int, int>? progress, CancellationToken token) {
    var source = document.Video;
    var requested = new MediaStreamInfo {
      Index = source.Index,
      Kind = MediaStreamKind.Video,
      Codec = codec,
      Handler = codec,
      Width = source.Width,
      Height = source.Height,
      TimeBase = source.TimeBase,
      FrameRate = source.FrameRate,
      DeclaredFrameCount = source.DeclaredFrameCount,
      Language = source.Language,
      Name = source.Name,
    };

    var encoder = VideoFormatRegistry.CreateEncoder(requested);
    var packets = new List<CodedPacket>();
    var count = 0;
    foreach (var frame in document.AllFrames(token)) {
      progress?.Invoke(count, document.FrameCount);
      if (encoder.TryEncode(frame, count, out var packet))
        packets.Add(packet);
      ++count;
    }
    packets.AddRange(encoder.Flush());

    var described = encoder.DescribeStream();
    var others = document.Streams.Where(s => s.Index != source.Index).ToList();
    var otherPackets = others.Count == 0
      ? []
      : document.AllPackets().Where(p => others.Any(s => s.Index == p.StreamIndex)).ToList();

    // Carry the other streams where the container will take them; where it will not, the video is
    // what was asked for and is written alone, and the summary says what was left out.
    if (others.Count != 0)
      try {
        var streams = document.Streams.Select(s => s.Index == source.Index ? described : s).ToList();
        var merged = packets.Concat(otherPackets).OrderBy(static p => p.DecodeTimestamp ?? p.PresentationTimestamp ?? 0).ToList();
        return (VideoFormatRegistry.Mux(container, streams, merged), $"{count} frames re-encoded, {others.Count} other stream(s) copied");
      } catch (Exception) {
        // fall through to video alone
      }

    var summary = others.Count == 0
      ? $"{count} frames re-encoded"
      : $"{count} frames re-encoded; the {container} container would not carry the other {others.Count} stream(s)";
    return (VideoFormatRegistry.Mux(container, [described], packets), summary);
  }

  /// <summary>Writes every frame of a video as its own picture.</summary>
  internal static IReadOnlyList<ConversionResult> ExtractFrames(
    VideoDocument document,
    FormatEntry target,
    DirectoryInfo destination,
    bool overwrite,
    Action<int, int>? progress = null,
    CancellationToken token = default) {
    destination.Create();
    var stem = Path.GetFileNameWithoutExtension(document.File.Name);
    var results = new List<ConversionResult>();
    var index = 0;
    foreach (var frame in document.AllFrames(token)) {
      progress?.Invoke(index, document.FrameCount);
      var file = ConversionService.PageFile(destination, stem, index, document.FrameCount, target.PrimaryExtension);
      file.Refresh();
      results.Add(!overwrite && file.Exists
        ? new(document.File, file.FullName, false, "A file of that name is already there.")
        : ConversionService.Write(frame, target, file, document.File));
      ++index;
    }
    progress?.Invoke(index, index);
    return results;
  }

  /// <summary>The containers the package writes, and the codecs it encodes, for a conversion dialog.</summary>
  internal static IReadOnlyList<VideoFormatEntry> WritableContainers
    => VideoFormatRegistry.WritableFormats.OrderBy(static e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();

  internal static IReadOnlyList<VideoCodecEncoderEntry> Encoders
    => VideoFormatRegistry.AllEncoders.OrderBy(static e => e.CodecName, StringComparer.OrdinalIgnoreCase).ToList();
}
