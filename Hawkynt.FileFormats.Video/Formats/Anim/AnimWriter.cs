using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;

namespace FileFormat.Anim;

/// <summary>Writes an IFF <c>FORM ANIM</c> around complete <c>FORM ILBM</c> frame packets.</summary>
public sealed class AnimWriter : IVideoContainerWriter<AnimWriter> {

  private readonly MemoryStream _output = new();
  private bool _finished;
  private int _frames;

  private AnimWriter(IReadOnlyList<MediaStreamInfo> streams, VideoMetadata metadata) {
    ArgumentNullException.ThrowIfNull(streams);
    ArgumentNullException.ThrowIfNull(metadata);
    if (streams.Count != 1 || streams[0].Index != 0 || streams[0].Kind != MediaStreamKind.Video
        || !streams[0].Codec.EqualsIgnoringCase(CodecTag.FromCharacters("ANIM")))
      throw new NotSupportedException("IFF ANIM contains exactly one ANIM video stream at index zero.");

    this._output.Write("FORM"u8);
    ContainerWriterTools.WriteUInt32BigEndian(this._output, 0); // patched by Finish
    this._output.Write("ANIM"u8);
  }

  public static string PrimaryExtension => ".anim";
  public static string[] FileExtensions => [".anim", ".iff"];

  public static AnimWriter Create(IReadOnlyList<MediaStreamInfo> streams, VideoMetadata metadata) => new(streams, metadata);

  public void WritePacket(CodedPacket packet) {
    if (this._finished)
      throw new InvalidOperationException("ANIM writer has already been finished.");
    if (packet.StreamIndex != 0)
      throw new ArgumentOutOfRangeException(nameof(packet), packet.StreamIndex, "IFF ANIM has only stream zero.");

    var frame = packet.Data.Span;
    if (frame.Length < 12 || !frame[..4].SequenceEqual("FORM"u8) || !frame.Slice(8, 4).SequenceEqual("ILBM"u8))
      throw new InvalidDataException("An ANIM packet must be a complete 'FORM ILBM' frame.");

    var declared = BinaryPrimitives.ReadUInt32BigEndian(frame[4..8]);
    if ((ulong)declared + 8 != (ulong)frame.Length)
      throw new InvalidDataException(
        $"The ILBM frame states {declared} bytes after its FORM header, but the packet is {frame.Length} bytes long.");

    this._output.Write(frame);
    if ((declared & 1) != 0)
      this._output.WriteByte(0);
    ++this._frames;
  }

  public byte[] Finish() {
    if (this._finished)
      throw new InvalidOperationException("ANIM writer has already been finished.");
    this._finished = true;
    if (this._frames == 0)
      throw new InvalidDataException("IFF ANIM needs at least one FORM ILBM frame.");

    var size = this._output.Length - 8;
    if (size > uint.MaxValue)
      throw new NotSupportedException("IFF FORM size exceeds its 32-bit field.");

    var bytes = this._output.ToArray();
    BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(4, 4), checked((uint)size));
    return bytes;
  }
}
