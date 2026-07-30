using System.Collections.Generic;
using FileFormat.Riff;

namespace FileFormat.Riff.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_EmptyFile_PreservesFormType() {
    var original = new RiffFile { FormType = "ACON" };
    var bytes = RiffWriter.ToBytes(original);
    var parsed = RiffReader.FromBytes(bytes);
    Assert.That(parsed.FormType.ToString(), Is.EqualTo("ACON"));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_SingleChunk_PreservesData() {
    var chunkData = new byte[] { 10, 20, 30, 40, 50 };
    var original = new RiffFile {
      FormType = "TEST",
      Chunks = [new RiffChunk { Id = "dat1", Data = chunkData }]
    };

    var bytes = RiffWriter.ToBytes(original);
    var parsed = RiffReader.FromBytes(bytes);

    Assert.That(parsed.Chunks, Has.Count.EqualTo(1));
    Assert.That(parsed.Chunks[0].Id.ToString(), Is.EqualTo("dat1"));
    Assert.That(parsed.Chunks[0].Data, Is.EqualTo(chunkData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_OddSizedChunk_WordAligned() {
    var chunkData = new byte[] { 1, 2, 3 }; // 3 bytes = odd, needs padding
    var original = new RiffFile {
      FormType = "TEST",
      Chunks = [
        new RiffChunk { Id = "odd1", Data = chunkData },
        new RiffChunk { Id = "dat2", Data = [99] }
      ]
    };

    var bytes = RiffWriter.ToBytes(original);
    var parsed = RiffReader.FromBytes(bytes);

    Assert.That(parsed.Chunks, Has.Count.EqualTo(2));
    Assert.That(parsed.Chunks[0].Data, Is.EqualTo(chunkData));
    Assert.That(parsed.Chunks[1].Data, Is.EqualTo(new byte[] { 99 }));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_NestedList_PreservesStructure() {
    var original = new RiffFile {
      FormType = "TEST",
      Lists = [
        new RiffList {
          ListType = "info",
          Chunks = [new RiffChunk { Id = "INAM", Data = [65, 66, 67, 68] }]
        }
      ]
    };

    var bytes = RiffWriter.ToBytes(original);
    var parsed = RiffReader.FromBytes(bytes);

    Assert.That(parsed.Lists, Has.Count.EqualTo(1));
    Assert.That(parsed.Lists[0].ListType.ToString(), Is.EqualTo("info"));
    Assert.That(parsed.Lists[0].Chunks, Has.Count.EqualTo(1));
    Assert.That(parsed.Lists[0].Chunks[0].Id.ToString(), Is.EqualTo("INAM"));
    Assert.That(parsed.Lists[0].Chunks[0].Data, Is.EqualTo(new byte[] { 65, 66, 67, 68 }));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_MultipleChunksAndLists_AllPreserved() {
    var original = new RiffFile {
      FormType = "WAVE",
      Chunks = [
        new RiffChunk { Id = "fmt ", Data = new byte[16] },
        new RiffChunk { Id = "data", Data = new byte[100] }
      ],
      Lists = [
        new RiffList {
          ListType = "INFO",
          Chunks = [
            new RiffChunk { Id = "IART", Data = [72, 101, 108, 108] },
            new RiffChunk { Id = "ICMT", Data = [84, 101, 115, 116] }
          ]
        }
      ]
    };

    var bytes = RiffWriter.ToBytes(original);
    var parsed = RiffReader.FromBytes(bytes);

    Assert.That(parsed.FormType.ToString(), Is.EqualTo("WAVE"));
    Assert.That(parsed.Lists, Has.Count.EqualTo(1));
    Assert.That(parsed.Lists[0].Chunks, Has.Count.EqualTo(2));
    Assert.That(parsed.Chunks, Has.Count.EqualTo(2));
  }
}
