using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;

namespace FileFormat.Vmd;

/// <summary>Writes classic 816-byte Sierra VMD files with a real fixed-stride block/part table.</summary>
public sealed class VmdWriter : IVideoContainerWriter<VmdWriter> {

  private const int _HEADER_LENGTH = 816;
  private const int _FRAME_RECORD_LENGTH = 16;
  private const int _BLOCK_RECORD_LENGTH = 6;
  private const int _OFFSET_NUM_BLOCKS = 6;
  private const int _OFFSET_WIDTH = 12;
  private const int _OFFSET_HEIGHT = 14;
  private const int _OFFSET_FLAGS = 16;
  private const int _OFFSET_FRAMES_PER_BLOCK = 18;
  private const int _OFFSET_MULTIMEDIA_DATA = 20;
  private const int _OFFSET_VIDEO_CODEC = 24;
  private const int _OFFSET_AUDIO_SAMPLE_RATE = 804;
  private const int _OFFSET_AUDIO_FRAME_LENGTH = 806;
  private const int _OFFSET_TOC = 812;
  private const ushort _FLAG_HAS_SOUND = 0x1000;
  private const byte _TYPE_AUDIO = 1;
  private const byte _TYPE_VIDEO = 2;

  private readonly IReadOnlyList<MediaStreamInfo> _streams;
  private readonly List<CodedPacket> _packets = [];
  private readonly bool _isIndeo3;
  private bool _finished;

  private readonly record struct _Part(byte[] Record, ReadOnlyMemory<byte> Payload, int StreamIndex);

  private VmdWriter(IReadOnlyList<MediaStreamInfo> streams, VideoMetadata metadata) {
    ArgumentNullException.ThrowIfNull(streams);
    ArgumentNullException.ThrowIfNull(metadata);
    if (streams.Count is < 1 or > 2)
      throw new NotSupportedException("Classic VMD contains one video stream and at most one audio stream.");

    var video = streams[0];
    var native = video.Codec.EqualsIgnoringCase(CodecTag.FromCharacters("VMDV"));
    var indeo3 = video.Codec.EqualsIgnoringCase(CodecTag.FromCharacters("IV31"))
      || video.Codec.EqualsIgnoringCase(CodecTag.FromCharacters("IV32"));
    if (video.Index != 0 || video.Kind != MediaStreamKind.Video || !(native || indeo3))
      throw new NotSupportedException("VMD stream zero must be native VMDV video or embedded Indeo 3.");
    if (video.CodecPrivateData.Length != _HEADER_LENGTH)
      throw new NotSupportedException(
        "VMD muxing needs the original or encoder-produced classic 816-byte VMD header in video CodecPrivateData.");

    if (streams.Count == 2) {
      var audio = streams[1];
      if (audio.Index != 1 || audio.Kind != MediaStreamKind.Audio
          || !audio.Codec.EqualsIgnoringCase(CodecTag.FromCharacters("VMDA")))
        throw new NotSupportedException("VMD's optional second stream must be VMDA audio at index one.");
    }

    this._streams = streams;
    this._isIndeo3 = indeo3;
  }

  public static string PrimaryExtension => ".vmd";
  public static string[] FileExtensions => [".vmd"];

  public static VmdWriter Create(IReadOnlyList<MediaStreamInfo> streams, VideoMetadata metadata) => new(streams, metadata);

  public void WritePacket(CodedPacket packet) {
    if (this._finished)
      throw new InvalidOperationException("VMD writer has already been finished.");
    if ((uint)packet.StreamIndex >= (uint)this._streams.Count)
      throw new ArgumentOutOfRangeException(nameof(packet), packet.StreamIndex, "Packet names no declared VMD stream.");

    if (!(this._isIndeo3 && packet.StreamIndex == 0)) {
      if (packet.Data.Length < _FRAME_RECORD_LENGTH)
        throw new InvalidDataException(
          "A native VMD packet must carry its sixteen-byte frame-information record before its coded bytes.");
      var expected = packet.StreamIndex == 0 ? _TYPE_VIDEO : _TYPE_AUDIO;
      if (packet.Data.Span[0] != expected)
        throw new InvalidDataException(
          $"VMD stream {packet.StreamIndex} packet carries record type {packet.Data.Span[0]}, expected {expected}.");
    }

    this._packets.Add(packet);
  }

  public byte[] Finish() {
    if (this._finished)
      throw new InvalidOperationException("VMD writer has already been finished.");
    this._finished = true;
    if (this._packets.Count == 0)
      throw new InvalidDataException("VMD needs at least one frame-information record.");

    var header = this._streams[0].CodecPrivateData.ToArray();
    BinaryPrimitives.WriteUInt16LittleEndian(header, 814);
    BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(_OFFSET_MULTIMEDIA_DATA), _HEADER_LENGTH);
    if (this._streams[0].Width > 0)
      BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(_OFFSET_WIDTH), checked((ushort)this._streams[0].Width));
    if (this._streams[0].Height > 0)
      BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(_OFFSET_HEIGHT), checked((ushort)this._streams[0].Height));

    if (this._isIndeo3) {
      var tag = this._streams[0].Codec.Value;
      BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(_OFFSET_VIDEO_CODEC), tag);
    }

    if (this._streams.Count == 2) {
      var flags = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(_OFFSET_FLAGS));
      BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(_OFFSET_FLAGS), (ushort)(flags | _FLAG_HAS_SOUND));
      var sampleRate = _SampleRate(this._streams[1]);
      if (sampleRate > 0)
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(_OFFSET_AUDIO_SAMPLE_RATE), checked((ushort)sampleRate));
    } else {
      var flags = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(_OFFSET_FLAGS));
      BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(_OFFSET_FLAGS), (ushort)(flags & ~_FLAG_HAS_SOUND));
      BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(_OFFSET_AUDIO_SAMPLE_RATE), 0);
      BinaryPrimitives.WriteInt16LittleEndian(header.AsSpan(_OFFSET_AUDIO_FRAME_LENGTH), 0);
    }

    var blocks = this._BuildBlocks(header);
    var framesPerBlock = blocks.Max(static block => block.Count);
    if (blocks.Count > ushort.MaxValue || framesPerBlock > ushort.MaxValue)
      throw new NotSupportedException("VMD's block count and fixed parts-per-block count are sixteen-bit fields.");

    using var output = new MemoryStream();
    output.Write(header);

    var blockOffsets = new uint[blocks.Count];
    foreach (var (block, index) in blocks.Select(static (value, index) => (value, index))) {
      blockOffsets[index] = checked((uint)output.Position);
      foreach (var part in block)
        output.Write(part.Payload.Span);
    }

    var tocOffset = checked((uint)output.Position);
    foreach (var offset in blockOffsets) {
      ContainerWriterTools.WriteUInt16LittleEndian(output, 0);
      ContainerWriterTools.WriteUInt32LittleEndian(output, offset);
    }

    var emptyRecord = new byte[_FRAME_RECORD_LENGTH];
    foreach (var block in blocks) {
      foreach (var part in block)
        output.Write(part.Record);
      for (var part = block.Count; part < framesPerBlock; ++part)
        output.Write(emptyRecord);
    }

    var bytes = output.ToArray();
    BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(_OFFSET_NUM_BLOCKS), checked((ushort)blocks.Count));
    BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(_OFFSET_FRAMES_PER_BLOCK), checked((ushort)framesPerBlock));
    BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(_OFFSET_TOC), tocOffset);
    return bytes;
  }

  private List<List<_Part>> _BuildBlocks(ReadOnlySpan<byte> header) {
    var blocks = new List<List<_Part>>();
    var current = new List<_Part>();
    var currentHasVideo = false;

    foreach (var packet in this._packets) {
      var isVideo = packet.StreamIndex == 0;
      if (isVideo && currentHasVideo) {
        blocks.Add(current);
        current = [];
        currentHasVideo = false;
      }

      current.Add(this._PartOf(packet, header));
      currentHasVideo |= isVideo;
    }

    if (current.Count != 0)
      blocks.Add(current);
    return blocks;
  }

  private _Part _PartOf(CodedPacket packet, ReadOnlySpan<byte> header) {
    if (this._isIndeo3 && packet.StreamIndex == 0) {
      var record = new byte[_FRAME_RECORD_LENGTH];
      record[0] = _TYPE_VIDEO;
      BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(2), checked((uint)packet.Data.Length));
      var left = BinaryPrimitives.ReadUInt16LittleEndian(header[10..]);
      var top = BinaryPrimitives.ReadUInt16LittleEndian(header[8..]);
      BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(6), left);
      BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(8), top);
      BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(10), checked((ushort)(left + this._streams[0].Width - 1)));
      BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(12), checked((ushort)(top + this._streams[0].Height - 1)));
      return new(record, packet.Data, packet.StreamIndex);
    }

    var payload = packet.Data[_FRAME_RECORD_LENGTH..];
    var copiedRecord = packet.Data[.._FRAME_RECORD_LENGTH].ToArray();
    copiedRecord[0] = packet.StreamIndex == 0 ? _TYPE_VIDEO : _TYPE_AUDIO;
    BinaryPrimitives.WriteUInt32LittleEndian(copiedRecord.AsSpan(2), checked((uint)payload.Length));
    return new(copiedRecord, payload, packet.StreamIndex);
  }

  private static int _SampleRate(MediaStreamInfo audio) {
    if (audio.SampleRate > 0)
      return audio.SampleRate;
    if (audio.TimeBase.IsKnown && audio.TimeBase.Numerator == 1
        && audio.TimeBase.Denominator is > 0 and <= ushort.MaxValue)
      return checked((int)audio.TimeBase.Denominator);
    return 0;
  }
}
