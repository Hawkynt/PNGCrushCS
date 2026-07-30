using System;
using FileFormat.Fli;

namespace FileFormat.Fli.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void FliFrameType_HasExpectedValues() {
    Assert.That((short)FliFrameType.Fli, Is.EqualTo(unchecked((short)0xAF11)));
    Assert.That((short)FliFrameType.Flc, Is.EqualTo(unchecked((short)0xAF12)));

    var values = Enum.GetValues<FliFrameType>();
    Assert.That(values, Has.Length.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void FliChunkType_HasExpectedValues() {
    Assert.That((short)FliChunkType.Color256, Is.EqualTo(4));
    Assert.That((short)FliChunkType.DeltaFlc, Is.EqualTo(7));
    Assert.That((short)FliChunkType.Color64, Is.EqualTo(11));
    Assert.That((short)FliChunkType.DeltaFli, Is.EqualTo(12));
    Assert.That((short)FliChunkType.Black, Is.EqualTo(13));
    Assert.That((short)FliChunkType.ByteRun, Is.EqualTo(15));
    Assert.That((short)FliChunkType.Literal, Is.EqualTo(16));

    var values = Enum.GetValues<FliChunkType>();
    Assert.That(values, Has.Length.EqualTo(7));
  }

  [Test]
  [Category("Unit")]
  public void FliFile_DefaultValues() {
    var file = new FliFile();

    Assert.That(file.Width, Is.EqualTo(0));
    Assert.That(file.Height, Is.EqualTo(0));
    Assert.That(file.FrameCount, Is.EqualTo(0));
    Assert.That(file.Speed, Is.EqualTo(0));
    Assert.That(file.Palette, Is.Null);
    Assert.That(file.Frames, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void FliFrame_DefaultValues() {
    var frame = new FliFrame();

    Assert.That(frame.Chunks, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void FliFrameChunk_DefaultValues() {
    var chunk = new FliFrameChunk();

    Assert.That(chunk.Data, Is.Empty);
  }
}
