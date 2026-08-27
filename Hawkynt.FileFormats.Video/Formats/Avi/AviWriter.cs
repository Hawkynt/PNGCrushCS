using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;

namespace FileFormat.Avi;

/// <summary>Writes a conventional RIFF AVI with one movi chunk per coded packet and an idx1 index.</summary>
public sealed class AviWriter : IVideoContainerWriter<AviWriter> {

  private readonly IReadOnlyList<MediaStreamInfo> _streams;
  private readonly VideoMetadata _metadata;
  private readonly List<CodedPacket> _packets = [];
  private bool _finished;

  private AviWriter(IReadOnlyList<MediaStreamInfo> streams, VideoMetadata metadata) {
    ArgumentNullException.ThrowIfNull(streams);
    ArgumentNullException.ThrowIfNull(metadata);
    if (streams.Count == 0)
      throw new ArgumentException("AVI needs at least one stream.", nameof(streams));
    if (streams.Count > 100)
      throw new NotSupportedException("Classic AVI chunk ids carry two decimal stream digits, so at most 100 streams can be written.");

    for (var i = 0; i < streams.Count; ++i) {
      var stream = streams[i] ?? throw new ArgumentException($"Stream {i} is null.", nameof(streams));
      if (stream.Index != i)
        throw new ArgumentException($"AVI streams must be indexed densely from zero; position {i} has index {stream.Index}.", nameof(streams));
      if (stream.Kind == MediaStreamKind.Audio && stream.CodecPrivateData.IsEmpty)
        throw new NotSupportedException($"AVI audio stream {i} needs its WAVEFORMATEX bytes in CodecPrivateData.");
    }

    this._streams = streams;
    this._metadata = metadata;
  }

  public static string PrimaryExtension => ".avi";
  public static string[] FileExtensions => [".avi"];

  public static AviWriter Create(IReadOnlyList<MediaStreamInfo> streams, VideoMetadata metadata) => new(streams, metadata);

  public void WritePacket(CodedPacket packet) {
    if (this._finished)
      throw new InvalidOperationException("AVI writer has already been finished.");
    if ((uint)packet.StreamIndex >= (uint)this._streams.Count)
      throw new ArgumentOutOfRangeException(nameof(packet), packet.StreamIndex, "Packet names no declared AVI stream.");
    this._packets.Add(packet);
  }

  public byte[] Finish() {
    if (this._finished)
      throw new InvalidOperationException("AVI writer has already been finished.");
    this._finished = true;

    var packetCounts = new int[this._streams.Count];
    var largestPacket = 0;
    foreach (var packet in this._packets) {
      ++packetCounts[packet.StreamIndex];
      largestPacket = Math.Max(largestPacket, packet.Data.Length);
    }

    using var body = new MemoryStream();
    ContainerWriterTools.WriteAscii(body, "AVI ");

    ContainerWriterTools.WriteRiffList(body, "hdrl", hdrl => {
      ContainerWriterTools.WriteRiffChunk(hdrl, "avih", this._MainHeader(packetCounts, largestPacket));
      for (var i = 0; i < this._streams.Count; ++i) {
        var index = i;
        ContainerWriterTools.WriteRiffList(hdrl, "strl", strl => this._WriteStreamList(strl, this._streams[index], packetCounts[index], largestPacket));
      }
    });

    if (!this._metadata.IsEmpty)
      this._WriteInfo(body);

    using var movi = new MemoryStream();
    ContainerWriterTools.WriteAscii(movi, "movi");
    var indexEntries = new List<(string Id, uint Flags, uint Offset, uint Size)>(this._packets.Count);

    foreach (var packet in this._packets) {
      var info = this._streams[packet.StreamIndex];
      var id = $"{packet.StreamIndex:00}{_ChunkSuffix(info)}";
      var offset = checked((uint)movi.Position);
      var data = packet.Data.Span;
      ContainerWriterTools.WriteRiffChunk(movi, id, data);
      var flags = info.Kind != MediaStreamKind.Video || packet.IsKeyFrame ? 0x10u : 0u;
      indexEntries.Add((id, flags, offset, checked((uint)data.Length)));
    }

    ContainerWriterTools.WriteAscii(body, "LIST");
    ContainerWriterTools.WriteUInt32LittleEndian(body, checked((uint)movi.Length));
    movi.Position = 0;
    movi.CopyTo(body);
    if ((movi.Length & 1) != 0)
      body.WriteByte(0);

    var idx1 = ContainerWriterTools.Build(index => {
      foreach (var entry in indexEntries) {
        ContainerWriterTools.WriteAscii(index, entry.Id);
        ContainerWriterTools.WriteUInt32LittleEndian(index, entry.Flags);
        ContainerWriterTools.WriteUInt32LittleEndian(index, entry.Offset);
        ContainerWriterTools.WriteUInt32LittleEndian(index, entry.Size);
      }
    });
    ContainerWriterTools.WriteRiffChunk(body, "idx1", idx1);

    using var result = new MemoryStream();
    ContainerWriterTools.WriteAscii(result, "RIFF");
    ContainerWriterTools.WriteUInt32LittleEndian(result, checked((uint)body.Length));
    body.Position = 0;
    body.CopyTo(result);
    return result.ToArray();
  }

  private byte[] _MainHeader(int[] packetCounts, int largestPacket) {
    var video = this._streams.FirstOrDefault(s => s.Kind == MediaStreamKind.Video);
    var videoIndex = video?.Index ?? -1;
    var framePeriod = 0u;
    if (video != null) {
      var seconds = video.TimeBase.IsKnown
        ? video.TimeBase.ToDouble()
        : video.FrameRate.IsKnown ? 1d / video.FrameRate.ToDouble() : 0d;
      if (seconds > 0)
        framePeriod = checked((uint)Math.Max(1, Math.Round(seconds * 1_000_000d)));
    }

    return ContainerWriterTools.Build(header => {
      ContainerWriterTools.WriteUInt32LittleEndian(header, framePeriod);
      ContainerWriterTools.WriteUInt32LittleEndian(header, 0); // max bytes/s: no promise
      ContainerWriterTools.WriteUInt32LittleEndian(header, 0); // padding granularity
      ContainerWriterTools.WriteUInt32LittleEndian(header, 0x10); // AVIF_HASINDEX
      ContainerWriterTools.WriteUInt32LittleEndian(header, videoIndex >= 0 ? checked((uint)packetCounts[videoIndex]) : 0);
      ContainerWriterTools.WriteUInt32LittleEndian(header, 0);
      ContainerWriterTools.WriteUInt32LittleEndian(header, checked((uint)this._streams.Count));
      ContainerWriterTools.WriteUInt32LittleEndian(header, checked((uint)largestPacket));
      ContainerWriterTools.WriteUInt32LittleEndian(header, checked((uint)Math.Max(0, video?.Width ?? 0)));
      ContainerWriterTools.WriteUInt32LittleEndian(header, checked((uint)Math.Max(0, video?.Height ?? 0)));
      for (var i = 0; i < 4; ++i)
        ContainerWriterTools.WriteUInt32LittleEndian(header, 0);
    });
  }

  private void _WriteStreamList(Stream destination, MediaStreamInfo stream, int packetCount, int largestPacket) {
    var (scale, rate) = _AviRate(stream);
    var type = stream.Kind switch {
      MediaStreamKind.Video => "vids",
      MediaStreamKind.Audio => "auds",
      MediaStreamKind.Subtitle => "txts",
      _ => "data",
    };
    var handler = stream.Handler != CodecTag.None ? stream.Handler : stream.Codec;

    var strh = ContainerWriterTools.Build(header => {
      ContainerWriterTools.WriteAscii(header, type);
      ContainerWriterTools.WriteUInt32LittleEndian(header, handler.Value);
      ContainerWriterTools.WriteUInt32LittleEndian(header, 0);
      ContainerWriterTools.WriteUInt16LittleEndian(header, 0);
      ContainerWriterTools.WriteUInt16LittleEndian(header, 0);
      ContainerWriterTools.WriteUInt32LittleEndian(header, 0);
      ContainerWriterTools.WriteUInt32LittleEndian(header, scale);
      ContainerWriterTools.WriteUInt32LittleEndian(header, rate);
      ContainerWriterTools.WriteUInt32LittleEndian(header, 0);
      ContainerWriterTools.WriteUInt32LittleEndian(header, checked((uint)packetCount));
      ContainerWriterTools.WriteUInt32LittleEndian(header, checked((uint)largestPacket));
      ContainerWriterTools.WriteUInt32LittleEndian(header, 0xFFFFFFFF);
      ContainerWriterTools.WriteUInt32LittleEndian(header, 0);
      ContainerWriterTools.WriteUInt16LittleEndian(header, 0);
      ContainerWriterTools.WriteUInt16LittleEndian(header, 0);
      ContainerWriterTools.WriteUInt16LittleEndian(header, checked((ushort)Math.Min(ushort.MaxValue, Math.Max(0, stream.Width))));
      ContainerWriterTools.WriteUInt16LittleEndian(header, checked((ushort)Math.Min(ushort.MaxValue, Math.Max(0, stream.Height))));
    });
    ContainerWriterTools.WriteRiffChunk(destination, "strh", strh);

    var format = stream.CodecPrivateData.IsEmpty ? _BitmapInfoHeader(stream) : stream.CodecPrivateData.ToArray();
    ContainerWriterTools.WriteRiffChunk(destination, "strf", format);

    if (!string.IsNullOrEmpty(stream.Name)) {
      var name = Encoding.Latin1.GetBytes(stream.Name + "\0");
      ContainerWriterTools.WriteRiffChunk(destination, "strn", name);
    }
  }

  private static (uint Scale, uint Rate) _AviRate(MediaStreamInfo stream) {
    long scale;
    long rate;
    if (stream.TimeBase.IsKnown) {
      var gcd = ContainerWriterTools.GreatestCommonDivisor(stream.TimeBase.Numerator, stream.TimeBase.Denominator);
      scale = stream.TimeBase.Numerator / gcd;
      rate = stream.TimeBase.Denominator / gcd;
    } else if (stream.FrameRate.IsKnown) {
      var gcd = ContainerWriterTools.GreatestCommonDivisor(stream.FrameRate.Numerator, stream.FrameRate.Denominator);
      scale = stream.FrameRate.Denominator / gcd;
      rate = stream.FrameRate.Numerator / gcd;
    } else {
      scale = 1;
      rate = 1000;
    }

    if (scale <= 0 || rate <= 0 || scale > uint.MaxValue || rate > uint.MaxValue)
      throw new NotSupportedException($"AVI cannot represent stream {stream.Index}'s time base {stream.TimeBase} in 32-bit dwScale/dwRate.");
    return ((uint)scale, (uint)rate);
  }

  private static byte[] _BitmapInfoHeader(MediaStreamInfo stream) {
    if (stream.Kind != MediaStreamKind.Video)
      throw new NotSupportedException($"AVI stream {stream.Index} needs its format bytes in CodecPrivateData.");

    return ContainerWriterTools.Build(format => {
      ContainerWriterTools.WriteUInt32LittleEndian(format, 40);
      ContainerWriterTools.WriteInt32LittleEndian(format, stream.Width);
      ContainerWriterTools.WriteInt32LittleEndian(format, stream.Height);
      ContainerWriterTools.WriteUInt16LittleEndian(format, 1);
      ContainerWriterTools.WriteUInt16LittleEndian(format, checked((ushort)Math.Max(0, stream.BitsPerPixel)));
      ContainerWriterTools.WriteUInt32LittleEndian(format, stream.Codec.Value);
      ContainerWriterTools.WriteUInt32LittleEndian(format, 0);
      ContainerWriterTools.WriteInt32LittleEndian(format, 0);
      ContainerWriterTools.WriteInt32LittleEndian(format, 0);
      ContainerWriterTools.WriteUInt32LittleEndian(format, 0);
      ContainerWriterTools.WriteUInt32LittleEndian(format, 0);
    });
  }

  private void _WriteInfo(Stream destination) {
    var entries = new List<(string Id, string Value)>();
    _Add("INAM", this._metadata.Title);
    _Add("IART", this._metadata.Artist);
    _Add("IPRD", this._metadata.Album);
    _Add("ISFT", this._metadata.EncodedBy);
    if (this._metadata.CreationTime is { } created)
      _Add("ICRD", created.ToString("O"));
    foreach (var text in this._metadata.TextEntries)
      if (text.Keyword.Length == 4)
        _Add(text.Keyword, text.Text);

    if (entries.Count == 0)
      return;

    ContainerWriterTools.WriteRiffList(destination, "INFO", info => {
      foreach (var (id, value) in entries)
        ContainerWriterTools.WriteRiffChunk(info, id, Encoding.Latin1.GetBytes(value + "\0"));
    });
    return;

    void _Add(string id, string? value) {
      if (!string.IsNullOrEmpty(value))
        entries.Add((id, value));
    }
  }

  private static string _ChunkSuffix(MediaStreamInfo stream)
    => stream.Kind switch {
      MediaStreamKind.Video => "dc",
      MediaStreamKind.Audio => "wb",
      MediaStreamKind.Subtitle => "tx",
      _ => "dc",
    };
}
