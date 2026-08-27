using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;

namespace FileFormat.Vmd;

/// <summary>Writes classic 816-byte Sierra VMD files from record-prefixed demux packets.</summary>
public sealed class VmdWriter : IVideoContainerWriter<VmdWriter> {

  private const int _HEADER_LENGTH = 816;
  private const int _FRAME_RECORD_LENGTH = 16;
  private const int _BLOCK_RECORD_LENGTH = 6;
  private const int _OFFSET_NUM_BLOCKS = 6;
  private const int _OFFSET_WIDTH = 12;
  private const int _OFFSET_HEIGHT = 14;
  private const int _OFFSET_FLAGS = 16;
  private const int _OFFSET_MULTIMEDIA_DATA = 20;
  private const int _OFFSET_AUDIO_SAMPLE_RATE = 804;
  private const int _OFFSET_AUDIO_FRAME_LENGTH = 806;
  private const int _OFFSET_TOC = 812;
  private const ushort _FLAG_HAS_SOUND = 0x1000;
  private const byte _TYPE_AUDIO = 1;
  private const byte _TYPE_VIDEO = 2;

  private readonly IReadOnlyList<MediaStreamInfo> _streams;
  private readonly List<CodedPacket> _packets = [];
  private bool _finished;

  private VmdWriter(IReadOnlyList<MediaStreamInfo> streams, VideoMetadata metadata) {
    ArgumentNullException.ThrowIfNull(streams);
    ArgumentNullException.ThrowIfNull(metadata);
    if (streams.Count is < 1 or > 2)
      throw new NotSupportedException("Classic VMD contains one video stream and at most one audio stream.");

    var video = streams[0];
    if (video.Index != 0 || video.Kind != MediaStreamKind.Video
        || !video.Codec.EqualsIgnoringCase(CodecTag.FromCharacters("VMDV")))
      throw new NotSupportedException("VMD stream zero must be VMDV video.");
    if (video.CodecPrivateData.Length != _HEADER_LENGTH)
      throw new NotSupportedException(
        "VMD muxing needs the original classic 816-byte header in video CodecPrivateData; the codec version and initial palette live there.");

    if (streams.Count == 2) {
      var audio = streams[1];
      if (audio.Index != 1 || audio.Kind != MediaStreamKind.Audio
          || !audio.Codec.EqualsIgnoringCase(CodecTag.FromCharacters("VMDA")))
        throw new NotSupportedException("VMD's optional second stream must be VMDA audio at index one.");
    }

    this._streams = streams;
  }

  public static string PrimaryExtension => ".vmd";
  public static string[] FileExtensions => [".vmd"];

  public static VmdWriter Create(IReadOnlyList<MediaStreamInfo> streams, VideoMetadata metadata) => new(streams, metadata);

  public void WritePacket(CodedPacket packet) {
    if (this._finished)
      throw new InvalidOperationException("VMD writer has already been finished.");
    if ((uint)packet.StreamIndex >= (uint)this._streams.Count)
      throw new ArgumentOutOfRangeException(nameof(packet), packet.StreamIndex, "Packet names no declared VMD stream.");
    if (packet.Data.Length < _FRAME_RECORD_LENGTH)
      throw new InvalidDataException("A VMD packet must carry its original sixteen-byte frame-information record in front of its coded bytes.");

    var expected = packet.StreamIndex == 0 ? _TYPE_VIDEO : _TYPE_AUDIO;
    if (packet.Data.Span[0] != expected)
      throw new InvalidDataException(
        $"VMD stream {packet.StreamIndex} packet carries record type {packet.Data.Span[0]}, expected {expected}.");

    this._packets.Add(packet);
  }

  public byte[] Finish() {
    if (this._finished)
      throw new InvalidOperationException("VMD writer has already been finished.");
    this._finished = true;
    if (this._packets.Count == 0)
      throw new InvalidDataException("VMD needs at least one frame-information record.");

    var header = this._streams[0].CodecPrivateData.ToArray();
    BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(0, 2), 814);
    BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(_OFFSET_MULTIMEDIA_DATA, 4), _HEADER_LENGTH);
    if (this._streams[0].Width > 0)
      BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(_OFFSET_WIDTH, 2), checked((ushort)this._streams[0].Width));
    if (this._streams[0].Height > 0)
      BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(_OFFSET_HEIGHT, 2), checked((ushort)this._streams[0].Height));

    if (this._streams.Count == 2) {
      BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(_OFFSET_FLAGS, 2),
        (ushort)(BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(_OFFSET_FLAGS, 2)) | _FLAG_HAS_SOUND));
      var audio = this._streams[1];
      var sampleRate = _SampleRate(audio);
      if (sampleRate > 0)
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(_OFFSET_AUDIO_SAMPLE_RATE, 2), checked((ushort)sampleRate));
    } else {
      BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(_OFFSET_FLAGS, 2),
        (ushort)(BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(_OFFSET_FLAGS, 2)) & ~_FLAG_HAS_SOUND));
      BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(_OFFSET_AUDIO_SAMPLE_RATE, 2), 0);
      BinaryPrimitives.WriteInt16LittleEndian(header.AsSpan(_OFFSET_AUDIO_FRAME_LENGTH, 2), 0);
    }

    using var output = new MemoryStream();
    output.Write(header);

    var records = new byte[this._packets.Count][];
    var blockOffsets = new List<uint>();
    var sawVideo = false;

    for (var i = 0; i < this._packets.Count; ++i) {
      var packet = this._packets[i];
      var type = packet.StreamIndex == 0 ? _TYPE_VIDEO : _TYPE_AUDIO;

      // A block begins at the first record and then at each subsequent video frame. This is the
      // canonical one-video-frame-per-block spelling; audio records between pictures remain in the
      // same block and therefore keep their interleaving without inventing codec timing.
      if (blockOffsets.Count == 0 || type == _TYPE_VIDEO && sawVideo)
        blockOffsets.Add(checked((uint)output.Position));
      if (type == _TYPE_VIDEO)
        sawVideo = true;

      var coded = packet.Data[_FRAME_RECORD_LENGTH..];
      output.Write(coded.Span);

      var record = packet.Data[.._FRAME_RECORD_LENGTH].ToArray();
      record[0] = type;
      BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(2, 4), checked((uint)coded.Length));
      records[i] = record;
    }

    if (blockOffsets.Count > ushort.MaxValue)
      throw new NotSupportedException("VMD block table exceeds its 16-bit block count.");

    var tocOffset = checked((uint)output.Position);
    foreach (var offset in blockOffsets) {
      ContainerWriterTools.WriteUInt16LittleEndian(output, 0);
      ContainerWriterTools.WriteUInt32LittleEndian(output, offset);
    }
    foreach (var record in records)
      output.Write(record);

    var bytes = output.ToArray();
    BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(_OFFSET_NUM_BLOCKS, 2), checked((ushort)blockOffsets.Count));
    BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(_OFFSET_TOC, 4), tocOffset);
    return bytes;
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
