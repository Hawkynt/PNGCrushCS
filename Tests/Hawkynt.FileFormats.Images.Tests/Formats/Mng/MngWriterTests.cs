using System;
using System.Buffers.Binary;
using FileFormat.Mng;

namespace FileFormat.Mng.Tests;

[TestFixture]
public sealed class MngWriterTests {

  private static readonly byte[] _MNG_SIGNATURE = { 0x8A, 0x4D, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

  [Test]
  [Category("Unit")]
  public void ToBytes_StartsWithMngSignature() {
    var file = new MngFile { Width = 1, Height = 1, TicksPerSecond = 1000, Frames = [] };
    var bytes = MngWriter.ToBytes(file);
    Assert.That(bytes.AsSpan(0, _MNG_SIGNATURE.Length).ToArray(), Is.EqualTo(_MNG_SIGNATURE));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_WritesNormativeVlcHeaderCountsAndProfile() {
    var png = MngTestHelper.BuildMinimalPng();
    var file = new MngFile { Width = 1, Height = 1, TicksPerSecond = 1000, Frames = [png, png] };

    var bytes = MngWriter.ToBytes(file);
    var mhdr = FindChunk(bytes, "MHDR");

    Assert.That(mhdr.Length, Is.EqualTo(28));
    Assert.That(BinaryPrimitives.ReadUInt32BigEndian(mhdr[12..]), Is.EqualTo(3u), "VLC layer count is embedded images + background");
    Assert.That(BinaryPrimitives.ReadUInt32BigEndian(mhdr[16..]), Is.EqualTo(2u));
    Assert.That(BinaryPrimitives.ReadUInt32BigEndian(mhdr[20..]), Is.EqualTo(2u));
    Assert.That(BinaryPrimitives.ReadUInt32BigEndian(mhdr[24..]), Is.EqualTo(457u), "VLC profile allowing transparency");
  }

  [TestCase(MngTermAction.ShowLast, 0)]
  [TestCase(MngTermAction.ShowBlank, 1)]
  [TestCase(MngTermAction.ShowFirst, 2)]
  [Category("Unit")]
  public void ToBytes_NonRepeatTermUsesOneByteForm(MngTermAction action, int wireValue) {
    var file = new MngFile { Width = 1, Height = 1, TicksPerSecond = 1, TermAction = action, Frames = [] };
    var term = FindChunk(MngWriter.ToBytes(file), "TERM");

    Assert.That(term.Length, Is.EqualTo(1));
    Assert.That(term[0], Is.EqualTo((byte)wireValue));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_RepeatTermUsesNormativeTenByteForm() {
    var file = new MngFile {
      Width = 1,
      Height = 1,
      TicksPerSecond = 100,
      TermAction = MngTermAction.Repeat,
      ActionAfterIterations = MngTermAction.ShowFirst,
      RepeatDelay = 25,
      NumPlays = 7,
      Frames = []
    };

    var term = FindChunk(MngWriter.ToBytes(file), "TERM");

    Assert.That(term.Length, Is.EqualTo(10));
    Assert.That(term[0], Is.EqualTo(3));
    Assert.That(term[1], Is.EqualTo(2));
    Assert.That(BinaryPrimitives.ReadUInt32BigEndian(term[2..]), Is.EqualTo(25u));
    Assert.That(BinaryPrimitives.ReadUInt32BigEndian(term[6..]), Is.EqualTo(7u));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_HasMendChunk() {
    var file = new MngFile { Width = 1, Height = 1, TicksPerSecond = 1000, Frames = [] };
    var bytes = MngWriter.ToBytes(file);
    Assert.That(FindChunk(bytes, "MEND").Length, Is.Zero);
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_FramesEmbedded_ContainsIhdrChunks() {
    var png = MngTestHelper.BuildMinimalPng();
    var file = new MngFile { Width = 1, Height = 1, TicksPerSecond = 1000, Frames = [png] };
    var bytes = MngWriter.ToBytes(file);
    Assert.That(FindChunk(bytes, "IHDR").Length, Is.EqualTo(13));
  }

  private static ReadOnlySpan<byte> FindChunk(byte[] bytes, string type) {
    var offset = 8;
    while (offset + 12 <= bytes.Length) {
      var length = checked((int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset, 4)));
      if (offset + 12 + length > bytes.Length)
        Assert.Fail($"Truncated {type} fixture while scanning MNG chunks.");

      var chunkType = System.Text.Encoding.ASCII.GetString(bytes, offset + 4, 4);
      if (chunkType == type)
        return bytes.AsSpan(offset + 8, length);
      offset += 12 + length;
    }

    Assert.Fail($"Chunk {type} not found.");
    return default;
  }
}
