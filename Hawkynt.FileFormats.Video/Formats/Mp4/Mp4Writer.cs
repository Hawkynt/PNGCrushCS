using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;

namespace FileFormat.Mp4;

/// <summary>Writes an ISO base media file with one chunk per coded sample and complete classic sample tables.</summary>
public sealed class Mp4Writer : IVideoContainerWriter<Mp4Writer> {

  private sealed class TrackState(MediaStreamInfo info) {
    internal MediaStreamInfo Info { get; } = info;
    internal List<PacketState> Packets { get; } = [];
    internal List<SampleState> Samples { get; } = [];
    internal long Timescale { get; set; }
    internal long Duration { get; set; }
  }

  private readonly record struct PacketState(CodedPacket Packet, int StorageOrdinal);
  private readonly record struct SampleState(uint Offset, uint Size, long Dts, long Pts, long Duration, bool Sync);

  private readonly IReadOnlyList<MediaStreamInfo> _streams;
  private readonly VideoMetadata _metadata;
  private readonly TrackState[] _tracks;
  private readonly List<PacketState> _storage = [];
  private bool _finished;

  private Mp4Writer(IReadOnlyList<MediaStreamInfo> streams, VideoMetadata metadata) {
    ArgumentNullException.ThrowIfNull(streams);
    ArgumentNullException.ThrowIfNull(metadata);
    if (streams.Count == 0)
      throw new ArgumentException("ISO base media needs at least one track.", nameof(streams));

    this._tracks = new TrackState[streams.Count];
    for (var i = 0; i < streams.Count; ++i) {
      var info = streams[i] ?? throw new ArgumentException($"Track {i} is null.", nameof(streams));
      if (info.Index != i)
        throw new ArgumentException($"Tracks must be indexed densely from zero; position {i} has index {info.Index}.", nameof(streams));
      _ValidateSampleEntry(info);
      this._tracks[i] = new(info);
    }

    this._streams = streams;
    this._metadata = metadata;
  }

  public static string PrimaryExtension => ".mp4";
  public static string[] FileExtensions => [".mp4", ".m4v", ".mov", ".qt", ".3gp", ".3g2", ".m4a"];

  public static Mp4Writer Create(IReadOnlyList<MediaStreamInfo> streams, VideoMetadata metadata) => new(streams, metadata);

  public void WritePacket(CodedPacket packet) {
    if (this._finished)
      throw new InvalidOperationException("MP4 writer has already been finished.");
    if ((uint)packet.StreamIndex >= (uint)this._tracks.Length)
      throw new ArgumentOutOfRangeException(nameof(packet), packet.StreamIndex, "Packet names no declared MP4 track.");

    var state = new PacketState(packet, this._storage.Count);
    this._storage.Add(state);
    this._tracks[packet.StreamIndex].Packets.Add(state);
  }

  public byte[] Finish() {
    if (this._finished)
      throw new InvalidOperationException("MP4 writer has already been finished.");
    this._finished = true;

    using var output = new MemoryStream();
    _WriteFtyp(output);

    var mdatHeader = output.Position;
    ContainerWriterTools.WriteUInt32BigEndian(output, 0);
    ContainerWriterTools.WriteAscii(output, "mdat");

    var offsets = new uint[this._storage.Count];
    for (var i = 0; i < this._storage.Count; ++i) {
      if (output.Position > uint.MaxValue)
        throw new NotSupportedException("This MP4 writer uses 32-bit chunk offsets and cannot exceed 4 GiB.");
      offsets[i] = checked((uint)output.Position);
      output.Write(this._storage[i].Packet.Data.Span);
    }

    var afterMdat = output.Position;
    var mdatSize = afterMdat - mdatHeader;
    if (mdatSize > uint.MaxValue)
      throw new NotSupportedException("This MP4 writer uses a 32-bit mdat size and cannot exceed 4 GiB.");
    output.Position = mdatHeader;
    ContainerWriterTools.WriteUInt32BigEndian(output, checked((uint)mdatSize));
    output.Position = afterMdat;

    for (var i = 0; i < this._tracks.Length; ++i)
      this._PrepareTrack(this._tracks[i], offsets);

    ContainerWriterTools.WriteBox(output, "moov", moov => {
      this._WriteMovieHeader(moov);
      for (var i = 0; i < this._tracks.Length; ++i)
        this._WriteTrack(moov, this._tracks[i], i + 1);
    });

    return output.ToArray();
  }

  private void _PrepareTrack(TrackState track, uint[] storageOffsets) {
    var info = track.Info;
    var timescale = _Timescale(info);
    track.Timescale = timescale;

    for (var i = 0; i < track.Packets.Count; ++i) {
      var current = track.Packets[i];
      var packet = current.Packet;
      var dtsSource = packet.DecodeTimestamp ?? packet.PresentationTimestamp ?? i;
      var ptsSource = packet.PresentationTimestamp ?? dtsSource;
      var dts = _MapTime(dtsSource, info, timescale);
      var pts = _MapTime(ptsSource, info, timescale);

      long duration;
      if (packet.Duration is > 0)
        duration = Math.Max(1, _MapDuration(packet.Duration.Value, info, timescale));
      else if (i + 1 < track.Packets.Count) {
        var next = track.Packets[i + 1].Packet;
        var nextSource = next.DecodeTimestamp ?? next.PresentationTimestamp ?? (i + 1);
        duration = Math.Max(1, _MapTime(nextSource, info, timescale) - dts);
      } else
        duration = _DefaultDuration(info, timescale);

      if (dts < 0)
        throw new NotSupportedException($"MP4 track {info.Index} has a negative decode timestamp {dts}; an edit list would be required to represent it.");

      track.Samples.Add(new(
        storageOffsets[current.StorageOrdinal],
        checked((uint)packet.Data.Length),
        dts,
        pts,
        duration,
        info.Kind != MediaStreamKind.Video || packet.IsKeyFrame));
    }

    track.Duration = track.Samples.Count == 0 ? 0 : track.Samples.Max(s => s.Dts + s.Duration);
    if (track.Duration > uint.MaxValue)
      throw new NotSupportedException($"MP4 track {info.Index} duration {track.Duration} exceeds version-0 media header range.");
  }

  private static void _WriteFtyp(Stream output) {
    ContainerWriterTools.WriteBox(output, "ftyp", body => {
      ContainerWriterTools.WriteAscii(body, "isom");
      ContainerWriterTools.WriteUInt32BigEndian(body, 0x00000200);
      ContainerWriterTools.WriteAscii(body, "isom");
      ContainerWriterTools.WriteAscii(body, "iso2");
      ContainerWriterTools.WriteAscii(body, "mp41");
    });
  }

  private void _WriteMovieHeader(Stream moov) {
    const uint movieTimescale = 1000;
    var duration = 0L;
    foreach (var track in this._tracks) {
      if (track.Timescale <= 0)
        continue;
      var scaled = (Int128)track.Duration * movieTimescale / track.Timescale;
      duration = Math.Max(duration, checked((long)scaled));
    }
    if (duration > uint.MaxValue)
      throw new NotSupportedException("MP4 movie duration exceeds version-0 mvhd range.");

    var created = _Mp4Time(this._metadata.CreationTime);
    ContainerWriterTools.WriteBox(moov, "mvhd", body => {
      _FullBox(body, 0, 0);
      ContainerWriterTools.WriteUInt32BigEndian(body, created);
      ContainerWriterTools.WriteUInt32BigEndian(body, created);
      ContainerWriterTools.WriteUInt32BigEndian(body, movieTimescale);
      ContainerWriterTools.WriteUInt32BigEndian(body, checked((uint)duration));
      ContainerWriterTools.WriteUInt32BigEndian(body, 0x00010000); // rate 1.0
      ContainerWriterTools.WriteUInt16BigEndian(body, 0x0100); // volume 1.0
      ContainerWriterTools.WriteUInt16BigEndian(body, 0);
      ContainerWriterTools.WriteUInt64BigEndian(body, 0);
      _Matrix(body);
      for (var i = 0; i < 6; ++i)
        ContainerWriterTools.WriteUInt32BigEndian(body, 0);
      ContainerWriterTools.WriteUInt32BigEndian(body, checked((uint)this._tracks.Length + 1));
    });
  }

  private void _WriteTrack(Stream moov, TrackState track, int trackId) {
    ContainerWriterTools.WriteBox(moov, "trak", trak => {
      this._WriteTrackHeader(trak, track, trackId);
      ContainerWriterTools.WriteBox(trak, "mdia", mdia => {
        this._WriteMediaHeader(mdia, track);
        _WriteHandler(mdia, track.Info);
        ContainerWriterTools.WriteBox(mdia, "minf", minf => this._WriteMediaInfo(minf, track));
      });
    });
  }

  private void _WriteTrackHeader(Stream trak, TrackState track, int trackId) {
    var info = track.Info;
    var movieDuration = track.Timescale == 0 ? 0 : (Int128)track.Duration * 1000 / track.Timescale;
    if (movieDuration > uint.MaxValue)
      throw new NotSupportedException($"MP4 track {info.Index} duration exceeds version-0 tkhd range.");
    var created = _Mp4Time(this._metadata.CreationTime);

    ContainerWriterTools.WriteBox(trak, "tkhd", body => {
      _FullBox(body, 0, 7);
      ContainerWriterTools.WriteUInt32BigEndian(body, created);
      ContainerWriterTools.WriteUInt32BigEndian(body, created);
      ContainerWriterTools.WriteUInt32BigEndian(body, checked((uint)trackId));
      ContainerWriterTools.WriteUInt32BigEndian(body, 0);
      ContainerWriterTools.WriteUInt32BigEndian(body, checked((uint)movieDuration));
      ContainerWriterTools.WriteUInt64BigEndian(body, 0);
      ContainerWriterTools.WriteUInt16BigEndian(body, 0);
      ContainerWriterTools.WriteUInt16BigEndian(body, 0);
      ContainerWriterTools.WriteUInt16BigEndian(body, info.Kind == MediaStreamKind.Audio ? (ushort)0x0100 : (ushort)0);
      ContainerWriterTools.WriteUInt16BigEndian(body, 0);
      _Matrix(body);
      ContainerWriterTools.WriteUInt32BigEndian(body, checked((uint)Math.Max(0, info.Width)) << 16);
      ContainerWriterTools.WriteUInt32BigEndian(body, checked((uint)Math.Max(0, info.Height)) << 16);
    });
  }

  private void _WriteMediaHeader(Stream mdia, TrackState track) {
    var created = _Mp4Time(this._metadata.CreationTime);
    ContainerWriterTools.WriteBox(mdia, "mdhd", body => {
      _FullBox(body, 0, 0);
      ContainerWriterTools.WriteUInt32BigEndian(body, created);
      ContainerWriterTools.WriteUInt32BigEndian(body, created);
      ContainerWriterTools.WriteUInt32BigEndian(body, checked((uint)track.Timescale));
      ContainerWriterTools.WriteUInt32BigEndian(body, checked((uint)track.Duration));
      ContainerWriterTools.WriteUInt16BigEndian(body, _Language(track.Info.Language));
      ContainerWriterTools.WriteUInt16BigEndian(body, 0);
    });
  }

  private static void _WriteHandler(Stream mdia, MediaStreamInfo info) {
    var handler = info.Kind switch {
      MediaStreamKind.Video => "vide",
      MediaStreamKind.Audio => "soun",
      MediaStreamKind.Subtitle => "subt",
      _ => "meta",
    };
    ContainerWriterTools.WriteBox(mdia, "hdlr", body => {
      _FullBox(body, 0, 0);
      ContainerWriterTools.WriteUInt32BigEndian(body, 0);
      ContainerWriterTools.WriteAscii(body, handler);
      ContainerWriterTools.WriteUInt64BigEndian(body, 0);
      ContainerWriterTools.WriteUInt32BigEndian(body, 0);
      ContainerWriterTools.WriteAscii(body, string.IsNullOrEmpty(info.Name) ? "PNGCrushCS" : info.Name);
      body.WriteByte(0);
    });
  }

  private void _WriteMediaInfo(Stream minf, TrackState track) {
    if (track.Info.Kind == MediaStreamKind.Video)
      ContainerWriterTools.WriteBox(minf, "vmhd", body => { _FullBox(body, 0, 1); for (var i = 0; i < 4; ++i) ContainerWriterTools.WriteUInt16BigEndian(body, 0); });
    else if (track.Info.Kind == MediaStreamKind.Audio)
      ContainerWriterTools.WriteBox(minf, "smhd", body => { _FullBox(body, 0, 0); ContainerWriterTools.WriteUInt32BigEndian(body, 0); });
    else
      ContainerWriterTools.WriteBox(minf, "nmhd", body => _FullBox(body, 0, 0));

    ContainerWriterTools.WriteBox(minf, "dinf", dinf =>
      ContainerWriterTools.WriteBox(dinf, "dref", dref => {
        _FullBox(dref, 0, 0);
        ContainerWriterTools.WriteUInt32BigEndian(dref, 1);
        ContainerWriterTools.WriteBox(dref, "url ", url => _FullBox(url, 0, 1));
      }));

    ContainerWriterTools.WriteBox(minf, "stbl", stbl => this._WriteSampleTable(stbl, track));
  }

  private void _WriteSampleTable(Stream stbl, TrackState track) {
    ContainerWriterTools.WriteBox(stbl, "stsd", body => {
      _FullBox(body, 0, 0);
      ContainerWriterTools.WriteUInt32BigEndian(body, 1);
      body.Write(_SampleEntry(track.Info));
    });

    _WriteTimeToSample(stbl, track);
    _WriteCompositionOffsets(stbl, track);

    ContainerWriterTools.WriteBox(stbl, "stsc", body => {
      _FullBox(body, 0, 0);
      ContainerWriterTools.WriteUInt32BigEndian(body, track.Samples.Count == 0 ? 0u : 1u);
      if (track.Samples.Count != 0) {
        ContainerWriterTools.WriteUInt32BigEndian(body, 1);
        ContainerWriterTools.WriteUInt32BigEndian(body, 1);
        ContainerWriterTools.WriteUInt32BigEndian(body, 1);
      }
    });

    ContainerWriterTools.WriteBox(stbl, "stsz", body => {
      _FullBox(body, 0, 0);
      ContainerWriterTools.WriteUInt32BigEndian(body, 0);
      ContainerWriterTools.WriteUInt32BigEndian(body, checked((uint)track.Samples.Count));
      foreach (var sample in track.Samples)
        ContainerWriterTools.WriteUInt32BigEndian(body, sample.Size);
    });

    ContainerWriterTools.WriteBox(stbl, "stco", body => {
      _FullBox(body, 0, 0);
      ContainerWriterTools.WriteUInt32BigEndian(body, checked((uint)track.Samples.Count));
      foreach (var sample in track.Samples)
        ContainerWriterTools.WriteUInt32BigEndian(body, sample.Offset);
    });

    if (track.Info.Kind == MediaStreamKind.Video && track.Samples.Any(s => !s.Sync))
      ContainerWriterTools.WriteBox(stbl, "stss", body => {
        _FullBox(body, 0, 0);
        var sync = track.Samples.Select((sample, index) => (sample, index)).Where(x => x.sample.Sync).ToArray();
        ContainerWriterTools.WriteUInt32BigEndian(body, checked((uint)sync.Length));
        foreach (var item in sync)
          ContainerWriterTools.WriteUInt32BigEndian(body, checked((uint)item.index + 1));
      });
  }

  private static void _WriteTimeToSample(Stream stbl, TrackState track) {
    var runs = new List<(uint Count, uint Duration)>();
    foreach (var sample in track.Samples) {
      if (sample.Duration is <= 0 or > uint.MaxValue)
        throw new NotSupportedException($"MP4 sample duration {sample.Duration} is outside stts range.");
      var duration = (uint)sample.Duration;
      if (runs.Count != 0 && runs[^1].Duration == duration)
        runs[^1] = (runs[^1].Count + 1, duration);
      else
        runs.Add((1, duration));
    }

    ContainerWriterTools.WriteBox(stbl, "stts", body => {
      _FullBox(body, 0, 0);
      ContainerWriterTools.WriteUInt32BigEndian(body, checked((uint)runs.Count));
      foreach (var run in runs) {
        ContainerWriterTools.WriteUInt32BigEndian(body, run.Count);
        ContainerWriterTools.WriteUInt32BigEndian(body, run.Duration);
      }
    });
  }

  private static void _WriteCompositionOffsets(Stream stbl, TrackState track) {
    if (!track.Samples.Any(s => s.Pts != s.Dts))
      return;

    var signed = track.Samples.Any(s => s.Pts < s.Dts);
    var runs = new List<(uint Count, long Offset)>();
    foreach (var sample in track.Samples) {
      var offset = sample.Pts - sample.Dts;
      if (offset < int.MinValue || offset > uint.MaxValue || signed && offset > int.MaxValue)
        throw new NotSupportedException($"MP4 composition offset {offset} is outside ctts range.");
      if (runs.Count != 0 && runs[^1].Offset == offset)
        runs[^1] = (runs[^1].Count + 1, offset);
      else
        runs.Add((1, offset));
    }

    ContainerWriterTools.WriteBox(stbl, "ctts", body => {
      _FullBox(body, signed ? (byte)1 : (byte)0, 0);
      ContainerWriterTools.WriteUInt32BigEndian(body, checked((uint)runs.Count));
      foreach (var run in runs) {
        ContainerWriterTools.WriteUInt32BigEndian(body, run.Count);
        if (signed)
          ContainerWriterTools.WriteInt32BigEndian(body, checked((int)run.Offset));
        else
          ContainerWriterTools.WriteUInt32BigEndian(body, checked((uint)run.Offset));
      }
    });
  }

  private static ReadOnlySpan<byte> _SampleEntry(MediaStreamInfo info) {
    if (!_HasWholeSampleEntry(info.CodecPrivateData.Span)) {
      if (info.Kind == MediaStreamKind.Video && info.Codec.EqualsIgnoringCase(CodecTag.FromCharacters("MJPG")))
        return _JpegSampleEntry(info);
      throw new NotSupportedException(
        $"MP4 track {info.Index} needs a complete sample entry in CodecPrivateData; synthesising codec configuration from '{info.Codec}' would cross into codec parsing.");
    }
    return info.CodecPrivateData.Span;
  }

  private static byte[] _JpegSampleEntry(MediaStreamInfo info) {
    return ContainerWriterTools.Build(entry => {
      ContainerWriterTools.WriteUInt32BigEndian(entry, 86);
      ContainerWriterTools.WriteAscii(entry, "jpeg");
      entry.Write(new byte[6]);
      ContainerWriterTools.WriteUInt16BigEndian(entry, 1);
      ContainerWriterTools.WriteUInt16BigEndian(entry, 0);
      ContainerWriterTools.WriteUInt16BigEndian(entry, 0);
      ContainerWriterTools.WriteUInt32BigEndian(entry, 0);
      ContainerWriterTools.WriteUInt32BigEndian(entry, 0);
      ContainerWriterTools.WriteUInt32BigEndian(entry, 0);
      ContainerWriterTools.WriteUInt16BigEndian(entry, checked((ushort)Math.Max(0, info.Width)));
      ContainerWriterTools.WriteUInt16BigEndian(entry, checked((ushort)Math.Max(0, info.Height)));
      ContainerWriterTools.WriteUInt32BigEndian(entry, 0x00480000);
      ContainerWriterTools.WriteUInt32BigEndian(entry, 0x00480000);
      ContainerWriterTools.WriteUInt32BigEndian(entry, 0);
      ContainerWriterTools.WriteUInt16BigEndian(entry, 1);
      entry.Write(new byte[32]);
      ContainerWriterTools.WriteUInt16BigEndian(entry, checked((ushort)(info.BitsPerPixel > 0 ? info.BitsPerPixel : 24)));
      ContainerWriterTools.WriteUInt16BigEndian(entry, 0xFFFF);
    });
  }

  private static void _ValidateSampleEntry(MediaStreamInfo info) {
    if (_HasWholeSampleEntry(info.CodecPrivateData.Span))
      return;
    if (info.Kind == MediaStreamKind.Video && info.Codec.EqualsIgnoringCase(CodecTag.FromCharacters("MJPG")))
      return;
    throw new NotSupportedException(
      $"MP4 track {info.Index} cannot be written without a complete sample entry in CodecPrivateData. The current bytes are container-specific to another format or absent.");
  }

  private static bool _HasWholeSampleEntry(ReadOnlySpan<byte> data)
    => data.Length >= 16 && BinaryPrimitives.ReadUInt32BigEndian(data) >= 16 && BinaryPrimitives.ReadUInt32BigEndian(data) <= data.Length;

  private static long _Timescale(MediaStreamInfo info) {
    if (info.TimeBase.IsKnown) {
      var units = ContainerWriterTools.UnitsPerSecond(info.TimeBase);
      if (units is <= 0 or > uint.MaxValue)
        throw new NotSupportedException($"MP4 track {info.Index} time base {info.TimeBase} needs an unsupported timescale.");
      return units;
    }
    if (info.FrameRate.IsKnown) {
      var gcd = ContainerWriterTools.GreatestCommonDivisor(info.FrameRate.Numerator, info.FrameRate.Denominator);
      var units = info.FrameRate.Numerator / gcd;
      if (units is > 0 and <= uint.MaxValue)
        return units;
    }
    return 1000;
  }

  private static long _MapTime(long value, MediaStreamInfo info, long timescale) {
    if (info.TimeBase.IsKnown)
      return ContainerWriterTools.Rescale(value, info.TimeBase, timescale);
    if (info.FrameRate.IsKnown) {
      var gcd = ContainerWriterTools.GreatestCommonDivisor(info.FrameRate.Numerator, info.FrameRate.Denominator);
      return checked(value * (info.FrameRate.Denominator / gcd));
    }
    return value;
  }

  private static long _MapDuration(long value, MediaStreamInfo info, long timescale)
    => _MapTime(value, info, timescale);

  private static long _DefaultDuration(MediaStreamInfo info, long timescale) {
    if (info.FrameRate.IsKnown) {
      var value = (Int128)timescale * info.FrameRate.Denominator / info.FrameRate.Numerator;
      return Math.Max(1, checked((long)value));
    }
    return 1;
  }

  private static uint _Mp4Time(DateTimeOffset? time) {
    if (time == null)
      return 0;
    var epoch = new DateTimeOffset(1904, 1, 1, 0, 0, 0, TimeSpan.Zero);
    var seconds = (time.Value.ToUniversalTime() - epoch).TotalSeconds;
    return seconds is >= 0 and <= uint.MaxValue ? (uint)seconds : 0;
  }

  private static ushort _Language(string? language) {
    var code = language?.Split('-')[0].ToLowerInvariant();
    if (code == null || code.Length != 3 || code.Any(c => c is < 'a' or > 'z'))
      code = "und";
    return (ushort)(((code[0] - 0x60) << 10) | ((code[1] - 0x60) << 5) | (code[2] - 0x60));
  }

  private static void _FullBox(Stream body, byte version, int flags) {
    body.WriteByte(version);
    body.WriteByte((byte)(flags >> 16));
    body.WriteByte((byte)(flags >> 8));
    body.WriteByte((byte)flags);
  }

  private static void _Matrix(Stream body) {
    ContainerWriterTools.WriteUInt32BigEndian(body, 0x00010000);
    ContainerWriterTools.WriteUInt32BigEndian(body, 0);
    ContainerWriterTools.WriteUInt32BigEndian(body, 0);
    ContainerWriterTools.WriteUInt32BigEndian(body, 0);
    ContainerWriterTools.WriteUInt32BigEndian(body, 0x00010000);
    ContainerWriterTools.WriteUInt32BigEndian(body, 0);
    ContainerWriterTools.WriteUInt32BigEndian(body, 0);
    ContainerWriterTools.WriteUInt32BigEndian(body, 0);
    ContainerWriterTools.WriteUInt32BigEndian(body, 0x40000000);
  }
}
