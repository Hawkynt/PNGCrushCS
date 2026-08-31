using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;

namespace FileFormat.Vqa;

/// <summary>Writes Westwood VQA FORM/WVQA files from VQFR video packets and optional WSAD sound.</summary>
public sealed class VqaWriter : IVideoContainerWriter<VqaWriter> {

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
  }

  /// <summary>Gets the primary file extension for this format.</summary>
  public static string PrimaryExtension => ".vqa";
  /// <summary>Gets the file extensions supported by this format.</summary>
  public static string[] FileExtensions => [".vqa"];
  /// <summary>Creates a writer for the specified stream descriptions and metadata.</summary>
  public static VqaWriter Create(IReadOnlyList<MediaStreamInfo> streams, VideoMetadata metadata) => new(streams, metadata);

  /// <summary>Writes the specified coded packet to the container.</summary>
  public void WritePacket(CodedPacket packet) {
    if (this._finished) throw new InvalidOperationException("VQA writer has already been finished.");
    if ((uint)packet.StreamIndex >= (uint)this._streams.Count) throw new ArgumentOutOfRangeException(nameof(packet));
    this._packets.Add(packet);
  }

  /// <summary>Finishes writing the container and returns its encoded bytes.</summary>
  public byte[] Finish() {
    if (this._finished) throw new InvalidOperationException("VQA writer has already been finished.");
    this._finished = true;

    var frames = 0;
    foreach (var packet in this._packets)
      if (packet.StreamIndex == 0)
        ++frames;
    if (frames > ushort.MaxValue)
      throw new NotSupportedException("VQA frame count exceeds VQHD's 16-bit field.");
    BinaryPrimitives.WriteUInt16LittleEndian(this._header.AsSpan(4, 2), checked((ushort)frames));

    using var body = new MemoryStream();
    ContainerWriterTools.WriteAscii(body, "WVQA");
    _Chunk(body, "VQHD", this._header);

    foreach (var packet in this._packets) {
      if (packet.StreamIndex == 0)
        _Chunk(body, "VQFR", packet.Data.Span);
      else
        // SND2 is Westwood's ADPCM sound chunk. The demuxer deliberately exposes only its payload;
        // packet timing and VQHD carry all remaining stream-level information.
        _Chunk(body, "SND2", packet.Data.Span);
    }

    using var output = new MemoryStream();
    ContainerWriterTools.WriteAscii(output, "FORM");
    ContainerWriterTools.WriteUInt32BigEndian(output, checked((uint)body.Length));
    body.Position = 0;
    body.CopyTo(output);
    return output.ToArray();
  }

  private static void _Chunk(Stream output, string id, ReadOnlySpan<byte> payload) {
    ContainerWriterTools.WriteAscii(output, id);
    ContainerWriterTools.WriteUInt32BigEndian(output, checked((uint)payload.Length));
    output.Write(payload);
    if ((payload.Length & 1) != 0)
      output.WriteByte(0);
  }
}
