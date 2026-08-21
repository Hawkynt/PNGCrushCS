using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FileFormat.Core;

namespace FileFormat.RoqVideo.Tests;

/// <summary>
/// The RoQ container's demuxing behaviour: which chunk becomes which stream's packet, and the two
/// chunk types that become no packet at all.
/// </summary>
/// <remarks>
/// Chunk-level bit packing is not exercised here — <see cref="Codecs.Tests.RoqVideoDecoderTests"/>
/// covers every block type and the codebook's own ambiguity, and three real files spanning 1 338
/// pictures were compared frame by frame against ffmpeg's decode with no differing plane anywhere.
/// What is worth a hand-built fixture is what a real file's own shape does not force a reader to
/// exercise: a signature that is not this format's, an <c>INFO</c> chunk missing altogether, a
/// <c>HANG</c> or <c>PACKET</c> chunk stepped over as neither a picture nor a sample, and which
/// packet — the first one only — is reported as a key frame.
/// </remarks>
[TestFixture]
public sealed class RoqReaderTests {

  private const ushort _INFO = 0x1001;
  private const ushort _QUAD_CODEBOOK = 0x1002;
  private const ushort _QUAD_VQ = 0x1011;
  private const ushort _SOUND_MONO = 0x1020;
  private const ushort _SOUND_STEREO = 0x1021;
  private const ushort _HANG = 0x1013;
  private const ushort _PACKET = 0x1030;

  // ============================================================================================
  // Opening
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void AFileNotOpeningWithTheSignatureIsRefused() {
    var failure = Assert.Throws<NotSupportedException>(() => RoqContainer.FromBytes(new byte[16]));
    Assert.That(failure!.Message, Does.Contain("signature"));
  }

  [Test]
  [Category("Unit")]
  public void AFileWithNoInfoChunkIsRefused() {
    var file = _File([_Vq(0, 0, [])]);

    var failure = Assert.Throws<InvalidDataException>(() => RoqContainer.FromBytes(file));
    Assert.That(failure!.Message, Does.Contain("RoQ_INFO"));
  }

  [Test]
  [Category("Unit")]
  public void PictureSizeComesFromInfoWhereverItSitsInTheFile() {
    // A real file's INFO chunk routinely sits well past the file's first chunk. A reader keyed to a
    // fixed offset would size the picture wrong or not find it at all.
    var file = _File([_Chunk(_SOUND_STEREO, 0, new byte[40]), _Info(32, 16), _Codebook(1, 1), _Vq(0, 0, [])]);
    var container = RoqContainer.FromBytes(file);

    Assert.That(container.Width, Is.EqualTo(32));
    Assert.That(container.Height, Is.EqualTo(16));
  }

  // ============================================================================================
  // Streams
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void AFileWithNoSoundDeclaresOneStream() {
    var file = _File([_Info(16, 16)]);
    var container = RoqContainer.FromBytes(file);

    var streams = RoqContainer.Streams(container);
    Assert.That(streams, Has.Count.EqualTo(1));
    Assert.That(streams[0].Kind, Is.EqualTo(MediaStreamKind.Video));
  }

  [Test]
  [Category("Unit")]
  public void AFileWithSoundDeclaresTwoStreams() {
    var file = _File([_Info(16, 16), _Chunk(_SOUND_MONO, 0, new byte[10])]);
    var container = RoqContainer.FromBytes(file);

    var streams = RoqContainer.Streams(container);
    Assert.That(streams, Has.Count.EqualTo(2));
    Assert.That(streams[1].Kind, Is.EqualTo(MediaStreamKind.Audio));
  }

  [Test]
  [Category("Unit")]
  public void TheDeclaredFrameCountIsHowManyQuadVqChunksTheFileHolds() {
    var file = _File([_Info(16, 16), _Vq(0, 0, []), _Vq(0, 0, []), _Vq(0, 0, [])]);
    var container = RoqContainer.FromBytes(file);

    Assert.That(RoqContainer.Streams(container)[0].DeclaredFrameCount, Is.EqualTo(3));
  }

  // ============================================================================================
  // Packets
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void OnlyTheFirstPictureIsReportedAsAKeyFrame() {
    var file = _File([_Info(16, 16), _Vq(0, 0, []), _Vq(0, 0, [])]);
    var container = RoqContainer.FromBytes(file);

    var pictures = RoqContainer.ReadPackets(container).Where(p => p.StreamIndex == 0 && _IsQuadVq(p.Data.Span)).ToArray();
    Assert.That(pictures, Has.Length.EqualTo(2));
    Assert.That(pictures[0].IsKeyFrame, Is.True);
    Assert.That(pictures[1].IsKeyFrame, Is.False);
  }

  [Test]
  [Category("Unit")]
  public void HangAndPacketChunksBecomeNoPacketAtAll() {
    var file = _File([_Info(16, 16), _Chunk(_HANG, 0, []), _Chunk(_PACKET, 0, [])]);
    var container = RoqContainer.FromBytes(file);

    var packets = RoqContainer.ReadPackets(container).ToArray();
    // Only the INFO chunk becomes a packet; HANG and PACKET carry nothing for a caller to use.
    Assert.That(packets, Has.Length.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void SoundChunksGoOnStreamOneAndPictureChunksOnStreamZero() {
    var file = _File([_Info(16, 16), _Chunk(_SOUND_MONO, 0, new byte[4]), _Vq(0, 0, [])]);
    var container = RoqContainer.FromBytes(file);

    // A sound packet is the DPCM payload alone, with no chunk header — unlike a video packet, it has
    // no ambiguity for a decoder to resolve from its own bytes, since which of mono or stereo it is
    // is already stated by which stream it belongs to.
    var packets = RoqContainer.ReadPackets(container).ToArray();
    var soundPacket = packets.Single(p => p.StreamIndex == 1);
    var picturePacket = packets.Single(p => _IsQuadVq(p.Data.Span));

    Assert.That(soundPacket.Data.Length, Is.EqualTo(4));

    Assert.That(soundPacket.StreamIndex, Is.EqualTo(1));
    Assert.That(picturePacket.StreamIndex, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void APacketCarriesItsOwnChunkHeader() {
    // The codec reads its own chunk id and argument out of the packet's own bytes, the way a
    // Cinepak or Microsoft Video 1 packet carries its own frame header.
    var file = _File([_Info(16, 16)]);
    var container = RoqContainer.FromBytes(file);

    var packet = RoqContainer.ReadPackets(container).Single();
    Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(packet.Data.Span), Is.EqualTo(_INFO));
  }

  // ============================================================================================
  // Helpers
  // ============================================================================================

  private static readonly byte[] _Signature = [0x84, 0x10, 0xFF, 0xFF, 0xFF, 0xFF, 0x1E, 0x00];

  private static byte[] _Chunk(ushort id, ushort argument, byte[] payload) {
    var chunk = new byte[8 + payload.Length];
    var span = chunk.AsSpan();
    BinaryPrimitives.WriteUInt16LittleEndian(span, id);
    BinaryPrimitives.WriteUInt32LittleEndian(span[2..], (uint)payload.Length);
    BinaryPrimitives.WriteUInt16LittleEndian(span[6..], argument);
    payload.CopyTo(span[8..]);
    return chunk;
  }

  private static byte[] _Info(int width, int height) {
    var payload = new byte[8];
    var span = payload.AsSpan();
    BinaryPrimitives.WriteUInt16LittleEndian(span, (ushort)width);
    BinaryPrimitives.WriteUInt16LittleEndian(span[2..], (ushort)height);
    BinaryPrimitives.WriteUInt16LittleEndian(span[4..], 8);
    BinaryPrimitives.WriteUInt16LittleEndian(span[6..], 4);
    return _Chunk(_INFO, 0, payload);
  }

  private static byte[] _Codebook(int cb2Count, int cb4Count) {
    var payload = new byte[cb2Count * 6 + cb4Count * 4];
    var argument = (ushort)(((cb2Count & 0xFF) << 8) | (cb4Count & 0xFF));
    return _Chunk(_QUAD_CODEBOOK, argument, payload);
  }

  private static byte[] _Vq(sbyte meanX, sbyte meanY, byte[] body) {
    var argument = (ushort)(((byte)meanX << 8) | (byte)meanY);
    return _Chunk(_QUAD_VQ, argument, body);
  }

  private static bool _IsQuadVq(ReadOnlySpan<byte> chunk) => BinaryPrimitives.ReadUInt16LittleEndian(chunk) == _QUAD_VQ;

  private static byte[] _File(IReadOnlyList<byte[]> chunks) {
    var totalLength = _Signature.Length + chunks.Sum(c => c.Length);
    var file = new byte[totalLength];
    _Signature.CopyTo(file, 0);
    var at = _Signature.Length;
    foreach (var chunk in chunks) {
      chunk.CopyTo(file, at);
      at += chunk.Length;
    }

    return file;
  }
}
