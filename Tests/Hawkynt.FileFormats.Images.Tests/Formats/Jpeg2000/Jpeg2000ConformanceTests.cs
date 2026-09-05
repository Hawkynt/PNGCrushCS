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
    // inverse transform and DC level shift against the standard's own worked example.
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
  public void Writer_UsesNormativeReversibleQuantizationForEightBitSamples() {
    var bytes = Jpeg2000Writer.ToCodestreamBytes(new Jpeg2000File {
      Width = 1,
      Height = 1,
      ComponentCount = 1,
      BitsPerComponent = 8,
      DecompositionLevels = 0,
      PixelData = [128],
    });

    var qcd = _FindMarker(bytes, 0xFF5C);
    Assert.Multiple(() => {
      Assert.That(BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(qcd + 2)), Is.EqualTo(4), "one subband");
      Assert.That(bytes[qcd + 4], Is.EqualTo(0x40), "two guard bits, no quantization");
      Assert.That(bytes[qcd + 5], Is.EqualTo(8 << 3), "epsilon_LL = source precision for a reversible LL band");
    });
  }

  /// <summary>
  /// A one-level codestream names four subbands, and E.1.1 gives the two singly high-pass ones a
  /// gain of one and the doubly high-pass one a gain of two. This is the pairing OpenJPEG's own
  /// Annex J fixture above uses, and it is what fixes Mb for every code-block.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void Writer_GivesEachSubbandItsOwnNominalRange() {
    var bytes = Jpeg2000Writer.ToCodestreamBytes(new Jpeg2000File {
      Width = 4,
      Height = 4,
      ComponentCount = 1,
      BitsPerComponent = 8,
      DecompositionLevels = 1,
      PixelData = new byte[16],
    });

    var qcd = _FindMarker(bytes, 0xFF5C);
    Assert.Multiple(() => {
      Assert.That(BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(qcd + 2)), Is.EqualTo(7), "four subbands");
      Assert.That(bytes[qcd + 5], Is.EqualTo(8 << 3), "LL");
      Assert.That(bytes[qcd + 6], Is.EqualTo(9 << 3), "HL");
      Assert.That(bytes[qcd + 7], Is.EqualTo(9 << 3), "LH");
      Assert.That(bytes[qcd + 8], Is.EqualTo(10 << 3), "HH");
    });
  }

  private static int _FindMarker(byte[] data, ushort marker) {
    for (var i = 0; i + 1 < data.Length; ++i)
      if (BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(i)) == marker)
        return i;

    Assert.Fail($"Marker 0x{marker:X4} not found.");
    return -1;
  }
}
