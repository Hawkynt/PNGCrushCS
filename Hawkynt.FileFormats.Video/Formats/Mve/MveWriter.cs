using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;

namespace FileFormat.InterplayMve;

/// <summary>
/// Writes Interplay MVE chunks around codec opcode streams, keeping every picture's decoding state
/// in the same video chunk until <c>SEND_BUFFER</c> closes it.
/// </summary>
/// <remarks>
/// This grouping is observable format semantics, not cosmetic muxing. FFmpeg, like Interplay's own
/// player, remembers DECODING_MAP/VIDEO_DATA while walking a chunk and only learns whether the
/// reconstructed back buffer is to be displayed when it reaches SEND_BUFFER. Emitting those opcodes
/// as separate chunks lets the demuxer hand the coded picture to its decoder before the display flag
/// exists, producing a perfectly parseable file with no displayed frame.
/// </remarks>
[VerifiedBy(ConformanceOracle.FFmpeg)]
public sealed class MveWriter : IVideoContainerWriter<MveWriter> {

  private static ReadOnlySpan<byte> _Header => "Interplay MVE File\x1A\0\x1A\0\0\x01\x33\x11"u8;

  private readonly IReadOnlyList<MediaStreamInfo> _streams;
  private readonly MemoryStream _output = new();
  private readonly MemoryStream _pendingVideo = new();
  private bool _pendingHasVideoData;
  private bool _finished;

  private MveWriter(IReadOnlyList<MediaStreamInfo> streams, VideoMetadata metadata) {
    ArgumentNullException.ThrowIfNull(streams);
    ArgumentNullException.ThrowIfNull(metadata);
    if (streams.Count is < 1 or > 2 || streams[0].Index != 0 || streams[0].Kind != MediaStreamKind.Video
        || !streams[0].Codec.EqualsIgnoringCase(CodecTag.FromCharacters("IMVE")))
      throw new NotSupportedException("MVE needs video stream 0 and optionally one Interplay audio stream at index 1.");
    if ((streams[0].Width & 7) != 0 || (streams[0].Height & 7) != 0 || streams[0].Width <= 0 || streams[0].Height <= 0)
      throw new NotSupportedException("MVE video dimensions are stated in 8-pixel blocks and must be positive multiples of eight.");
    if (streams[0].Width / 8 > ushort.MaxValue || streams[0].Height / 8 > ushort.MaxValue)
      throw new NotSupportedException("MVE video dimensions exceed INIT_VIDEO_BUFFERS' 16-bit block counts.");
    if (streams[0].BitsPerPixel is not (0 or 8 or 16))
      throw new NotSupportedException("Interplay MVE video is either 8-bit palettised or 16-bit RGB555.");
    if (streams.Count == 2 && (streams[1].Index != 1 || streams[1].Kind != MediaStreamKind.Audio))
      throw new NotSupportedException("MVE's optional second stream is audio at index 1.");

    this._streams = streams;
    this._output.Write(_Header);
    this._WriteInitialVideo(streams[0]);
    if (streams.Count == 2)
      this._WriteInitialAudio(streams[1]);
  }

  public static string PrimaryExtension => ".mve";
  public static string[] FileExtensions => [".mve"];
  public static MveWriter Create(IReadOnlyList<MediaStreamInfo> streams, VideoMetadata metadata) => new(streams, metadata);

  public void WritePacket(CodedPacket packet) {
    if (this._finished)
      throw new InvalidOperationException("MVE writer has already been finished.");
    if ((uint)packet.StreamIndex >= (uint)this._streams.Count)
      throw new ArgumentOutOfRangeException(nameof(packet));

    if (packet.StreamIndex != 0) {
      _ValidateOpcodeSequence(packet.Data.Span);
      this._WriteChunk(MveChunkType.AUDIO_ONLY, packet.Data.Span);
      return;
    }

    foreach (var opcode in _Opcodes(packet.Data))
      this._WriteVideoOpcode(opcode.Span);
  }

  public byte[] Finish() {
    if (this._finished)
      throw new InvalidOperationException("MVE writer has already been finished.");
    this._finished = true;

    if (this._pendingVideo.Length != 0)
      this._FlushVideoChunk();

    this._WriteTrailer();
    return this._output.ToArray();
  }

  /// <summary>
  /// Closes the file the way every shipped MVE closes: a shutdown chunk carrying the end-of-stream
  /// and end-of-chunk opcodes, then an empty end chunk.
  /// </summary>
  /// <remarks>
  /// This used to be one <c>END</c> chunk with an <c>END_OF_STREAM</c> opcode inside it, which reads
  /// as the same statement and is not the same file. FFmpeg decoded every picture of such a file and
  /// then reported "Invalid data found when processing input" once, at the end, with an exit status
  /// of zero — so the pictures were right, the diagnostic was real, and nothing that merely counted
  /// frames would ever have seen it.
  /// <para/>
  /// The mechanism is in <c>ipmovie.c</c>. Its chunk loop checks <c>avio_feof</c> before reading each
  /// opcode header, and a chunk that runs to the last byte of the file makes that check true while
  /// opcodes are still being read, which yields <c>CHUNK_EOF</c> — and <c>ipmovie_read_packet</c>
  /// maps <c>CHUNK_EOF</c> to <c>AVERROR_INVALIDDATA</c>, not to a clean end. A clean end needs the
  /// chunk that carries the closing opcodes to have something after it. Every real file arranges
  /// exactly that: the opcodes sit in a shutdown chunk and an empty end chunk follows, so the opcode
  /// loop never runs against the end of the file, and the demuxer returns on the chunk type instead.
  /// <para/>
  /// Measured rather than reasoned about. The four samples on <c>samples.ffmpeg.org</c> —
  /// interplay-logo, baldursgate-logo, MARIO1 and descent3-level5-16bit — all end with a
  /// <c>SHUTDOWN</c> chunk of eight bytes holding <c>END_OF_STREAM</c> and <c>END_OF_CHUNK</c>,
  /// followed by an <c>END</c> chunk of length zero, and FFmpeg reads all four without a word.
  /// Writing that trailer here makes FFmpeg read ours without a word too.
  /// </remarks>
  private void _WriteTrailer() {
    Span<byte> shutdown = stackalloc byte[8];
    _Opcode(MveOpcodeType.END_OF_STREAM, 0, ReadOnlySpan<byte>.Empty).CopyTo(shutdown);
    _Opcode(MveOpcodeType.END_OF_CHUNK, 0, ReadOnlySpan<byte>.Empty).CopyTo(shutdown[4..]);
    this._WriteChunk(MveChunkType.SHUTDOWN, shutdown);
    this._WriteChunk(MveChunkType.END, ReadOnlySpan<byte>.Empty);
  }

  private void _WriteVideoOpcode(ReadOnlySpan<byte> opcode) {
    var type = opcode[2];

    if (type == MveOpcodeType.INIT_VIDEO_BUFFERS)
      return;

    if (_IsVideoData(type) && this._pendingHasVideoData)
      this._FlushVideoChunk();

    this._pendingVideo.Write(opcode);
    if (_IsVideoData(type))
      this._pendingHasVideoData = true;

    if (type is MveOpcodeType.SEND_BUFFER or MveOpcodeType.END_OF_CHUNK)
      this._FlushVideoChunk();
  }

  private void _FlushVideoChunk() {
    if (this._pendingVideo.Length == 0)
      return;

    if (this._pendingVideo.Length + 4 > ushort.MaxValue)
      throw new NotSupportedException(
        $"The pending MVE video chunk is {this._pendingVideo.Length} bytes before END_OF_CHUNK; MVE chunks are limited to 65,535 bytes.");

    var payload = this._pendingVideo.ToArray();
    if (!_EndsWithEndOfChunk(payload)) {
      var end = _Opcode(MveOpcodeType.END_OF_CHUNK, 0, ReadOnlySpan<byte>.Empty);
      using var chunk = new MemoryStream(payload.Length + end.Length);
      chunk.Write(payload);
      chunk.Write(end);
      payload = chunk.ToArray();
    }

    this._WriteChunk(MveChunkType.VIDEO, payload);
    this._pendingVideo.SetLength(0);
    this._pendingHasVideoData = false;
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

    var is16Bit = video.BitsPerPixel == 16;
    var buffers = ContainerWriterTools.Build(payload => {
      ContainerWriterTools.WriteUInt16LittleEndian(payload, checked((ushort)(video.Width / 8)));
      ContainerWriterTools.WriteUInt16LittleEndian(payload, checked((ushort)(video.Height / 8)));
      if (is16Bit) {
        ContainerWriterTools.WriteUInt16LittleEndian(payload, 2);
        ContainerWriterTools.WriteUInt16LittleEndian(payload, 1);
      }
    });

    using var chunk = new MemoryStream();
    chunk.Write(_Opcode(MveOpcodeType.CREATE_TIMER, 0, timer));
    chunk.Write(_Opcode(MveOpcodeType.INIT_VIDEO_BUFFERS, is16Bit ? (byte)2 : (byte)0, buffers));
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

  private static IReadOnlyList<ReadOnlyMemory<byte>> _Opcodes(ReadOnlyMemory<byte> packet) {
    _ValidateOpcodeSequence(packet.Span);
    var result = new List<ReadOnlyMemory<byte>>();
    var at = 0;
    while (at < packet.Length) {
      var length = BinaryPrimitives.ReadUInt16LittleEndian(packet.Span[at..]);
      var total = length + 4;
      result.Add(packet.Slice(at, total));
      at += total;
    }
    return result;
  }

  private static void _ValidateOpcodeSequence(ReadOnlySpan<byte> packet) {
    var at = 0;
    while (at < packet.Length) {
      if (packet.Length - at < 4)
        throw new InvalidDataException("An MVE packet ends inside an opcode's four-byte header.");
      var length = BinaryPrimitives.ReadUInt16LittleEndian(packet[at..]);
      if (length > packet.Length - at - 4)
        throw new InvalidDataException(
          $"MVE opcode at byte {at} says {length} payload bytes but the packet has only {packet.Length - at - 4} left.");
      at += length + 4;
    }
  }

  private static bool _EndsWithEndOfChunk(ReadOnlySpan<byte> packet) {
    var at = 0;
    byte lastType = 0xFF;
    while (at < packet.Length) {
      var length = BinaryPrimitives.ReadUInt16LittleEndian(packet[at..]);
      lastType = packet[at + 2];
      at += length + 4;
    }
    return lastType == MveOpcodeType.END_OF_CHUNK;
  }

  private static bool _IsVideoData(byte type)
    => type is MveOpcodeType.VIDEO_DATA_06 or MveOpcodeType.VIDEO_DATA_10 or MveOpcodeType.VIDEO_DATA_11;
}
