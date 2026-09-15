using System;
using System.Collections.Generic;

namespace FileFormat.Codecs.H265.Tests;

/// <summary>
/// Holds the arithmetic encoder against the decoder that has to read it.
/// </summary>
/// <remarks>
/// There is no useful half-way check for an arithmetic coder. A bin is not a bit and leaves no
/// recognisable mark in the output, so the only statement worth asserting is that the decoder gets
/// back the bins that were written — and, because every bin also moves the context it was coded
/// against, that it gets them back in a state array that ended up where the encoder's did. A coder
/// that agreed on the bins but diverged on the states would pass a shorter test and fail on the
/// first slice long enough to matter.
/// </remarks>
[TestFixture]
public sealed class H265CabacEncoderTests {

  private enum BinKind { Context, Bypass, Terminate }

  private readonly record struct Bin(BinKind Kind, int ContextIndex, int Value);

  [Test]
  [Category("RoundTrip")]
  [Category("HappyPath")]
  public void ContextCodedBinsDecodeBackToTheValuesThatWereWritten() {
    var bins = _MixedBins(new Random(20260912), count: 4096, bypassShare: 0);

    _AssertRoundTrips(bins);
  }

  [Test]
  [Category("RoundTrip")]
  [Category("HappyPath")]
  public void ContextAndBypassBinsInterleavedDecodeBack() {
    var bins = _MixedBins(new Random(7), count: 8192, bypassShare: 40);

    _AssertRoundTrips(bins);
  }

  [Test]
  [Category("RoundTrip")]
  [Category("Boundary")]
  public void ASingleBinRoundTrips() {
    _AssertRoundTrips([new Bin(BinKind.Context, H265CabacContexts.SPLIT_CU_FLAG, 1)]);
    _AssertRoundTrips([new Bin(BinKind.Bypass, 0, 1)]);
    _AssertRoundTrips([new Bin(BinKind.Bypass, 0, 0)]);
  }

  [Test]
  [Category("RoundTrip")]
  [Category("Boundary")]
  public void ARunOfOnesLongEnoughToNeedCarryPropagationRoundTrips() {
    // A byte of all ones cannot absorb a carry, so the encoder holds it back. Feeding one context a
    // long run of the value it has become confident about is what produces those bytes: each bin
    // then costs a fraction of a bit and the low end creeps upwards for thousands of them before
    // anything settles.
    var bins = new List<Bin>();
    for (var i = 0; i < 20000; ++i)
      bins.Add(new Bin(BinKind.Context, H265CabacContexts.SPLIT_CU_FLAG, 0));

    _AssertRoundTrips(bins);
  }

  [Test]
  [Category("RoundTrip")]
  [Category("Boundary")]
  public void BypassBitsRoundTripAsOneUnsignedNumber() {
    var random = new Random(99);
    var values = new List<(int Value, int Count)>();
    for (var i = 0; i < 200; ++i) {
      var count = random.Next(1, 17);
      values.Add((random.Next(0, 1 << count), count));
    }

    var states = _FreshStates();
    var encoder = new H265CabacEncoder(states);
    foreach (var (value, count) in values)
      encoder.EncodeBypassBits(value, count);

    encoder.EncodeTerminate(1);
    var payload = encoder.Finish();

    var decoder = new H265CabacEngine(payload, _FreshStates());
    decoder.Start(0);
    Assert.Multiple(() => {
      for (var i = 0; i < values.Count; ++i)
        Assert.That(decoder.DecodeBypassBits(values[i].Count), Is.EqualTo(values[i].Value), $"value {i}");
    });
  }

  [Test]
  [Category("RoundTrip")]
  public void TheTerminatingBinEndsThePayloadWhereTheDecoderExpectsIt() {
    var states = _FreshStates();
    var encoder = new H265CabacEncoder(states);
    for (var i = 0; i < 64; ++i) {
      encoder.EncodeBin(H265CabacContexts.SPLIT_CU_FLAG, i & 1);
      encoder.EncodeTerminate(0);
    }

    encoder.EncodeTerminate(1);
    var payload = encoder.Finish();

    var decoder = new H265CabacEngine(payload, _FreshStates());
    decoder.Start(0);
    Assert.Multiple(() => {
      for (var i = 0; i < 64; ++i) {
        Assert.That(decoder.DecodeBin(H265CabacContexts.SPLIT_CU_FLAG), Is.EqualTo(i & 1), $"bin {i}");
        Assert.That(decoder.DecodeTerminate(), Is.Zero, $"not the end after bin {i}");
      }

      Assert.That(decoder.DecodeTerminate(), Is.EqualTo(1), "the end");
    });
  }

  [Test]
  [Category("HappyPath")]
  public void TheTwoDirectionsLeaveTheContextsInTheSameState() {
    var bins = _MixedBins(new Random(4242), count: 3000, bypassShare: 25);

    var written = _FreshStates();
    var encoder = new H265CabacEncoder(written);
    foreach (var bin in bins)
      _Write(encoder, bin);

    encoder.EncodeTerminate(1);
    var payload = encoder.Finish();

    var read = _FreshStates();
    var decoder = new H265CabacEngine(payload, read);
    decoder.Start(0);
    foreach (var bin in bins)
      _Read(ref decoder, bin);

    Assert.That(read, Is.EqualTo(written),
      "the contexts a slice ends on are what the next substream inherits; the two directions have to "
      + "arrive at the same ones");
  }

  private static void _AssertRoundTrips(IReadOnlyList<Bin> bins) {
    var encoder = new H265CabacEncoder(_FreshStates());
    foreach (var bin in bins)
      _Write(encoder, bin);

    encoder.EncodeTerminate(1);
    var payload = encoder.Finish();

    var decoder = new H265CabacEngine(payload, _FreshStates());
    decoder.Start(0);
    for (var i = 0; i < bins.Count; ++i) {
      var decoded = _Read(ref decoder, bins[i]);
      if (decoded == bins[i].Value)
        continue;

      Assert.Fail($"bin {i} ({bins[i].Kind}) was written as {bins[i].Value} and decoded as {decoded}");
    }

    Assert.That(decoder.DecodeTerminate(), Is.EqualTo(1), "the payload ends where the encoder ended it");
  }

  private static void _Write(H265CabacEncoder encoder, Bin bin) {
    switch (bin.Kind) {
      case BinKind.Context: encoder.EncodeBin(bin.ContextIndex, bin.Value); break;
      case BinKind.Bypass: encoder.EncodeBypass(bin.Value); break;
      default: encoder.EncodeTerminate(bin.Value); break;
    }
  }

  private static int _Read(ref H265CabacEngine decoder, Bin bin) => bin.Kind switch {
    BinKind.Context => decoder.DecodeBin(bin.ContextIndex),
    BinKind.Bypass => decoder.DecodeBypass(),
    _ => decoder.DecodeTerminate(),
  };

  /// <summary>
  /// A spread of bins over a handful of contexts, so that the states are exercised rather than left
  /// where they started.
  /// </summary>
  /// <param name="bypassShare">Percent of the bins that bypass the context machinery.</param>
  private static List<Bin> _MixedBins(Random random, int count, int bypassShare) {
    int[] contexts = [
      H265CabacContexts.SPLIT_CU_FLAG,
      H265CabacContexts.SPLIT_CU_FLAG + 1,
      H265CabacContexts.CU_SKIP_FLAG,
      H265CabacContexts.MERGE_FLAG,
      H265CabacContexts.PART_MODE,
      H265CabacContexts.RQT_ROOT_CBF,
    ];

    var bins = new List<Bin>(count);
    for (var i = 0; i < count; ++i) {
      var bypass = random.Next(100) < bypassShare;

      // Skewed rather than even: a context only leaves its starting state when one symbol is more
      // common than the other, and a coder that mishandles a confident state passes an even test.
      var value = random.Next(100) < 80 ? 0 : 1;
      bins.Add(bypass
        ? new Bin(BinKind.Bypass, 0, value)
        : new Bin(BinKind.Context, contexts[random.Next(contexts.Length)], value));
    }

    return bins;
  }

  private static byte[] _FreshStates() {
    var states = new byte[H265CabacContexts.COUNT];
    H265CabacContexts.Initialize(states, 0, 26);
    return states;
  }
}
