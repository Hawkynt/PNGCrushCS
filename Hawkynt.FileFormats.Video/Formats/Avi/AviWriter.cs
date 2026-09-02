using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;

namespace FileFormat.Avi;

/// <summary>Writes AVI with OpenDML indexes, using AVIX RIFF extensions when one RIFF would exceed the OpenDML size limit.</summary>
public sealed class AviWriter : IVideoContainerWriter<AviWriter> {

  private const int _DEFAULT_MAX_RIFF_SIZE = 1 << 30;

  private readonly record struct IndexEntry(int StreamIndex, string Id, uint Flags, uint Offset, uint Size);
  private readonly record struct SuperIndexEntry(ulong Offset, uint Size, uint Duration);
  private readonly record struct SegmentRange(int StartPacket, int PacketCount, long PacketBytes, int[] StreamCounts);

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

  public byte[] Finish() => this._Finish(_DEFAULT_MAX_RIFF_SIZE);

  internal byte[] Finish(int maxRiffSize) => this._Finish(maxRiffSize);

  private byte[] _Finish(int maxRiffSize) {
    if (this._finished)
      throw new InvalidOperationException("AVI writer has already been finished.");
    this._finished = true;
    if (maxRiffSize < 12)
      throw new ArgumentOutOfRangeException(nameof(maxRiffSize), maxRiffSize, "A RIFF chunk must at least contain its header and form type.");

    var packetCounts = new int[this._streams.Count];
    var largestPacket = 0;
    var packetChunkSizes = new long[this._packets.Count];
    long packetBytes = 0;
    for (var i = 0; i < this._packets.Count; ++i) {
      var packet = this._packets[i];
      ++packetCounts[packet.StreamIndex];
      largestPacket = Math.Max(largestPacket, packet.Data.Length);
      var serializedSize = _PacketChunkSize(packet);
      packetChunkSizes[i] = serializedSize;
      packetBytes = checked(packetBytes + serializedSize);
    }

    var totalFrames = this._VideoFrames(packetCounts);
    var sizingEntries = this._BuildIndexEntries(0, this._packets.Count, includeOffsets: false);
    var sizingPrefix = this._BuildPrefix(
      packetCounts,
      largestPacket,
      totalFrames,
      totalFrames,
      sizingEntries,
      0,
      null
    );
    var directLength = checked(8L + sizingPrefix.Length + 12L + packetBytes + 8L + 16L * this._packets.Count);
    if (directLength <= maxRiffSize)
      return this._FinishDirect(packetCounts, largestPacket, packetBytes, totalFrames, sizingPrefix.Length);

    return this._FinishSegmented(packetCounts, largestPacket, packetChunkSizes, totalFrames, maxRiffSize);
  }

  private byte[] _FinishDirect(int[] packetCounts, int largestPacket, long packetBytes, uint totalFrames, int prefixLength) {
    var entries = this._BuildIndexEntries(0, this._packets.Count, includeOffsets: true);
    var moviBaseOffset = checked((ulong)prefixLength + 16UL); // RIFF header + LIST header; points at the 'movi' list type.
    var prefix = this._BuildPrefix(
      packetCounts,
      largestPacket,
      totalFrames,
      totalFrames,
      entries,
      moviBaseOffset,
      null
    );
    if (prefix.Length != prefixLength)
      throw new InvalidOperationException("AVI header size changed while resolving its absolute OpenDML offsets.");

    var segment = new SegmentRange(0, this._packets.Count, packetBytes, packetCounts);
    var totalLength = checked(8L + prefix.Length + 12L + packetBytes + 8L + 16L * this._packets.Count);
    if (totalLength > int.MaxValue)
      throw new NotSupportedException("The in-memory AVI writer returns one byte array and cannot represent a file larger than Int32.MaxValue bytes.");

    using var result = new MemoryStream((int)totalLength);
    ContainerWriterTools.WriteAscii(result, "RIFF");
    ContainerWriterTools.WriteUInt32LittleEndian(result, checked((uint)(totalLength - 8)));
    result.Write(prefix);
    this._WriteMovieList(result, segment, entries, moviBaseOffset, writeStandardIndexes: false);
    this._WriteLegacyIndex(result, entries);
    if (result.Length != totalLength)
      throw new InvalidOperationException("AVI direct-index size planning disagreed with the bytes that were written.");
    return result.ToArray();
  }

  private byte[] _FinishSegmented(
    int[] packetCounts,
    int largestPacket,
    long[] packetChunkSizes,
    uint totalFrames,
    int maxRiffSize
  ) {
    if (this._packets.Count == 0)
      throw new NotSupportedException($"The AVI header itself exceeds the configured RIFF limit of {maxRiffSize} bytes.");

    var segmentCount = 2;
    SegmentRange[] segments;
    int prefixLength;
    for (;;) {
      var placeholders = this._EmptySuperIndexes(segmentCount);
      var prefix = this._BuildPrefix(packetCounts, largestPacket, 0, totalFrames, null, 0, placeholders);
      prefixLength = prefix.Length;
      segments = this._PlanSegments(packetChunkSizes, prefixLength, maxRiffSize);
      if (segments.Length == segmentCount)
        break;
      segmentCount = segments.Length;
    }

    var (superIndexes, totalLength) = this._BuildSuperIndexes(segments, prefixLength);
    if (totalLength > int.MaxValue)
      throw new NotSupportedException("The in-memory AVI writer returns one byte array and cannot represent a file larger than Int32.MaxValue bytes.");

    var firstRiffFrames = this._VideoFrames(segments[0].StreamCounts);
    var prefixBytes = this._BuildPrefix(
      packetCounts,
      largestPacket,
      firstRiffFrames,
      totalFrames,
      null,
      0,
      superIndexes
    );
    if (prefixBytes.Length != prefixLength)
      throw new InvalidOperationException("AVI super-index header size changed while resolving its absolute index offsets.");

    using var result = new MemoryStream((int)totalLength);
    long fileOffset = 0;
    for (var i = 0; i < segments.Length; ++i) {
      var segment = segments[i];
      var segmentLength = this._SegmentLength(segment, prefixLength, i == 0);
      var moviBaseOffset = i == 0
        ? checked((ulong)prefixLength + 16UL)
        : checked((ulong)fileOffset + 20UL);
      var entries = this._BuildIndexEntries(segment.StartPacket, segment.PacketCount, includeOffsets: true);

      ContainerWriterTools.WriteAscii(result, "RIFF");
      ContainerWriterTools.WriteUInt32LittleEndian(result, checked((uint)(segmentLength - 8)));
      if (i == 0)
        result.Write(prefixBytes);
      else
        ContainerWriterTools.WriteAscii(result, "AVIX");

      this._WriteMovieList(result, segment, entries, moviBaseOffset, writeStandardIndexes: true);
      if (i == 0)
        this._WriteLegacyIndex(result, entries);

      fileOffset = checked(fileOffset + segmentLength);
      if (result.Position != fileOffset)
        throw new InvalidOperationException($"AVI RIFF segment {i} size planning disagreed with the bytes that were written.");
    }

    return result.ToArray();
  }

  private SegmentRange[] _PlanSegments(long[] packetChunkSizes, int prefixLength, int maxRiffSize) {
    var result = new List<SegmentRange>();
    for (var start = 0; start < this._packets.Count;) {
      var first = result.Count == 0;
      var counts = new int[this._streams.Count];
      long bytes = 0;
      var count = 0;

      while (start + count < this._packets.Count) {
        var packet = this._packets[start + count];
        var nextBytes = checked(bytes + packetChunkSizes[start + count]);
        var nextCount = count + 1;
        var fixedIndexBytes = checked(32L * this._streams.Count + 8L * nextCount);
        var candidateLength = first
          ? checked(28L + prefixLength + nextBytes + fixedIndexBytes + 16L * nextCount)
          : checked(24L + nextBytes + fixedIndexBytes);
        if (candidateLength > maxRiffSize)
          break;

        bytes = nextBytes;
        ++counts[packet.StreamIndex];
        count = nextCount;
      }

      if (count == 0)
        throw new NotSupportedException(
          $"AVI packet {start} cannot fit in a {maxRiffSize}-byte OpenDML RIFF segment together with the required indexes.");

      result.Add(new(start, count, bytes, counts));
      start += count;
    }

    return [.. result];
  }

  private (SuperIndexEntry[][] Indexes, long TotalLength) _BuildSuperIndexes(IReadOnlyList<SegmentRange> segments, int prefixLength) {
    var indexes = this._EmptySuperIndexes(segments.Count);
    long fileOffset = 0;
    for (var segmentIndex = 0; segmentIndex < segments.Count; ++segmentIndex) {
      var segment = segments[segmentIndex];
      var moviBaseOffset = segmentIndex == 0
        ? checked((ulong)prefixLength + 16UL)
        : checked((ulong)fileOffset + 20UL);
      var indexOffset = checked(moviBaseOffset + 4UL + (ulong)segment.PacketBytes);

      for (var streamIndex = 0; streamIndex < this._streams.Count; ++streamIndex) {
        var entryCount = segment.StreamCounts[streamIndex];
        var indexSize = checked((uint)(32L + 8L * entryCount));
        indexes[streamIndex][segmentIndex] = new(indexOffset, indexSize, checked((uint)entryCount));
        indexOffset = checked(indexOffset + indexSize);
      }

      fileOffset = checked(fileOffset + this._SegmentLength(segment, prefixLength, segmentIndex == 0));
    }

    return (indexes, fileOffset);
  }

  private long _SegmentLength(SegmentRange segment, int prefixLength, bool first)
    => first
      ? checked(28L + prefixLength + segment.PacketBytes + 32L * this._streams.Count + 24L * segment.PacketCount)
      : checked(24L + segment.PacketBytes + 32L * this._streams.Count + 8L * segment.PacketCount);

  private SuperIndexEntry[][] _EmptySuperIndexes(int segmentCount) {
    var result = new SuperIndexEntry[this._streams.Count][];
    for (var i = 0; i < result.Length; ++i)
      result[i] = new SuperIndexEntry[segmentCount];
    return result;
  }

  private List<IndexEntry> _BuildIndexEntries(int startPacket, int packetCount, bool includeOffsets) {
    var result = new List<IndexEntry>(packetCount);
    long offset = 4;
    for (var i = 0; i < packetCount; ++i) {
      var packet = this._packets[startPacket + i];
      var info = this._streams[packet.StreamIndex];
      var id = $"{packet.StreamIndex:00}{_ChunkSuffix(info)}";
      var flags = info.Kind != MediaStreamKind.Video || packet.IsKeyFrame ? 0x10u : 0u;
      result.Add(new(
        packet.StreamIndex,
        id,
        flags,
        includeOffsets ? checked((uint)offset) : 0,
        checked((uint)packet.Data.Length)
      ));
      offset = checked(offset + _PacketChunkSize(packet));
    }
    return result;
  }

  private void _WriteMovieList(
    Stream destination,
    SegmentRange segment,
    IReadOnlyList<IndexEntry> entries,
    ulong moviBaseOffset,
    bool writeStandardIndexes
  ) {
    var indexBytes = writeStandardIndexes
      ? checked(32L * this._streams.Count + 8L * segment.PacketCount)
      : 0L;
    var listSize = checked(4L + segment.PacketBytes + indexBytes);
    ContainerWriterTools.WriteAscii(destination, "LIST");
    ContainerWriterTools.WriteUInt32LittleEndian(destination, checked((uint)listSize));
    ContainerWriterTools.WriteAscii(destination, "movi");

    for (var i = 0; i < segment.PacketCount; ++i) {
      var packet = this._packets[segment.StartPacket + i];
      ContainerWriterTools.WriteRiffChunk(destination, entries[i].Id, packet.Data.Span);
    }

    if (!writeStandardIndexes)
      return;

    for (var streamIndex = 0; streamIndex < this._streams.Count; ++streamIndex)
      this._WriteStandardIndex(
        destination,
        $"ix{streamIndex:00}",
        this._streams[streamIndex],
        segment.StreamCounts[streamIndex],
        entries,
        moviBaseOffset
      );
  }

  private void _WriteLegacyIndex(Stream destination, IReadOnlyList<IndexEntry> entries) {
    var idx1 = ContainerWriterTools.Build(index => {
      foreach (var entry in entries) {
        ContainerWriterTools.WriteAscii(index, entry.Id);
        ContainerWriterTools.WriteUInt32LittleEndian(index, entry.Flags);
        ContainerWriterTools.WriteUInt32LittleEndian(index, entry.Offset);
        ContainerWriterTools.WriteUInt32LittleEndian(index, entry.Size);
      }
    });
    ContainerWriterTools.WriteRiffChunk(destination, "idx1", idx1);
  }

  private byte[] _BuildPrefix(
    int[] packetCounts,
    int largestPacket,
    uint firstRiffFrames,
    uint totalFrames,
    IReadOnlyList<IndexEntry>? directIndexEntries,
    ulong moviBaseOffset,
    IReadOnlyList<SuperIndexEntry[]>? superIndexes
  ) => ContainerWriterTools.Build(body => {
    ContainerWriterTools.WriteAscii(body, "AVI ");

    ContainerWriterTools.WriteRiffList(body, "hdrl", hdrl => {
      ContainerWriterTools.WriteRiffChunk(hdrl, "avih", this._MainHeader(largestPacket, firstRiffFrames));
      for (var i = 0; i < this._streams.Count; ++i) {
        var index = i;
        var superIndexEntries = superIndexes == null ? null : superIndexes[index];
        ContainerWriterTools.WriteRiffList(hdrl, "strl", strl => this._WriteStreamList(
          strl,
          this._streams[index],
          packetCounts[index],
          largestPacket,
          directIndexEntries,
          moviBaseOffset,
          superIndexEntries
        ));
      }

      ContainerWriterTools.WriteRiffList(hdrl, "odml", odml => {
        var dmlh = ContainerWriterTools.Build(header => ContainerWriterTools.WriteUInt32LittleEndian(header, totalFrames));
        ContainerWriterTools.WriteRiffChunk(odml, "dmlh", dmlh);
      });
    });

    if (!this._metadata.IsEmpty)
      this._WriteInfo(body);
  });

  private uint _VideoFrames(IReadOnlyList<int> packetCounts) {
    var video = this._streams.FirstOrDefault(stream => stream.Kind == MediaStreamKind.Video);
    return video == null ? 0 : checked((uint)packetCounts[video.Index]);
  }

  private byte[] _MainHeader(int largestPacket, uint firstRiffFrames) {
    var video = this._streams.FirstOrDefault(s => s.Kind == MediaStreamKind.Video);
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
      ContainerWriterTools.WriteUInt32LittleEndian(header, firstRiffFrames);
      ContainerWriterTools.WriteUInt32LittleEndian(header, 0);
      ContainerWriterTools.WriteUInt32LittleEndian(header, checked((uint)this._streams.Count));
      ContainerWriterTools.WriteUInt32LittleEndian(header, checked((uint)largestPacket));
      ContainerWriterTools.WriteUInt32LittleEndian(header, checked((uint)Math.Max(0, video?.Width ?? 0)));
      ContainerWriterTools.WriteUInt32LittleEndian(header, checked((uint)Math.Max(0, video?.Height ?? 0)));
      for (var i = 0; i < 4; ++i)
        ContainerWriterTools.WriteUInt32LittleEndian(header, 0);
    });
  }

  private void _WriteStreamList(
    Stream destination,
    MediaStreamInfo stream,
    int packetCount,
    int largestPacket,
    IReadOnlyList<IndexEntry>? directIndexEntries,
    ulong moviBaseOffset,
    IReadOnlyList<SuperIndexEntry>? superIndexEntries
  ) {
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
    if (superIndexEntries != null)
      this._WriteSuperIndex(destination, stream, superIndexEntries);
    else if (directIndexEntries != null)
      this._WriteStandardIndex(destination, "indx", stream, packetCount, directIndexEntries, moviBaseOffset);
    else
      throw new InvalidOperationException("AVI stream header has neither a direct OpenDML index nor a super index.");

    if (!string.IsNullOrEmpty(stream.Name)) {
      var name = Encoding.Latin1.GetBytes(stream.Name + "\0");
      ContainerWriterTools.WriteRiffChunk(destination, "strn", name);
    }
  }

  private void _WriteStandardIndex(
    Stream destination,
    string indexChunkId,
    MediaStreamInfo stream,
    int packetCount,
    IReadOnlyList<IndexEntry> indexEntries,
    ulong moviBaseOffset
  ) {
    var chunkId = $"{stream.Index:00}{_ChunkSuffix(stream)}";
    var index = ContainerWriterTools.Build(body => {
      ContainerWriterTools.WriteUInt16LittleEndian(body, 2); // two DWORDs per AVISTDINDEX entry
      body.WriteByte(0); // bIndexSubType
      body.WriteByte(1); // AVI_INDEX_OF_CHUNKS
      ContainerWriterTools.WriteUInt32LittleEndian(body, checked((uint)packetCount));
      ContainerWriterTools.WriteAscii(body, chunkId);
      ContainerWriterTools.WriteUInt64LittleEndian(body, moviBaseOffset);
      ContainerWriterTools.WriteUInt32LittleEndian(body, 0); // dwReserved3

      foreach (var entry in indexEntries) {
        if (entry.StreamIndex != stream.Index)
          continue;

        // The OpenDML offset targets the chunk payload itself. The movi-relative offset captured
        // before writing the RIFF chunk names its header, so skip that eight-byte header here.
        ContainerWriterTools.WriteUInt32LittleEndian(body, checked(entry.Offset + 8));
        var isNonKeyVideoFrame = stream.Kind == MediaStreamKind.Video && (entry.Flags & 0x10) == 0;
        ContainerWriterTools.WriteUInt32LittleEndian(body, entry.Size | (isNonKeyVideoFrame ? 0x80000000u : 0));
      }
    });

    ContainerWriterTools.WriteRiffChunk(destination, indexChunkId, index);
  }

  private void _WriteSuperIndex(Stream destination, MediaStreamInfo stream, IReadOnlyList<SuperIndexEntry> entries) {
    var chunkId = $"{stream.Index:00}{_ChunkSuffix(stream)}";
    var index = ContainerWriterTools.Build(body => {
      ContainerWriterTools.WriteUInt16LittleEndian(body, 4); // four DWORDs per AVISUPERINDEX entry
      body.WriteByte(0); // bIndexSubType: standard frame index
      body.WriteByte(0); // AVI_INDEX_OF_INDEXES
      ContainerWriterTools.WriteUInt32LittleEndian(body, checked((uint)entries.Count));
      ContainerWriterTools.WriteAscii(body, chunkId);
      for (var i = 0; i < 3; ++i)
        ContainerWriterTools.WriteUInt32LittleEndian(body, 0);

      foreach (var entry in entries) {
        ContainerWriterTools.WriteUInt64LittleEndian(body, entry.Offset);
        ContainerWriterTools.WriteUInt32LittleEndian(body, entry.Size);
        ContainerWriterTools.WriteUInt32LittleEndian(body, entry.Duration);
      }
    });
    ContainerWriterTools.WriteRiffChunk(destination, "indx", index);
  }

  private static long _PacketChunkSize(CodedPacket packet)
    => checked(8L + packet.Data.Length + (packet.Data.Length & 1));

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
