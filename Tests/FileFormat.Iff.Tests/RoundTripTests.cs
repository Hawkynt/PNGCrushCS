using System.Collections.Generic;
using FileFormat.Iff;

namespace FileFormat.Iff.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_EmptyForm_PreservesFormType() {
    var original = new IffFile { FormType = "ILBM", Chunks = [] };
    var bytes = IffWriter.ToBytes(original);
    var parsed = IffReader.FromBytes(bytes);
    Assert.That(parsed.FormType.ToString(), Is.EqualTo("ILBM"));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_SingleChunk_PreservesData() {
    var chunkData = new byte[] { 10, 20, 30, 40, 50 };
    var original = new IffFile {
      FormType = "ILBM",
      Chunks = [new IffChunk { ChunkId = "BMHD", Data = chunkData }]
    };

    var bytes = IffWriter.ToBytes(original);
    var parsed = IffReader.FromBytes(bytes);

    Assert.That(parsed.Chunks, Has.Count.EqualTo(1));
    Assert.That(parsed.Chunks[0].ChunkId.ToString(), Is.EqualTo("BMHD"));
    Assert.That(parsed.Chunks[0].Data, Is.EqualTo(chunkData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_OddSizedChunk_WordAligned() {
    var chunkData = new byte[] { 1, 2, 3 }; // 3 bytes = odd, needs padding
    var original = new IffFile {
      FormType = "ILBM",
      Chunks = [
        new IffChunk { ChunkId = "CMAP", Data = chunkData },
        new IffChunk { ChunkId = "BODY", Data = [99] }
      ]
    };

    var bytes = IffWriter.ToBytes(original);
    var parsed = IffReader.FromBytes(bytes);

    Assert.That(parsed.Chunks, Has.Count.EqualTo(2));
    Assert.That(parsed.Chunks[0].Data, Is.EqualTo(chunkData));
    Assert.That(parsed.Chunks[1].Data, Is.EqualTo(new byte[] { 99 }));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_NestedForm_PreservesStructure() {
    // Build a nested FORM by hand: FORM chunk containing sub-form type + inner chunk data
    var innerChunkData = new byte[] { 65, 66, 67, 68 };
    var innerChunk = new IffChunk { ChunkId = "BMHD", Data = innerChunkData };

    // Build the nested FORM's raw data: 4-byte sub-form type + inner chunk header + inner chunk data
    var subFormType = new byte[] { (byte)'I', (byte)'L', (byte)'B', (byte)'M' };
    var innerHeaderBytes = new byte[IffChunkHeader.StructSize];
    new IffChunkHeader("BMHD", innerChunkData.Length).WriteTo(innerHeaderBytes);
    var nestedData = new byte[4 + IffChunkHeader.StructSize + innerChunkData.Length];
    subFormType.CopyTo(nestedData, 0);
    innerHeaderBytes.CopyTo(nestedData, 4);
    innerChunkData.CopyTo(nestedData, 4 + IffChunkHeader.StructSize);

    var original = new IffFile {
      FormType = "TEST",
      Chunks = [
        new IffChunk { ChunkId = "FORM", Data = nestedData, SubChunks = [innerChunk] }
      ]
    };

    var bytes = IffWriter.ToBytes(original);
    var parsed = IffReader.FromBytes(bytes);

    Assert.That(parsed.Chunks, Has.Count.EqualTo(1));
    Assert.That(parsed.Chunks[0].ChunkId.ToString(), Is.EqualTo("FORM"));
    Assert.That(parsed.Chunks[0].SubChunks, Is.Not.Null);
    Assert.That(parsed.Chunks[0].SubChunks!, Has.Count.EqualTo(1));
    Assert.That(parsed.Chunks[0].SubChunks![0].ChunkId.ToString(), Is.EqualTo("BMHD"));
    Assert.That(parsed.Chunks[0].SubChunks![0].Data, Is.EqualTo(innerChunkData));
  }
}
