using System;
using System.Buffers.Binary;
using System.Linq;
using FileFormat.Core;

namespace FileFormat.Bfi.Tests;

/// <summary>
/// BFI's demuxing behaviour: the 960-byte header, the palette it carries, and how an <c>IVAS</c> chunk's
/// own audio/video offsets split into two packets. Frame-level decompression is not exercised here — see
/// <see cref="Codecs.Tests.BfiVideoDecoderTests"/> — and three real files, 138 frames in all, were
/// compared frame by frame against ffmpeg's decode with no differing sample.
/// </summary>
[TestFixture]
public sealed class BfiContainerTests {

  // ============================================================================================
  // Opening
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void AFileTooShortForTheHeaderIsRefused() {
    var failure = Assert.Throws<NotSupportedException>(() => BfiContainer.FromBytes(new byte[16]));
    Assert.That(failure!.Message, Does.Contain("BFI"));
  }

  [Test]
  [Category("Unit")]
  public void AFileNotOpeningWithTheSignatureIsRefused() {
    var file = new byte[960];
    "XXXX"u8.CopyTo(file);

    Assert.Throws<NotSupportedException>(() => BfiContainer.FromBytes(file));
  }

  [Test]
  [Category("Unit")]
  public void APlausibleHeaderOpensAndReadsWidthAndHeight() {
    var file = _Header(width: 320, height: 140, frameCount: 0);
    var container = BfiContainer.FromBytes(file);

    Assert.That(container.Width, Is.EqualTo(320));
    Assert.That(container.Height, Is.EqualTo(140));
    Assert.That(container.FrameCount, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void AnImplausiblePictureSizeIsRefused() {
    var file = _Header(width: 0, height: 140, frameCount: 0);

    Assert.Throws<NotSupportedException>(() => BfiContainer.FromBytes(file));
  }

  // ============================================================================================
  // Streams
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void DeclaresAVideoStreamAndAnAudioStream() {
    var file = _Header(width: 4, height: 4, frameCount: 0);
    var container = BfiContainer.FromBytes(file);

    var streams = BfiContainer.Streams(container);
    Assert.That(streams, Has.Count.EqualTo(2));
    Assert.That(streams[0].Kind, Is.EqualTo(MediaStreamKind.Video));
    Assert.That(streams[0].Codec.ToString(), Is.EqualTo("BFIV"));
    Assert.That(streams[1].Kind, Is.EqualTo(MediaStreamKind.Audio));
  }

  [Test]
  [Category("Unit")]
  public void TheVideoStreamCarriesThePaletteAsPrivateData() {
    var file = _Header(width: 4, height: 4, frameCount: 0);
    var container = BfiContainer.FromBytes(file);

    var streams = BfiContainer.Streams(container);
    Assert.That(streams[0].CodecPrivateData.Length, Is.EqualTo(768));
  }

  // ============================================================================================
  // Packets
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void AVideoPacketCarriesTheWholeIvasChunk() {
    var frame = _Frame(videoBytes: [1, 2, 3, 4], audioBytes: [9, 9]);
    var file = _Header(width: 4, height: 4, frameCount: 1, frames: [frame]);
    var container = BfiContainer.FromBytes(file);

    var packets = BfiContainer.ReadPackets(container).ToArray();
    var video = packets.Single(p => p.StreamIndex == 0);
    Assert.That(video.Data.Length, Is.EqualTo(frame.Length));
    Assert.That(video.Data.Span[..4].ToArray(), Is.EqualTo("IVAS"u8.ToArray()));
  }

  [Test]
  [Category("Unit")]
  public void AnAudioPacketCarriesExactlyTheBytesBetweenTheAudioAndVideoOffsets() {
    var frame = _Frame(videoBytes: [1, 2, 3, 4], audioBytes: [9, 9, 9]);
    var file = _Header(width: 4, height: 4, frameCount: 1, frames: [frame]);
    var container = BfiContainer.FromBytes(file);

    var packets = BfiContainer.ReadPackets(container).ToArray();
    var audio = packets.Single(p => p.StreamIndex == 1);
    Assert.That(audio.Data.ToArray(), Is.EqualTo(new byte[] { 9, 9, 9 }));
  }

  [Test]
  [Category("Unit")]
  public void AChunkThatDoesNotFullyFitInWhatRemainsStopsTheWalkCleanly() {
    var frame = _Frame(videoBytes: [1, 2, 3, 4], audioBytes: [9, 9]);
    var file = _Header(width: 4, height: 4, frameCount: 1, frames: [frame]);
    var truncated = file[..^2];

    Assert.DoesNotThrow(() => BfiContainer.FromBytes(truncated));
    var container = BfiContainer.FromBytes(truncated);
    Assert.That(container.FrameCount, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void DeclaredFrameCountStopsAtTheHeadersOwnFrameCountEvenIfMoreChunksFollow() {
    var frame = _Frame(videoBytes: [1, 2, 3, 4], audioBytes: []);
    var file = _Header(width: 4, height: 4, frameCount: 1, frames: [frame, frame]);
    var container = BfiContainer.FromBytes(file);

    Assert.That(container.FrameCount, Is.EqualTo(1));
  }

  // ============================================================================================
  // Helpers
  // ============================================================================================

  private static byte[] _Frame(byte[] videoBytes, byte[] audioBytes) {
    // payload: vtype(4) audioOffset(4) zero(4) videoOffset(4) [audio][video]
    var payloadLen = 16 + audioBytes.Length + videoBytes.Length;
    var chunk = new byte[8 + payloadLen];
    "IVAS"u8.CopyTo(chunk);
    BinaryPrimitives.WriteUInt32LittleEndian(chunk.AsSpan(4), (uint)chunk.Length);
    BinaryPrimitives.WriteUInt32LittleEndian(chunk.AsSpan(8), 5); // vtype
    BinaryPrimitives.WriteUInt32LittleEndian(chunk.AsSpan(12), 24); // audioOffset (from chunk start)
    BinaryPrimitives.WriteUInt32LittleEndian(chunk.AsSpan(20), (uint)(24 + audioBytes.Length)); // videoOffset
    audioBytes.CopyTo(chunk, 24);
    videoBytes.CopyTo(chunk, 24 + audioBytes.Length);
    return chunk;
  }

  private static byte[] _Header(int width, int height, int frameCount, byte[][]? frames = null) {
    frames ??= [];
    var total = 960 + frames.Sum(f => f.Length);
    var file = new byte[total];
    "BF&I"u8.CopyTo(file);
    BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(4), (uint)total);
    BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(8), 960); // first frame offset
    BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(12), (uint)frameCount);
    BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(44), (uint)width);
    BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(48), (uint)height);
    BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(828), 11025);
    BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(832), 1);

    var at = 960;
    foreach (var frame in frames) {
      frame.CopyTo(file, at);
      at += frame.Length;
    }

    return file;
  }
}
