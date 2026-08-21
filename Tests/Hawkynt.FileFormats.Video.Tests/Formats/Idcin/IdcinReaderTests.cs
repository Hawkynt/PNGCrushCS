using System;
using System.Buffers.Binary;
using System.Linq;
using FileFormat.Core;

namespace FileFormat.Idcin.Tests;

/// <summary>
/// The id Cinematic container's demuxing behaviour: which frame command becomes which stream's packet,
/// how many pictures a file declares, and how it treats a file whose last frame command does not fully
/// fit in what remains — the shape of one of the two real files this was measured against.
/// </summary>
/// <remarks>
/// Frame-level Huffman decoding is not exercised here — <see
/// cref="Codecs.Tests.IdcinVideoDecoderTests"/> covers the tree and the palette, and two real files
/// spanning 130 pictures were compared frame by frame against ffmpeg's decode with no differing sample
/// anywhere. What is worth a hand-built fixture is what a real file's own shape does not force a reader
/// to exercise: a header stating an implausible picture size, a file with no audio at all, and a
/// command that plainly ends the file rather than one the file simply runs out of room for.
/// </remarks>
[TestFixture]
public sealed class IdcinReaderTests {

  private const int _HEADER_LENGTH = 20;
  private const int _HUFFMAN_TABLE_LENGTH = 64 * 1024;
  private const uint _COMMAND_NONE = 0;
  private const uint _COMMAND_PALETTE = 1;
  private const uint _COMMAND_END_OF_FILE = 2;

  // ============================================================================================
  // Opening
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void AFileTooShortForTheHeaderAndTableIsRefused() {
    var failure = Assert.Throws<NotSupportedException>(() => IdcinContainer.FromBytes(new byte[32]));
    Assert.That(failure!.Message, Does.Contain("id Cinematic"));
  }

  [Test]
  [Category("Unit")]
  public void AHeaderStatingAnImplausiblePictureSizeIsRefused() {
    var file = _File(width: 0, height: 240, sampleRate: 0, bytesPerSample: 0, channels: 0, chunks: []);

    var failure = Assert.Throws<NotSupportedException>(() => IdcinContainer.FromBytes(file));
    Assert.That(failure!.Message, Does.Contain("id Cinematic"));
  }

  [Test]
  [Category("Unit")]
  public void APlausibleSilentHeaderOpens() {
    var file = _File(width: 320, height: 240, sampleRate: 0, bytesPerSample: 0, channels: 0, chunks: []);
    var container = IdcinContainer.FromBytes(file);

    Assert.That(container.Width, Is.EqualTo(320));
    Assert.That(container.Height, Is.EqualTo(240));
  }

  // ============================================================================================
  // Streams
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void AFileWithNoSoundDeclaresOneStream() {
    var file = _File(320, 240, 0, 0, 0, []);
    var container = IdcinContainer.FromBytes(file);

    var streams = IdcinContainer.Streams(container);
    Assert.That(streams, Has.Count.EqualTo(1));
    Assert.That(streams[0].Kind, Is.EqualTo(MediaStreamKind.Video));
  }

  [Test]
  [Category("Unit")]
  public void AFileWithSoundDeclaresTwoStreams() {
    var file = _File(320, 240, 22050, 2, 2, []);
    var container = IdcinContainer.FromBytes(file);

    var streams = IdcinContainer.Streams(container);
    Assert.That(streams, Has.Count.EqualTo(2));
    Assert.That(streams[1].Kind, Is.EqualTo(MediaStreamKind.Audio));
    Assert.That(streams[1].TimeBase, Is.EqualTo(new Rational(1, 22050)));
  }

  [Test]
  [Category("Unit")]
  public void TheVideoStreamCarriesTheHuffmanTableAsPrivateData() {
    var file = _File(1, 1, 0, 0, 0, []);
    var container = IdcinContainer.FromBytes(file);

    var streams = IdcinContainer.Streams(container);
    Assert.That(streams[0].CodecPrivateData.Length, Is.EqualTo(_HUFFMAN_TABLE_LENGTH));
  }

  [Test]
  [Category("Unit")]
  public void TheDeclaredFrameCountIsHowManyVideoCommandsTheFileHolds() {
    var file = _File(1, 1, 0, 0, 0, [
      _VideoChunk(_COMMAND_NONE, null, []),
      _VideoChunk(_COMMAND_NONE, null, []),
    ]);
    var container = IdcinContainer.FromBytes(file);

    Assert.That(IdcinContainer.Streams(container)[0].DeclaredFrameCount, Is.EqualTo(2));
  }

  // ============================================================================================
  // Packets
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void EveryVideoPacketGoesOnStreamZero() {
    var file = _File(1, 1, 0, 0, 0, [
      _VideoChunk(_COMMAND_NONE, null, []),
      _VideoChunk(_COMMAND_NONE, null, []),
    ]);
    var container = IdcinContainer.FromBytes(file);

    var packets = IdcinContainer.ReadPackets(container).ToArray();
    Assert.That(packets, Has.Length.EqualTo(2));
    Assert.That(packets.All(p => p.StreamIndex == 0), Is.True);
  }

  [Test]
  [Category("Unit")]
  public void AnEndOfFileCommandStopsTheWalkWithoutBecomingAPacket() {
    var endOfFile = new byte[4];
    BinaryPrimitives.WriteUInt32LittleEndian(endOfFile, _COMMAND_END_OF_FILE);
    var file = _File(1, 1, 0, 0, 0, [
      _VideoChunk(_COMMAND_NONE, null, []),
      endOfFile,
      _VideoChunk(_COMMAND_NONE, null, []), // never reached
    ]);
    var container = IdcinContainer.FromBytes(file);

    var packets = IdcinContainer.ReadPackets(container).ToArray();
    Assert.That(packets, Has.Length.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void AFileThatRunsOutOfRoomForItsNextChunkStopsCleanlyRatherThanThrowing() {
    var secondChunk = _VideoChunk(_COMMAND_NONE, null, []);
    var file = _File(1, 1, 0, 0, 0, [
      _VideoChunk(_COMMAND_NONE, null, []),
      secondChunk,
    ]);
    // Trim the file to end two bytes into the second video command's own four-byte word, leaving the
    // first command whole and the second short of even its command word.
    var truncated = file[..^(secondChunk.Length - 2)];
    var container = IdcinContainer.FromBytes(truncated);

    Assert.DoesNotThrow(() => IdcinContainer.ReadPackets(container).ToArray());
    Assert.That(IdcinContainer.ReadPackets(container).ToArray(), Has.Length.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void APacketCarriesItsOwnCommandWord() {
    var file = _File(1, 1, 0, 0, 0, [_VideoChunk(_COMMAND_NONE, null, [7, 8])]);
    var container = IdcinContainer.FromBytes(file);

    var packet = IdcinContainer.ReadPackets(container).Single();
    var command = BinaryPrimitives.ReadUInt32LittleEndian(packet.Data.Span);
    Assert.That(command, Is.EqualTo(_COMMAND_NONE));
  }

  [Test]
  [Category("Unit")]
  public void SoundChunksGoOnStreamOneInterleavedWithVideoOnStreamZero() {
    var file = _File(1, 1, 8000, 1, 1, [
      _VideoChunk(_COMMAND_NONE, null, []),
      new byte[8000 / 14], // one audio chunk's worth of mono 8-bit samples
      _VideoChunk(_COMMAND_NONE, null, []),
    ]);
    var container = IdcinContainer.FromBytes(file);

    var packets = IdcinContainer.ReadPackets(container).ToArray();
    Assert.That(packets.Select(p => p.StreamIndex), Is.EqualTo(new[] { 0, 1, 0 }));
  }

  // ============================================================================================
  // Helpers
  // ============================================================================================

  private static byte[] _VideoChunk(uint command, byte[]? palette, byte[] huffmanBytes) {
    var hasPalette = command == _COMMAND_PALETTE;
    var length = 4 + (hasPalette ? 768 : 0) + 8 + huffmanBytes.Length;
    var chunk = new byte[length];
    BinaryPrimitives.WriteUInt32LittleEndian(chunk, command);
    var at = 4;
    if (hasPalette) {
      (palette ?? new byte[768]).CopyTo(chunk, at);
      at += 768;
    }

    BinaryPrimitives.WriteUInt32LittleEndian(chunk.AsSpan(at), (uint)(4 + huffmanBytes.Length));
    BinaryPrimitives.WriteUInt32LittleEndian(chunk.AsSpan(at + 4), 0);
    huffmanBytes.CopyTo(chunk, at + 8);
    return chunk;
  }

  private static byte[] _File(int width, int height, int sampleRate, int bytesPerSample, int channels, byte[][] chunks) {
    var totalLength = _HEADER_LENGTH + _HUFFMAN_TABLE_LENGTH + chunks.Sum(c => c.Length);
    var file = new byte[totalLength];
    BinaryPrimitives.WriteUInt32LittleEndian(file, (uint)width);
    BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(4), (uint)height);
    BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(8), (uint)sampleRate);
    BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(12), (uint)bytesPerSample);
    BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(16), (uint)channels);

    var at = _HEADER_LENGTH + _HUFFMAN_TABLE_LENGTH;
    foreach (var chunk in chunks) {
      chunk.CopyTo(file, at);
      at += chunk.Length;
    }

    return file;
  }
}
