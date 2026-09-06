using System;
using FileFormat.Avif.Codec;

namespace FileFormat.Avif.Tests;

/// <summary>The AV1 arithmetic coder, which every other symbol in the format is built on.</summary>
[TestFixture]
public sealed class Av1SymbolCoderTests {

  [Test]
  [Category("Unit")]
  public void ReadLiteral_MatchesAv1Section82ArithmeticBitProcess() {
    // Fixed section 8.2 trace vector. The second and third octets deliberately differ from the
    // input bytes: read_bool() is arithmetic-coded and is not a raw bit reader after initialization.
    var decoder = new Av1SymbolDecoder([0x12, 0x34, 0x56, 0x78], 0, 4, false);

    Assert.Multiple(() => {
      Assert.That(decoder.ReadLiteral(8), Is.EqualTo(0x12u));
      Assert.That(decoder.ReadLiteral(8), Is.EqualTo(0x01u));
      Assert.That(decoder.ReadLiteral(8), Is.EqualTo(0x36u));
    });
  }

  [Test]
  [Category("Unit")]
  public void ReadSymbol_UsesNormativeIntervalsAndAdaptiveCdfUpdate() {
    var decoder = new Av1SymbolDecoder([0x12, 0x34, 0x56, 0x78], 0, 4, false);
    ushort[] cdf = [8192, 24576, 32768, 0];

    var symbols = new int[6];
    for (var i = 0; i < symbols.Length; ++i)
      symbols[i] = decoder.ReadSymbol(cdf, 0, 3);

    Assert.Multiple(() => {
      Assert.That(symbols, Is.EqualTo(new[] { 0, 0, 2, 2, 0, 1 }));
      Assert.That(cdf, Is.EqualTo(new ushort[] { 10547, 23718, 32768, 6 }));
    });
  }

  [Test]
  [Category("Unit")]
  public void ReadSymbol_RespectsDisableCdfUpdate() {
    var decoder = new Av1SymbolDecoder([0x12, 0x34, 0x56, 0x78], 0, 4, true);
    ushort[] cdf = [8192, 24576, 32768, 0];

    var symbols = new int[6];
    for (var i = 0; i < symbols.Length; ++i)
      symbols[i] = decoder.ReadSymbol(cdf, 0, 3);

    Assert.Multiple(() => {
      Assert.That(symbols, Is.EqualTo(new[] { 0, 1, 0, 1, 0, 0 }));
      Assert.That(cdf, Is.EqualTo(new ushort[] { 8192, 24576, 32768, 0 }));
    });
  }

  [Test]
  [Category("Unit")]
  public void ReadSymbol_KeepsTerminalCdfProbabilityFixed() {
    var decoder = new Av1SymbolDecoder([0xA5, 0x5A, 0xC3, 0x3C], 0, 4, false);
    ushort[] cdf = [8192, 24576, 32768, 0];

    for (var i = 0; i < 40; ++i)
      decoder.ReadSymbol(cdf, 0, 3);

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

    var decoder = new Av1SymbolDecoder(encoded, 0, encoded.Length, true);

    Assert.Multiple(() => {
      // Fixed arithmetic-writer trace for rng=0x8000/cnt=-9, not a raw literal byte stream.
      Assert.That(encoded, Is.EqualTo(new byte[] { 0x12, 0x67, 0x79, 0x80 }));
      Assert.That(decoder.ReadLiteral(24), Is.EqualTo(0x123456u));
    });
  }

  /// <summary>
  /// The encoder and decoder have to adapt in lockstep, because a divergence is invisible: the
  /// stream keeps decoding, it just stops meaning what was written.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void SymbolEncoder_AdaptiveSymbols_RoundTripThroughTheDecoder([Values(false, true)] bool disableCdfUpdate) {
    ushort[] writerCdf = [8192, 24576, 32768, 0];
    ushort[] readerCdf = [8192, 24576, 32768, 0];

    var symbols = new int[400];
    var random = new Random(20240607);
    for (var i = 0; i < symbols.Length; ++i)
      symbols[i] = random.Next(3);

    var encoder = new Av1SymbolEncoder(disableCdfUpdate);
    foreach (var symbol in symbols)
      encoder.WriteSymbol(writerCdf, 0, 3, symbol);
    var encoded = encoder.Finish();

    var decoder = new Av1SymbolDecoder(encoded, 0, encoded.Length, disableCdfUpdate);
    var decoded = new int[symbols.Length];
    for (var i = 0; i < decoded.Length; ++i)
      decoded[i] = decoder.ReadSymbol(readerCdf, 0, 3);

    Assert.Multiple(() => {
      Assert.That(decoded, Is.EqualTo(symbols));
      Assert.That(readerCdf, Is.EqualTo(writerCdf), "the two sides must end on the same probabilities");
    });
  }

  [Test]
  [Category("Unit")]
  public void SymbolEncoder_Golomb_RoundTripsEveryLengthTheDecoderAccepts() {
    int[] values = [0, 1, 2, 3, 7, 8, 15, 16, 255, 256, 4095, 65535, 262143];

    var encoder = new Av1SymbolEncoder(true);
    foreach (var value in values)
      encoder.WriteGolomb(value);
    var encoded = encoder.Finish();

    var decoder = new Av1SymbolDecoder(encoded, 0, encoded.Length, true);
    var decoded = new int[values.Length];
    for (var i = 0; i < decoded.Length; ++i)
      decoded[i] = decoder.ReadGolomb();

    Assert.That(decoded, Is.EqualTo(values));
  }
}
