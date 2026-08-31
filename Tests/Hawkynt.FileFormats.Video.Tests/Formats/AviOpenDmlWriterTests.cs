using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using FileFormat.Avi;
using FileFormat.Core;

namespace Hawkynt.FileFormats.Video.Tests.Formats;

[TestFixture]
public sealed class AviOpenDmlWriterTests {

  private readonly record struct RiffChunk(string Id, int DataOffset, int DataLength, string? ListType);
  private readonly record struct StandardIndexEntry(uint Offset, uint Size);
  private readonly record struct StandardIndex(string ChunkId, ulong BaseOffset, StandardIndexEntry[] Entries);

  [Test]
  [Category("Unit")]
  public void WriterEmitsNormativeDirectStandardIndexesAndExtendedHeader() {
    var waveFormat = new byte[16];
    BinaryPrimitives.WriteUInt16LittleEndian(waveFormat, 1);
    BinaryPrimitives.WriteUInt16LittleEndian(waveFormat.AsSpan(2), 2);
    BinaryPrimitives.WriteUInt32LittleEndian(waveFormat.AsSpan(4), 48_000);
    BinaryPrimitives.WriteUInt32LittleEndian(waveFormat.AsSpan(8), 192_000);
    BinaryPrimitives.WriteUInt16LittleEndian(waveFormat.AsSpan(12), 4);
    BinaryPrimitives.WriteUInt16LittleEndian(waveFormat.AsSpan(14), 16);

    var videoStream = new MediaStreamInfo {
      Index = 0,
      Kind = MediaStreamKind.Video,
      Codec = CodecTag.FromCharacters("MJPG"),
      Width = 32,
      Height = 24,
      BitsPerPixel = 24,
      TimeBase = new Rational(1, 25),
      FrameRate = new Rational(25, 1),
    };
    var audioStream = new MediaStreamInfo {
      Index = 1,
      Kind = MediaStreamKind.Audio,
      Codec = new CodecTag(1),
      TimeBase = new Rational(1, 48_000),
      CodecPrivateData = waveFormat,
    };

    var firstVideo = new byte[] { 0x11, 0x22, 0x33 }; // odd length deliberately exercises RIFF padding.
    var audio = new byte[] { 0xA1, 0xA2 };
    var secondVideo = new byte[] { 0x44, 0x55, 0x66, 0x77 };
    var file = VideoIO.Mux<AviWriter>(
      [videoStream, audioStream],
      [
        new CodedPacket(0, firstVideo, IsKeyFrame: true),
        new CodedPacket(1, audio, IsKeyFrame: false),
        new CodedPacket(0, secondVideo, IsKeyFrame: false),
      ],
      new VideoMetadata { Title = "OpenDML base-offset probe" }
    );

    Assert.That(Encoding.ASCII.GetString(file, 0, 4), Is.EqualTo("RIFF"));
    Assert.That(Encoding.ASCII.GetString(file, 8, 4), Is.EqualTo("AVI "));
    var riffEnd = checked(8 + (int)BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(4, 4)));
    Assert.That(riffEnd, Is.EqualTo(file.Length));

    var root = _Children(file, 12, riffEnd);
    var hdrl = root.Single(chunk => chunk is { Id: "LIST", ListType: "hdrl" });
    var movi = root.Single(chunk => chunk is { Id: "LIST", ListType: "movi" });
    Assert.That(root.Any(chunk => chunk.Id == "idx1"), Is.True, "Legacy idx1 must remain for AVI 1.0 readers.");

    var hdrlChildren = _Children(file, hdrl.DataOffset + 4, hdrl.DataOffset + hdrl.DataLength);
    var streamLists = hdrlChildren.Where(chunk => chunk is { Id: "LIST", ListType: "strl" }).ToArray();
    Assert.That(streamLists, Has.Length.EqualTo(2));

    var videoIndex = _ReadDirectStandardIndex(file, streamLists[0]);
    var audioIndex = _ReadDirectStandardIndex(file, streamLists[1]);
    Assert.That(videoIndex.ChunkId, Is.EqualTo("00dc"));
    Assert.That(audioIndex.ChunkId, Is.EqualTo("01wb"));
    Assert.That(videoIndex.BaseOffset, Is.EqualTo((ulong)movi.DataOffset));
    Assert.That(audioIndex.BaseOffset, Is.EqualTo((ulong)movi.DataOffset));

    Assert.That(videoIndex.Entries, Has.Length.EqualTo(2));
    Assert.That(audioIndex.Entries, Has.Length.EqualTo(1));

    _AssertIndexEntryTargetsPayload(file, videoIndex, videoIndex.Entries[0], "00dc", firstVideo);
    _AssertIndexEntryTargetsPayload(file, audioIndex, audioIndex.Entries[0], "01wb", audio);
    _AssertIndexEntryTargetsPayload(file, videoIndex, videoIndex.Entries[1], "00dc", secondVideo);

    Assert.That(videoIndex.Entries[0].Size & 0x80000000u, Is.Zero, "Keyframe entries keep bit 31 clear.");
    Assert.That(videoIndex.Entries[1].Size & 0x80000000u, Is.EqualTo(0x80000000u), "Non-keyframes set bit 31.");
    Assert.That(audioIndex.Entries[0].Size & 0x80000000u, Is.Zero, "Non-video packets remain independently seekable/key entries.");

    var odml = hdrlChildren.Single(chunk => chunk is { Id: "LIST", ListType: "odml" });
    var dmlh = _Children(file, odml.DataOffset + 4, odml.DataOffset + odml.DataLength).Single(chunk => chunk.Id == "dmlh");
    Assert.That(dmlh.DataLength, Is.EqualTo(4), "OpenDML v1.02 defines ODMLExtendedAVIHeader as one DWORD.");
    Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(dmlh.DataOffset, 4)), Is.EqualTo(2u));

    var avih = hdrlChildren.Single(chunk => chunk.Id == "avih");
    Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(avih.DataOffset + 16, 4)), Is.EqualTo(2u));

    var moviChildren = _Children(file, movi.DataOffset + 4, movi.DataOffset + movi.DataLength);
    Assert.That(moviChildren.Select(chunk => chunk.Id), Is.EqualTo(new[] { "00dc", "01wb", "00dc" }));
    Assert.That(moviChildren.Any(chunk => chunk.Id.StartsWith("ix", StringComparison.Ordinal)), Is.False,
      "A direct indx in strl has no ix## standard-index chunks in movi.");

    var roundTrip = AviContainer.FromBytes(file);
    Assert.That(AviContainer.ReadPackets(roundTrip).Select(packet => packet.Data.ToArray()),
      Is.EqualTo(new[] { firstVideo, audio, secondVideo }));
  }

  private static StandardIndex _ReadDirectStandardIndex(byte[] file, RiffChunk streamList) {
    var children = _Children(file, streamList.DataOffset + 4, streamList.DataOffset + streamList.DataLength);
    var strfPosition = Array.FindIndex(children, chunk => chunk.Id == "strf");
    var indxPosition = Array.FindIndex(children, chunk => chunk.Id == "indx");
    Assert.That(strfPosition, Is.GreaterThanOrEqualTo(0));
    Assert.That(indxPosition, Is.EqualTo(strfPosition + 1), "OpenDML direct indx follows strf in LIST strl.");

    var indx = children[indxPosition];
    var body = file.AsSpan(indx.DataOffset, indx.DataLength);
    Assert.That(body.Length, Is.GreaterThanOrEqualTo(24));
    Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(body), Is.EqualTo(2), "AVISTDINDEX has two DWORDs per entry.");
    Assert.That(body[2], Is.Zero, "bIndexSubType must be zero for a standard index.");
    Assert.That(body[3], Is.EqualTo(1), "bIndexType must be AVI_INDEX_OF_CHUNKS.");

    var count = BinaryPrimitives.ReadUInt32LittleEndian(body[4..]);
    Assert.That(count, Is.LessThanOrEqualTo(int.MaxValue));
    Assert.That(body.Length, Is.EqualTo(checked(24 + (int)count * 8)));
    var chunkId = Encoding.ASCII.GetString(body[8..12]);
    var baseOffset = BinaryPrimitives.ReadUInt64LittleEndian(body[12..20]);
    Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(body[20..24]), Is.Zero, "dwReserved3 must be zero.");

    var entries = new StandardIndexEntry[count];
    for (var i = 0; i < entries.Length; ++i) {
      var entry = body[(24 + i * 8)..];
      entries[i] = new(
        BinaryPrimitives.ReadUInt32LittleEndian(entry),
        BinaryPrimitives.ReadUInt32LittleEndian(entry[4..])
      );
    }

    return new(chunkId, baseOffset, entries);
  }

  private static void _AssertIndexEntryTargetsPayload(
    byte[] file,
    StandardIndex index,
    StandardIndexEntry entry,
    string expectedChunkId,
    byte[] expectedPayload
  ) {
    var absolute = checked((int)(index.BaseOffset + entry.Offset));
    Assert.That(absolute, Is.GreaterThanOrEqualTo(8));
    Assert.That(Encoding.ASCII.GetString(file, absolute - 8, 4), Is.EqualTo(expectedChunkId));
    Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(absolute - 4, 4)), Is.EqualTo((uint)expectedPayload.Length));
    Assert.That(file.AsSpan(absolute, expectedPayload.Length).ToArray(), Is.EqualTo(expectedPayload));
    Assert.That(entry.Size & 0x7FFFFFFFu, Is.EqualTo((uint)expectedPayload.Length));
  }

  private static RiffChunk[] _Children(byte[] file, int start, int end) {
    var chunks = new List<RiffChunk>();
    for (var position = start; position < end;) {
      Assert.That(end - position, Is.GreaterThanOrEqualTo(8), "Truncated RIFF child header in writer output.");
      var id = Encoding.ASCII.GetString(file, position, 4);
      var length = BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(position + 4, 4));
      Assert.That(length, Is.LessThanOrEqualTo(int.MaxValue));
      var dataLength = checked((int)length);
      var dataOffset = checked(position + 8);
      var dataEnd = checked(dataOffset + dataLength);
      Assert.That(dataEnd, Is.LessThanOrEqualTo(end), $"RIFF child {id} exceeds its parent.");

      string? listType = null;
      if (id == "LIST") {
        Assert.That(dataLength, Is.GreaterThanOrEqualTo(4));
        listType = Encoding.ASCII.GetString(file, dataOffset, 4);
      }

      chunks.Add(new(id, dataOffset, dataLength, listType));
      position = checked(dataEnd + (dataLength & 1));
    }

    return [.. chunks];
  }
}
