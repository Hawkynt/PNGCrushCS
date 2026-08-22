using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using FileFormat.Core;

namespace FileFormat.Rpl.Tests;

/// <summary>
/// The ARMovie/RPL container's demuxing behaviour: the twenty-one-field text header, the chunk
/// catalogue's own "number of chunks names the highest index, not a count" quirk, and the closed-loop
/// checks that catch a catalogue that does not agree with itself.
/// </summary>
/// <remarks>
/// This is the same container Escape 124's own investigation mapped and recorded in
/// <c>undecodable-codecs.md</c> before any codec riding it was implemented, and the fixtures here are
/// built to the same layout that was measured against real files there: text header, chunk data, then
/// a text catalogue naming every chunk's own file offset and byte sizes.
/// </remarks>
[TestFixture]
public sealed class RplReaderTests {

  // ============================================================================================
  // Opening
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void AFileNotOpeningWithTheSignatureIsRefused() {
    var file = _Build(320, 240, [_Chunk([1, 2, 3])]);
    file[0] = (byte)'X';

    Assert.Throws<NotSupportedException>(() => RplContainer.FromBytes(file));
  }

  [Test]
  [Category("Unit")]
  public void WidthAndHeightComeStraightFromTheHeader() {
    var file = _Build(64, 32, [_Chunk([1, 2, 3])]);
    var container = RplContainer.FromBytes(file);

    Assert.That(container.Header.Width, Is.EqualTo(64));
    Assert.That(container.Header.Height, Is.EqualTo(32));
  }

  [Test]
  [Category("Unit")]
  public void TheFrameRateIsReadAsAnExactRatioNotADouble() {
    var file = _Build(64, 32, [_Chunk([1])], frameRate: "25.000000");
    var container = RplContainer.FromBytes(file);

    Assert.That(container.Header.FrameRate, Is.EqualTo(new Rational(25, 1)));
  }

  [Test]
  [Category("Unit")]
  public void MoreThanOneFrameAChunkIsRefused() {
    var file = _Build(64, 32, [_Chunk([1, 2, 3])], framesPerChunk: 2);

    var failure = Assert.Throws<NotSupportedException>(() => RplContainer.FromBytes(file));
    Assert.That(failure!.Message, Does.Contain("2 frames a chunk"));
  }

  // ============================================================================================
  // Chunk catalogue
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void TheHighestChunkIndexFieldUndercountsTheCatalogueByOne() {
    // Four chunks means the header's own "number of chunks" field states 3, not 4 — the same
    // off-by-one Escape 124's own investigation found and recorded.
    var file = _Build(64, 32, [_Chunk([1]), _Chunk([2]), _Chunk([3]), _Chunk([4])]);
    var container = RplContainer.FromBytes(file);

    Assert.That(container.Header.HighestChunkIndex, Is.EqualTo(3));
    Assert.That(container.Chunks, Has.Count.EqualTo(4));
  }

  [Test]
  [Category("Unit")]
  public void ACatalogueEntryWhoseEndDoesNotMeetTheNextEntrysOffsetIsRefused() {
    var file = _Build(64, 32, [_Chunk([1, 2]), _Chunk([3, 4])], corruptSecondChunkOffset: true);

    var failure = Assert.Throws<InvalidDataException>(() => RplContainer.FromBytes(file));
    Assert.That(failure!.Message, Does.Contain("not internally consistent"));
  }

  // ============================================================================================
  // Streams
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void TheVideoStreamsCodecIsThePlainDecimalNumberFromTheHeader() {
    var file = _Build(64, 32, [_Chunk([1, 2, 3])], videoFormat: 130);
    var container = RplContainer.FromBytes(file);

    var video = RplContainer.Streams(container).Single(s => s.Kind == MediaStreamKind.Video);
    Assert.That(video.Codec.Value, Is.EqualTo(130u));
  }

  [Test]
  [Category("Unit")]
  public void AFileWithNoSoundCompressionFormatDeclaresOnlyOneStream() {
    var file = _Build(64, 32, [_Chunk([1, 2, 3])], soundFormat: 0);
    var container = RplContainer.FromBytes(file);

    Assert.That(RplContainer.Streams(container), Has.Count.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void AFileWithASoundCompressionFormatDeclaresTwoStreams() {
    var file = _Build(64, 32, [_Chunk([1, 2, 3], audioPayload: [9, 9])], soundFormat: 1, sampleRate: 22050);
    var container = RplContainer.FromBytes(file);

    var streams = RplContainer.Streams(container);
    Assert.That(streams, Has.Count.EqualTo(2));
    Assert.That(streams[1].Kind, Is.EqualTo(MediaStreamKind.Audio));
    Assert.That(streams[1].Codec.Value, Is.EqualTo(1u));
    Assert.That(streams[1].TimeBase, Is.EqualTo(new Rational(1, 22050)));
  }

  // ============================================================================================
  // Packets
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void EachChunksVideoBytesBecomeOnePacketOnStreamZero() {
    var file = _Build(64, 32, [_Chunk([11, 22, 33]), _Chunk([44, 55])]);
    var container = RplContainer.FromBytes(file);

    var packets = RplContainer.ReadPackets(container).Where(p => p.StreamIndex == 0).ToArray();
    Assert.That(packets, Has.Length.EqualTo(2));
    Assert.That(packets[0].Data.ToArray(), Is.EqualTo(new byte[] { 11, 22, 33 }));
    Assert.That(packets[1].Data.ToArray(), Is.EqualTo(new byte[] { 44, 55 }));
  }

  [Test]
  [Category("Unit")]
  public void OnlyTheFirstVideoPacketIsAKeyFrame() {
    var file = _Build(64, 32, [_Chunk([1]), _Chunk([2])]);
    var container = RplContainer.FromBytes(file);

    var packets = RplContainer.ReadPackets(container).Where(p => p.StreamIndex == 0).ToArray();
    Assert.That(packets[0].IsKeyFrame, Is.True);
    Assert.That(packets[1].IsKeyFrame, Is.False);
  }

  [Test]
  [Category("Unit")]
  public void AudioAndVideoBytesOfOneChunkGoOnDifferentStreams() {
    var file = _Build(64, 32, [_Chunk([1, 2, 3], audioPayload: [9, 9])], soundFormat: 1, sampleRate: 8000);
    var container = RplContainer.FromBytes(file);

    var packets = RplContainer.ReadPackets(container).ToArray();
    Assert.That(packets.Single(p => p.StreamIndex == 0).Data.ToArray(), Is.EqualTo(new byte[] { 1, 2, 3 }));
    Assert.That(packets.Single(p => p.StreamIndex == 1).Data.ToArray(), Is.EqualTo(new byte[] { 9, 9 }));
  }

  // ============================================================================================
  // Helpers
  // ============================================================================================

  private readonly record struct ChunkSpec(byte[] VideoPayload, byte[] AudioPayload);

  private static ChunkSpec _Chunk(byte[] videoPayload, byte[]? audioPayload = null) => new(videoPayload, audioPayload ?? []);

  private static byte[] _Build(
    int width, int height, IReadOnlyList<ChunkSpec> chunks,
    int videoFormat = 130, int soundFormat = 0, int sampleRate = 0, string frameRate = "30.000000",
    int framesPerChunk = 1, bool corruptSecondChunkOffset = false) {
    var lines = new List<string> {
      "ARMovie",
      "test.rpl",
      "Copyright (c) test",
      "TEST 1.0",
      $"{videoFormat}        video format",
      $"{width}        pixels",
      $"{height}        pixels",
      "16         bits per pixel RGB",
      $"{frameRate}  frames per second",
      $"{soundFormat}          sound format",
      $"{sampleRate}          Hz samples",
      "1          channels",
      "16         bits per sample",
      $"{framesPerChunk}          frames per chunk",
      $"{chunks.Count - 1}          number of chunks",
      "0          even chunk size",
      "0          odd chunk size",
      "0000000000 offset to chunk cat", // ten digits reserved so patching it below never changes the header's own byte length
      "0          offset to sprite",
      "0          size of sprite",
      "0          offset to key frames",
    };

    var header = new StringBuilder();
    foreach (var line in lines)
      header.Append(line).Append('\n');

    var headerBytes = Encoding.ASCII.GetBytes(header.ToString());

    var chunkData = new List<byte>();
    var offsets = new List<(long FileOffset, long VideoSize, long AudioSize)>();
    foreach (var chunk in chunks) {
      offsets.Add((headerBytes.Length + chunkData.Count, chunk.VideoPayload.Length, chunk.AudioPayload.Length));
      chunkData.AddRange(chunk.VideoPayload);
      chunkData.AddRange(chunk.AudioPayload);
    }

    if (corruptSecondChunkOffset && offsets.Count > 1)
      offsets[1] = offsets[1] with { FileOffset = offsets[1].FileOffset + 1 };

    var catalogueOffset = headerBytes.Length + chunkData.Count;
    var catalogue = new StringBuilder();
    foreach (var entry in offsets)
      catalogue.Append($"{entry.FileOffset},{entry.VideoSize};{entry.AudioSize}\n");
    var catalogueBytes = Encoding.ASCII.GetBytes(catalogue.ToString());

    // Patch the catalogue offset field (line 17, 0-indexed) now that it is known, keeping the same
    // ten-character width reserved above so no byte offset already computed shifts underneath it.
    var headerText = header.ToString().Replace(
      "0000000000 offset to chunk cat",
      $"{catalogueOffset.ToString().PadLeft(10, '0')} offset to chunk cat");
    headerBytes = Encoding.ASCII.GetBytes(headerText);

    var file = new byte[headerBytes.Length + chunkData.Count + catalogueBytes.Length];
    headerBytes.CopyTo(file, 0);
    for (var i = 0; i < chunkData.Count; ++i)
      file[headerBytes.Length + i] = chunkData[i];
    catalogueBytes.CopyTo(file, headerBytes.Length + chunkData.Count);
    return file;
  }
}
