using System;
using System.Collections.Generic;
using FileFormat.JpegXl.Codec;

namespace FileFormat.JpegXl.Tests;

/// <summary>
/// The writing half of the entropy coder, measured against the reading half
/// (ISO/IEC 18181-1 §C.1–C.5).
/// </summary>
/// <remarks>
/// <see cref="JxlEntropyDecoder"/> is the piece of this package that has been
/// checked sample-for-sample against libjxl's own output, so a block it reads
/// back exactly is a block libjxl reads back exactly. That makes it the right
/// thing to write against, and these tests aim at the shapes of prefix code the
/// writer has to choose between rather than at whole pictures: one symbol, two,
/// four, many, and a histogram skewed hard enough that the natural code runs
/// deeper than the fifteen bits the format allows.
/// </remarks>
[TestFixture]
public sealed class JxlTokenStreamTests {

  private static void _AssertRoundTrip(IReadOnlyList<uint> values) {
    var stream = new JxlTokenStream();
    foreach (var value in values)
      stream.Add(value);

    var writer = new JxlBitWriter();
    stream.WriteHeader(writer, contextCount: 1);
    stream.WriteTokens(writer);
    writer.ZeroPadToByte();

    var reader = new JxlBitReader(writer.ToArray(), 0);
    var decoder = JxlEntropyDecoder.Read(reader, numContexts: 1);
    for (var i = 0; i < values.Count; ++i)
      Assert.That((uint)decoder.ReadInt(0), Is.EqualTo(values[i]), $"value {i} of {values.Count}");
  }

  [Test]
  [Category("Unit")]
  public void OneSymbol_RoundTrips() => _AssertRoundTrip([7, 7, 7, 7, 7, 7]);

  [Test]
  [Category("Unit")]
  public void OneSymbolAtZero_RoundTrips() => _AssertRoundTrip(new uint[64]);

  [TestCase(2)]
  [TestCase(3)]
  [TestCase(4)]
  [TestCase(5)]
  [TestCase(9)]
  [TestCase(40)]
  [Category("Unit")]
  public void SmallAlphabets_RoundTrip(int distinct) {
    var values = new List<uint>();
    for (var i = 0; i < 500; ++i)
      values.Add((uint)(i % distinct));
    _AssertRoundTrip(values);
  }

  /// <summary>
  /// Values far above the point where the token stops standing for the value,
  /// so every one of them carries a tail of raw bits.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void LargeValues_RoundTrip() {
    var values = new List<uint>();
    var rng = new Random(4242);
    for (var i = 0; i < 400; ++i)
      values.Add((uint)rng.Next(1 << 20));
    values.Add(0);
    values.Add(uint.MaxValue >> 2);
    _AssertRoundTrip(values);
  }

  /// <summary>
  /// A histogram whose counts follow the Fibonacci numbers is the worst case for
  /// a plain Huffman code: it produces one code length per symbol, so nineteen
  /// symbols mean a nineteen-bit code and the format permits fifteen. The writer
  /// has to flatten it, and the reader still has to get every value back.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void DeeplySkewedHistogram_IsFlattenedAndStillRoundTrips() {
    var counts = new int[20];
    counts[0] = 1;
    counts[1] = 1;
    for (var i = 2; i < counts.Length; ++i)
      counts[i] = counts[i - 1] + counts[i - 2];

    var values = new List<uint>();
    for (var symbol = 0; symbol < counts.Length; ++symbol)
      for (var n = 0; n < counts[counts.Length - 1 - symbol]; ++n)
        values.Add((uint)symbol);
    _AssertRoundTrip(values);
  }

  /// <summary>
  /// The tree at the head of a modular frame is read through six contexts that
  /// all share one code, which is the only place the writer emits a cluster map.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void SixContexts_ShareOneCodeAndRoundTrip() {
    uint[] values = [0, 5, 0, 0, 0];
    var stream = new JxlTokenStream();
    foreach (var value in values)
      stream.Add(value);

    var writer = new JxlBitWriter();
    stream.WriteHeader(writer, contextCount: 6);
    stream.WriteTokens(writer);
    writer.ZeroPadToByte();

    var reader = new JxlBitReader(writer.ToArray(), 0);
    var decoder = JxlEntropyDecoder.Read(reader, numContexts: 6);
    Assert.Multiple(() => {
      for (var i = 0; i < values.Length; ++i)
        Assert.That((uint)decoder.ReadInt(i), Is.EqualTo(values[i]));
    });
  }

  [Test]
  [Category("Unit")]
  public void PackSigned_FoldsNegativesBetweenThePositives() => Assert.Multiple(() => {
    Assert.That(JxlTokenStream.PackSigned(0), Is.EqualTo(0u));
    Assert.That(JxlTokenStream.PackSigned(-1), Is.EqualTo(1u));
    Assert.That(JxlTokenStream.PackSigned(1), Is.EqualTo(2u));
    Assert.That(JxlTokenStream.PackSigned(-2), Is.EqualTo(3u));
    Assert.That(JxlTokenStream.PackSigned(2), Is.EqualTo(4u));
    Assert.That(JxlTokenStream.PackSigned(-255), Is.EqualTo(509u));
    Assert.That(JxlTokenStream.PackSigned(255), Is.EqualTo(510u));
  });
}
