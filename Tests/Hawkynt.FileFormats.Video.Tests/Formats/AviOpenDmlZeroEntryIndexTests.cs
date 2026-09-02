using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using FileFormat.Avi;
using FileFormat.Core;

namespace Hawkynt.FileFormats.Video.Tests.Formats;

[TestFixture]
public sealed class AviOpenDmlZeroEntryIndexTests {

  private readonly record struct Chunk(string Id, int HeaderOffset, int DataOffset, int DataLength, string? ListType);
  private readonly record struct Form(string Type, int HeaderOffset, int EndOffset, Chunk[] Children);

  [Test]
  [Category("Unit")]
  public void SegmentedWriterIndexesDeclaredButUnusedStreamWithEmptyLeafPerRiff() {
    var writer = AviWriter.Create(_Streams(), new VideoMetadata());
    for (var i = 0; i < 12; ++i)
      writer.WritePacket(new CodedPacket(0, Enumerable.Repeat(checked((byte)(i + 1)), 100).ToArray(), IsKeyFrame: true));

    var file = writer.Finish(900);
    var forms = _Forms(file);
    Assert.That(forms, Has.Length.GreaterThan(1));

    var hdrl = forms[0].Children.Single(chunk => chunk is { Id: "LIST", ListType: "hdrl" });
    var headerChildren = _Children(file, hdrl.DataOffset + 4, hdrl.DataOffset + hdrl.DataLength);
    var streamLists = headerChildren.Where(chunk => chunk is { Id: "LIST", ListType: "strl" }).ToArray();
    Assert.That(streamLists, Has.Length.EqualTo(2));

    var audioStreamChildren = _Children(file, streamLists[1].DataOffset + 4, streamLists[1].DataOffset + streamLists[1].DataLength);
    var superChunk = audioStreamChildren.Single(chunk => chunk.Id == "indx");
    var super = file.AsSpan(superChunk.DataOffset, superChunk.DataLength);
    Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(super), Is.EqualTo(4));
    Assert.That(super[2], Is.Zero);
    Assert.That(super[3], Is.Zero);
    var superCount = BinaryPrimitives.ReadUInt32LittleEndian(super[4..]);
    Assert.That(superCount, Is.EqualTo((uint)forms.Length));
    Assert.That(Encoding.ASCII.GetString(super[8..12]), Is.EqualTo("01wb"));

    for (var segmentIndex = 0; segmentIndex < forms.Length; ++segmentIndex) {
      var movi = forms[segmentIndex].Children.Single(chunk => chunk is { Id: "LIST", ListType: "movi" });
      var movieChildren = _Children(file, movi.DataOffset + 4, movi.DataOffset + movi.DataLength);
      var leaf = movieChildren.Single(chunk => chunk.Id == "ix01");
      var leafBody = file.AsSpan(leaf.DataOffset, leaf.DataLength);

      Assert.That(leaf.DataLength, Is.EqualTo(24), "An empty standard index still has its complete AVISTDINDEX header.");
      Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(leafBody), Is.EqualTo(2));
      Assert.That(leafBody[2], Is.Zero);
      Assert.That(leafBody[3], Is.EqualTo(1));
      Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(leafBody[4..]), Is.Zero);
      Assert.That(Encoding.ASCII.GetString(leafBody[8..12]), Is.EqualTo("01wb"));
      Assert.That(BinaryPrimitives.ReadUInt64LittleEndian(leafBody[12..20]), Is.EqualTo((ulong)movi.DataOffset));
      Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(leafBody[20..24]), Is.Zero);

      var superEntry = super[(24 + segmentIndex * 16)..];
      Assert.That(BinaryPrimitives.ReadUInt64LittleEndian(superEntry), Is.EqualTo((ulong)leaf.HeaderOffset));
      Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(superEntry[8..]), Is.EqualTo(32u));
      Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(superEntry[12..]), Is.Zero);
    }
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

  private static Form[] _Forms(byte[] file) {
    var forms = new List<Form>();
    for (var position = 0; position < file.Length;) {
      Assert.That(file.Length - position, Is.GreaterThanOrEqualTo(12));
      Assert.That(Encoding.ASCII.GetString(file, position, 4), Is.EqualTo("RIFF"));
      var size = BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(position + 4, 4));
      Assert.That(size, Is.LessThanOrEqualTo(int.MaxValue));
      var end = checked(position + 8 + (int)size);
      Assert.That(end, Is.LessThanOrEqualTo(file.Length));
      forms.Add(new(Encoding.ASCII.GetString(file, position + 8, 4), position, end, _Children(file, position + 12, end)));
      position = checked(end + (int)(size & 1));
    }
    return [.. forms];
  }

  private static Chunk[] _Children(byte[] file, int start, int end) {
    var result = new List<Chunk>();
    for (var position = start; position < end;) {
      Assert.That(end - position, Is.GreaterThanOrEqualTo(8));
      var id = Encoding.ASCII.GetString(file, position, 4);
      var size = BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(position + 4, 4));
      Assert.That(size, Is.LessThanOrEqualTo(int.MaxValue));
      var dataLength = checked((int)size);
      var dataOffset = checked(position + 8);
      var dataEnd = checked(dataOffset + dataLength);
      Assert.That(dataEnd, Is.LessThanOrEqualTo(end));
      var listType = id == "LIST" && dataLength >= 4 ? Encoding.ASCII.GetString(file, dataOffset, 4) : null;
      result.Add(new(id, position, dataOffset, dataLength, listType));
      position = checked(dataEnd + (dataLength & 1));
    }
    return [.. result];
  }
}
