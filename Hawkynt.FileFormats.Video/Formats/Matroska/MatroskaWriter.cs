using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;

namespace FileFormat.Matroska;

/// <summary>Writes a finite Matroska document with explicit-size EBML elements and one block per packet.</summary>
public sealed class MatroskaWriter : IVideoContainerWriter<MatroskaWriter> {

  private const long _TIMESTAMP_SCALE = 1_000_000; // ns, one millisecond
  private const long _NANOSECONDS_PER_SECOND = 1_000_000_000;

  private readonly IReadOnlyList<MediaStreamInfo> _streams;
  private readonly VideoMetadata _metadata;
  private readonly MemoryStream _clusters = new();
  private bool _finished;

  private MatroskaWriter(IReadOnlyList<MediaStreamInfo> streams, VideoMetadata metadata) {
    ArgumentNullException.ThrowIfNull(streams);
    ArgumentNullException.ThrowIfNull(metadata);
    if (streams.Count == 0)
      throw new ArgumentException("Matroska needs at least one track.", nameof(streams));
    for (var i = 0; i < streams.Count; ++i) {
      var stream = streams[i] ?? throw new ArgumentException($"Track {i} is null.", nameof(streams));
      if (stream.Index != i)
        throw new ArgumentException($"Matroska tracks must be indexed densely from zero; position {i} has index {stream.Index}.", nameof(streams));
      _ = _CodecId(stream);
      if (stream.Kind == MediaStreamKind.Audio && _AudioGeometry(stream) is (0, _))
        throw new NotSupportedException($"Matroska audio track {i} has no sample rate in the interchange model or recognised codec header.");
    }
    this._streams = streams;
    this._metadata = metadata;
  }

  /// <summary>Gets the primary file extension for this format.</summary>
  public static string PrimaryExtension => ".mkv";
  /// <summary>Gets the file extensions supported by this format.</summary>
  public static string[] FileExtensions => [".mkv", ".mka", ".mks", ".mk3d", ".webm"];

  /// <summary>Creates a writer for the specified stream descriptions and metadata.</summary>
  public static MatroskaWriter Create(IReadOnlyList<MediaStreamInfo> streams, VideoMetadata metadata) => new(streams, metadata);

  /// <summary>Writes the specified coded packet to the container.</summary>
  public void WritePacket(CodedPacket packet) {
    if (this._finished)
      throw new InvalidOperationException("Matroska writer has already been finished.");
    if ((uint)packet.StreamIndex >= (uint)this._streams.Count)
      throw new ArgumentOutOfRangeException(nameof(packet), packet.StreamIndex, "Packet names no declared Matroska track.");

    var stream = this._streams[packet.StreamIndex];
    var sourceTimestamp = packet.PresentationTimestamp ?? packet.DecodeTimestamp ?? 0;
    var timestamp = ContainerWriterTools.Rescale(sourceTimestamp, stream.TimeBase, 1000);
    var clusterTime = Math.Max(0, timestamp);
    var relative = timestamp - clusterTime;
    if (relative is < short.MinValue or > short.MaxValue)
      throw new NotSupportedException($"Matroska packet timestamp {timestamp} cannot be represented by this writer's one-packet cluster.");

    ContainerWriterTools.WriteEbml(this._clusters, MatroskaElementId.CLUSTER, cluster => {
      ContainerWriterTools.WriteEbmlUnsigned(cluster, MatroskaElementId.CLUSTER_TIMESTAMP, checked((ulong)clusterTime));
      var block = ContainerWriterTools.Build(body => {
        _WriteTrackNumber(body, checked((ulong)packet.StreamIndex + 1));
        ContainerWriterTools.WriteInt16BigEndian(body, checked((short)relative));
        body.WriteByte((byte)(packet.IsKeyFrame || stream.Kind != MediaStreamKind.Video ? 0x80 : 0));
        body.Write(packet.Data.Span);
      });

      if (packet.Duration is > 0) {
        ContainerWriterTools.WriteEbml(cluster, MatroskaElementId.BLOCK_GROUP, group => {
          ContainerWriterTools.WriteEbml(group, MatroskaElementId.BLOCK, block);
          var duration = ContainerWriterTools.Rescale(packet.Duration.Value, stream.TimeBase, 1000);
          if (duration > 0)
            ContainerWriterTools.WriteEbmlUnsigned(group, MatroskaElementId.BLOCK_DURATION, checked((ulong)duration));
          if (stream.Kind == MediaStreamKind.Video && !packet.IsKeyFrame)
            ContainerWriterTools.WriteEbmlSigned(group, MatroskaElementId.REFERENCE_BLOCK, -1);
        });
      } else
        ContainerWriterTools.WriteEbml(cluster, MatroskaElementId.SIMPLE_BLOCK, block);
    });
  }

  /// <summary>Finishes writing the container and returns its encoded bytes.</summary>
  public byte[] Finish() {
    if (this._finished)
      throw new InvalidOperationException("Matroska writer has already been finished.");
    this._finished = true;

    using var output = new MemoryStream();
    ContainerWriterTools.WriteEbml(output, MatroskaElementId.EBML_HEADER, header => {
      ContainerWriterTools.WriteEbmlUnsigned(header, 0x4286, 1); // EBMLVersion
      ContainerWriterTools.WriteEbmlUnsigned(header, 0x42F7, 1); // EBMLReadVersion
      ContainerWriterTools.WriteEbmlUnsigned(header, 0x42F2, 4); // EBMLMaxIDLength
      ContainerWriterTools.WriteEbmlUnsigned(header, 0x42F3, 8); // EBMLMaxSizeLength
      ContainerWriterTools.WriteEbmlText(header, MatroskaElementId.DOC_TYPE, "matroska");
      ContainerWriterTools.WriteEbmlUnsigned(header, 0x4287, 4); // DocTypeVersion
      ContainerWriterTools.WriteEbmlUnsigned(header, 0x4285, 2); // DocTypeReadVersion
    });

    ContainerWriterTools.WriteEbml(output, MatroskaElementId.SEGMENT, segment => {
      this._WriteInfo(segment);
      this._WriteTracks(segment);
      this._WriteAttachments(segment);
      this._clusters.Position = 0;
      this._clusters.CopyTo(segment);
    });

    return output.ToArray();
  }

  private void _WriteInfo(Stream segment) {
    ContainerWriterTools.WriteEbml(segment, MatroskaElementId.INFO, info => {
      ContainerWriterTools.WriteEbmlUnsigned(info, MatroskaElementId.TIMESTAMP_SCALE, _TIMESTAMP_SCALE);
      ContainerWriterTools.WriteEbmlText(info, MatroskaElementId.MUXING_APP, "PNGCrushCS");
      ContainerWriterTools.WriteEbmlText(info, MatroskaElementId.WRITING_APP, this._metadata.EncodedBy ?? "PNGCrushCS");
      if (!string.IsNullOrEmpty(this._metadata.Title))
        ContainerWriterTools.WriteEbmlText(info, MatroskaElementId.TITLE, this._metadata.Title);
      if (this._metadata.Duration is { } duration && duration > TimeSpan.Zero)
        ContainerWriterTools.WriteEbmlFloat(info, MatroskaElementId.DURATION, duration.TotalMilliseconds);
      if (this._metadata.CreationTime is { } created) {
        var epoch = new DateTimeOffset(2001, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var nanos = checked((created.ToUniversalTime() - epoch).Ticks * 100L);
        ContainerWriterTools.WriteEbmlSigned(info, MatroskaElementId.DATE_UTC, nanos);
      }
    });
  }

  private void _WriteTracks(Stream segment) {
    ContainerWriterTools.WriteEbml(segment, MatroskaElementId.TRACKS, tracks => {
      for (var i = 0; i < this._streams.Count; ++i) {
        var stream = this._streams[i];
        ContainerWriterTools.WriteEbml(tracks, MatroskaElementId.TRACK_ENTRY, entry => {
          var number = checked((ulong)i + 1);
          ContainerWriterTools.WriteEbmlUnsigned(entry, MatroskaElementId.TRACK_NUMBER, number);
          ContainerWriterTools.WriteEbmlUnsigned(entry, 0x73C5, number); // TrackUID
          ContainerWriterTools.WriteEbmlUnsigned(entry, MatroskaElementId.TRACK_TYPE, _TrackType(stream.Kind));
          ContainerWriterTools.WriteEbmlText(entry, MatroskaElementId.CODEC_ID, _CodecId(stream));
          if (!stream.CodecPrivateData.IsEmpty)
            ContainerWriterTools.WriteEbml(entry, MatroskaElementId.CODEC_PRIVATE, stream.CodecPrivateData.Span);
          if (!string.IsNullOrEmpty(stream.Language)) {
            ContainerWriterTools.WriteEbmlText(entry, MatroskaElementId.LANGUAGE_BCP47, stream.Language);
            var legacy = stream.Language.Split('-')[0];
            if (legacy.Length == 3)
              ContainerWriterTools.WriteEbmlText(entry, MatroskaElementId.LANGUAGE, legacy);
          }
          if (!string.IsNullOrEmpty(stream.Name))
            ContainerWriterTools.WriteEbmlText(entry, MatroskaElementId.TRACK_NAME, stream.Name);
          if (stream.FrameRate.IsKnown) {
            var ns = (Int128)_NANOSECONDS_PER_SECOND * stream.FrameRate.Denominator / stream.FrameRate.Numerator;
            if (ns > 0 && ns <= ulong.MaxValue)
              ContainerWriterTools.WriteEbmlUnsigned(entry, MatroskaElementId.DEFAULT_DURATION, (ulong)ns);
          }

          if (stream.Kind == MediaStreamKind.Video)
            ContainerWriterTools.WriteEbml(entry, MatroskaElementId.VIDEO, video => {
              if (stream.Width > 0) ContainerWriterTools.WriteEbmlUnsigned(video, MatroskaElementId.PIXEL_WIDTH, checked((ulong)stream.Width));
              if (stream.Height > 0) ContainerWriterTools.WriteEbmlUnsigned(video, MatroskaElementId.PIXEL_HEIGHT, checked((ulong)stream.Height));
            });
          else if (stream.Kind == MediaStreamKind.Audio) {
            var (sampleRate, channels) = _AudioGeometry(stream);
            ContainerWriterTools.WriteEbml(entry, 0xE1, audio => {
              ContainerWriterTools.WriteEbmlFloat(audio, 0xB5, sampleRate);
              ContainerWriterTools.WriteEbmlUnsigned(audio, 0x9F, checked((ulong)Math.Max(1, channels)));
              if (stream.BitsPerSample > 0)
                ContainerWriterTools.WriteEbmlUnsigned(audio, 0x6264, checked((ulong)stream.BitsPerSample));
            });
          }
        });
      }
    });
  }

  private void _WriteAttachments(Stream segment) {
    if (this._metadata.CoverArt.Count == 0)
      return;

    ContainerWriterTools.WriteEbml(segment, MatroskaElementId.ATTACHMENTS, attachments => {
      ulong uid = 1;
      foreach (var cover in this._metadata.CoverArt) {
        ContainerWriterTools.WriteEbml(attachments, MatroskaElementId.ATTACHED_FILE, file => {
          ContainerWriterTools.WriteEbmlText(file, MatroskaElementId.FILE_NAME, cover.Kind ?? $"cover-{uid}");
          ContainerWriterTools.WriteEbmlText(file, MatroskaElementId.FILE_MIME_TYPE, cover.MimeType ?? "application/octet-stream");
          if (!string.IsNullOrEmpty(cover.Description))
            ContainerWriterTools.WriteEbmlText(file, MatroskaElementId.FILE_DESCRIPTION, cover.Description);
          ContainerWriterTools.WriteEbmlUnsigned(file, 0x46AE, uid++); // FileUID
          ContainerWriterTools.WriteEbml(file, MatroskaElementId.FILE_DATA, cover.Data);
        });
      }
    });
  }

  private static ulong _TrackType(MediaStreamKind kind)
    => kind switch {
      MediaStreamKind.Video => 1,
      MediaStreamKind.Audio => 2,
      MediaStreamKind.Subtitle => 0x11,
      MediaStreamKind.Data => 0x20,
      _ => throw new NotSupportedException($"Matroska writer has no TrackType for {kind}."),
    };

  private static string _CodecId(MediaStreamInfo stream) {
    if (!string.IsNullOrWhiteSpace(stream.CodecId))
      return stream.CodecId;

    var code = stream.Codec;
    if (code.EqualsIgnoringCase(CodecTag.FromCharacters("MJPG"))) return "V_MJPEG";
    if (code.EqualsIgnoringCase(CodecTag.FromCharacters("avc1")) || code.EqualsIgnoringCase(CodecTag.FromCharacters("H264"))) return "V_MPEG4/ISO/AVC";
    if (code.EqualsIgnoringCase(CodecTag.FromCharacters("hvc1")) || code.EqualsIgnoringCase(CodecTag.FromCharacters("H265"))) return "V_MPEGH/ISO/HEVC";
    if (code.EqualsIgnoringCase(CodecTag.FromCharacters("VP80"))) return "V_VP8";
    if (code.EqualsIgnoringCase(CodecTag.FromCharacters("VP90"))) return "V_VP9";
    if (code.EqualsIgnoringCase(CodecTag.FromCharacters("MPG1")) || code.EqualsIgnoringCase(CodecTag.FromCharacters("mpg1"))) return "V_MPEG1";
    if (code.EqualsIgnoringCase(CodecTag.FromCharacters("MPG2")) || code.EqualsIgnoringCase(CodecTag.FromCharacters("mpg2"))) return "V_MPEG2";
    if (code.EqualsIgnoringCase(CodecTag.FromCharacters("mp4a"))) return "A_AAC";
    if (code.EqualsIgnoringCase(CodecTag.FromCharacters(".mp3")) || code.EqualsIgnoringCase(CodecTag.FromCharacters("mpga"))) return "A_MPEG/L3";
    if (code.EqualsIgnoringCase(CodecTag.FromCharacters("ac-3"))) return "A_AC3";
    if (code.EqualsIgnoringCase(CodecTag.FromCharacters("dts "))) return "A_DTS";
    throw new NotSupportedException($"No Matroska CodecID is known for stream {stream.Index} tagged '{stream.Codec}'. Supply CodecId explicitly.");
  }

  private static (int SampleRate, int Channels) _AudioGeometry(MediaStreamInfo stream) {
    if (stream.SampleRate > 0)
      return (stream.SampleRate, stream.Channels);

    var data = stream.CodecPrivateData.Span;
    var id = stream.CodecId;
    if (id == "A_OPUS" && data.Length >= 19 && data[..8].SequenceEqual("OpusHead"u8))
      return (48000, data[9]);

    if (id == "A_VORBIS" && _FirstXiphHeader(data, out var header) && header.Length >= 16 && header[0] == 1 && header.Slice(1, 6).SequenceEqual("vorbis"u8))
      return (checked((int)BinaryPrimitives.ReadUInt32LittleEndian(header.Slice(12, 4))), header[11]);

    return (0, stream.Channels);
  }

  private static bool _FirstXiphHeader(ReadOnlySpan<byte> data, out ReadOnlySpan<byte> header) {
    header = default;
    if (data.Length < 2)
      return false;
    var at = 1;
    var length = 0;
    while (at < data.Length) {
      var value = data[at++];
      length += value;
      if (value != 255)
        break;
    }
    if (length <= 0 || at + length > data.Length)
      return false;
    header = data.Slice(at, length);
    return true;
  }

  private static void _WriteTrackNumber(Stream body, ulong number) {
    var bytes = 1;
    while (bytes < 8 && number >= (1UL << (7 * bytes)) - 1)
      ++bytes;
    var value = (1UL << (7 * bytes)) | number;
    for (var shift = (bytes - 1) * 8; shift >= 0; shift -= 8)
      body.WriteByte((byte)(value >> shift));
  }
}
