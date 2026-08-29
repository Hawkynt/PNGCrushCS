using FileFormat.Avif.Codec;

namespace FileFormat.Avif.Tests;

[TestFixture]
public sealed class Av1AnsDecoderTests {

  [Test]
  [Category("Unit")]
  public void ReadLiteral_MatchesAv1Section82ArithmeticBitProcess() {
    // Fixed section 8.2 trace vector. The second/third octets intentionally differ from the input
    // bytes: read_bool() is arithmetic-coded and is not a raw bit-reader after initialization.
    var decoder = new Av1AnsDecoder([0x12, 0x34, 0x56, 0x78], 0, 4);

    Assert.Multiple(() => {
      Assert.That(decoder.DecodeLiteralBits(8), Is.EqualTo(0x12u));
      Assert.That(decoder.DecodeLiteralBits(8), Is.EqualTo(0x01u));
      Assert.That(decoder.DecodeLiteralBits(8), Is.EqualTo(0x36u));
    });
  }

  [Test]
  [Category("Unit")]
  public void ReadSymbol_UsesNormativeIntervalsAndAdaptiveCdfUpdate() {
    var decoder = new Av1AnsDecoder([0x12, 0x34, 0x56, 0x78], 0, 4);
    ushort[] cdf = [8192, 24576, 32768, 0];

    var symbols = new int[6];
    for (var i = 0; i < symbols.Length; ++i)
      symbols[i] = decoder.DecodeSymbol(cdf, 3);

    Assert.Multiple(() => {
      Assert.That(symbols, Is.EqualTo(new[] { 0, 0, 2, 2, 0, 1 }));
      Assert.That(cdf, Is.EqualTo(new ushort[] { 10547, 23718, 32768, 6 }));
    });
  }

  [Test]
  [Category("Unit")]
  public void ReadSymbol_RespectsDisableCdfUpdate() {
    var decoder = new Av1AnsDecoder([0x12, 0x34, 0x56, 0x78], 0, 4, disableCdfUpdate: true);
    ushort[] cdf = [8192, 24576, 32768, 0];

    var symbols = new int[6];
    for (var i = 0; i < symbols.Length; ++i)
      symbols[i] = decoder.DecodeSymbol(cdf, 3);

    Assert.Multiple(() => {
      Assert.That(symbols, Is.EqualTo(new[] { 0, 1, 0, 1, 0, 0 }));
      Assert.That(cdf, Is.EqualTo(new ushort[] { 8192, 24576, 32768, 0 }));
    });
  }

  [Test]
  [Category("Unit")]
  public void ReadSymbol_KeepsTerminalCdfProbabilityFixed() {
    var decoder = new Av1AnsDecoder([0xA5, 0x5A, 0xC3, 0x3C], 0, 4);
    ushort[] cdf = [8192, 24576, 32768, 0];

    for (var i = 0; i < 40; ++i)
      decoder.DecodeSymbol(cdf, 3);

    Assert.Multiple(() => {
      Assert.That(cdf[2], Is.EqualTo(32768));
      Assert.That(cdf[3], Is.EqualTo(32), "the AV1 adaptation counter saturates at 32");
      Assert.That(cdf[0], Is.LessThanOrEqualTo(cdf[1]));
      Assert.That(cdf[1], Is.LessThanOrEqualTo(cdf[2]));
    });
  }

  [Test]
  [Category("Unit")]
  public void RangeEncoder_EquiprobableLiteral_RoundTripsThroughSection82Decoder() {
    var encoder = new Av1RangeEncoder();
    encoder.WriteLiteral(0x123456, 24);
    var encoded = encoder.Finish();

    var decoder = new Av1AnsDecoder(encoded, 0, encoded.Length, disableCdfUpdate: true);

    Assert.Multiple(() => {
      // Fixed arithmetic-writer trace for rng=0x8000/cnt=-9, not a raw literal byte stream.
      Assert.That(encoded, Is.EqualTo(new byte[] { 0x12, 0x67, 0x79, 0x80 }));
      Assert.That(decoder.DecodeLiteralBits(24), Is.EqualTo(0x123456u));
    });
  }

  [Test]
  [Category("Unit")]
  public void RangeEncoder_InverseCdfSymbols_RoundTripWithoutAdaptation() {
    // Decoder-side CDF is cumulative from the low end. Encoder-side AV1 CDF is inverse from the
    // high end. These describe the same 1/4, 1/2, 1/4 three-symbol distribution.
    ushort[] decoderCdf = [8192, 24576, 32768, 0];
    ushort[] encoderInverseCdf = [24576, 8192, 0];
    int[] symbols = [0, 2, 1, 1, 0, 2, 2, 1];

    var encoder = new Av1RangeEncoder();
    foreach (var symbol in symbols)
      encoder.WriteSymbol(symbol, encoderInverseCdf);
    var encoded = encoder.Finish();

    var decoder = new Av1AnsDecoder(encoded, 0, encoded.Length, disableCdfUpdate: true);
    var decoded = new int[symbols.Length];
    for (var i = 0; i < decoded.Length; ++i)
      decoded[i] = decoder.DecodeSymbol(decoderCdf, 3);

    Assert.That(decoded, Is.EqualTo(symbols));
  }
}
