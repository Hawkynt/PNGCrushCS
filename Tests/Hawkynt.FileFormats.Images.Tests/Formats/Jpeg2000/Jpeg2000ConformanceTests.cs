using System;
using System.Buffers.Binary;
using FileFormat.Jpeg2000;

namespace FileFormat.Jpeg2000.Tests;

[TestFixture]
public sealed class Jpeg2000ConformanceTests {

  [Test]
  [Category("Unit")]
  public void Reader_DecodesTheNormativeAnnexJOneByNineCodestream() {
    // ITU-T T.800 Annex J's complete 1x9 reversible codestream. This fixture is deliberately not
    // produced by any code in this project: it exercises packet tag trees, MQ contexts, the 5/3
    // inverse transform and DC level shift against the standard's worked example.
    var codestream = Convert.FromHexString(
      "FF4FFF51002900000000000100000009"
      + "00000000000000000000000100000009"
      + "00000000000000000001070101FF5C00"
      + "074040484850FF52000C000000010001"
      + "04040001FF90000A00000000001E0001"
      + "FF93C7D40C018F0DC8755DC07C21800F"
      + "B176FFD9");

    var image = Jpeg2000Reader.FromBytes(codestream);

    Assert.Multiple(() => {
      Assert.That(image.Width, Is.EqualTo(1));
      Assert.That(image.Height, Is.EqualTo(9));
      Assert.That(image.ComponentCount, Is.EqualTo(1));
      Assert.That(image.PixelData, Is.EqualTo(new byte[] { 101, 103, 104, 105, 96, 97, 96, 102, 109 }));
    });
  }

  [Test]
  [Category("Unit")]
  public void BaselineWriter_UsesNormativeReversibleQcdForEightBitLl() {
    var bytes = Jpeg2000BaselineWriter.ToBytes(new Jpeg2000File {
      Width = 1,
      Height = 1,
      ComponentCount = 1,
      BitsPerComponent = 8,
      DecompositionLevels = 0,
      PixelData = [128],
    });

    var qcd = FindMarker(bytes, 0xFF5C);
    Assert.Multiple(() => {
      Assert.That(BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(qcd + 2)), Is.EqualTo(4));
      Assert.That(bytes[qcd + 4], Is.EqualTo(0x20), "one guard bit, no quantization");
      Assert.That(bytes[qcd + 5], Is.EqualTo(8 << 3), "epsilon_LL = source precision for reversible LL");
    });
  }

  private static int FindMarker(byte[] data, ushort marker) {
    for (var i = 0; i + 1 < data.Length; ++i)
      if (BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(i)) == marker)
        return i;

    Assert.Fail($"Marker 0x{marker:X4} not found.");
    return -1;
  }
}
