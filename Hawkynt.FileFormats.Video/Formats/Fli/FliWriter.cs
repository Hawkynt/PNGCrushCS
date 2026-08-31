using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;

namespace FileFormat.FlicVideo;

/// <summary>Writes an Autodesk Animator Pro FLC file from already-coded FLIC frame sub-chunks.</summary>
public sealed class FliWriter : IVideoContainerWriter<FliWriter> {

  private const ushort _FRAME_MAGIC = 0xF1FA;
  private readonly MediaStreamInfo _stream;
  private readonly List<CodedPacket> _packets = [];
  private bool _finished;

  private FliWriter(IReadOnlyList<MediaStreamInfo> streams, VideoMetadata metadata) {
    ArgumentNullException.ThrowIfNull(streams);
    ArgumentNullException.ThrowIfNull(metadata);
    if (streams.Count != 1 || streams[0].Index != 0 || streams[0].Kind != MediaStreamKind.Video)
      throw new NotSupportedException("FLIC carries exactly one video stream at index 0.");
    if (!streams[0].Codec.EqualsIgnoringCase(CodecTag.FromCharacters("FLIC")))
      throw new NotSupportedException($"FLIC writer needs FLIC-coded packets, not '{streams[0].Codec}'.");
    if (streams[0].Width is <= 0 or > ushort.MaxValue || streams[0].Height is <= 0 or > ushort.MaxValue)
      throw new NotSupportedException("FLIC width and height must fit unsigned 16-bit header fields.");
    this._stream = streams[0];
  }

  /// <summary>Gets the primary file extension for this format.</summary>
  public static string PrimaryExtension => ".flc";
  /// <summary>Gets the file extensions supported by this format.</summary>
  public static string[] FileExtensions => [".fli", ".flc", ".flx"];

  /// <summary>Creates a writer for the specified stream descriptions and metadata.</summary>
  public static FliWriter Create(IReadOnlyList<MediaStreamInfo> streams, VideoMetadata metadata) => new(streams, metadata);

  /// <summary>Writes the specified coded packet to the container.</summary>
  public void WritePacket(CodedPacket packet) {
    if (this._finished) throw new InvalidOperationException("FLIC writer has already been finished.");
    if (packet.StreamIndex != 0) throw new ArgumentOutOfRangeException(nameof(packet));
    _ValidateSubChunks(packet.Data.Span);
    this._packets.Add(packet);
  }

  /// <summary>Finishes writing the container and returns its encoded bytes.</summary>
  public byte[] Finish() {
    if (this._finished) throw new InvalidOperationException("FLIC writer has already been finished.");
    this._finished = true;
    if (this._packets.Count > ushort.MaxValue)
      throw new NotSupportedException("FLIC frame count exceeds its 16-bit header field.");

    using var output = new MemoryStream();
    output.Write(new byte[FliReader.HEADER_SIZE]);
    var firstFrame = (uint)output.Position;

    foreach (var packet in this._packets) {
      var payload = packet.Data.Span;
      var frameSize = checked((uint)(16 + payload.Length));
      ContainerWriterTools.WriteUInt32LittleEndian(output, frameSize);
      ContainerWriterTools.WriteUInt16LittleEndian(output, _FRAME_MAGIC);
      ContainerWriterTools.WriteUInt16LittleEndian(output, checked((ushort)_CountSubChunks(payload)));
      var delay = packet.Duration is > 0
        ? ContainerWriterTools.Rescale(packet.Duration.Value, this._stream.TimeBase, 1000)
        : 0;
      ContainerWriterTools.WriteUInt16LittleEndian(output, checked((ushort)Math.Clamp(delay, 0, ushort.MaxValue)));
      ContainerWriterTools.WriteUInt16LittleEndian(output, 0);
      ContainerWriterTools.WriteUInt16LittleEndian(output, 0);
      ContainerWriterTools.WriteUInt16LittleEndian(output, 0);
      output.Write(payload);
    }

    var result = output.ToArray();
    BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(0, 4), checked((uint)result.Length));
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(4, 2), FliReader.MAGIC_FLC);
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(6, 2), checked((ushort)this._packets.Count));
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(8, 2), checked((ushort)this._stream.Width));
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(10, 2), checked((ushort)this._stream.Height));
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(12, 2), 8);
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(14, 2), 3); // finished + updated
    var speed = this._stream.FrameRate.IsKnown
      ? Math.Max(1, (long)Math.Round(1000d / this._stream.FrameRate.ToDouble()))
      : this._packets.Count != 0 && this._packets[0].Duration is > 0
        ? ContainerWriterTools.Rescale(this._packets[0].Duration!.Value, this._stream.TimeBase, 1000)
        : 100;
    BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(16, 4), checked((uint)Math.Clamp(speed, 1, uint.MaxValue)));
    BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(80, 4), firstFrame);
    return result;
  }

  private static void _ValidateSubChunks(ReadOnlySpan<byte> data) {
    var at = 0;
    while (at < data.Length) {
      if (at + 6 > data.Length)
        throw new InvalidDataException("FLIC frame packet ends inside a sub-chunk header.");
      var size = BinaryPrimitives.ReadUInt32LittleEndian(data[at..]);
      if (size < 6 || size > data.Length - at)
        throw new InvalidDataException($"FLIC sub-chunk at {at} states invalid size {size}.");
      at += checked((int)size);
    }
  }

  private static int _CountSubChunks(ReadOnlySpan<byte> data) {
    var at = 0;
    var count = 0;
    while (at < data.Length) {
      at += checked((int)BinaryPrimitives.ReadUInt32LittleEndian(data[at..]));
      ++count;
    }
    return count;
  }
}
