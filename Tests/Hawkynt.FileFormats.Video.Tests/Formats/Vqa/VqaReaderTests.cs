using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FileFormat.Core;

namespace FileFormat.Vqa.Tests;

/// <summary>The VQA container's header, demuxing, VQFL association and packet ordering behaviour.</summary>
[TestFixture]
public sealed class VqaReaderTests {

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

    Assert.Multiple(() => {
      Assert.That(container.Width, Is.EqualTo(320));
      Assert.That(container.Height, Is.EqualTo(156));
      Assert.That(container.BlockWidth, Is.EqualTo(4));
      Assert.That(container.BlockHeight, Is.EqualTo(2));
      Assert.That(container.VideoFrameCount, Is.EqualTo(85));
      Assert.That(container.AudioSampleRate, Is.EqualTo(22050));
      Assert.That(container.AudioChannels, Is.EqualTo(1));
    });
  }

  [Test]
  [Category("Unit")]
  public void ChunksPastWhereFormSaysItEndsAreStillWalked() {
    var vqfr = _Vqfr([]);
    var file = _FileWithUndersizedForm(width: 4, height: 2, blockWidth: 4, blockHeight: 2, frames: 1, chunks: [vqfr]);
    var container = VqaContainer.FromBytes(file);

    Assert.That(VqaContainer.ReadPackets(container).ToArray(), Has.Length.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void AFileWithNoSoundDeclaresOneStream() {
    var file = _File(width: 4, height: 2, blockWidth: 4, blockHeight: 2, frames: 0, sampleRate: 0, channels: 0, chunks: []);
    var streams = VqaContainer.Streams(VqaContainer.FromBytes(file));

    Assert.That(streams, Has.Count.EqualTo(1));
    Assert.That(streams[0].Kind, Is.EqualTo(MediaStreamKind.Video));
  }

  [Test]
  [Category("Unit")]
  public void AFileWithSoundDeclaresTwoStreams() {
    var file = _File(width: 4, height: 2, blockWidth: 4, blockHeight: 2, frames: 0, sampleRate: 22050, channels: 1, chunks: []);
    var streams = VqaContainer.Streams(VqaContainer.FromBytes(file));

    Assert.Multiple(() => {
      Assert.That(streams, Has.Count.EqualTo(2));
      Assert.That(streams[1].Kind, Is.EqualTo(MediaStreamKind.Audio));
      Assert.That(streams[1].TimeBase, Is.EqualTo(new Rational(1, 22050)));
      Assert.That(streams[1].SampleRate, Is.EqualTo(22050));
      Assert.That(streams[1].Channels, Is.EqualTo(1));
    });
  }

  [Test]
  [Category("Unit")]
  public void VideoStreamUsesVqhdFrameRateAndColourDepth() {
    var palFile = _File(width: 4, height: 2, blockWidth: 4, blockHeight: 2, frames: 0, sampleRate: 0, channels: 0, chunks: [], frameRate: 10, highColour: false);
    var highFile = _File(width: 4, height: 2, blockWidth: 4, blockHeight: 2, frames: 0, sampleRate: 0, channels: 0, chunks: [], frameRate: 24, highColour: true, version: 3);

    var pal = VqaContainer.Streams(VqaContainer.FromBytes(palFile))[0];
    var high = VqaContainer.Streams(VqaContainer.FromBytes(highFile))[0];

    Assert.Multiple(() => {
      Assert.That(pal.FrameRate, Is.EqualTo(new Rational(10, 1)));
      Assert.That(pal.TimeBase, Is.EqualTo(new Rational(1, 10)));
      Assert.That(pal.BitsPerPixel, Is.EqualTo(8));
      Assert.That(high.FrameRate, Is.EqualTo(new Rational(24, 1)));
      Assert.That(high.BitsPerPixel, Is.EqualTo(15));
    });
  }

  [Test]
  [Category("Unit")]
  public void TheVideoStreamCarriesTheHeaderPayloadAsPrivateData() {
    var file = _File(width: 4, height: 2, blockWidth: 4, blockHeight: 2, frames: 0, sampleRate: 0, channels: 0, chunks: []);
    var container = VqaContainer.FromBytes(file);

    Assert.That(VqaContainer.Streams(container)[0].CodecPrivateData.Length, Is.EqualTo(42));
  }

  [Test]
  [Category("Unit")]
  public void VqfrChunksGoOnStreamZeroAndSoundChunksOnStreamOne() {
    var file = _File(width: 4, height: 2, blockWidth: 4, blockHeight: 2, frames: 1, sampleRate: 22050, channels: 1, chunks: [
      _Chunk("SND2", [1, 2, 3, 4]),
      _Vqfr([]),
    ]);
    var packets = VqaContainer.ReadPackets(VqaContainer.FromBytes(file)).ToArray();

    Assert.That(packets.Select(p => p.StreamIndex), Is.EqualTo(new[] { 1, 0 }));
  }

  [Test]
  [Category("Unit")]
  public void APacketCarriesItsPictureSubChunksVerbatim() {
    var subChunk = _Chunk("CPL0", new byte[768]);
    var file = _File(width: 4, height: 2, blockWidth: 4, blockHeight: 2, frames: 1, sampleRate: 0, channels: 0, chunks: [_Vqfr(subChunk)]);

    var packet = VqaContainer.ReadPackets(VqaContainer.FromBytes(file)).Single();
    Assert.That(packet.Data.Span[..4].ToArray(), Is.EqualTo("CPL0"u8.ToArray()));
  }

  [Test]
  [Category("Unit")]
  public void VqflPayloadIsPrependedToTheFollowingPicture() {
    var codebook = _Chunk("CBF0", [1, 2, 3, 4]);
    var pointers = _Chunk("VPTR", [0, 0]);
    var file = _File(width: 4, height: 2, blockWidth: 4, blockHeight: 2, frames: 1, sampleRate: 0, channels: 0, highColour: true, version: 3, chunks: [
      _Chunk("VQFL", codebook),
      _Vqfr(pointers),
    ]);

    var packet = VqaContainer.ReadPackets(VqaContainer.FromBytes(file)).Single();

    Assert.That(packet.Data.ToArray(), Is.EqualTo(codebook.Concat(pointers)));
  }

  [Test]
  [Category("Unit")]
  public void SeveralVqflChunksStayWithOneFollowingPicture() {
    var one = _Chunk("CBF0", [1, 2]);
    var two = _Chunk("JUNK", [3, 4]);
    var frame = _Chunk("VPTR", [0, 0]);
    var file = _File(width: 4, height: 2, blockWidth: 4, blockHeight: 2, frames: 1, sampleRate: 0, channels: 0, highColour: true, version: 3, chunks: [
      _Chunk("VQFL", one),
      _Chunk("VQFL", two),
      _Vqfr(frame),
    ]);

    var packet = VqaContainer.ReadPackets(VqaContainer.FromBytes(file)).Single();
    Assert.That(packet.Data.ToArray(), Is.EqualTo(one.Concat(two).Concat(frame)));
  }

  [Test]
  [Category("Unit")]
  public void DanglingVqflWithoutPictureRefuses() {
    var file = _File(width: 4, height: 2, blockWidth: 4, blockHeight: 2, frames: 1, sampleRate: 0, channels: 0, highColour: true, version: 3, chunks: [
      _Chunk("VQFL", _Chunk("CBF0", [1, 2])),
    ]);

    Assert.Throws<InvalidDataException>(() => VqaContainer.ReadPackets(VqaContainer.FromBytes(file)).ToArray());
  }

  [Test]
  [Category("Unit")]
  public void OnlyTheFirstPacketIsUniversallySafeAsASeekPoint() {
    var file = _File(width: 4, height: 2, blockWidth: 4, blockHeight: 2, frames: 2, sampleRate: 0, channels: 0, chunks: [_Vqfr([]), _Vqfr([])]);
    var packets = VqaContainer.ReadPackets(VqaContainer.FromBytes(file)).ToArray();

    Assert.That(packets.Select(p => p.IsKeyFrame), Is.EqualTo(new[] { true, false }));
  }

  [Test]
  [Category("Unit")]
  public void UnrecognisedTopLevelChunksAreSkippedRatherThanBreakingTheWalk() {
    var file = _File(width: 4, height: 2, blockWidth: 4, blockHeight: 2, frames: 1, sampleRate: 0, channels: 0, chunks: [
      _Chunk("PINF", [0, 0, 0, 0]),
      _Chunk("CMDS", new byte[8]),
      _Vqfr([]),
    ]);

    Assert.That(VqaContainer.ReadPackets(VqaContainer.FromBytes(file)).ToArray(), Has.Length.EqualTo(1));
  }

  private static byte[] _Chunk(string id, byte[] payload) {
    var chunk = new byte[8 + payload.Length + (payload.Length & 1)];
    System.Text.Encoding.ASCII.GetBytes(id).CopyTo(chunk, 0);
    BinaryPrimitives.WriteUInt32BigEndian(chunk.AsSpan(4), (uint)payload.Length);
    payload.CopyTo(chunk, 8);
    return chunk;
  }

  private static byte[] _Vqfr(byte[] subChunks) => _Chunk("VQFR", subChunks);

  private static byte[] _Header(
    int width,
    int height,
    int blockWidth,
    int blockHeight,
    int frames,
    int sampleRate,
    int channels,
    int frameRate = 15,
    bool highColour = false,
    int version = 2) {
    var payload = new byte[42];
    BinaryPrimitives.WriteUInt16LittleEndian(payload, (ushort)version);
    BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(2), highColour ? (ushort)0x10 : (ushort)0);
    BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(4), (ushort)frames);
    BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(6), (ushort)width);
    BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(8), (ushort)height);
    payload[10] = (byte)blockWidth;
    payload[11] = (byte)blockHeight;
    payload[12] = (byte)frameRate;
    payload[13] = highColour ? (byte)0 : (byte)8;
    BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(14), highColour ? (ushort)0 : (ushort)256);
    BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(24), (ushort)sampleRate);
    payload[26] = (byte)channels;
    payload[27] = sampleRate == 0 ? (byte)0 : (byte)16;
    return _Chunk("VQHD", payload);
  }

  private static byte[] _File(
    int width,
    int height,
    int blockWidth,
    int blockHeight,
    int frames,
    int sampleRate,
    int channels,
    IReadOnlyList<byte[]> chunks,
    int frameRate = 15,
    bool highColour = false,
    int version = 2) {
    var vqhd = _Header(width, height, blockWidth, blockHeight, frames, sampleRate, channels, frameRate, highColour, version);
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
      ? _Header(width, height, 4, 2, 0, 0, 0).Concat(chunks.SelectMany(c => c)).ToArray()
      : chunks.SelectMany(c => c).ToArray();
    var file = new byte[12 + body.Length];
    System.Text.Encoding.ASCII.GetBytes("FORM").CopyTo(file, 0);
    BinaryPrimitives.WriteUInt32BigEndian(file.AsSpan(4), (uint)(4 + body.Length));
    System.Text.Encoding.ASCII.GetBytes("WVQA").CopyTo(file, 8);
    body.CopyTo(file, 12);
    return file;
  }

  private static byte[] _FileWithUndersizedForm(int width, int height, int blockWidth, int blockHeight, int frames, IReadOnlyList<byte[]> chunks) {
    var vqhd = _Header(width, height, blockWidth, blockHeight, frames, 0, 0);
    var body = vqhd.Concat(chunks.SelectMany(c => c)).ToArray();
    var file = new byte[12 + body.Length];
    System.Text.Encoding.ASCII.GetBytes("FORM").CopyTo(file, 0);
    BinaryPrimitives.WriteUInt32BigEndian(file.AsSpan(4), (uint)(4 + vqhd.Length));
    System.Text.Encoding.ASCII.GetBytes("WVQA").CopyTo(file, 8);
    body.CopyTo(file, 12);
    return file;
  }
}
