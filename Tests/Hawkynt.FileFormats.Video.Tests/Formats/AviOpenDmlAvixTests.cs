using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using FileFormat.Avi;
using FileFormat.Core;

namespace Hawkynt.FileFormats.Video.Tests.Formats;

[TestFixture]
public sealed class AviOpenDmlAvixTests {

  private readonly record struct RiffChunk(string Id, int HeaderOffset, int DataOffset, int DataLength, string? ListType);
  private readonly record struct RiffForm(string FormType, int HeaderOffset, int EndOffset, RiffChunk[] Children);
  private readonly record struct StandardIndexEntry(uint Offset, uint Size);
  private readonly record struct StandardIndex(string ChunkId, ulong BaseOffset, StandardIndexEntry[] Entries);
  private readonly record struct SuperIndexEntry(ulong Offset, uint Size, uint Duration);
  private readonly record struct SuperIndex(string ChunkId, SuperIndexEntry[] Entries);
  private readonly record struct Segment(RiffForm Form, RiffChunk Movi, RiffChunk[] MovieChildren, RiffChunk[] Media);

  [Test]
  [Category("Unit")]
  public void WriterSegmentsIntoAvixWithNormativeTwoTierIndexesAndReaderWalksEveryMovi() {
    var streams = _Streams();
    var packets = _Packets(20);
    var writer = AviWriter.Create(streams, new VideoMetadata());
    foreach (var packet in packets)
      writer.WritePacket(packet);

    const int maxRiffSize = 900;
    var file = writer.Finish(maxRiffSize);
    var forms = _Forms(file);

    Assert.That(forms.Length, Is.GreaterThan(1), "The small test limit must force at least one AVIX extension.");
    Assert.That(forms[0].FormType, Is.EqualTo("AVI "));
    Assert.That(forms.Skip(1).Select(form => form.FormType), Is.All.EqualTo("AVIX"));
    Assert.That(forms.Select(form => form.EndOffset - form.HeaderOffset), Is.All.LessThanOrEqualTo(maxRiffSize));

    var firstChildren = forms[0].Children;
    var hdrl = firstChildren.Single(chunk => chunk is { Id: "LIST", ListType: "hdrl" });
    Assert.That(firstChildren.Count(chunk => chunk is { Id: "LIST", ListType: "movi" }), Is.EqualTo(1));
    Assert.That(firstChildren.Count(chunk => chunk.Id == "idx1"), Is.EqualTo(1), "The first RIFF keeps its AVI 1.0 index.");
    foreach (var form in forms.Skip(1)) {
      Assert.That(form.Children, Has.Length.EqualTo(1), "OpenDML AVIX extensions contain only LIST movi.");
      Assert.That(form.Children[0], Has.Property(nameof(RiffChunk.Id)).EqualTo("LIST"));
      Assert.That(form.Children[0].ListType, Is.EqualTo("movi"));
    }

    var segments = forms.Select(form => {
      var movi = form.Children.Single(chunk => chunk is { Id: "LIST", ListType: "movi" });
      var children = _Children(file, movi.DataOffset + 4, movi.DataOffset + movi.DataLength);
      var media = children.Where(chunk => _IsMedia(chunk.Id)).ToArray();
      Assert.That(children.Count(chunk => chunk.Id == "ix00"), Is.EqualTo(1));
      Assert.That(children.Count(chunk => chunk.Id == "ix01"), Is.EqualTo(1));
      return new Segment(form, movi, children, media);
    }).ToArray();

    var allMedia = segments.SelectMany(segment => segment.Media).ToArray();
    Assert.That(allMedia, Has.Length.EqualTo(packets.Length));
    for (var i = 0; i < packets.Length; ++i) {
      var expectedId = packets[i].StreamIndex == 0 ? "00dc" : "01wb";
      Assert.That(allMedia[i].Id, Is.EqualTo(expectedId));
      Assert.That(file.AsSpan(allMedia[i].DataOffset, allMedia[i].DataLength).ToArray(), Is.EqualTo(packets[i].Data.ToArray()));
    }
    var packetByChunkOffset = allMedia.Select((chunk, index) => (chunk.HeaderOffset, index)).ToDictionary(pair => pair.HeaderOffset, pair => pair.index);

    var hdrlChildren = _Children(file, hdrl.DataOffset + 4, hdrl.DataOffset + hdrl.DataLength);
    var streamLists = hdrlChildren.Where(chunk => chunk is { Id: "LIST", ListType: "strl" }).ToArray();
    Assert.That(streamLists, Has.Length.EqualTo(2));
    var superIndexes = new[] {
      _ReadSuperIndex(file, streamLists[0], "00dc"),
      _ReadSuperIndex(file, streamLists[1], "01wb"),
    };
    Assert.That(superIndexes.Select(index => index.Entries.Length), Is.All.EqualTo(forms.Length));

    for (var segmentIndex = 0; segmentIndex < segments.Length; ++segmentIndex) {
      var segment = segments[segmentIndex];
      for (var streamIndex = 0; streamIndex < streams.Length; ++streamIndex) {
        var leaf = segment.MovieChildren.Single(chunk => chunk.Id == $"ix{streamIndex:00}");
        var superEntry = superIndexes[streamIndex].Entries[segmentIndex];
        Assert.That(superEntry.Offset, Is.EqualTo((ulong)leaf.HeaderOffset), "Super-index qwOffset targets the ix## chunk header.");
        Assert.That(superEntry.Size, Is.EqualTo(checked((uint)(8 + leaf.DataLength))), "Super-index dwSize includes the ix## RIFF header.");

        var mediaForStream = segment.Media.Where(chunk => chunk.Id.StartsWith($"{streamIndex:00}", StringComparison.Ordinal)).ToArray();
        Assert.That(superEntry.Duration, Is.EqualTo((uint)mediaForStream.Length), "dwDuration is measured in this writer's packet-per-tick stream units.");

        var standard = _ReadStandardIndex(file, leaf);
        Assert.That(standard.ChunkId, Is.EqualTo(streamIndex == 0 ? "00dc" : "01wb"));
        Assert.That(standard.BaseOffset, Is.EqualTo((ulong)segment.Movi.DataOffset));
        Assert.That(standard.Entries, Has.Length.EqualTo(mediaForStream.Length));

        for (var entryIndex = 0; entryIndex < standard.Entries.Length; ++entryIndex) {
          var entry = standard.Entries[entryIndex];
          var absolutePayload = checked((int)(standard.BaseOffset + entry.Offset));
          var chunkHeaderOffset = checked(absolutePayload - 8);
          Assert.That(mediaForStream[entryIndex].HeaderOffset, Is.EqualTo(chunkHeaderOffset));
          Assert.That(entry.Size & 0x7FFFFFFFu, Is.EqualTo((uint)mediaForStream[entryIndex].DataLength));
          Assert.That(packetByChunkOffset.TryGetValue(chunkHeaderOffset, out var packetIndex), Is.True);

          var expectedNonKey = packets[packetIndex].StreamIndex == 0 && !packets[packetIndex].IsKeyFrame;
          Assert.That((entry.Size & 0x80000000u) != 0, Is.EqualTo(expectedNonKey));
        }
      }
    }

    var firstVideoCount = segments[0].Media.Count(chunk => chunk.Id == "00dc");
    var totalVideoCount = packets.Count(packet => packet.StreamIndex == 0);
    var avih = hdrlChildren.Single(chunk => chunk.Id == "avih");
    Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(avih.DataOffset + 16, 4)), Is.EqualTo((uint)firstVideoCount));

    var odml = hdrlChildren.Single(chunk => chunk is { Id: "LIST", ListType: "odml" });
    var dmlh = _Children(file, odml.DataOffset + 4, odml.DataOffset + odml.DataLength).Single(chunk => chunk.Id == "dmlh");
    Assert.That(dmlh.DataLength, Is.EqualTo(4));
    Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(dmlh.DataOffset, 4)), Is.EqualTo((uint)totalVideoCount));

    var firstIdx1 = firstChildren.Single(chunk => chunk.Id == "idx1");
    Assert.That(firstIdx1.DataLength, Is.EqualTo(16 * segments[0].Media.Length));
    Assert.That(forms.Skip(1).SelectMany(form => form.Children).Any(chunk => chunk.Id == "idx1"), Is.False);

    var roundTrip = AviContainer.FromBytes(file);
    var readPackets = AviContainer.ReadPackets(roundTrip).ToArray();
    Assert.That(readPackets, Has.Length.EqualTo(packets.Length));
    for (var i = 0; i < packets.Length; ++i) {
      Assert.That(readPackets[i].StreamIndex, Is.EqualTo(packets[i].StreamIndex));
      Assert.That(readPackets[i].Data.ToArray(), Is.EqualTo(packets[i].Data.ToArray()));
    }
    Assert.That(AviContainer.Metadata(roundTrip).Duration, Is.EqualTo(TimeSpan.FromMilliseconds(totalVideoCount * 40)));
  }

  [Test]
  [Category("Unit")]
  public void ReaderKeepsCompleteMediaFromTruncatedFinalAvixIndex() {
    var packets = _Packets(20);
    var writer = AviWriter.Create(_Streams(), new VideoMetadata());
    foreach (var packet in packets)
      writer.WritePacket(packet);

    var file = writer.Finish(900);
    Assert.That(_Forms(file), Has.Length.GreaterThan(1));
    var truncated = file[..^4]; // leaf indexes trail the media, so every coded packet is still complete.

    var container = AviContainer.FromBytes(truncated);
    var readPackets = AviContainer.ReadPackets(container).ToArray();
    Assert.That(readPackets, Has.Length.EqualTo(packets.Length));
    for (var i = 0; i < packets.Length; ++i)
      Assert.That(readPackets[i].Data.ToArray(), Is.EqualTo(packets[i].Data.ToArray()));
  }

  [Test]
  [Category("Unit")]
  public void WriterRejectsPacketThatCannotFitInOneOpenDmlRiffSegment() {
    var writer = AviWriter.Create([_Streams()[0]], new VideoMetadata());
    writer.WritePacket(new CodedPacket(0, new byte[2048], IsKeyFrame: true));

    Assert.That(() => writer.Finish(700), Throws.TypeOf<NotSupportedException>());
  }

  private static MediaStreamInfo[] _Streams() {
    var waveFormat = new byte[16];
    BinaryPrimitives.WriteUInt16LittleEndian(waveFormat, 1);
    BinaryPrimitives.WriteUInt16LittleEndian(waveFormat.AsSpan(2), 2);
    BinaryPrimitives.WriteUInt32LittleEndian(waveFormat.AsSpan(4), 48_000);
    BinaryPrimitives.WriteUInt32LittleEndian(waveFormat.AsSpan(8), 192_000);
    BinaryPrimitives.WriteUInt16LittleEndian(waveFormat.AsSpan(12), 4);
    BinaryPrimitives.WriteUInt16LittleEndian(waveFormat.AsSpan(14), 16);

    return [
      new MediaStreamInfo {
        Index = 0,
        Kind = MediaStreamKind.Video,
        Codec = CodecTag.FromCharacters("MJPG"),
        Width = 32,
        Height = 24,
        BitsPerPixel = 24,
        TimeBase = new Rational(1, 25),
        FrameRate = new Rational(25, 1),
      },
      new MediaStreamInfo {
        Index = 1,
        Kind = MediaStreamKind.Audio,
        Codec = new CodecTag(1),
        TimeBase = new Rational(1, 48_000),
        CodecPrivateData = waveFormat,
      },
    ];
  }

  private static CodedPacket[] _Packets(int count) {
    var result = new CodedPacket[count];
    for (var i = 0; i < result.Length; ++i) {
      var streamIndex = i & 1;
      var payload = Enumerable.Repeat(checked((byte)(i + 1)), 79 + i % 4).ToArray();
      result[i] = new(streamIndex, payload, IsKeyFrame: streamIndex != 0 || i % 4 == 0);
    }
    return result;
  }

  private static SuperIndex _ReadSuperIndex(byte[] file, RiffChunk streamList, string expectedChunkId) {
    var children = _Children(file, streamList.DataOffset + 4, streamList.DataOffset + streamList.DataLength);
    var strfPosition = Array.FindIndex(children, chunk => chunk.Id == "strf");
    var indxPosition = Array.FindIndex(children, chunk => chunk.Id == "indx");
    Assert.That(strfPosition, Is.GreaterThanOrEqualTo(0));
    Assert.That(indxPosition, Is.EqualTo(strfPosition + 1));

    var body = file.AsSpan(children[indxPosition].DataOffset, children[indxPosition].DataLength);
    Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(body), Is.EqualTo(4));
    Assert.That(body[2], Is.Zero);
    Assert.That(body[3], Is.Zero, "A super index is AVI_INDEX_OF_INDEXES.");
    var count = BinaryPrimitives.ReadUInt32LittleEndian(body[4..]);
    Assert.That(count, Is.LessThanOrEqualTo(int.MaxValue));
    Assert.That(body.Length, Is.EqualTo(checked(24 + (int)count * 16)));
    var chunkId = Encoding.ASCII.GetString(body[8..12]);
    Assert.That(chunkId, Is.EqualTo(expectedChunkId));
    Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(body[12..]), Is.Zero);
    Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(body[16..]), Is.Zero);
    Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(body[20..]), Is.Zero);

    var entries = new SuperIndexEntry[count];
    for (var i = 0; i < entries.Length; ++i) {
      var entry = body[(24 + i * 16)..];
      entries[i] = new(
        BinaryPrimitives.ReadUInt64LittleEndian(entry),
        BinaryPrimitives.ReadUInt32LittleEndian(entry[8..]),
        BinaryPrimitives.ReadUInt32LittleEndian(entry[12..])
      );
    }
    return new(chunkId, entries);
  }

  private static StandardIndex _ReadStandardIndex(byte[] file, RiffChunk indexChunk) {
    var body = file.AsSpan(indexChunk.DataOffset, indexChunk.DataLength);
    Assert.That(body.Length, Is.GreaterThanOrEqualTo(24));
    Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(body), Is.EqualTo(2));
    Assert.That(body[2], Is.Zero);
    Assert.That(body[3], Is.EqualTo(1), "A leaf ix## is AVI_INDEX_OF_CHUNKS.");
    var count = BinaryPrimitives.ReadUInt32LittleEndian(body[4..]);
    Assert.That(count, Is.LessThanOrEqualTo(int.MaxValue));
    Assert.That(body.Length, Is.EqualTo(checked(24 + (int)count * 8)));
    var chunkId = Encoding.ASCII.GetString(body[8..12]);
    var baseOffset = BinaryPrimitives.ReadUInt64LittleEndian(body[12..20]);
    Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(body[20..24]), Is.Zero);

    var entries = new StandardIndexEntry[count];
    for (var i = 0; i < entries.Length; ++i) {
      var entry = body[(24 + i * 8)..];
      entries[i] = new(BinaryPrimitives.ReadUInt32LittleEndian(entry), BinaryPrimitives.ReadUInt32LittleEndian(entry[4..]));
    }
    return new(chunkId, baseOffset, entries);
  }

  private static RiffForm[] _Forms(byte[] file) {
    var forms = new List<RiffForm>();
    for (var position = 0; position < file.Length;) {
      Assert.That(file.Length - position, Is.GreaterThanOrEqualTo(12), "Truncated top-level RIFF header in writer output.");
      Assert.That(Encoding.ASCII.GetString(file, position, 4), Is.EqualTo("RIFF"));
      var size = BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(position + 4, 4));
      Assert.That(size, Is.GreaterThanOrEqualTo(4));
      var end = checked(position + 8 + (int)size);
      Assert.That(end, Is.LessThanOrEqualTo(file.Length));
      var formType = Encoding.ASCII.GetString(file, position + 8, 4);
      forms.Add(new(formType, position, end, _Children(file, position + 12, end)));
      position = checked(end + (int)(size & 1));
    }
    return [.. forms];
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

      chunks.Add(new(id, position, dataOffset, dataLength, listType));
      position = checked(dataEnd + (dataLength & 1));
    }
    return [.. chunks];
  }

  private static bool _IsMedia(string id)
    => id.Length == 4
       && char.IsAsciiDigit(id[0])
       && char.IsAsciiDigit(id[1])
       && id.Substring(2) is "dc" or "db" or "wb" or "tx";
}
