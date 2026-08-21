using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FileFormat.Core;

namespace FileFormat.InterplayMve.Tests;

/// <summary>
/// The Interplay MVE container's demuxing behaviour: which opcode becomes which stream's packet, and
/// how a picture's size and frame rate are found among opcodes that carry other things too.
/// </summary>
/// <remarks>
/// Opcode-level bit packing is not exercised here — <see cref="Codecs.Tests.MveVideoDecoderTests"/>
/// covers every block encoding, and two real files spanning 555 pictures were compared frame by frame
/// against ffmpeg's decode with no differing sample anywhere. What is worth a hand-built fixture is
/// what a real file's own shape does not force a reader to exercise: a signature that is not this
/// format's, a file with no <c>INIT_VIDEO_BUFFERS</c> opcode at all, and which packet — the first one
/// only — is reported as a key frame.
/// </remarks>
[TestFixture]
public sealed class MveReaderTests {

  private const byte _INIT_VIDEO_BUFFERS = 0x05;
  private const byte _CREATE_TIMER = 0x02;
  private const byte _SET_PALETTE = 0x0C;
  private const byte _DECODING_MAP = 0x0F;
  private const byte _VIDEO_DATA = 0x11;
  private const byte _AUDIO_FRAME = 0x08;
  private const byte _INIT_AUDIO_BUFFERS = 0x03;

  private const ushort _CHUNK_INIT_AUDIO = 0;
  private const ushort _CHUNK_AUDIO_ONLY = 1;
  private const ushort _CHUNK_INIT_VIDEO = 2;
  private const ushort _CHUNK_VIDEO = 3;

  // ============================================================================================
  // Opening
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void AFileNotOpeningWithTheSignatureIsRefused() {
    var failure = Assert.Throws<NotSupportedException>(() => MveContainer.FromBytes(new byte[32]));
    Assert.That(failure!.Message, Does.Contain("Interplay MVE"));
  }

  [Test]
  [Category("Unit")]
  public void AFileWithNoInitVideoBuffersOpcodeIsRefused() {
    var file = _File([_Chunk(_CHUNK_VIDEO, _Opcode(_VIDEO_DATA, 0, []))]);

    var failure = Assert.Throws<InvalidDataException>(() => MveContainer.FromBytes(file));
    Assert.That(failure!.Message, Does.Contain("INIT_VIDEO_BUFFERS"));
  }

  [Test]
  [Category("Unit")]
  public void PictureSizeIsEightTimesTheStatedMacroblockCount() {
    var file = _File([_Chunk(_CHUNK_INIT_VIDEO, _InitVideoBuffers(4, 3))]);
    var container = MveContainer.FromBytes(file);

    Assert.That(container.Width, Is.EqualTo(32));
    Assert.That(container.Height, Is.EqualTo(24));
  }

  // ============================================================================================
  // Streams
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void AFileWithNoSoundDeclaresOneStream() {
    var file = _File([_Chunk(_CHUNK_INIT_VIDEO, _InitVideoBuffers(1, 1))]);
    var container = MveContainer.FromBytes(file);

    var streams = MveContainer.Streams(container);
    Assert.That(streams, Has.Count.EqualTo(1));
    Assert.That(streams[0].Kind, Is.EqualTo(MediaStreamKind.Video));
  }

  [Test]
  [Category("Unit")]
  public void AFileWithSoundDeclaresTwoStreams() {
    var audioInit = new byte[8];
    BinaryPrimitives.WriteUInt16LittleEndian(audioInit.AsSpan(4), 22050);
    var file = _File([
      _Chunk(_CHUNK_INIT_AUDIO, _Opcode(_INIT_AUDIO_BUFFERS, 0, audioInit)),
      _Chunk(_CHUNK_INIT_VIDEO, _InitVideoBuffers(1, 1)),
    ]);
    var container = MveContainer.FromBytes(file);

    var streams = MveContainer.Streams(container);
    Assert.That(streams, Has.Count.EqualTo(2));
    Assert.That(streams[1].Kind, Is.EqualTo(MediaStreamKind.Audio));
    Assert.That(streams[1].TimeBase, Is.EqualTo(new Rational(1, 22050)));
  }

  [Test]
  [Category("Unit")]
  public void TheDeclaredFrameCountIsHowManyVideoDataOpcodesTheFileHolds() {
    var file = _File([
      _Chunk(_CHUNK_INIT_VIDEO, _InitVideoBuffers(1, 1)),
      _Chunk(_CHUNK_VIDEO, [.. _Opcode(_DECODING_MAP, 0, [0]), .. _Opcode(_VIDEO_DATA, 0, _VideoDataPayload(1, 1, []))]),
      _Chunk(_CHUNK_VIDEO, [.. _Opcode(_DECODING_MAP, 0, [0]), .. _Opcode(_VIDEO_DATA, 0, _VideoDataPayload(1, 1, []))]),
    ]);
    var container = MveContainer.FromBytes(file);

    Assert.That(MveContainer.Streams(container)[0].DeclaredFrameCount, Is.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void TheFrameDurationComesFromRateTimesSubdivision() {
    var timer = new byte[6];
    BinaryPrimitives.WriteUInt32LittleEndian(timer, 8341);
    BinaryPrimitives.WriteUInt16LittleEndian(timer.AsSpan(4), 8);
    var file = _File([
      _Chunk(_CHUNK_VIDEO, _Opcode(_CREATE_TIMER, 0, timer)),
      _Chunk(_CHUNK_INIT_VIDEO, _InitVideoBuffers(1, 1)),
    ]);
    var container = MveContainer.FromBytes(file);

    Assert.That(container.FrameDurationMicroseconds, Is.EqualTo(8341L * 8));
  }

  // ============================================================================================
  // Packets
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void OnlyTheFirstPictureIsReportedAsAKeyFrame() {
    var picture = (byte[])[.. _Opcode(_DECODING_MAP, 0, [0]), .. _Opcode(_VIDEO_DATA, 0, _VideoDataPayload(1, 1, []))];
    var file = _File([
      _Chunk(_CHUNK_INIT_VIDEO, _InitVideoBuffers(1, 1)),
      _Chunk(_CHUNK_VIDEO, picture),
      _Chunk(_CHUNK_VIDEO, picture),
    ]);
    var container = MveContainer.FromBytes(file);

    var pictures = MveContainer.ReadPackets(container).Where(p => p.StreamIndex == 0 && _IsVideoData(p.Data.Span)).ToArray();
    Assert.That(pictures, Has.Length.EqualTo(2));
    Assert.That(pictures[0].IsKeyFrame, Is.True);
    Assert.That(pictures[1].IsKeyFrame, Is.False);
  }

  [Test]
  [Category("Unit")]
  public void SoundOpcodesGoOnStreamOneAndVideoOpcodesOnStreamZero() {
    var audioInit = new byte[8];
    var audioFrame = new byte[6];
    BinaryPrimitives.WriteUInt16LittleEndian(audioFrame.AsSpan(4), 100); // sample count
    var file = _File([
      _Chunk(_CHUNK_INIT_AUDIO, _Opcode(_INIT_AUDIO_BUFFERS, 0, audioInit)),
      _Chunk(_CHUNK_INIT_VIDEO, _InitVideoBuffers(1, 1)),
      _Chunk(_CHUNK_AUDIO_ONLY, _Opcode(_AUDIO_FRAME, 0, audioFrame)),
      _Chunk(_CHUNK_VIDEO, [.. _Opcode(_DECODING_MAP, 0, [0]), .. _Opcode(_VIDEO_DATA, 0, _VideoDataPayload(1, 1, []))]),
    ]);
    var container = MveContainer.FromBytes(file);

    var packets = MveContainer.ReadPackets(container).ToArray();
    Assert.That(packets.Any(p => p.StreamIndex == 1), Is.True, "the audio frame should be on stream 1");
    Assert.That(packets.Where(p => p.StreamIndex == 0).Select(p => p.Data.Span[2]), Does.Not.Contain(_AUDIO_FRAME));
  }

  [Test]
  [Category("Unit")]
  public void APacketCarriesItsOwnOpcodeHeader() {
    var file = _File([_Chunk(_CHUNK_INIT_VIDEO, _InitVideoBuffers(1, 1))]);
    var container = MveContainer.FromBytes(file);

    var packet = MveContainer.ReadPackets(container).Single();
    Assert.That(packet.Data.Span[2], Is.EqualTo(_INIT_VIDEO_BUFFERS));
  }

  [Test]
  [Category("Unit")]
  public void PaletteAndMapOpcodesAreAlsoCarriedAsStreamZeroPackets() {
    var file = _File([
      _Chunk(_CHUNK_INIT_VIDEO, _InitVideoBuffers(1, 1)),
      _Chunk(_CHUNK_VIDEO, [.. _Opcode(_SET_PALETTE, 0, [0, 0, 1, 0, 63, 63, 63]), .. _Opcode(_DECODING_MAP, 0, [0]), .. _Opcode(_VIDEO_DATA, 0, _VideoDataPayload(1, 1, []))]),
    ]);
    var container = MveContainer.FromBytes(file);

    var types = MveContainer.ReadPackets(container).Where(p => p.StreamIndex == 0).Select(p => p.Data.Span[2]).ToArray();
    Assert.That(types, Is.EqualTo(new byte[] { _INIT_VIDEO_BUFFERS, _SET_PALETTE, _DECODING_MAP, _VIDEO_DATA }));
  }

  // ============================================================================================
  // Helpers
  // ============================================================================================

  private static readonly byte[] _Signature = [0x49, 0x6E, 0x74, 0x65, 0x72, 0x70, 0x6C, 0x61, 0x79, 0x20, 0x4D, 0x56, 0x45, 0x20, 0x46, 0x69, 0x6C, 0x65, 0x1A, 0x00];
  private static readonly byte[] _MagicParameters = [0x1A, 0x00, 0x00, 0x01, 0x33, 0x11];

  private static byte[] _Opcode(byte type, byte version, byte[] payload) {
    var opcode = new byte[4 + payload.Length];
    BinaryPrimitives.WriteUInt16LittleEndian(opcode, (ushort)payload.Length);
    opcode[2] = type;
    opcode[3] = version;
    payload.CopyTo(opcode, 4);
    return opcode;
  }

  private static byte[] _InitVideoBuffers(int widthBlocks, int heightBlocks) {
    var payload = new byte[4];
    BinaryPrimitives.WriteUInt16LittleEndian(payload, (ushort)widthBlocks);
    BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(2), (ushort)heightBlocks);
    return _Opcode(_INIT_VIDEO_BUFFERS, 0, payload);
  }

  private static byte[] _VideoDataPayload(int widthBlocks, int heightBlocks, byte[] blockData) {
    var payload = new byte[14 + blockData.Length];
    BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(8), (ushort)widthBlocks);
    BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(10), (ushort)heightBlocks);
    blockData.CopyTo(payload, 14);
    return payload;
  }

  private static bool _IsVideoData(ReadOnlySpan<byte> opcode) => opcode[2] == _VIDEO_DATA;

  private static byte[] _Chunk(ushort type, byte[] opcodes) {
    var chunk = new byte[4 + opcodes.Length];
    BinaryPrimitives.WriteUInt16LittleEndian(chunk, (ushort)opcodes.Length);
    BinaryPrimitives.WriteUInt16LittleEndian(chunk.AsSpan(2), type);
    opcodes.CopyTo(chunk, 4);
    return chunk;
  }

  private static byte[] _File(IReadOnlyList<byte[]> chunks) {
    var totalLength = _Signature.Length + _MagicParameters.Length + chunks.Sum(c => c.Length);
    var file = new byte[totalLength];
    _Signature.CopyTo(file, 0);
    _MagicParameters.CopyTo(file, _Signature.Length);
    var at = _Signature.Length + _MagicParameters.Length;
    foreach (var chunk in chunks) {
      chunk.CopyTo(file, at);
      at += chunk.Length;
    }

    return file;
  }
}
