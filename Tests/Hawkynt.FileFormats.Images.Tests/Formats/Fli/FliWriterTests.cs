using System;
using System.Buffers.Binary;
using FileFormat.Fli;

namespace FileFormat.Fli.Tests;

[TestFixture]
public sealed class FliWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_FliMagic_WritesCorrectMagic() {
    var file = _CreateTestFliFile(1, FliFrameType.Fli);
    var bytes = FliWriter.ToBytes(file);

    var magic = BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(4));
    Assert.That(magic, Is.EqualTo(unchecked((short)0xAF11)));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_FlcMagic_WritesCorrectMagic() {
    var file = _CreateTestFliFile(1, FliFrameType.Flc);
    var bytes = FliWriter.ToBytes(file);

    var magic = BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(4));
    Assert.That(magic, Is.EqualTo(unchecked((short)0xAF12)));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_HeaderSize_MatchesFileLength() {
    var file = _CreateTestFliFile(1, FliFrameType.Fli);
    var bytes = FliWriter.ToBytes(file);

    var size = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(0));
    Assert.That(size, Is.EqualTo(bytes.Length));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_FrameCount_MatchesInput() {
    var file = _CreateTestFliFile(3, FliFrameType.Fli);
    var bytes = FliWriter.ToBytes(file);

    var frames = BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(6));
    Assert.That(frames, Is.EqualTo(3));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_FrameMagic_IsF1FA() {
    var file = _CreateTestFliFile(1, FliFrameType.Fli);
    var bytes = FliWriter.ToBytes(file);

    // Frame starts at offset 128
    var frameMagic = BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(128 + 4));
    Assert.That(frameMagic, Is.EqualTo(unchecked((short)0xF1FA)));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Dimensions_Written() {
    var file = _CreateTestFliFile(1, FliFrameType.Fli, 160, 100);
    var bytes = FliWriter.ToBytes(file);

    var width = BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(8));
    var height = BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(10));
    Assert.That(width, Is.EqualTo(160));
    Assert.That(height, Is.EqualTo(100));
  }

  private static FliFile _CreateTestFliFile(int frameCount, FliFrameType frameType, short width = 320, short height = 200) {
    var frames = new FliFrame[frameCount];
    for (var i = 0; i < frameCount; ++i)
      frames[i] = new FliFrame {
        Chunks = [new FliFrameChunk { ChunkType = FliChunkType.Black, Data = [] }]
      };

    return new FliFile {
      Width = width,
      Height = height,
      FrameCount = (short)frameCount,
      Speed = 100,
      FrameType = frameType,
      Frames = frames
    };
  }
}
