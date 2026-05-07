#if CW_SIBLING
using System.IO;
using Compression.Core.BitIO;

namespace FileFormat.Jpeg.Tests;

/// <summary>
/// Smoke tests for the cross-repo source link to CompressionWorkbench.
/// Compiled only when the CW sibling is present (CI clones it; local dev needs
/// ..\..\CompressionWorkbench checked out next to PNGCrushCS).
///
/// Purpose: catch regressions in the linking pipeline (csproj wiring, global
/// usings, namespace resolution) before any consuming codec breaks.
/// </summary>
[TestFixture]
public sealed class CwSiblingLinkSmokeTests {

  [Test]
  public void BitWriter_LsbFirst_PacksBitsIntoLsbsOfOutputByte() {
    using var ms = new MemoryStream();
    var writer = new BitWriter<LsbBitOrder>(ms);
    writer.WriteBits(0b1011U, 4);
    writer.WriteBits(0b1100U, 4);
    writer.FlushBits();

    Assert.That(ms.ToArray(), Is.EqualTo(new byte[] { 0b1100_1011 }),
      "LSB-first packs the first-written bits into the low bits of the byte (Deflate/PNG order).");
  }

  [Test]
  public void BitWriter_MsbFirst_PacksBitsIntoMsbsOfOutputByte() {
    using var ms = new MemoryStream();
    var writer = new BitWriter<MsbBitOrder>(ms);
    writer.WriteBits(0b1011U, 4);
    writer.WriteBits(0b1100U, 4);
    writer.FlushBits();

    Assert.That(ms.ToArray(), Is.EqualTo(new byte[] { 0b1011_1100 }),
      "MSB-first packs the first-written bits into the high bits of the byte (JPEG order).");
  }

  [Test]
  public void BitReader_BitWriter_FullRoundTrip_MsbFirst() {
    using var ms = new MemoryStream();
    var writer = new BitWriter<MsbBitOrder>(ms);
    writer.WriteBits(0b1010_1100_0011U, 12);
    writer.WriteBits(0b1U, 1);
    writer.WriteBits(0b101U, 3);
    writer.FlushBits();

    ms.Position = 0;
    var reader = new BitReader<MsbBitOrder>(ms);
    Assert.Multiple(() => {
      Assert.That(reader.ReadBits(12), Is.EqualTo(0b1010_1100_0011U));
      Assert.That(reader.ReadBits(1),  Is.EqualTo(0b1U));
      Assert.That(reader.ReadBits(3),  Is.EqualTo(0b101U));
    });
  }
}
#endif
