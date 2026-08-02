using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace FileFormat.WebP.Tests;

/// <summary>
/// An animated WebP keeps its pictures inside ANMF chunks rather than at the top level.
/// </summary>
/// <remarks>
/// A file holding seventeen frames looked to the reader like a file holding none, and was refused for
/// containing neither VP8 nor VP8L data — which was true of its outermost level and of nothing else.
/// </remarks>
[TestFixture]
public sealed class WebPAnimationTests {

  /// <summary>Builds the smallest animation that carries a lossless frame inside an ANMF chunk.</summary>
  private static byte[] _Animation(byte[] frameChunk) {
    using var body = new MemoryStream();

    void Chunk(string id, byte[] payload) {
      body.Write(Encoding.ASCII.GetBytes(id));
      var length = new byte[4];
      BinaryPrimitives.WriteInt32LittleEndian(length, payload.Length);
      body.Write(length);
      body.Write(payload);
      if ((payload.Length & 1) != 0)
        body.WriteByte(0);
    }

    Chunk("VP8X", [0x02, 0, 0, 0, 15, 0, 0, 15, 0, 0]);
    Chunk("ANIM", [0, 0, 0, 0, 0, 0]);

    // Sixteen bytes of frame description, then the frame's own chunks.
    using var frame = new MemoryStream();
    frame.Write(new byte[16]);
    frame.Write(frameChunk);
    Chunk("ANMF", frame.ToArray());

    using var file = new MemoryStream();
    file.Write("RIFF"u8);
    var size = new byte[4];
    BinaryPrimitives.WriteInt32LittleEndian(size, 4 + (int)body.Length);
    file.Write(size);
    file.Write("WEBP"u8);
    file.Write(body.ToArray());

    return file.ToArray();
  }

  [Test]
  [Category("Unit")]
  public void Read_LooksInsideAnAnimationFrameForThePicture() {
    // A reader that never looks inside ANMF refuses this outright for holding neither VP8 nor VP8L,
    // which is true of its outermost level and of nothing else.
    using var inner = new MemoryStream();
    inner.Write("VP8L"u8);
    var length = new byte[4];
    BinaryPrimitives.WriteInt32LittleEndian(length, 8);
    inner.Write(length);
    inner.Write(new byte[8]);

    Assert.DoesNotThrow(() => WebPReader.FromBytes(_Animation(inner.ToArray())),
      "the frame's picture chunk should have been found inside the animation");
  }
}
