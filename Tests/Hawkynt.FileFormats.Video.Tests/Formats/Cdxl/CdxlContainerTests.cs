using System;
using System.Buffers.Binary;
using System.Linq;
using FileFormat.Core;

namespace FileFormat.Cdxl.Tests;

/// <summary>
/// CDXL's demuxing behaviour: how a chunk's own header sizes its video and audio packets, how many
/// chunks a file declares, and how a truncated or genuinely unrecognised chunk is treated — the shape
/// four real files from <c>samples.ffmpeg.org/cdxl/</c> forced this reader to settle. Frame-level
/// pixel decoding is not exercised here — <see cref="Codecs.Tests.CdxlVideoDecoderTests"/> covers that,
/// and the same four real files were compared frame by frame against ffmpeg's decode with no differing
/// sample on the encodings this decoder accepts.
/// </summary>
[TestFixture]
public sealed class CdxlContainerTests {

  private const int _HEADER_LENGTH = 32;

  // ============================================================================================
  // Opening
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void AFileTooShortForTheHeaderIsRefused() {
    var failure = Assert.Throws<NotSupportedException>(() => CdxlContainer.FromBytes(new byte[16]));
    Assert.That(failure!.Message, Does.Contain("CDXL"));
  }

  [Test]
  [Category("Unit")]
  public void AHeaderStatingAnUndocumentedFileTypeIsRefused() {
    var chunk = _Chunk(fileType: 9, width: 4, height: 4, planes: 1);
    var failure = Assert.Throws<NotSupportedException>(() => CdxlContainer.FromBytes(chunk));
    Assert.That(failure!.Message, Does.Contain("CDXL"));
  }

  [Test]
  [Category("Unit")]
  public void AHeaderStatingAPlaneArrangementOtherThanBitPlanarIsRefused() {
    var chunk = _Chunk(planeArrangement: 2, width: 4, height: 4, planes: 1);
    Assert.Throws<NotSupportedException>(() => CdxlContainer.FromBytes(chunk));
  }

  [Test]
  [Category("Unit")]
  public void APlausibleSilentHeaderOpens() {
    var file = _Chunk(width: 16, height: 8, planes: 1);
    var container = CdxlContainer.FromBytes(file);

    Assert.That(container.Width, Is.EqualTo(16));
    Assert.That(container.Height, Is.EqualTo(8));
  }

  // ============================================================================================
  // Streams
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void AFileWithNoSoundDeclaresOneStream() {
    var file = _Chunk(width: 8, height: 8, planes: 1, soundSize: 0);
    var container = CdxlContainer.FromBytes(file);

    var streams = CdxlContainer.Streams(container);
    Assert.That(streams, Has.Count.EqualTo(1));
    Assert.That(streams[0].Kind, Is.EqualTo(MediaStreamKind.Video));
  }

  [Test]
  [Category("Unit")]
  public void AFileWithSoundDeclaresTwoStreams() {
    var file = _Chunk(width: 8, height: 8, planes: 1, soundSize: 4);
    var container = CdxlContainer.FromBytes(file);

    var streams = CdxlContainer.Streams(container);
    Assert.That(streams, Has.Count.EqualTo(2));
    Assert.That(streams[1].Kind, Is.EqualTo(MediaStreamKind.Audio));
  }

  [Test]
  [Category("Unit")]
  public void AStereoFileNamesTheStereoSyntheticAudioTag() {
    var file = _Chunk(width: 8, height: 8, planes: 1, soundSize: 4, stereo: true);
    var container = CdxlContainer.FromBytes(file);

    var streams = CdxlContainer.Streams(container);
    Assert.That(streams[1].Codec.ToString(), Is.EqualTo("CDX2"));
  }

  [Test]
  [Category("Unit")]
  public void AMonoFileNamesTheMonoSyntheticAudioTag() {
    var file = _Chunk(width: 8, height: 8, planes: 1, soundSize: 4, stereo: false);
    var container = CdxlContainer.FromBytes(file);

    var streams = CdxlContainer.Streams(container);
    Assert.That(streams[1].Codec.ToString(), Is.EqualTo("CDX1"));
  }

  [Test]
  [Category("Unit")]
  public void TheDeclaredFrameCountIsHowManyChunksTheFileHolds() {
    var file = _File(
      _Chunk(width: 8, height: 8, planes: 1),
      _Chunk(width: 8, height: 8, planes: 1),
      _Chunk(width: 8, height: 8, planes: 1));
    var container = CdxlContainer.FromBytes(file);

    Assert.That(CdxlContainer.Streams(container)[0].DeclaredFrameCount, Is.EqualTo(3));
  }

  // ============================================================================================
  // Packets
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void EveryVideoPacketGoesOnStreamZero() {
    var file = _File(
      _Chunk(width: 8, height: 8, planes: 1),
      _Chunk(width: 8, height: 8, planes: 1));
    var container = CdxlContainer.FromBytes(file);

    var packets = CdxlContainer.ReadPackets(container).ToArray();
    Assert.That(packets.All(p => p.StreamIndex == 0), Is.True);
  }

  [Test]
  [Category("Unit")]
  public void SoundPacketsGoOnStreamOneAlongsideVideoOnStreamZero() {
    var file = _File(
      _Chunk(width: 8, height: 8, planes: 1, soundSize: 4),
      _Chunk(width: 8, height: 8, planes: 1, soundSize: 4));
    var container = CdxlContainer.FromBytes(file);

    var packets = CdxlContainer.ReadPackets(container).ToArray();
    Assert.That(packets.Select(p => p.StreamIndex), Is.EqualTo(new[] { 0, 1, 0, 1 }));
  }

  [Test]
  [Category("Unit")]
  public void AVideoPacketCarriesItsHeaderPaletteAndPixelsTogether() {
    var file = _Chunk(width: 8, height: 8, planes: 1, paletteEntries: 2);
    var container = CdxlContainer.FromBytes(file);

    var packet = CdxlContainer.ReadPackets(container).First();
    // header (32) + palette (2 entries * 2 bytes) + pixels (1 byte/row * 8 rows * 1 plane)
    Assert.That(packet.Data.Length, Is.EqualTo(_HEADER_LENGTH + 4 + 8));
  }

  [Test]
  [Category("Unit")]
  public void ChunkSizeSlackBeyondTheDocumentedFieldsIsSteppedOverRatherThanReadAsData() {
    // Two real files measured carry chunk-size slack the documented fields do not account for — see
    // CdxlChunkReader's remarks. A chunk stating twenty extra bytes of slack still has to land on a
    // second chunk's real header, not twenty bytes into what looks like one.
    var first = _Chunk(width: 8, height: 8, planes: 1, extraSlack: 20);
    var second = _Chunk(width: 8, height: 8, planes: 1);
    var file = new byte[first.Length + second.Length];
    first.CopyTo(file, 0);
    second.CopyTo(file, first.Length);

    var container = CdxlContainer.FromBytes(file);
    Assert.That(container.FrameCount, Is.EqualTo(2));

    var packets = CdxlContainer.ReadPackets(container).ToArray();
    Assert.That(packets, Has.Length.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void AChunkThatDoesNotFullyFitInWhatRemainsStopsTheWalkCleanly() {
    var whole = _Chunk(width: 8, height: 8, planes: 1);
    var truncated = whole[..^4]; // four bytes short of the pixel data it states

    Assert.DoesNotThrow(() => CdxlContainer.FromBytes(truncated));
    var container = CdxlContainer.FromBytes(truncated);
    Assert.That(container.FrameCount, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void ALaterChunkStatingAnUnsupportedPlaneArrangementIsRefusedRatherThanSkipped() {
    var good = _Chunk(width: 8, height: 8, planes: 1);
    var bad = _Chunk(width: 8, height: 8, planes: 1, planeArrangement: 4);
    var file = new byte[good.Length + bad.Length];
    good.CopyTo(file, 0);
    bad.CopyTo(file, good.Length);

    Assert.Throws<NotSupportedException>(() => CdxlContainer.FromBytes(file));
  }

  // ============================================================================================
  // Helpers
  // ============================================================================================

  private static byte[] _File(params byte[][] chunks) {
    var total = chunks.Sum(c => c.Length);
    var file = new byte[total];
    var at = 0;
    foreach (var chunk in chunks) {
      chunk.CopyTo(file, at);
      at += chunk.Length;
    }

    return file;
  }

  private static byte[] _Chunk(
    int width, int height, int planes,
    byte fileType = 1, int videoEncoding = 0, bool stereo = false, int planeArrangement = 0,
    int paletteEntries = 0, int soundSize = 0, int extraSlack = 0) {
    var bytesPerRow = (width + 7) / 8;
    var pixelBytes = bytesPerRow * height * planes;
    var paletteBytes = paletteEntries * 2;
    var chunkSize = _HEADER_LENGTH + paletteBytes + pixelBytes + soundSize + extraSlack;

    var chunk = new byte[chunkSize];
    chunk[0] = fileType;
    chunk[1] = (byte)((videoEncoding & 0x07) | (stereo ? 0x08 : 0) | ((planeArrangement & 0x07) << 5));
    BinaryPrimitives.WriteUInt32BigEndian(chunk.AsSpan(2), (uint)chunkSize);
    // bytes 6-9: previous chunk size, left zero
    // bytes 10-11: reserved
    // bytes 12-13: frame number, left zero
    BinaryPrimitives.WriteUInt16BigEndian(chunk.AsSpan(14), (ushort)width);
    BinaryPrimitives.WriteUInt16BigEndian(chunk.AsSpan(16), (ushort)height);
    BinaryPrimitives.WriteUInt16BigEndian(chunk.AsSpan(18), (ushort)planes);
    BinaryPrimitives.WriteUInt16BigEndian(chunk.AsSpan(20), (ushort)paletteBytes);
    BinaryPrimitives.WriteUInt16BigEndian(chunk.AsSpan(22), (ushort)soundSize);

    return chunk;
  }
}
