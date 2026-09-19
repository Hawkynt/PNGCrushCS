using System;
using System.Buffers.Binary;
using System.Linq;
using FileFormat.Core;

namespace FileFormat.Vmd.Tests;

/// <summary>VMD cases that require the real fixed block/part table rather than the legacy flat-table fallback.</summary>
[TestFixture]
public sealed class VmdCompletionTests {

  private const int _HEADER_LENGTH = 816;
  private const int _BLOCK_RECORD_LENGTH = 6;
  private const int _FRAME_RECORD_LENGTH = 16;

  [Test]
  [Category("Unit")]
  public void BlockOffsetsRatherThanAFlatRunningCursorLocateFrameData() {
    var file = _Build(
      blocks: [
        new BlockSpec(816, [new PartSpec(2, [0x02, 1])]),
        new BlockSpec(820, [new PartSpec(2, [0x02, 9])]),
      ],
      framesPerBlock: 1,
      dataEnd: 822);

    var packets = VmdContainer.ReadPackets(VmdContainer.FromBytes(file)).ToArray();

    Assert.That(packets, Has.Length.EqualTo(2));
    Assert.That(packets[0].Data.Span[^1], Is.EqualTo(1));
    Assert.That(packets[1].Data.Span[^1], Is.EqualTo(9));
    Assert.That(packets.Select(p => p.PresentationTimestamp), Is.EqualTo(new long?[] { 0, 1 }));
  }

  [Test]
  [Category("Unit")]
  public void UnknownPartsConsumeTheirBytesWithoutBecomingPackets() {
    var file = _Build(
      blocks: [new BlockSpec(816, [
        new PartSpec(5, [0xAA, 0xBB]),
        new PartSpec(2, [0x02, 7]),
      ])],
      framesPerBlock: 2,
      dataEnd: 820);

    var packet = VmdContainer.ReadPackets(VmdContainer.FromBytes(file)).Single();
    Assert.That(packet.Data.Span[^1], Is.EqualTo(7));
  }

  [Test]
  [Category("Unit")]
  public void EmbeddedIndeo3IsExposedAsIndeoAndItsPacketHasNoVmdRecordPrefix() {
    var file = _Build(
      blocks: [new BlockSpec(816, [new PartSpec(2, [1, 2, 3, 4])])],
      framesPerBlock: 1,
      dataEnd: 820,
      indeo3: true);

    var container = VmdContainer.FromBytes(file);
    var stream = VmdContainer.Streams(container).Single();
    var packet = VmdContainer.ReadPackets(container).Single();

    Assert.Multiple(() => {
      Assert.That(stream.Codec.EqualsIgnoringCase(CodecTag.FromCharacters("IV32")), Is.True);
      Assert.That(stream.Width, Is.EqualTo(16));
      Assert.That(stream.Height, Is.EqualTo(16));
      Assert.That(packet.Data.ToArray(), Is.EqualTo(new byte[] { 1, 2, 3, 4 }));
    });
  }

  private readonly record struct PartSpec(byte Type, byte[] Data);
  private readonly record struct BlockSpec(int Offset, PartSpec[] Parts);

  private static byte[] _Build(BlockSpec[] blocks, int framesPerBlock, int dataEnd, bool indeo3 = false) {
    var tocOffset = dataEnd;
    var frameCount = blocks.Length * framesPerBlock;
    var file = new byte[tocOffset + blocks.Length * _BLOCK_RECORD_LENGTH + frameCount * _FRAME_RECORD_LENGTH];
    var header = file.AsSpan(0, _HEADER_LENGTH);
    BinaryPrimitives.WriteUInt16LittleEndian(header, 814);
    BinaryPrimitives.WriteUInt16LittleEndian(header[4..], indeo3 ? (ushort)7 : (ushort)1);
    BinaryPrimitives.WriteUInt16LittleEndian(header[6..], checked((ushort)blocks.Length));
    BinaryPrimitives.WriteUInt16LittleEndian(header[12..], 16);
    BinaryPrimitives.WriteUInt16LittleEndian(header[14..], 16);
    BinaryPrimitives.WriteUInt16LittleEndian(header[18..], checked((ushort)framesPerBlock));
    BinaryPrimitives.WriteUInt32LittleEndian(header[20..], _HEADER_LENGTH);
    BinaryPrimitives.WriteUInt32LittleEndian(header[800..], 4096);
    BinaryPrimitives.WriteUInt32LittleEndian(header[812..], checked((uint)tocOffset));
    if (indeo3)
      "iv32"u8.CopyTo(header[24..]);

    var blockTable = file.AsSpan(tocOffset, blocks.Length * _BLOCK_RECORD_LENGTH);
    var frameTable = file.AsSpan(tocOffset + blockTable.Length, frameCount * _FRAME_RECORD_LENGTH);

    for (var blockIndex = 0; blockIndex < blocks.Length; ++blockIndex) {
      var block = blocks[blockIndex];
      BinaryPrimitives.WriteUInt32LittleEndian(blockTable[(blockIndex * _BLOCK_RECORD_LENGTH + 2)..], checked((uint)block.Offset));
      var dataOffset = block.Offset;

      for (var partIndex = 0; partIndex < framesPerBlock; ++partIndex) {
        var record = frameTable.Slice((blockIndex * framesPerBlock + partIndex) * _FRAME_RECORD_LENGTH, _FRAME_RECORD_LENGTH);
        if (partIndex >= block.Parts.Length)
          continue;

        var part = block.Parts[partIndex];
        record[0] = part.Type;
        BinaryPrimitives.WriteUInt32LittleEndian(record[2..], checked((uint)part.Data.Length));
        if (part.Type == 2) {
          BinaryPrimitives.WriteUInt16LittleEndian(record[10..], 15);
          BinaryPrimitives.WriteUInt16LittleEndian(record[12..], 15);
        }
        part.Data.CopyTo(file, dataOffset);
        dataOffset += part.Data.Length;
      }
    }

    return file;
  }
}
