using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;

namespace FileFormat.SmackerVideo;

/// <summary>Writes Smacker headers, frame tables and blobs from demux-shaped video/audio packets.</summary>
public sealed class SmackerWriter : IVideoContainerWriter<SmackerWriter> {

  private const int _HEADER_LENGTH = 104;
  private readonly IReadOnlyList<MediaStreamInfo> _streams;
  private readonly List<CodedPacket> _packets = [];
  private readonly int[] _audioTrackByStream;
  private bool _finished;

  private SmackerWriter(IReadOnlyList<MediaStreamInfo> streams, VideoMetadata metadata) {
    ArgumentNullException.ThrowIfNull(streams);
    ArgumentNullException.ThrowIfNull(metadata);
    if (streams.Count == 0 || streams[0].Index != 0 || streams[0].Kind != MediaStreamKind.Video
        || !(streams[0].Codec.EqualsIgnoringCase(CodecTag.FromCharacters("SMK2")) || streams[0].Codec.EqualsIgnoringCase(CodecTag.FromCharacters("SMK4"))))
      throw new NotSupportedException("Smacker needs SMK2/SMK4 video at stream 0.");
    if (streams[0].CodecPrivateData.Length < 16)
      throw new NotSupportedException("Smacker video CodecPrivateData must contain four table sizes followed by the packed Huffman trees.");
    if (streams.Count > 8)
      throw new NotSupportedException("Smacker carries at most seven audio tracks beside video.");

    this._audioTrackByStream = new int[streams.Count];
    Array.Fill(this._audioTrackByStream, -1);
    var physicalTrack = 0;
    for (var i = 1; i < streams.Count; ++i) {
      var stream = streams[i];
      if (stream.Index != i || stream.Kind != MediaStreamKind.Audio || !stream.Codec.EqualsIgnoringCase(CodecTag.FromCharacters("SMKA")))
        throw new NotSupportedException($"Smacker stream {i} must be SMKA audio.");
      if (stream.CodecPrivateData.Length < 4)
        throw new NotSupportedException($"Smacker audio stream {i} needs its original AudioRate dword in CodecPrivateData.");
      this._audioTrackByStream[i] = physicalTrack++;
    }

    this._streams = streams;
  }

  /// <summary>Gets the primary file extension for this format.</summary>
  public static string PrimaryExtension => ".smk";
  /// <summary>Gets the file extensions supported by this format.</summary>
  public static string[] FileExtensions => [".smk"];
  /// <summary>Creates a writer for the specified stream descriptions and metadata.</summary>
  public static SmackerWriter Create(IReadOnlyList<MediaStreamInfo> streams, VideoMetadata metadata) => new(streams, metadata);

  /// <summary>Writes the specified coded packet to the container.</summary>
  public void WritePacket(CodedPacket packet) {
    if (this._finished) throw new InvalidOperationException("Smacker writer has already been finished.");
    if ((uint)packet.StreamIndex >= (uint)this._streams.Count) throw new ArgumentOutOfRangeException(nameof(packet));
    this._packets.Add(packet);
  }

  /// <summary>Finishes writing the container and returns its encoded bytes.</summary>
  public byte[] Finish() {
    if (this._finished) throw new InvalidOperationException("Smacker writer has already been finished.");
    this._finished = true;

    var frames = new List<(byte Type, byte[] Blob)>();
    var pendingAudio = new Dictionary<int, ReadOnlyMemory<byte>>();

    foreach (var packet in this._packets) {
      if (packet.StreamIndex != 0) {
        if (pendingAudio.ContainsKey(packet.StreamIndex))
          throw new InvalidDataException($"Two Smacker audio packets for stream {packet.StreamIndex} arrived before the next video frame.");
        pendingAudio[packet.StreamIndex] = packet.Data;
        continue;
      }

      var video = packet.Data;
      if (video.IsEmpty)
        throw new InvalidDataException("Smacker video packet is missing its frame-type byte.");
      var type = video.Span[0];
      var at = 1;
      var palette = ReadOnlyMemory<byte>.Empty;
      if ((type & 1) != 0) {
        if (at >= video.Length)
          throw new InvalidDataException("Smacker frame says it has a palette but the video packet contains none.");
        var length = video.Span[at] * 4;
        if (length == 0 || at + length > video.Length)
          throw new InvalidDataException("Smacker palette chunk length runs past its video packet.");
        palette = video.Slice(at, length);
        at += length;
      }

      var videoPayload = video[at..];
      var blob = ContainerWriterTools.Build(frame => {
        if (!palette.IsEmpty)
          frame.Write(palette.Span);
        for (var streamIndex = 1; streamIndex < this._streams.Count; ++streamIndex) {
          var physical = this._audioTrackByStream[streamIndex];
          var bit = 2 << physical;
          var expected = (type & bit) != 0;
          if (!pendingAudio.TryGetValue(streamIndex, out var audio)) {
            if (expected)
              throw new InvalidDataException($"Smacker frame type requires audio stream {streamIndex}, but no packet arrived for this frame.");
            continue;
          }
          if (!expected)
            throw new InvalidDataException($"Smacker audio stream {streamIndex} arrived, but the following video's frame-type byte does not mark that track present.");
          ContainerWriterTools.WriteUInt32LittleEndian(frame, checked((uint)audio.Length + 4));
          frame.Write(audio.Span);
        }
        frame.Write(videoPayload.Span);
      });
      frames.Add((type, blob));
      pendingAudio.Clear();
    }

    if (pendingAudio.Count != 0)
      throw new InvalidDataException("Smacker file ends with audio packets that have no following video frame.");
    if (frames.Count == 0)
      throw new InvalidDataException("Smacker needs at least one video frame.");

    var videoInfo = this._streams[0];
    var privateData = videoInfo.CodecPrivateData.Span;
    var treesSize = privateData.Length - 16;

    using var output = new MemoryStream();
    var header = new byte[_HEADER_LENGTH];
    BinaryPrimitives.WriteUInt32LittleEndian(header, videoInfo.Codec.Value);
    BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), checked((uint)videoInfo.Width));
    BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(8), checked((uint)videoInfo.Height));
    BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(12), checked((uint)frames.Count));
    BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(16), _FrameRateField(videoInfo));
    BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(20), 0); // no ring frame
    BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(52), checked((uint)treesSize));
    privateData[..16].CopyTo(header.AsSpan(56, 16));
    for (var streamIndex = 1; streamIndex < this._streams.Count; ++streamIndex) {
      var physical = this._audioTrackByStream[streamIndex];
      var rate = BinaryPrimitives.ReadUInt32LittleEndian(this._streams[streamIndex].CodecPrivateData.Span);
      BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(72 + physical * 4), rate);
    }
    output.Write(header);

    foreach (var frame in frames)
      ContainerWriterTools.WriteUInt32LittleEndian(output, checked((uint)frame.Blob.Length));
    foreach (var frame in frames)
      output.WriteByte(frame.Type);
    output.Write(privateData[16..]);
    foreach (var frame in frames)
      output.Write(frame.Blob);

    return output.ToArray();
  }

  private static int _FrameRateField(MediaStreamInfo video) {
    var seconds = video.TimeBase.IsKnown
      ? video.TimeBase.ToDouble()
      : video.FrameRate.IsKnown ? 1d / video.FrameRate.ToDouble() : 0d;
    if (seconds <= 0)
      throw new NotSupportedException("Smacker needs a known video time base or frame rate.");
    var milliseconds = seconds * 1000d;
    var whole = Math.Round(milliseconds);
    if (Math.Abs(milliseconds - whole) < 1e-9 && whole is >= 1 and <= int.MaxValue)
      return checked((int)whole);
    var hundredths = Math.Round(seconds * 100_000d);
    if (hundredths is < 1 or > int.MaxValue)
      throw new NotSupportedException("Smacker frame period does not fit either header timing spelling.");
    return -checked((int)hundredths);
  }
}
