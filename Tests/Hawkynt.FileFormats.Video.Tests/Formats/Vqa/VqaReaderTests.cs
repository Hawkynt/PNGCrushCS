using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FileFormat.Core;

namespace FileFormat.Vqa.Tests;

/// <summary>
/// The VQA container's demuxing behaviour: which chunk becomes which stream's packet, how the header
/// fields are read, and how it treats a file that runs out of room for its next chunk — the shape a
/// real recording is free to take.
/// </summary>
/// <remarks>
/// Chunk-level codebook and index-table decoding is not exercised here — <see
/// cref="Codecs.Tests.VqaVideoDecoderTests"/> and <see cref="Codecs.Vqa.Tests.VqaFormat80Tests"/> cover
/// that, and three real files spanning 245 pictures were compared frame by frame against ffmpeg's
/// decode with no differing sample anywhere. What is worth a hand-built fixture is what a real file's
/// own shape does not force a reader to exercise: a signature that is not <c>FORM</c>/<c>WVQA</c>, a
/// <c>FORM</c> chunk whose own stated size undershoots the real file (every real sample this reader was
/// measured against has one that either matches or does exactly this), and which chunk types become
/// which stream's packets.
/// </remarks>
[TestFixture]
public sealed class VqaReaderTests {

  // ============================================================================================
  // Opening
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void AFileNotOpeningWithFormWvqaIsRefused() {
    var failure = Assert.Throws<NotSupportedException>(() => VqaContainer.FromBytes(new byte[32]));
    Assert.That(failure!.Message, Does.Contain("WVQA"));
  }

  [Test]
  [Category("Unit")]
  public void AFileWithNoVqhdChunkIsRefused() {
    var file = _File(width: 0, height: 0, includeVqhd: false, chunks: []);

    Assert.Throws<InvalidDataException>(() => VqaContainer.FromBytes(file));
  }

  [Test]
  [Category("Unit")]
  public void HeaderFieldsAreReadLittleEndian() {
    var file = _File(width: 320, height: 156, blockWidth: 4, blockHeight: 2, frames: 85, sampleRate: 22050, channels: 1, chunks: []);
    var container = VqaContainer.FromBytes(file);

    Assert.That(container.Width, Is.EqualTo(320));
    Assert.That(container.Height, Is.EqualTo(156));
    Assert.That(container.BlockWidth, Is.EqualTo(4));
    Assert.That(container.BlockHeight, Is.EqualTo(2));
    Assert.That(container.VideoFrameCount, Is.EqualTo(85));
    Assert.That(container.AudioSampleRate, Is.EqualTo(22050));
    Assert.That(container.AudioChannels, Is.EqualTo(1));
  }

  /// <summary>A FORM chunk's own stated size is not trustworthy — measured against a real file, one
  /// covers only its header chunks and the real file runs on for megabytes past it.</summary>
  [Test]
  [Category("Unit")]
  public void ChunksPastWhereFormSaysItEndsAreStillWalked() {
    var vqfr = _Vqfr([]);
    var file = _FileWithUndersizedForm(width: 4, height: 2, blockWidth: 4, blockHeight: 2, frames: 1, chunks: [vqfr]);
    var container = VqaContainer.FromBytes(file);

    var packets = VqaContainer.ReadPackets(container).ToArray();
    Assert.That(packets, Has.Length.EqualTo(1));
  }

  // ============================================================================================
  // Streams
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void AFileWithNoSoundDeclaresOneStream() {
    var file = _File(width: 4, height: 2, blockWidth: 4, blockHeight: 2, frames: 0, sampleRate: 0, channels: 0, chunks: []);
    var container = VqaContainer.FromBytes(file);

    var streams = VqaContainer.Streams(container);
    Assert.That(streams, Has.Count.EqualTo(1));
    Assert.That(streams[0].Kind, Is.EqualTo(MediaStreamKind.Video));
  }

  [Test]
  [Category("Unit")]
  public void AFileWithSoundDeclaresTwoStreams() {
    var file = _File(width: 4, height: 2, blockWidth: 4, blockHeight: 2, frames: 0, sampleRate: 22050, channels: 1, chunks: []);
    var container = VqaContainer.FromBytes(file);

    var streams = VqaContainer.Streams(container);
    Assert.That(streams, Has.Count.EqualTo(2));
    Assert.That(streams[1].Kind, Is.EqualTo(MediaStreamKind.Audio));
    Assert.That(streams[1].TimeBase, Is.EqualTo(new Rational(1, 22050)));
  }

  [Test]
  [Category("Unit")]
  public void TheVideoStreamCarriesTheHeaderPayloadAsPrivateData() {
    var file = _File(width: 4, height: 2, blockWidth: 4, blockHeight: 2, frames: 0, sampleRate: 0, channels: 0, chunks: []);
    var container = VqaContainer.FromBytes(file);

    Assert.That(VqaContainer.Streams(container)[0].CodecPrivateData.Length, Is.EqualTo(42));
  }

  // ============================================================================================
  // Packets
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void VqfrChunksGoOnStreamZeroAndSoundChunksOnStreamOne() {
    var file = _File(width: 4, height: 2, blockWidth: 4, blockHeight: 2, frames: 1, sampleRate: 22050, channels: 1, chunks: [
      _Chunk("SND2", [1, 2, 3, 4]),
      _Vqfr([]),
    ]);
    var container = VqaContainer.FromBytes(file);

    var packets = VqaContainer.ReadPackets(container).ToArray();
    Assert.That(packets.Select(p => p.StreamIndex), Is.EqualTo(new[] { 1, 0 }));
  }

  [Test]
  [Category("Unit")]
  public void APacketCarriesItsPictureSSubChunksVerbatim() {
    var subChunk = _Chunk("CPL0", new byte[768]);
    var file = _File(width: 4, height: 2, blockWidth: 4, blockHeight: 2, frames: 1, sampleRate: 0, channels: 0, chunks: [_Vqfr(subChunk)]);
    var container = VqaContainer.FromBytes(file);

    var packet = VqaContainer.ReadPackets(container).Single();
    Assert.That(packet.Data.Span[..4].ToArray(), Is.EqualTo("CPL0"u8.ToArray()));
  }

  [Test]
  [Category("Unit")]
  public void UnrecognisedTopLevelChunksAreSkippedRatherThanBreakingTheWalk() {
    var file = _File(width: 4, height: 2, blockWidth: 4, blockHeight: 2, frames: 1, sampleRate: 0, channels: 0, chunks: [
      _Chunk("PINF", [0, 0, 0, 0]),
      _Chunk("CMDS", new byte[8]),
      _Vqfr([]),
    ]);
    var container = VqaContainer.FromBytes(file);

    var packets = VqaContainer.ReadPackets(container).ToArray();
    Assert.That(packets, Has.Length.EqualTo(1));
  }

  // ============================================================================================
  // Helpers
  // ============================================================================================

  private static byte[] _Chunk(string id, byte[] payload) {
    var chunk = new byte[8 + payload.Length + (payload.Length & 1)];
    System.Text.Encoding.ASCII.GetBytes(id).CopyTo(chunk, 0);
    BinaryPrimitives.WriteUInt32BigEndian(chunk.AsSpan(4), (uint)payload.Length);
    payload.CopyTo(chunk, 8);
    return chunk;
  }

  private static byte[] _Vqfr(byte[] subChunks) {
    var payload = subChunks;
    return _Chunk("VQFR", payload);
  }

  private static byte[] _Header(int width, int height, int blockWidth, int blockHeight, int frames, int sampleRate, int channels) {
    var payload = new byte[42];
    BinaryPrimitives.WriteUInt16LittleEndian(payload, 2); // version
    BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(4), (ushort)frames);
    BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(6), (ushort)width);
    BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(8), (ushort)height);
    payload[10] = (byte)blockWidth;
    payload[11] = (byte)blockHeight;
    BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(24), (ushort)sampleRate);
    payload[26] = (byte)channels;
    return _Chunk("VQHD", payload);
  }

  private static byte[] _File(int width, int height, int blockWidth, int blockHeight, int frames, int sampleRate, int channels, IReadOnlyList<byte[]> chunks) {
    var vqhd = _Header(width, height, blockWidth, blockHeight, frames, sampleRate, channels);
    var body = vqhd.Concat(chunks.SelectMany(c => c)).ToArray();
    var file = new byte[12 + body.Length];
    System.Text.Encoding.ASCII.GetBytes("FORM").CopyTo(file, 0);
    BinaryPrimitives.WriteUInt32BigEndian(file.AsSpan(4), (uint)(4 + body.Length));
    System.Text.Encoding.ASCII.GetBytes("WVQA").CopyTo(file, 8);
    body.CopyTo(file, 12);
    return file;
  }

  private static byte[] _File(int width, int height, bool includeVqhd, IReadOnlyList<byte[]> chunks) {
    var body = includeVqhd
      ? _Header(width, height, 1, 1, 0, 0, 0).Concat(chunks.SelectMany(c => c)).ToArray()
      : chunks.SelectMany(c => c).ToArray();
    var file = new byte[12 + body.Length];
    System.Text.Encoding.ASCII.GetBytes("FORM").CopyTo(file, 0);
    BinaryPrimitives.WriteUInt32BigEndian(file.AsSpan(4), (uint)(4 + body.Length));
    System.Text.Encoding.ASCII.GetBytes("WVQA").CopyTo(file, 8);
    body.CopyTo(file, 12);
    return file;
  }

  /// <summary>A file whose FORM chunk states a size covering only VQHD, the same shape a real sample
  /// takes, with the real chunks running on past it.</summary>
  private static byte[] _FileWithUndersizedForm(int width, int height, int blockWidth, int blockHeight, int frames, IReadOnlyList<byte[]> chunks) {
    var vqhd = _Header(width, height, blockWidth, blockHeight, frames, 0, 0);
    var body = vqhd.Concat(chunks.SelectMany(c => c)).ToArray();
    var file = new byte[12 + body.Length];
    System.Text.Encoding.ASCII.GetBytes("FORM").CopyTo(file, 0);
    BinaryPrimitives.WriteUInt32BigEndian(file.AsSpan(4), (uint)(4 + vqhd.Length)); // undersized on purpose
    System.Text.Encoding.ASCII.GetBytes("WVQA").CopyTo(file, 8);
    body.CopyTo(file, 12);
    return file;
  }
}
