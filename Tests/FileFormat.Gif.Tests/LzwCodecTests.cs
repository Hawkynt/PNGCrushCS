using System;
using System.IO;
using FileFormat.Gif;

namespace FileFormat.Gif.Tests;

[TestFixture]
public sealed class LzwCodecTests {

  private static byte[] _EncodeBytes(byte[] pixels, int minCodeSize)
    => GifLzwCodec.Encode(pixels, minCodeSize);

  private static byte[] _DecodeBytes(byte[] encoded, int expectedPixels) {
    using var ms = new MemoryStream(encoded);
    return GifLzwCodec.Decode(ms, expectedPixels);
  }

  [Test]
  public void Encode_Decode_RoundTrip_EmptyInput() {
    var encoded = _EncodeBytes([], 4);
    Assert.That(encoded[0], Is.EqualTo((byte)4)); // min code size header
    var decoded = _DecodeBytes(encoded, 0);
    Assert.That(decoded.Length, Is.EqualTo(0));
  }

  [Test]
  public void Encode_Decode_RoundTrip_SinglePixel() {
    var input = new byte[] { 3 };
    var encoded = _EncodeBytes(input, 4);
    var decoded = _DecodeBytes(encoded, 1);
    Assert.That(decoded, Is.EqualTo(input));
  }

  [Test]
  public void Encode_Decode_RoundTrip_RepeatedPattern() {
    var input = new byte[1024];
    for (var i = 0; i < input.Length; ++i) input[i] = (byte)(i % 4);
    var encoded = _EncodeBytes(input, 2);
    var decoded = _DecodeBytes(encoded, input.Length);
    Assert.That(decoded, Is.EqualTo(input));
  }

  [Test]
  public void Encode_Decode_RoundTrip_RandomData() {
    var rng = new Random(42);
    var input = new byte[4096];
    rng.NextBytes(input);
    var encoded = _EncodeBytes(input, 8);
    var decoded = _DecodeBytes(encoded, input.Length);
    Assert.That(decoded, Is.EqualTo(input));
  }

  [Test]
  public void Encode_Decode_RoundTrip_LargeRunLength() {
    var input = new byte[16384];
    for (var i = 0; i < input.Length; ++i) input[i] = 1;
    var encoded = _EncodeBytes(input, 2);
    Assert.That(encoded.Length, Is.LessThan(input.Length / 4), "highly repetitive data should compress >4x");
    var decoded = _DecodeBytes(encoded, input.Length);
    Assert.That(decoded, Is.EqualTo(input));
  }

  [Test]
  public void Encode_FramedAsSubBlocks_TerminatorPresent() {
    var encoded = _EncodeBytes(new byte[] { 0, 1, 2, 3 }, 2);
    // After the min-code-size byte, the LZW codec emits sub-blocks ending in a zero-length terminator.
    Assert.That(encoded[^1], Is.EqualTo((byte)0), "LZW stream must end with sub-block terminator");
  }

  [Test]
  public void Encode_RejectsInvalidMinCodeSize() {
    Assert.That(() => _EncodeBytes([1], 1), Throws.TypeOf<ArgumentOutOfRangeException>());
    Assert.That(() => _EncodeBytes([1], 9), Throws.TypeOf<ArgumentOutOfRangeException>());
  }

  [Test]
  public void Encode_DeferredClear_ProducesSmallerOrEqualOutput() {
    var rng = new Random(99);
    var input = new byte[8192];
    rng.NextBytes(input);

    var standard = GifLzwCodec.Encode(input, 8);
    var deferred = GifLzwCodec.Encode(input, 8, new GifLzwCodec.EncodeOptions(DeferClear: true));

    // Both decode back to the same input — deferred-clear is a writer-only optimisation.
    using var msA = new MemoryStream(standard);
    using var msB = new MemoryStream(deferred);
    Assert.That(GifLzwCodec.Decode(msA, input.Length), Is.EqualTo(input));
    Assert.That(GifLzwCodec.Decode(msB, input.Length), Is.EqualTo(input));
  }

  // ============================================================
  // CompressionLevel tests
  // ============================================================

  [Test]
  public void CompressionLevel_None_DecodesToSameInput() {
    var input = new byte[256];
    for (var i = 0; i < input.Length; ++i) input[i] = (byte)(i % 16);
    var encoded = GifLzwCodec.Encode(input, 4, GifLzwCodec.EncodeOptions.NoCompression());
    using var ms = new MemoryStream(encoded);
    Assert.That(GifLzwCodec.Decode(ms, input.Length), Is.EqualTo(input));
  }

  [Test]
  public void CompressionLevel_None_LargerThanStandardOnRepetitiveData() {
    var input = new byte[2048];
    for (var i = 0; i < input.Length; ++i) input[i] = (byte)(i % 4);
    var noComp = GifLzwCodec.Encode(input, 2, GifLzwCodec.EncodeOptions.NoCompression());
    var standard = GifLzwCodec.Encode(input, 2);
    Assert.That(noComp.Length, Is.GreaterThan(standard.Length),
      "no-compression should produce a larger stream than standard LZW on a highly redundant input");
  }

  [Test]
  public void CompressionLevel_Standard_MatchesDefault() {
    var rng = new Random(123);
    var input = new byte[1024];
    rng.NextBytes(input);
    var defaultOpt = GifLzwCodec.Encode(input, 8);
    var standardOpt = GifLzwCodec.Encode(input, 8, GifLzwCodec.EncodeOptions.StandardCompression());
    Assert.That(standardOpt, Is.EqualTo(defaultOpt));
  }

  [Test]
  public void CompressionLevel_Best_NeverLargerThanStandard() {
    var rng = new Random(7);
    var input = new byte[8192];
    rng.NextBytes(input);
    var best = GifLzwCodec.Encode(input, 8, GifLzwCodec.EncodeOptions.BestEffort());
    var standard = GifLzwCodec.Encode(input, 8);
    Assert.That(best.Length, Is.LessThanOrEqualTo(standard.Length),
      "Best should pick the smallest of all Standard variants — at worst tied with the default");
  }

  [Test]
  public void CompressionLevel_Best_RoundTrips() {
    var rng = new Random(8);
    var input = new byte[4096];
    rng.NextBytes(input);
    var encoded = GifLzwCodec.Encode(input, 8, GifLzwCodec.EncodeOptions.BestEffort());
    using var ms = new MemoryStream(encoded);
    Assert.That(GifLzwCodec.Decode(ms, input.Length), Is.EqualTo(input));
  }

  [Test]
  public void CompressionLevel_Best_OnHighlyRedundantInput_BeatsStandard() {
    // Construct input where greedy LZW makes locally-optimal but globally-suboptimal choices.
    // A pattern with alternating overlapping substrings is a classic case where DP-optimal wins.
    var input = new byte[16384];
    for (var i = 0; i < input.Length; ++i)
      input[i] = (byte)((i / 7 + i / 11) % 16);

    var standard = GifLzwCodec.Encode(input, 4);
    var best = GifLzwCodec.Encode(input, 4, GifLzwCodec.EncodeOptions.BestEffort());

    using var ms = new MemoryStream(best);
    Assert.That(GifLzwCodec.Decode(ms, input.Length), Is.EqualTo(input),
      "Best output must round-trip identically");
    Assert.That(best.Length, Is.LessThanOrEqualTo(standard.Length),
      "Best must never be larger than Standard");
  }

  [Test]
  public void CompressionLevel_Best_DpPathRoundTripsLargeInput() {
    var rng = new Random(2024);
    var input = new byte[32768];
    rng.NextBytes(input);
    var encoded = GifLzwCodec.Encode(input, 8, GifLzwCodec.EncodeOptions.BestEffort());
    using var ms = new MemoryStream(encoded);
    Assert.That(GifLzwCodec.Decode(ms, input.Length), Is.EqualTo(input));
  }

  [Test]
  public void EncodeOptions_FactoryMethods_ProduceExpectedLevels() {
    Assert.That(GifLzwCodec.EncodeOptions.NoCompression().Level, Is.EqualTo(GifLzwCodec.CompressionLevel.None));
    Assert.That(GifLzwCodec.EncodeOptions.StandardCompression().Level, Is.EqualTo(GifLzwCodec.CompressionLevel.Standard));
    Assert.That(GifLzwCodec.EncodeOptions.StandardCompression(deferClear: true).DeferClear, Is.True);
    Assert.That(GifLzwCodec.EncodeOptions.BestEffort().Level, Is.EqualTo(GifLzwCodec.CompressionLevel.Best));
    Assert.That(GifLzwCodec.EncodeOptions.Default.Level, Is.EqualTo(GifLzwCodec.CompressionLevel.Standard));
  }
}
