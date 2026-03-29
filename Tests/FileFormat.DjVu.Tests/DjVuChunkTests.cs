using System;
using System.Buffers.Binary;
using System.Text;
using FileFormat.DjVu;

namespace FileFormat.DjVu.Tests;

[TestFixture]
public sealed class DjVuChunkTests {

  [Test]
  [Category("Unit")]
  public void ParseChunks_SingleChunk() {
    var data = new byte[12]; // 4 ID + 4 size + 4 data
    Encoding.ASCII.GetBytes("TEST", data.AsSpan(0));
    BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(4), 4);
    data[8] = 0xAA;
    data[9] = 0xBB;
    data[10] = 0xCC;
    data[11] = 0xDD;

    var chunks = DjVuReader.ParseChunks(data, 0, data.Length);

    Assert.That(chunks, Has.Count.EqualTo(1));
    Assert.That(chunks[0].ChunkId, Is.EqualTo("TEST"));
    Assert.That(chunks[0].Data.Length, Is.EqualTo(4));
    Assert.That(chunks[0].Data[0], Is.EqualTo(0xAA));
  }

  [Test]
  [Category("Unit")]
  public void ParseChunks_MultipleChunks() {
    // First chunk: 4+4+2 = 10 bytes, then word-aligned = 10 (even)
    // Actually: 4 ID + 4 size + 2 data = 10 bytes, data size is 2, so offset = 8 + 2 = 10 (even, no padding)
    // Second chunk: 4+4+3 = 11 bytes
    var data = new byte[10 + 12]; // first chunk (4+4+2) + second chunk (4+4+3+1 padding)
    Encoding.ASCII.GetBytes("CH_1", data.AsSpan(0));
    BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(4), 2);
    data[8] = 0x11;
    data[9] = 0x22;

    Encoding.ASCII.GetBytes("CH_2", data.AsSpan(10));
    BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(14), 3);
    data[18] = 0x33;
    data[19] = 0x44;
    data[20] = 0x55;

    var chunks = DjVuReader.ParseChunks(data, 0, data.Length);

    Assert.That(chunks, Has.Count.EqualTo(2));
    Assert.That(chunks[0].ChunkId, Is.EqualTo("CH_1"));
    Assert.That(chunks[0].Data.Length, Is.EqualTo(2));
    Assert.That(chunks[1].ChunkId, Is.EqualTo("CH_2"));
    Assert.That(chunks[1].Data.Length, Is.EqualTo(3));
  }

  [Test]
  [Category("Unit")]
  public void ParseChunks_WordAlignment_OddSizeChunk() {
    // First chunk has odd size (3 bytes), should be padded to word boundary
    // Chunk 1: 4 ID + 4 size + 3 data + 1 pad = 12
    // Chunk 2: 4 ID + 4 size + 2 data = 10
    var data = new byte[22];
    Encoding.ASCII.GetBytes("ODD_", data.AsSpan(0));
    BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(4), 3);
    data[8] = 0xAA;
    data[9] = 0xBB;
    data[10] = 0xCC;
    // data[11] is padding byte

    Encoding.ASCII.GetBytes("EVEN", data.AsSpan(12));
    BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(16), 2);
    data[20] = 0xDD;
    data[21] = 0xEE;

    var chunks = DjVuReader.ParseChunks(data, 0, data.Length);

    Assert.That(chunks, Has.Count.EqualTo(2));
    Assert.That(chunks[0].ChunkId, Is.EqualTo("ODD_"));
    Assert.That(chunks[0].Data.Length, Is.EqualTo(3));
    Assert.That(chunks[1].ChunkId, Is.EqualTo("EVEN"));
    Assert.That(chunks[1].Data.Length, Is.EqualTo(2));
    Assert.That(chunks[1].Data[0], Is.EqualTo(0xDD));
  }

  [Test]
  [Category("Unit")]
  public void ParseChunks_EmptyData_ReturnsEmptyList() {
    var chunks = DjVuReader.ParseChunks([], 0, 0);

    Assert.That(chunks, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void ParseChunks_TruncatedData_PartialRead() {
    // Only 4 bytes, not enough for a full chunk header
    var data = new byte[4];
    Encoding.ASCII.GetBytes("TEST", data.AsSpan(0));

    var chunks = DjVuReader.ParseChunks(data, 0, data.Length);

    Assert.That(chunks, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void DjVuChunk_Properties_RoundTrip() {
    var chunkData = new byte[] { 1, 2, 3 };
    var chunk = new DjVuChunk {
      ChunkId = "TEST",
      Data = chunkData
    };

    Assert.That(chunk.ChunkId, Is.EqualTo("TEST"));
    Assert.That(chunk.Data, Is.SameAs(chunkData));
  }
}
