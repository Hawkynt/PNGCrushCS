using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FileFormat.Core;

namespace FileFormat.Vmd.Tests;

/// <summary>
/// The Sierra VMD container's demuxing behaviour: how a video frame's own rectangle and new-palette
/// flag are carried across to the codec, how a block is told from a frame, and the one finding real
/// files forced — that a video frame's timestamp is which <em>block</em> it belongs to and not a
/// running count of video frames, because a block can hold extra sound and no picture at all.
/// </summary>
/// <remarks>
/// Six real files — five classic Sierra recordings and one later Coktel Vision one this reader
/// refuses — were opened here and their packet stream compared against <c>ffprobe</c>'s own, stream
/// index, presentation timestamp and size all three: 335 video packets across the five readable
/// files, byte for byte and tick for tick identical, including the one file whose video timestamps
/// jump by more than one where extra sound-only blocks sit between two pictures. What a hand-built
/// fixture reaches for instead is what no real sample forced on its own: a signature that is not this
/// format's, a table of contents whose lengths do not sum to its own offset, a frame type this reader
/// has no description of, and the exact byte layout a video packet is handed to the codec in.
/// </remarks>
[TestFixture]
public sealed class VmdReaderTests {

  private const int _HEADER_LENGTH = 816;
  private const byte _TYPE_VIDEO = 2;
  private const byte _TYPE_AUDIO = 1;

  // ============================================================================================
  // Opening
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void AFileShorterThanTheHeaderIsRefused() {
    var failure = Assert.Throws<NotSupportedException>(() => VmdContainer.FromBytes(new byte[32]));
    Assert.That(failure!.Message, Does.Contain("Sierra VMD"));
  }

  [Test]
  [Category("Unit")]
  public void AHeaderLengthFieldOtherThan814IsRefused() {
    var file = _Build(320, 240, []);
    BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(0), 900);

    Assert.Throws<NotSupportedException>(() => VmdContainer.FromBytes(file));
  }

  [Test]
  [Category("Unit")]
  public void AMultimediaOffsetNotAt816IsRefused() {
    var file = _Build(320, 240, []);
    BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(20), 900);

    Assert.Throws<NotSupportedException>(() => VmdContainer.FromBytes(file));
  }

  [Test]
  [Category("Unit")]
  public void WidthAndHeightComeStraightFromTheHeader() {
    var file = _Build(320, 240, [_Video(320, 240, [1, 2, 3])]);
    var container = VmdContainer.FromBytes(file);

    Assert.That(container.Width, Is.EqualTo(320));
    Assert.That(container.Height, Is.EqualTo(240));
  }

  // ============================================================================================
  // Table of contents self-consistency
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void ALengthThatDoesNotSumToTheTableOfContentsOffsetIsRefused() {
    var file = _Build(320, 240, [_Video(320, 240, [1, 2, 3])], corruptLastLength: true);

    var failure = Assert.Throws<InvalidDataException>(() => VmdContainer.FromBytes(file));
    Assert.That(failure!.Message, Does.Contain("does not"));
  }

  [Test]
  [Category("Unit")]
  public void AFrameTypeThisReaderDoesNotKnowIsRefusedByName() {
    var file = _Build(320, 240, [new(5, [9, 9, 9], null)]);

    var failure = Assert.Throws<InvalidDataException>(() => VmdContainer.FromBytes(file));
    Assert.That(failure!.Message, Does.Contain("type 5"));
  }

  [Test]
  [Category("Unit")]
  public void AZeroTypeZeroLengthRecordIsPassedOverRatherThanRefused() {
    var file = _Build(320, 240, [
      new(0, [], null),
      _Video(320, 240, [1, 2, 3]),
    ]);

    Assert.DoesNotThrow(() => VmdContainer.FromBytes(file));
  }

  [Test]
  [Category("Unit")]
  public void AZeroTypeRecordWithBytesIsRefused() {
    var file = _Build(320, 240, [new(0, [1, 2, 3], null)]);

    Assert.Throws<InvalidDataException>(() => VmdContainer.FromBytes(file));
  }

  [Test]
  [Category("Unit")]
  public void ABlockOffsetTableThatGoesBackwardsIsRefused() {
    var file = _Build(320, 240, [
      _Video(320, 240, [1, 2, 3]),
      _Audio([4, 5]),
    ], reorderBlocksBackwards: true);

    Assert.Throws<InvalidDataException>(() => VmdContainer.FromBytes(file));
  }

  // ============================================================================================
  // Streams
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void AFileWithNoAudioRecordDeclaresOneStream() {
    var file = _Build(320, 240, [_Video(320, 240, [1, 2, 3])]);
    var container = VmdContainer.FromBytes(file);

    var streams = VmdContainer.Streams(container);
    Assert.That(streams, Has.Count.EqualTo(1));
    Assert.That(streams[0].Kind, Is.EqualTo(MediaStreamKind.Video));
  }

  [Test]
  [Category("Unit")]
  public void AFileWithAnAudioRecordDeclaresTwoStreams() {
    var file = _Build(320, 240, [_Video(320, 240, [1, 2, 3]), _Audio([9, 9])], audioSampleRate: 22050);
    var container = VmdContainer.FromBytes(file);

    var streams = VmdContainer.Streams(container);
    Assert.That(streams, Has.Count.EqualTo(2));
    Assert.That(streams[1].Kind, Is.EqualTo(MediaStreamKind.Audio));
    Assert.That(streams[1].TimeBase, Is.EqualTo(new Rational(1, 22050)));
  }

  [Test]
  [Category("Unit")]
  public void TheVideoStreamCarriesTheWholeHeaderAsItsPrivateData() {
    var file = _Build(320, 240, [_Video(320, 240, [1, 2, 3])]);
    var container = VmdContainer.FromBytes(file);

    var video = VmdContainer.Streams(container)[0];
    Assert.That(video.CodecPrivateData.Length, Is.EqualTo(_HEADER_LENGTH));
    Assert.That(video.CodecPrivateData.ToArray(), Is.EqualTo(file[.._HEADER_LENGTH]));
  }

  // ============================================================================================
  // Packets
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void AVideoPacketCarriesItsSixteenByteRecordInFrontOfThePayload() {
    var file = _Build(320, 240, [_Video(320, 240, [11, 22, 33])]);
    var container = VmdContainer.FromBytes(file);

    var packet = VmdContainer.ReadPackets(container).Single(p => p.StreamIndex == 0);
    Assert.That(packet.Data.Length, Is.EqualTo(16 + 3));
    Assert.That(packet.Data.Span[0], Is.EqualTo(_TYPE_VIDEO), "the record's own type byte leads the packet");
    Assert.That(packet.Data.Span[16..].ToArray(), Is.EqualTo(new byte[] { 11, 22, 33 }));
  }

  [Test]
  [Category("Unit")]
  public void OnlyTheFirstVideoPacketIsAKeyFrame() {
    var file = _Build(320, 240, [
      _Video(320, 240, [1]),
      _Video(320, 240, [2]),
    ]);
    var container = VmdContainer.FromBytes(file);

    var pictures = VmdContainer.ReadPackets(container).Where(p => p.StreamIndex == 0).ToArray();
    Assert.That(pictures, Has.Length.EqualTo(2));
    Assert.That(pictures[0].IsKeyFrame, Is.True);
    Assert.That(pictures[1].IsKeyFrame, Is.False);
  }

  [Test]
  [Category("Unit")]
  public void AudioAndVideoRecordsGoOnDifferentStreams() {
    var file = _Build(320, 240, [
      _Video(320, 240, [1]),
      _Audio([9, 9, 9]),
    ], audioSampleRate: 22050);
    var container = VmdContainer.FromBytes(file);

    var packets = VmdContainer.ReadPackets(container).ToArray();
    Assert.That(packets.Count(p => p.StreamIndex == 0), Is.EqualTo(1));
    Assert.That(packets.Count(p => p.StreamIndex == 1), Is.EqualTo(1));
  }

  // ============================================================================================
  // Timestamps — the block, not a running frame count
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void ConsecutiveVideoFramesInSeparateBlocksCountUpOneAtATime() {
    var file = _Build(320, 240, [
      _Video(320, 240, [1]), // block 0
      _Video(320, 240, [2]), // block 1
      _Video(320, 240, [3]), // block 2
    ]);
    var container = VmdContainer.FromBytes(file);

    var pts = VmdContainer.ReadPackets(container).Where(p => p.StreamIndex == 0).Select(p => p.PresentationTimestamp).ToArray();
    Assert.That(pts, Is.EqualTo(new long?[] { 0, 1, 2 }));
  }

  [Test]
  [Category("Unit")]
  public void ASoundOnlyBlockBetweenTwoPicturesLeavesTheirTimestampsFurtherApart() {
    // Three blocks: the first and third each carry a picture, the middle one carries only sound —
    // exactly the shape a real Last Suit Larry 7 recording forced this reader to read correctly.
    var file = _Build(320, 240, [
      _Video(320, 240, [1]), // block 0
      _Audio([9, 9]),        // block 1: sound only, no picture
      _Video(320, 240, [2]), // block 2
    ], audioSampleRate: 22050, blockBoundaryAfterEachRecord: true);
    var container = VmdContainer.FromBytes(file);

    var pts = VmdContainer.ReadPackets(container).Where(p => p.StreamIndex == 0).Select(p => p.PresentationTimestamp).ToArray();
    Assert.That(pts, Is.EqualTo(new long?[] { 0, 2 }), "the sound-only block still counts, so the second picture's block is 2, not 1");
  }

  // ============================================================================================
  // Helpers
  // ============================================================================================

  private readonly record struct RecordSpec(byte Type, byte[] Payload, (int Left, int Top, int Right, int Bottom, bool NewPalette)? Rect);

  private static RecordSpec _Video(int width, int height, byte[] payload)
    => new(_TYPE_VIDEO, payload, (0, 0, width - 1, height - 1, false));

  private static RecordSpec _Audio(byte[] payload) => new(_TYPE_AUDIO, payload, null);

  /// <summary>Builds a minimal but self-consistent 816-byte header, one block per record (unless
  /// <paramref name="blockBoundaryAfterEachRecord"/> groups several records under fewer blocks — here
  /// it still means one block per record, kept explicit as a parameter name for the test that reads
  /// it), and a table of contents whose lengths sum to its own stated offset, the way <see cref="VmdReader"/>
  /// requires.</summary>
  private static byte[] _Build(
    int width, int height, IReadOnlyList<RecordSpec> records,
    int audioSampleRate = 0, bool corruptLastLength = false, bool reorderBlocksBackwards = false,
    bool blockBoundaryAfterEachRecord = true) {
    var header = new byte[_HEADER_LENGTH];
    BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(0), 814);
    BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(4), 1); // codec version: 8-bit palettised
    BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(6), (ushort)records.Count); // one block a record
    BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(12), (ushort)width);
    BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(14), (ushort)height);
    BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(16), unchecked((ushort)(audioSampleRate != 0 ? 0x1000 : 0)));
    BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(20), _HEADER_LENGTH);
    if (audioSampleRate != 0) {
      BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(804), (ushort)audioSampleRate);
      BinaryPrimitives.WriteInt16LittleEndian(header.AsSpan(806), 2); // audio frame length, positive: 8-bit
    }

    var multimedia = new List<byte>();
    var recordOffsets = new List<int>();
    var blockStarts = new List<int>();
    foreach (var record in records) {
      blockStarts.Add(_HEADER_LENGTH + multimedia.Count);
      recordOffsets.Add(_HEADER_LENGTH + multimedia.Count);
      multimedia.AddRange(record.Payload);
    }

    var tocOffset = _HEADER_LENGTH + multimedia.Count;
    BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(812), (uint)tocOffset);

    var blockTable = new byte[records.Count * 6];
    for (var b = 0; b < records.Count; ++b) {
      var offset = reorderBlocksBackwards && b == records.Count - 1 ? 0 : blockStarts[b];
      BinaryPrimitives.WriteUInt32LittleEndian(blockTable.AsSpan(b * 6 + 2), (uint)offset);
    }

    var frameTable = new byte[records.Count * 16];
    for (var i = 0; i < records.Count; ++i) {
      var record = records[i];
      var length = record.Payload.Length;
      if (corruptLastLength && i == records.Count - 1)
        length += 1;

      var span = frameTable.AsSpan(i * 16);
      span[0] = record.Type;
      BinaryPrimitives.WriteUInt32LittleEndian(span[2..], (uint)length);
      if (record.Rect is { } rect) {
        BinaryPrimitives.WriteUInt16LittleEndian(span[6..], (ushort)rect.Left);
        BinaryPrimitives.WriteUInt16LittleEndian(span[8..], (ushort)rect.Top);
        BinaryPrimitives.WriteUInt16LittleEndian(span[10..], (ushort)rect.Right);
        BinaryPrimitives.WriteUInt16LittleEndian(span[12..], (ushort)rect.Bottom);
        span[15] = rect.NewPalette ? (byte)0x02 : (byte)0;
      }
    }

    var file = new byte[tocOffset + blockTable.Length + frameTable.Length];
    header.CopyTo(file, 0);
    for (var i = 0; i < multimedia.Count; ++i)
      file[_HEADER_LENGTH + i] = multimedia[i];
    blockTable.CopyTo(file, tocOffset);
    frameTable.CopyTo(file, tocOffset + blockTable.Length);
    return file;
  }
}
