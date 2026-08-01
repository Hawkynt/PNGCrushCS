using System;
using System.Buffers.Binary;
using System.Text;
using FileFormat.Core;

namespace FileFormat.IffDeep.Tests;

/// <summary>
/// DEEP: a global chunk, a component list, and the pixels in DBOD rather than BODY.
/// </summary>
[TestFixture]
public sealed class IffDeepTests {

  private static RawImage _Picture(int width, int height) {
    var pixels = new byte[width * height * 3];
    for (var i = 0; i < pixels.Length; i += 3) {
      pixels[i] = (byte)(i % 251);
      pixels[i + 1] = (byte)(i % 241);
      pixels[i + 2] = (byte)(i % 239);
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = pixels };
  }

  private static int _ChunkAt(byte[] bytes, string id) {
    for (var at = 12; at + 8 <= bytes.Length;) {
      var length = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(at + 4));
      if (Encoding.ASCII.GetString(bytes, at, 4) == id)
        return at;

      at += 8 + length + (length & 1);
    }

    return -1;
  }

  [Test]
  [Category("Unit")]
  public void Written_NamesEachComponentAndCountsThemFirst() {
    var bytes = IffDeepWriter.ToBytes(IffDeepFile.FromRawImage(_Picture(32, 16)));
    var dpel = _ChunkAt(bytes, "DPEL");

    Assert.That(dpel, Is.GreaterThan(0), "there must be a DPEL");
    var length = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(dpel + 4));
    var components = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(dpel + 8));

    Assert.Multiple(() => {
      Assert.That(components, Is.EqualTo(3));
      Assert.That(length, Is.EqualTo((components + 1) * 4), "the length follows from the count");
      Assert.That(bytes[dpel + 13], Is.EqualTo(1), "red");
      Assert.That(bytes[dpel + 15], Is.EqualTo(8), "eight bits");
      Assert.That(bytes[dpel + 17], Is.EqualTo(2), "green");
      Assert.That(bytes[dpel + 21], Is.EqualTo(3), "blue");
    });
  }

  [Test]
  [Category("Unit")]
  public void Written_PutsThePixelsInDbod() {
    var bytes = IffDeepWriter.ToBytes(IffDeepFile.FromRawImage(_Picture(32, 16)));

    Assert.Multiple(() => {
      Assert.That(Encoding.ASCII.GetString(bytes, 8, 4), Is.EqualTo("DEEP"));
      Assert.That(_ChunkAt(bytes, "DBOD"), Is.GreaterThan(0));
      Assert.That(_ChunkAt(bytes, "BODY"), Is.EqualTo(-1), "BODY is the ILBM name and no DEEP reader looks for it");
    });
  }

  [Test]
  [Category("Unit")]
  public void Written_LeavesTheLastWordOfDgblAsThePixelAspect() {
    var bytes = IffDeepWriter.ToBytes(IffDeepFile.FromRawImage(_Picture(32, 16)));
    var dgbl = _ChunkAt(bytes, "DGBL");

    Assert.Multiple(() => {
      Assert.That(BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(dgbl + 8)), Is.EqualTo(32));
      Assert.That(BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(dgbl + 10)), Is.EqualTo(16));
      Assert.That(bytes[dgbl + 12], Is.EqualTo(0), "the high byte of the compression word");
      Assert.That(bytes[dgbl + 14], Is.EqualTo(1), "square pixels");
      Assert.That(bytes[dgbl + 15], Is.EqualTo(1));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_KeepsEveryPixel() {
    var original = _Picture(64, 32);
    var restored = IffDeepFile.ToRawImage(
      IffDeepReader.FromBytes(IffDeepWriter.ToBytes(IffDeepFile.FromRawImage(original))));

    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }
}
