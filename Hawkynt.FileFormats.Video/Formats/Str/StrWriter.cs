using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Str;

/// <summary>
/// Writes raw PlayStation STR sectors, including the CD-ROM XA Mode-2 Form-1 EDC/ECC and Form-2 EDC
/// bytes required by a real disc sector rather than merely by this package's demuxer.
/// </summary>
public sealed class StrWriter : IVideoContainerWriter<StrWriter> {

  private const int _SectorSize = 2352;
  private const int _PayloadOffset = 24;
  private const int _ChunkHeaderLength = 32;
  private const int _ChunkPayloadLength = 2016;
  private const int _XaAudioLength = 2304;

  private static readonly byte[] _Sync = [0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x00];
  private static readonly uint[] _EdcLut = new uint[256];
  private static readonly byte[] _EccForward = new byte[256];
  private static readonly byte[] _EccBackward = new byte[256];

  private readonly IReadOnlyList<MediaStreamInfo> _streams;
  private readonly MemoryStream _output = new();
  private uint _sectorOrdinal;
  private uint _nextFrameNumber = 1;
  private int _videoPackets;
  private bool _finished;

  static StrWriter() {
    for (var i = 0; i < 256; ++i) {
      var doubled = (i << 1) ^ ((i & 0x80) != 0 ? 0x11D : 0);
      _EccForward[i] = (byte)doubled;
      _EccBackward[i ^ doubled] = (byte)i;

      uint edc = (uint)i;
      for (var bit = 0; bit < 8; ++bit)
        edc = (edc >> 1) ^ ((edc & 1) != 0 ? 0xD8018001u : 0u);
      _EdcLut[i] = edc;
    }
  }

  private StrWriter(IReadOnlyList<MediaStreamInfo> streams, VideoMetadata metadata) {
    ArgumentNullException.ThrowIfNull(streams);
    ArgumentNullException.ThrowIfNull(metadata);

    if (streams.Count is < 1 or > 2)
      throw new NotSupportedException("PlayStation STR needs one MDEC video stream and optionally one XA-ADPCM audio stream.");

    var video = streams[0];
    if (video.Index != 0 || video.Kind != MediaStreamKind.Video || !video.Codec.EqualsIgnoringCase(CodecTag.FromCharacters("MDEC")))
      throw new NotSupportedException("PlayStation STR stream 0 must be MDEC video.");
    if (video.Width is <= 0 or > ushort.MaxValue || video.Height is <= 0 or > ushort.MaxValue)
      throw new NotSupportedException("PlayStation STR video dimensions must fit the sector header's 16-bit fields.");

    if (streams.Count == 2) {
      var audio = streams[1];
      if (audio.Index != 1 || audio.Kind != MediaStreamKind.Audio || !audio.Codec.EqualsIgnoringCase(CodecTag.FromCharacters("XAAD")))
        throw new NotSupportedException("PlayStation STR stream 1 must be XA-ADPCM audio.");
      if (audio.SampleRate is not (18_900 or 37_800))
        throw new NotSupportedException("XA-ADPCM sample rate must be 18,900 or 37,800 Hz.");
      if (audio.Channels is not (1 or 2))
        throw new NotSupportedException("XA-ADPCM must be mono or stereo.");
      if (audio.BitsPerSample is not (4 or 8))
        throw new NotSupportedException("XA-ADPCM coding precision must be 4 or 8 bits.");
    }

    this._streams = streams;
  }

  public static string PrimaryExtension => ".str";
  public static string[] FileExtensions => [".str"];
  public static StrWriter Create(IReadOnlyList<MediaStreamInfo> streams, VideoMetadata metadata) => new(streams, metadata);

  public void WritePacket(CodedPacket packet) {
    if (this._finished)
      throw new InvalidOperationException("STR writer has already been finished.");
    if ((uint)packet.StreamIndex >= (uint)this._streams.Count)
      throw new ArgumentOutOfRangeException(nameof(packet), packet.StreamIndex, "Packet names no declared STR stream.");

    if (packet.StreamIndex == 0)
      this._WriteVideo(packet);
    else
      this._WriteAudio(packet);
  }

  public byte[] Finish() {
    if (this._finished)
      throw new InvalidOperationException("STR writer has already been finished.");
    this._finished = true;
    if (this._videoPackets == 0)
      throw new InvalidDataException("A PlayStation STR file needs at least one video frame.");
    return this._output.ToArray();
  }

  private void _WriteVideo(CodedPacket packet) {
    var data = packet.Data;
    if (data.IsEmpty)
      throw new InvalidDataException("An STR video frame cannot be empty.");
    if ((data.Length & 3) != 0)
      throw new NotSupportedException("The STR frame-size field is defined in four-byte units; MDEC packet length must be a multiple of four.");

    var chunkCount = checked((data.Length + _ChunkPayloadLength - 1) / _ChunkPayloadLength);
    if (chunkCount > ushort.MaxValue)
      throw new NotSupportedException("One STR frame needs more than 65,535 CD sectors.");

    uint frameNumber;
    if (packet.PresentationTimestamp is { } timestamp) {
      if (timestamp is < 0 or >= uint.MaxValue)
        throw new NotSupportedException("STR video presentation timestamps must fit the 32-bit frame-number field after the one-based offset.");
      frameNumber = checked((uint)timestamp + 1);
      this._nextFrameNumber = frameNumber == uint.MaxValue ? uint.MaxValue : frameNumber + 1;
    } else {
      frameNumber = this._nextFrameNumber;
      if (this._nextFrameNumber != uint.MaxValue)
        ++this._nextFrameNumber;
    }

    var video = this._streams[0];
    for (var chunkIndex = 0; chunkIndex < chunkCount; ++chunkIndex) {
      var sector = new byte[_SectorSize];
      this._WriteSectorPrefix(sector, fileNumber: 1, channel: 1, submode: 0x48, codingInformation: 0);

      var header = sector.AsSpan(_PayloadOffset, _ChunkHeaderLength);
      BinaryPrimitives.WriteUInt16LittleEndian(header, 0x0160);
      BinaryPrimitives.WriteUInt16LittleEndian(header[2..], 0x8001);
      BinaryPrimitives.WriteUInt16LittleEndian(header[4..], checked((ushort)chunkIndex));
      BinaryPrimitives.WriteUInt16LittleEndian(header[6..], checked((ushort)chunkCount));
      BinaryPrimitives.WriteUInt32LittleEndian(header[8..], frameNumber);
      BinaryPrimitives.WriteUInt32LittleEndian(header[12..], checked((uint)data.Length));
      BinaryPrimitives.WriteUInt16LittleEndian(header[16..], checked((ushort)video.Width));
      BinaryPrimitives.WriteUInt16LittleEndian(header[18..], checked((ushort)video.Height));
      this._WriteFramePrivateHeader(packet, header[20..]);

      var sourceOffset = chunkIndex * _ChunkPayloadLength;
      var take = Math.Min(_ChunkPayloadLength, data.Length - sourceOffset);
      data.Span.Slice(sourceOffset, take).CopyTo(sector.AsSpan(_PayloadOffset + _ChunkHeaderLength, take));

      _StampMode2Form1(sector);
      this._output.Write(sector);
      ++this._sectorOrdinal;
    }

    ++this._videoPackets;
  }

  private void _WriteFramePrivateHeader(CodedPacket packet, Span<byte> destination) {
    destination.Clear();
    if (!packet.ContainerPrivateData.IsEmpty) {
      if (packet.ContainerPrivateData.Length != 12)
        throw new InvalidDataException("STR video ContainerPrivateData must be the twelve bytes from sector-header offsets 20..31.");
      packet.ContainerPrivateData.Span.CopyTo(destination);
    } else if (packet.Data.Length >= 8)
      packet.Data.Span[..8].CopyTo(destination);
    else
      BinaryPrimitives.WriteUInt16LittleEndian(destination[2..], 0x3800);

    if (BinaryPrimitives.ReadUInt16LittleEndian(destination[2..]) != 0x3800)
      throw new NotSupportedException("STR's replicated MDEC frame header must carry the 0x3800 magic word.");
  }

  private void _WriteAudio(CodedPacket packet) {
    if (packet.Data.Length != _XaAudioLength)
      throw new InvalidDataException($"An XA-ADPCM STR packet is exactly {_XaAudioLength} bytes (18 sound groups). Got {packet.Data.Length}.");

    var audio = this._streams[1];
    var coding = (byte)((audio.Channels == 2 ? 0x01 : 0)
      | (audio.SampleRate == 18_900 ? 0x04 : 0)
      | (audio.BitsPerSample == 8 ? 0x10 : 0));

    var sector = new byte[_SectorSize];
    this._WriteSectorPrefix(sector, fileNumber: 1, channel: 1, submode: 0x64, codingInformation: coding);
    packet.Data.Span.CopyTo(sector.AsSpan(_PayloadOffset, _XaAudioLength));
    _StampMode2Form2(sector);
    this._output.Write(sector);
    ++this._sectorOrdinal;
  }

  private void _WriteSectorPrefix(byte[] sector, byte fileNumber, byte channel, byte submode, byte codingInformation) {
    _Sync.CopyTo(sector, 0);

    var absoluteFrame = checked(this._sectorOrdinal + 150u);
    var minute = absoluteFrame / (75u * 60u);
    if (minute > 99)
      throw new NotSupportedException("Raw STR output exceeds the two-digit CD MSF address range.");
    var second = absoluteFrame / 75u % 60u;
    var frame = absoluteFrame % 75u;
    sector[12] = _Bcd(minute);
    sector[13] = _Bcd(second);
    sector[14] = _Bcd(frame);
    sector[15] = 2;

    sector[16] = fileNumber;
    sector[17] = channel;
    sector[18] = submode;
    sector[19] = codingInformation;
    sector[20] = fileNumber;
    sector[21] = channel;
    sector[22] = submode;
    sector[23] = codingInformation;
  }

  private static byte _Bcd(uint value) => checked((byte)(((value / 10) << 4) | (value % 10)));

  private static void _StampMode2Form1(byte[] sector) {
    BinaryPrimitives.WriteUInt32LittleEndian(sector.AsSpan(0x818, 4), _Edc(sector.AsSpan(0x10, 0x808)));

    Span<byte> savedAddress = stackalloc byte[4];
    sector.AsSpan(0x0C, 4).CopyTo(savedAddress);
    sector.AsSpan(0x0C, 4).Clear();
    _Ecc(sector.AsSpan(0x0C, 2064), 86, 24, 2, 86, sector.AsSpan(0x81C, 172));
    _Ecc(sector.AsSpan(0x0C, 2236), 52, 43, 86, 88, sector.AsSpan(0x8C8, 104));
    savedAddress.CopyTo(sector.AsSpan(0x0C, 4));
  }

  private static void _StampMode2Form2(byte[] sector)
    => BinaryPrimitives.WriteUInt32LittleEndian(sector.AsSpan(0x92C, 4), _Edc(sector.AsSpan(0x10, 0x91C)));

  private static uint _Edc(ReadOnlySpan<byte> source) {
    uint edc = 0;
    foreach (var value in source)
      edc = (edc >> 8) ^ _EdcLut[(int)((edc ^ value) & 0xFF)];
    return edc;
  }

  private static void _Ecc(
    ReadOnlySpan<byte> source,
    int majorCount,
    int minorCount,
    int majorMultiplier,
    int minorIncrement,
    Span<byte> destination) {
    var size = majorCount * minorCount;
    if (source.Length < size || destination.Length < majorCount * 2)
      throw new ArgumentException("ECC source or destination is too short.");

    for (var major = 0; major < majorCount; ++major) {
      var index = (major >> 1) * majorMultiplier + (major & 1);
      byte a = 0;
      byte b = 0;
      for (var minor = 0; minor < minorCount; ++minor) {
        var value = source[index];
        index += minorIncrement;
        if (index >= size)
          index -= size;
        a ^= value;
        b ^= value;
        a = _EccForward[a];
      }

      a = _EccBackward[_EccForward[a] ^ b];
      destination[major] = a;
      destination[major + majorCount] = (byte)(a ^ b);
    }
  }
}
