using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;

namespace FileFormat.InterplayMve;

/// <summary>Writes Interplay MVE chunks around the opcode packets exposed by the demuxer.</summary>
public sealed class MveWriter : IVideoContainerWriter<MveWriter> {

  private static ReadOnlySpan<byte> _Header => "Interplay MVE File\x1A\0\x1A\0\0\x01\x33\x11"u8;

  private readonly IReadOnlyList<MediaStreamInfo> _streams;
  private readonly MemoryStream _output = new();
  private bool _finished;

  private MveWriter(IReadOnlyList<MediaStreamInfo> streams, VideoMetadata metadata) {
    ArgumentNullException.ThrowIfNull(streams);
    ArgumentNullException.ThrowIfNull(metadata);
    if (streams.Count is < 1 or > 2 || streams[0].Index != 0 || streams[0].Kind != MediaStreamKind.Video
        || !streams[0].Codec.EqualsIgnoringCase(CodecTag.FromCharacters("IMVE")))
      throw new NotSupportedException("MVE needs video stream 0 and optionally one Interplay audio stream at index 1.");
    if ((streams[0].Width & 7) != 0 || (streams[0].Height & 7) != 0 || streams[0].Width <= 0 || streams[0].Height <= 0)
      throw new NotSupportedException("MVE video dimensions are stated in 8-pixel blocks and must be positive multiples of eight.");
    if (streams.Count == 2 && (streams[1].Index != 1 || streams[1].Kind != MediaStreamKind.Audio))
      throw new NotSupportedException("MVE's optional second stream is audio at index 1.");

    this._streams = streams;
    this._output.Write(_Header);
    this._WriteInitialVideo(streams[0]);
    if (streams.Count == 2)
      this._WriteInitialAudio(streams[1]);
  }

  /// <summary>Gets the primary file extension for this format.</summary>
  public static string PrimaryExtension => ".mve";
  /// <summary>Gets the file extensions supported by this format.</summary>
  public static string[] FileExtensions => [".mve"];
  /// <summary>Creates a writer for the specified stream descriptions and metadata.</summary>
  public static MveWriter Create(IReadOnlyList<MediaStreamInfo> streams, VideoMetadata metadata) => new(streams, metadata);

  /// <summary>Writes the specified coded packet to the container.</summary>
  public void WritePacket(CodedPacket packet) {
    if (this._finished) throw new InvalidOperationException("MVE writer has already been finished.");
    if ((uint)packet.StreamIndex >= (uint)this._streams.Count) throw new ArgumentOutOfRangeException(nameof(packet));
    _ValidateOpcode(packet.Data.Span);
    var chunkType = packet.StreamIndex == 0 ? MveChunkType.VIDEO : MveChunkType.AUDIO_ONLY;
    this._WriteChunk(chunkType, packet.Data.Span);
  }

  /// <summary>Finishes writing the container and returns its encoded bytes.</summary>
  public byte[] Finish() {
    if (this._finished) throw new InvalidOperationException("MVE writer has already been finished.");
    this._finished = true;
    var end = _Opcode(MveOpcodeType.END_OF_STREAM, 0, ReadOnlySpan<byte>.Empty);
    this._WriteChunk(MveChunkType.END, end);
    return this._output.ToArray();
  }

  private void _WriteInitialVideo(MediaStreamInfo video) {
    var duration = video.FrameRate.IsKnown
      ? Math.Max(1, (long)Math.Round(1_000_000d / video.FrameRate.ToDouble()))
      : video.TimeBase.IsKnown ? Math.Max(1, (long)Math.Round(video.TimeBase.ToDouble() * 1_000_000d)) : 33_333;
    if (duration > uint.MaxValue)
      throw new NotSupportedException("MVE frame duration exceeds CREATE_TIMER's 32-bit rate field.");

    var timer = ContainerWriterTools.Build(payload => {
      ContainerWriterTools.WriteUInt32LittleEndian(payload, checked((uint)duration));
      ContainerWriterTools.WriteUInt16LittleEndian(payload, 1);
    });
    var buffers = ContainerWriterTools.Build(payload => {
      ContainerWriterTools.WriteUInt16LittleEndian(payload, checked((ushort)(video.Width / 8)));
      ContainerWriterTools.WriteUInt16LittleEndian(payload, checked((ushort)(video.Height / 8)));
    });

    using var chunk = new MemoryStream();
    chunk.Write(_Opcode(MveOpcodeType.CREATE_TIMER, 0, timer));
    chunk.Write(_Opcode(MveOpcodeType.INIT_VIDEO_BUFFERS, 0, buffers));
    chunk.Write(_Opcode(MveOpcodeType.END_OF_CHUNK, 0, ReadOnlySpan<byte>.Empty));
    this._WriteChunk(MveChunkType.INIT_VIDEO, chunk.ToArray());
  }

  private void _WriteInitialAudio(MediaStreamInfo audio) {
    var sampleRate = audio.SampleRate > 0
      ? audio.SampleRate
      : audio.TimeBase.IsKnown && audio.TimeBase.Numerator == 1 ? checked((int)audio.TimeBase.Denominator) : 0;
    if (sampleRate is <= 0 or > ushort.MaxValue)
      throw new NotSupportedException("MVE audio needs a sample rate that fits INIT_AUDIO_BUFFERS' 16-bit field.");
    var stereo = audio.Channels == 2 || audio.Codec.EqualsIgnoringCase(CodecTag.FromCharacters("IMVS"));
    var flags = (stereo ? 1 : 0) | (audio.BitsPerSample == 16 ? 2 : 0);
    var init = ContainerWriterTools.Build(payload => {
      ContainerWriterTools.WriteUInt16LittleEndian(payload, 0);
      ContainerWriterTools.WriteUInt16LittleEndian(payload, checked((ushort)flags));
      ContainerWriterTools.WriteUInt16LittleEndian(payload, checked((ushort)sampleRate));
      ContainerWriterTools.WriteUInt16LittleEndian(payload, 0);
    });
    using var chunk = new MemoryStream();
    chunk.Write(_Opcode(MveOpcodeType.INIT_AUDIO_BUFFERS, 0, init));
    chunk.Write(_Opcode(MveOpcodeType.END_OF_CHUNK, 0, ReadOnlySpan<byte>.Empty));
    this._WriteChunk(MveChunkType.INIT_AUDIO, chunk.ToArray());
  }

  private void _WriteChunk(ushort type, ReadOnlySpan<byte> payload) {
    if (payload.Length > ushort.MaxValue)
      throw new NotSupportedException("An MVE chunk may carry at most 65,535 bytes.");
    ContainerWriterTools.WriteUInt16LittleEndian(this._output, checked((ushort)payload.Length));
    ContainerWriterTools.WriteUInt16LittleEndian(this._output, type);
    this._output.Write(payload);
  }

  private static byte[] _Opcode(byte type, byte version, ReadOnlySpan<byte> payload) {
    if (payload.Length > ushort.MaxValue)
      throw new NotSupportedException("An MVE opcode may carry at most 65,535 bytes.");

    using var opcode = new MemoryStream(4 + payload.Length);
    ContainerWriterTools.WriteUInt16LittleEndian(opcode, checked((ushort)payload.Length));
    opcode.WriteByte(type);
    opcode.WriteByte(version);
    opcode.Write(payload);
    return opcode.ToArray();
  }

  private static void _ValidateOpcode(ReadOnlySpan<byte> packet) {
    if (packet.Length < 4)
      throw new InvalidDataException("An MVE packet must include its four-byte opcode header.");
    var length = BinaryPrimitives.ReadUInt16LittleEndian(packet);
    if (packet.Length != length + 4)
      throw new InvalidDataException($"MVE opcode says {length} payload bytes but packet carries {packet.Length - 4}.");
  }
}
