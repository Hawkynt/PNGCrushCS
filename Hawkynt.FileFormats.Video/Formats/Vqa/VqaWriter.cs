using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;

namespace FileFormat.Vqa;

/// <summary>Writes complete Westwood VQA FORM/WVQA files from coded WSVQ video and optional WSAD sound.</summary>
/// <remarks>
/// VQA is not just a sequence of VQFR chunks: normal readers expect the FINF frame-position table
/// between VQHD and the media. FINF stores half-byte-offsets because every chunk begins on an even
/// address; its 0x40000000 bit marks a frame carrying a palette update. This writer derives both from
/// the actual packet order and VQFR sub-chunks rather than trusting caller metadata.
/// </remarks>
public sealed class VqaWriter : IVideoContainerWriter<VqaWriter> {

  private const uint _FINF_PALETTE_FLAG = 0x40000000;
  private const uint _FINF_OFFSET_MASK = 0x3fffffff;

  private readonly IReadOnlyList<MediaStreamInfo> _streams;
  private readonly byte[] _header;
  private readonly List<CodedPacket> _packets = [];
  private bool _finished;

  private VqaWriter(IReadOnlyList<MediaStreamInfo> streams, VideoMetadata metadata) {
    ArgumentNullException.ThrowIfNull(streams);
    ArgumentNullException.ThrowIfNull(metadata);
    if (streams.Count is < 1 or > 2 || streams[0].Index != 0 || streams[0].Kind != MediaStreamKind.Video
        || !streams[0].Codec.EqualsIgnoringCase(CodecTag.FromCharacters("WSVQ")))
      throw new NotSupportedException("VQA needs WSVQ video stream 0 and optionally WSAD audio stream 1.");
    if (streams[0].CodecPrivateData.Length < 42)
      throw new NotSupportedException("VQA needs the 42-byte VQHD payload in video CodecPrivateData.");
    if (streams.Count == 2 && (streams[1].Index != 1 || streams[1].Kind != MediaStreamKind.Audio
        || !streams[1].Codec.EqualsIgnoringCase(CodecTag.FromCharacters("WSAD"))))
      throw new NotSupportedException("VQA's optional second stream must be WSAD audio at index 1.");

    this._streams = streams;
    this._header = streams[0].CodecPrivateData[..42].ToArray();
    this._ApplyAudioDescription();
  }

  public static string PrimaryExtension => ".vqa";
  public static string[] FileExtensions => [".vqa"];
  public static VqaWriter Create(IReadOnlyList<MediaStreamInfo> streams, VideoMetadata metadata) => new(streams, metadata);

  public void WritePacket(CodedPacket packet) {
    if (this._finished)
      throw new InvalidOperationException("VQA writer has already been finished.");
    if ((uint)packet.StreamIndex >= (uint)this._streams.Count)
      throw new ArgumentOutOfRangeException(nameof(packet));
    this._packets.Add(packet);
  }

  public byte[] Finish() {
    if (this._finished)
      throw new InvalidOperationException("VQA writer has already been finished.");
    this._finished = true;

    var frames = 0;
    var maxPointerPayload = 0;
    foreach (var packet in this._packets) {
      if (packet.StreamIndex != 0)
        continue;

      ++frames;
      maxPointerPayload = Math.Max(maxPointerPayload, _LargestPointerPayload(packet.Data.Span));
    }

    if (frames > ushort.MaxValue)
      throw new NotSupportedException("VQA frame count exceeds VQHD's 16-bit field.");

    BinaryPrimitives.WriteUInt16LittleEndian(this._header.AsSpan(4, 2), checked((ushort)frames));
    BinaryPrimitives.WriteUInt16LittleEndian(this._header.AsSpan(22, 2), (ushort)Math.Min(ushort.MaxValue, maxPointerPayload));

    using var body = new MemoryStream();
    ContainerWriterTools.WriteAscii(body, "WVQA");
    _Chunk(body, "VQHD", this._header);

    var finf = new byte[checked(frames * 4)];
    var finfPayloadPosition = body.Position + 8;
    _Chunk(body, "FINF", finf);

    var frame = 0;
    long groupStart = -1;
    foreach (var packet in this._packets) {
      var absoluteChunkStart = checked(body.Position + 8); // FORM's own eight-byte header precedes body.
      if (groupStart < 0)
        groupStart = absoluteChunkStart;

      if (packet.StreamIndex == 0) {
        var halfOffset = checked((ulong)groupStart / 2);
        if ((groupStart & 1) != 0 || halfOffset > _FINF_OFFSET_MASK)
          throw new NotSupportedException("A VQA FINF entry cannot represent this frame's file offset.");

        var entry = (uint)halfOffset;
        if (_HasPalette(packet.Data.Span))
          entry |= _FINF_PALETTE_FLAG;
        BinaryPrimitives.WriteUInt32LittleEndian(finf.AsSpan(frame * 4, 4), entry);

        _Chunk(body, "VQFR", packet.Data.Span);
        ++frame;
        groupStart = -1;
      } else {
        // WSAD packets are the payload of Westwood's IMA-ADPCM SND2 chunks. FINF points at the first
        // sound chunk belonging to a frame, so any run of audio packets before VQFR remains one group.
        _Chunk(body, "SND2", packet.Data.Span);
      }
    }

    var end = body.Position;
    body.Position = finfPayloadPosition;
    body.Write(finf);
    body.Position = end;

    using var output = new MemoryStream();
    ContainerWriterTools.WriteAscii(output, "FORM");
    ContainerWriterTools.WriteUInt32BigEndian(output, checked((uint)body.Length));
    body.Position = 0;
    body.CopyTo(output);
    return output.ToArray();
  }

  private void _ApplyAudioDescription() {
    var flags = BinaryPrimitives.ReadUInt16LittleEndian(this._header.AsSpan(2, 2));
    if (this._streams.Count == 1) {
      flags = (ushort)(flags & ~1);
      BinaryPrimitives.WriteUInt16LittleEndian(this._header.AsSpan(2, 2), flags);
      BinaryPrimitives.WriteUInt16LittleEndian(this._header.AsSpan(24, 2), 0);
      this._header[26] = 0;
      this._header[27] = 0;
      return;
    }

    var audio = this._streams[1];
    flags |= 1;
    BinaryPrimitives.WriteUInt16LittleEndian(this._header.AsSpan(2, 2), flags);

    if (audio.SampleRate is < 0 or > ushort.MaxValue)
      throw new NotSupportedException($"VQA's audio sample-rate field cannot represent {audio.SampleRate} Hz.");
    if (audio.Channels is < 0 or > byte.MaxValue)
      throw new NotSupportedException($"VQA's audio channel field cannot represent {audio.Channels} channels.");
    if (audio.BitsPerSample is < 0 or > byte.MaxValue)
      throw new NotSupportedException($"VQA's audio bit-depth field cannot represent {audio.BitsPerSample} bits per sample.");

    if (audio.SampleRate != 0)
      BinaryPrimitives.WriteUInt16LittleEndian(this._header.AsSpan(24, 2), checked((ushort)audio.SampleRate));
    if (audio.Channels != 0)
      this._header[26] = checked((byte)audio.Channels);
    if (audio.BitsPerSample != 0)
      this._header[27] = checked((byte)audio.BitsPerSample);
  }

  private static bool _HasPalette(ReadOnlySpan<byte> packet) {
    foreach (var chunk in _SubChunks(packet))
      if (chunk.Id[..3].SequenceEqual("CPL"u8))
        return true;
    return false;
  }

  private static int _LargestPointerPayload(ReadOnlySpan<byte> packet) {
    var largest = 0;
    foreach (var chunk in _SubChunks(packet))
      if (chunk.Id[..3].SequenceEqual("VPT"u8) || chunk.Id[..3].SequenceEqual("VPR"u8))
        largest = Math.Max(largest, chunk.PayloadLength);
    return largest;
  }

  private readonly ref struct SubChunk(ReadOnlySpan<byte> id, int payloadLength) {
    public ReadOnlySpan<byte> Id { get; } = id;
    public int PayloadLength { get; } = payloadLength;
  }

  private static SubChunkWalker _SubChunks(ReadOnlySpan<byte> data) => new(data);

  private ref struct SubChunkWalker(ReadOnlySpan<byte> data) {
    private readonly ReadOnlySpan<byte> _data = data;
    private int _at;
    public SubChunk Current { get; private set; }

    public readonly SubChunkWalker GetEnumerator() => this;

    public bool MoveNext() {
      if (this._at >= this._data.Length)
        return false;
      if (this._at + 8 > this._data.Length)
        throw new InvalidDataException("A coded VQA packet ends inside an eight-byte sub-chunk header.");

      var payloadLength = checked((int)BinaryPrimitives.ReadUInt32BigEndian(this._data[(this._at + 4)..]));
      var payloadStart = this._at + 8;
      if (payloadStart + payloadLength > this._data.Length)
        throw new InvalidDataException("A coded VQA packet contains a sub-chunk extending beyond the packet.");

      this.Current = new(this._data.Slice(this._at, 4), payloadLength);
      this._at = payloadStart + payloadLength + (payloadLength & 1);
      return true;
    }
  }

  private static void _Chunk(Stream output, string id, ReadOnlySpan<byte> payload) {
    ContainerWriterTools.WriteAscii(output, id);
    ContainerWriterTools.WriteUInt32BigEndian(output, checked((uint)payload.Length));
    output.Write(payload);
    if ((payload.Length & 1) != 0)
      output.WriteByte(0);
  }
}
