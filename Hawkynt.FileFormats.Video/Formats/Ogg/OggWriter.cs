using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;

namespace FileFormat.Ogg;

/// <summary>Writes Ogg logical bitstreams, preserving codec header packets and granule timing.</summary>
public sealed class OggWriter : IVideoContainerWriter<OggWriter> {

  private sealed class StreamState(MediaStreamInfo info, uint serial, ReadOnlyMemory<byte>[] headers) {
    internal MediaStreamInfo Info { get; } = info;
    internal uint Serial { get; } = serial;
    internal ReadOnlyMemory<byte>[] Headers { get; } = headers;
    internal uint Sequence { get; set; }
    internal int TheoraShift { get; set; }
    internal long TheoraFrame { get; set; }
    internal long TheoraLastKey { get; set; } = -1;
  }

  private readonly record struct Stored(CodedPacket Packet, int Ordinal);

  private readonly IReadOnlyList<MediaStreamInfo> _streams;
  private readonly StreamState[] _states;
  private readonly List<Stored> _storage = [];
  private bool _finished;

  private OggWriter(IReadOnlyList<MediaStreamInfo> streams, VideoMetadata metadata) {
    ArgumentNullException.ThrowIfNull(streams);
    ArgumentNullException.ThrowIfNull(metadata);
    if (streams.Count == 0)
      throw new ArgumentException("Ogg needs at least one logical bitstream.", nameof(streams));

    this._states = new StreamState[streams.Count];
    for (var i = 0; i < streams.Count; ++i) {
      var info = streams[i] ?? throw new ArgumentException($"Bitstream {i} is null.", nameof(streams));
      if (info.Index != i)
        throw new ArgumentException($"Ogg bitstreams must be indexed densely from zero; position {i} has index {info.Index}.", nameof(streams));

      var headers = _UnpackHeaders(info.CodecPrivateData);
      if (headers.Length == 0)
        throw new NotSupportedException(
          $"Ogg bitstream {i} needs its mapping/header packets in CodecPrivateData. Without a BOS identification packet the logical bitstream cannot be declared before interleaved data begins.");

      var state = new StreamState(info, 0x504E4700u + checked((uint)i + 1), headers);
      if (info.CodecId?.Equals("theora", StringComparison.OrdinalIgnoreCase) == true) {
        var first = headers[0].Span;
        if (first.Length < 42 || first[0] != 0x80 || !first.Slice(1, 6).SequenceEqual("theora"u8))
          throw new InvalidDataException("Theora CodecPrivateData does not begin with a valid identification header.");
        state.TheoraShift = (BinaryPrimitives.ReadUInt16BigEndian(first.Slice(40, 2)) >> 5) & 0x1F;
      }
      this._states[i] = state;
    }

    this._streams = streams;
  }

  /// <summary>Gets the primary file extension for this format.</summary>
  public static string PrimaryExtension => ".ogg";
  /// <summary>Gets the file extensions supported by this format.</summary>
  public static string[] FileExtensions => [".ogg", ".ogv", ".oga", ".ogx", ".opus", ".spx"];

  /// <summary>Creates a writer for the specified stream descriptions and metadata.</summary>
  public static OggWriter Create(IReadOnlyList<MediaStreamInfo> streams, VideoMetadata metadata) => new(streams, metadata);

  /// <summary>Writes the specified coded packet to the container.</summary>
  public void WritePacket(CodedPacket packet) {
    if (this._finished)
      throw new InvalidOperationException("Ogg writer has already been finished.");
    if ((uint)packet.StreamIndex >= (uint)this._states.Length)
      throw new ArgumentOutOfRangeException(nameof(packet), packet.StreamIndex, "Packet names no declared Ogg bitstream.");
    this._storage.Add(new(packet, this._storage.Count));
  }

  /// <summary>Finishes writing the container and returns its encoded bytes.</summary>
  public byte[] Finish() {
    if (this._finished)
      throw new InvalidOperationException("Ogg writer has already been finished.");
    this._finished = true;

    using var output = new MemoryStream();

    // RFC 3533 requires every BOS page before any non-BOS page, so first headers are emitted across
    // streams first, then the remaining mapping headers.
    foreach (var state in this._states)
      this._WritePacketPages(output, state, state.Headers[0].Span, granule: 0, firstPageFlags: OggPage.FLAG_BEGIN_OF_STREAM, finalPageFlags: 0);

    foreach (var state in this._states)
      for (var i = 1; i < state.Headers.Length; ++i)
        this._WritePacketPages(output, state, state.Headers[i].Span, granule: 0, firstPageFlags: 0, finalPageFlags: 0);

    var perStream = new List<Stored>[this._states.Length];
    for (var i = 0; i < perStream.Length; ++i)
      perStream[i] = [];
    foreach (var stored in this._storage)
      perStream[stored.Packet.StreamIndex].Add(stored);

    var nextPosition = new Dictionary<int, long?>();
    var lastOrdinal = new int[this._states.Length];
    Array.Fill(lastOrdinal, -1);
    for (var streamIndex = 0; streamIndex < perStream.Length; ++streamIndex) {
      var list = perStream[streamIndex];
      if (list.Count != 0)
        lastOrdinal[streamIndex] = list[^1].Ordinal;
      for (var i = 0; i < list.Count; ++i) {
        long? next = null;
        if (i + 1 < list.Count)
          next = list[i + 1].Packet.PresentationTimestamp ?? list[i + 1].Packet.DecodeTimestamp;
        nextPosition[list[i].Ordinal] = next;
      }
    }

    foreach (var stored in this._storage) {
      var packet = stored.Packet;
      var state = this._states[packet.StreamIndex];
      var granule = _Granule(state, packet, nextPosition[stored.Ordinal]);
      var eos = stored.Ordinal == lastOrdinal[packet.StreamIndex] ? OggPage.FLAG_END_OF_STREAM : 0;
      this._WritePacketPages(output, state, packet.Data.Span, granule, 0, eos);
    }

    // A header-only logical bitstream still needs an EOS page. Use an empty packet, which terminates
    // with a zero lacing value and carries no codec bytes.
    for (var i = 0; i < this._states.Length; ++i)
      if (lastOrdinal[i] < 0)
        this._WritePacketPages(output, this._states[i], ReadOnlySpan<byte>.Empty, 0, 0, OggPage.FLAG_END_OF_STREAM);

    return output.ToArray();
  }

  private static long _Granule(StreamState state, CodedPacket packet, long? nextPosition) {
    if (state.Info.CodecId?.Equals("theora", StringComparison.OrdinalIgnoreCase) == true) {
      var frame = state.TheoraFrame++;
      if (packet.IsKeyFrame)
        state.TheoraLastKey = frame;
      if (state.TheoraLastKey < 0)
        throw new InvalidDataException("A Theora logical bitstream must begin its coded pictures at a keyframe before a granule position can be formed.");
      return ((state.TheoraLastKey + 1) << state.TheoraShift) | (frame - state.TheoraLastKey);
    }

    long position;
    if (nextPosition is { } next)
      position = next;
    else if (packet.PresentationTimestamp is { } pts && packet.Duration is { } duration)
      position = pts + duration;
    else if (packet.DecodeTimestamp is { } dts && packet.Duration is { } decodeDuration)
      position = dts + decodeDuration;
    else
      position = packet.PresentationTimestamp ?? packet.DecodeTimestamp ?? 0;

    if (state.Info.CodecId?.Equals("opus", StringComparison.OrdinalIgnoreCase) == true) {
      var head = state.Headers[0].Span;
      if (head.Length >= 12 && head[..8].SequenceEqual("OpusHead"u8))
        position += BinaryPrimitives.ReadUInt16LittleEndian(head.Slice(10, 2));
    }

    return position;
  }

  private void _WritePacketPages(
    Stream output, StreamState state, ReadOnlySpan<byte> packet, long granule,
    int firstPageFlags, int finalPageFlags) {
    var offset = 0;
    var continued = false;

    // Every packet needs a terminating lacing value below 255; an exact multiple of 255 therefore
    // has a final zero segment. Zero-length packets are represented by that zero alone.
    var totalSegments = packet.Length / 255 + 1;
    var segmentIndex = 0;
    while (segmentIndex < totalSegments) {
      var segmentCount = Math.Min(255, totalSegments - segmentIndex);
      Span<byte> lacing = stackalloc byte[segmentCount];
      var bodyLength = 0;
      for (var i = 0; i < segmentCount; ++i) {
        var global = segmentIndex + i;
        var remaining = packet.Length - global * 255;
        var value = global == totalSegments - 1 ? Math.Max(0, remaining) : Math.Min(255, Math.Max(0, remaining));
        lacing[i] = checked((byte)value);
        bodyLength += value;
      }

      var isLast = segmentIndex + segmentCount == totalSegments;
      var flags = (segmentIndex == 0 ? firstPageFlags : 0)
                  | (continued ? OggPage.FLAG_CONTINUED : 0)
                  | (isLast ? finalPageFlags : 0);
      var pageGranule = isLast ? granule : -1;
      var body = packet.Slice(offset, bodyLength);
      this._WritePage(output, state, checked((byte)flags), pageGranule, lacing, body);
      offset += bodyLength;
      segmentIndex += segmentCount;
      continued = !isLast;
    }
  }

  private void _WritePage(Stream output, StreamState state, byte flags, long granule, ReadOnlySpan<byte> lacing, ReadOnlySpan<byte> body) {
    var page = new byte[OggPage.HEADER_SIZE + lacing.Length + body.Length];
    var span = page.AsSpan();
    OggPage.CapturePattern.CopyTo(span);
    span[4] = OggPage.VERSION;
    span[5] = flags;
    BinaryPrimitives.WriteInt64LittleEndian(span.Slice(6, 8), granule);
    BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(14, 4), state.Serial);
    BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(18, 4), state.Sequence++);
    span[26] = checked((byte)lacing.Length);
    lacing.CopyTo(span[OggPage.HEADER_SIZE..]);
    body.CopyTo(span[(OggPage.HEADER_SIZE + lacing.Length)..]);
    BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(OggCrc.CHECKSUM_AT, 4), OggCrc.Compute(span));
    output.Write(page);
  }

  private static ReadOnlyMemory<byte>[] _UnpackHeaders(ReadOnlyMemory<byte> packed) {
    var data = packed.Span;
    if (data.IsEmpty)
      return [];

    var count = data[0] + 1;
    var lengths = new int[count];
    var at = 1;
    var stated = 0;
    for (var i = 0; i < count - 1; ++i) {
      var length = 0;
      while (true) {
        if (at >= data.Length)
          throw new InvalidDataException("Ogg CodecPrivateData ends inside its Xiph lacing table.");
        var value = data[at++];
        length += value;
        if (value != 255)
          break;
      }
      lengths[i] = length;
      stated += length;
    }

    var last = data.Length - at - stated;
    if (last < 0)
      throw new InvalidDataException("Ogg CodecPrivateData's Xiph lacing lengths exceed the bytes supplied.");
    lengths[^1] = last;

    var result = new ReadOnlyMemory<byte>[count];
    var payloadAt = at;
    for (var i = 0; i < count; ++i) {
      result[i] = packed.Slice(payloadAt, lengths[i]);
      payloadAt += lengths[i];
    }
    return result;
  }
}
