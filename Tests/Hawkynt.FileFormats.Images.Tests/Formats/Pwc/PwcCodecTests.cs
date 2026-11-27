using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using FileFormat.Pwc;

namespace FileFormat.Pwc.Tests;

[TestFixture]
public sealed class PwcCodecTests {

  /// <summary>
  /// The shipped coder carries its split ratios as a 65536-byte table (<c>muldiv</c> in <c>pwc.lib</c>'s
  /// <c>Ncencode.obj</c>, the same bytes inside <c>pwcdecode.exe</c>). This package computes it; the hash below
  /// is of the table as extracted from the library, so any drift in the formula fails here.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void SplitTable_IsTheShippedTable() {
    var hash = Convert.ToHexString(SHA256.HashData(PwcArithmeticCoder.SplitTable)).ToLowerInvariant();
    Assert.That(hash, Is.EqualTo("db197b41ba1f067c39bf5198d97b969a3e4b02320e35107d173a9db8a9eddbc9"));
  }

  [TestCase(1, 0, 255)]
  [TestCase(1, 2, 127)]
  [TestCase(1, 3, 84)]
  [TestCase(2, 3, 170)]
  [TestCase(100, 250, 101)]
  [TestCase(5, 5, 255)]
  [Category("Unit")]
  public void SplitTable_SampleEntries(int zeros, int total, int expected)
    => Assert.That(PwcArithmeticCoder.SplitTable[zeros << 8 | total], Is.EqualTo(expected));

  [Test]
  [Category("Unit")]
  public void ArithmeticCoder_RoundTripsBothAdaptations() {
    var random = new Random(3);
    var bits = new int[20000];
    for (var i = 0; i < bits.Length; ++i)
      bits[i] = random.Next(10) < (i % 7 == 0 ? 8 : 2) ? 1 : 0;

    var output = new List<byte>();
    var encoder = PwcArithmeticCoder.ForEncoding(output, 7);
    var states = new PwcStatistic[8];
    Array.Fill(states, PwcStatistic.Fresh);
    for (var i = 0; i < bits.Length; ++i)
      if ((i & 1) == 0)
        encoder.Adaptive(ref states[i % 8], bits[i]);
      else
        encoder.Fixed(ref states[i % 8], bits[i]);

    encoder.Flush();

    var decoder = PwcArithmeticCoder.ForDecoding([.. output], 0, 7);
    Array.Fill(states, PwcStatistic.Fresh);
    for (var i = 0; i < bits.Length; ++i) {
      var bit = (i & 1) == 0 ? decoder.Adaptive(ref states[i % 8], 0) : decoder.Fixed(ref states[i % 8], 0);
      Assert.That(bit, Is.EqualTo(bits[i]), $"decision {i}");
    }

    Assert.That(decoder.EndedCleanly, Is.True);
    Assert.That(decoder.Position, Is.EqualTo(output.Count));
  }

  [TestCase(1)]
  [TestCase(4)]
  [TestCase(15)]
  [TestCase(39)]
  [TestCase(255)]
  [Category("Unit")]
  public void SymbolCoder_RoundTripsEveryValueAroundEveryPrediction(int maximum) {
    var output = new List<byte>();
    var encoder = PwcArithmeticCoder.ForEncoding(output, 7);
    var encoding = new PwcSymbolCoder(maximum);
    var cases = new List<(int Value, int Prediction, int Activity)>();
    for (var prediction = 0; prediction <= maximum; ++prediction)
    for (var value = 0; value <= maximum; ++value)
      cases.Add((value, prediction, (value * 7 + prediction) % 10));

    foreach (var (value, prediction, activity) in cases)
      encoding.Code(encoder, value, prediction, activity);

    encoder.Flush();

    var decoder = PwcArithmeticCoder.ForDecoding([.. output], 0, 7);
    var decoding = new PwcSymbolCoder(maximum);
    foreach (var (value, prediction, activity) in cases)
      Assert.That(decoding.Code(decoder, 0, prediction, activity), Is.EqualTo(value), $"value {value} around {prediction}");
  }
}
