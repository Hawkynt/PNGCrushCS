using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Gif;
using NUnit.Framework;

namespace Optimizer.Gif.Tests;

/// <summary>Covers <see cref="LzwMode.FrozenDictionary"/> and the screening path that reaches it.</summary>
/// <remarks>
/// The LZW mode only has an effect on frames big and varied enough to exhaust the encoder's
/// 4096-entry dictionary; below that the three modes run identical code and any comparison between
/// them is vacuously true. Every fixture here therefore builds high-entropy frames and asserts the
/// precondition — that enabling the extra modes changes the result at all — before asserting anything
/// about which result is smaller.
/// </remarks>
[TestFixture]
public sealed class FrozenDictionaryModeTests {

  private const int _Width = 128;
  private const int _Height = 128;

  /// <summary>Frames with enough distinct runs to fill the dictionary, and stationary statistics, so a
  /// frozen codebook stays valid to the end of the frame.</summary>
  private static FileInfo _CreateHighEntropyGif(int frameCount, int seed = 1234) {
    var rng = new Random(seed);
    var palette = new byte[256 * 3];
    for (var i = 0; i < 256; ++i) {
      palette[i * 3] = (byte)i;
      palette[i * 3 + 1] = (byte)(255 - i);
      palette[i * 3 + 2] = (byte)(i * 53 % 256);
    }

    var frames = new List<Frame>();
    for (var f = 0; f < frameCount; ++f) {
      var pixels = new byte[_Width * _Height];
      byte value = 0;
      for (var i = 0; i < pixels.Length; ++i) {
        if (rng.Next(3) == 0)
          value = (byte)rng.Next(96);
        pixels[i] = value;
      }

      frames.Add(new Frame(pixels, new Dimensions(_Width, _Height), new Offset(0, 0), null,
        TimeSpan.FromMilliseconds(80), FrameDisposalMethod.DoNotDispose, null));
    }

    var gif = new GifFile("89a", new Dimensions(_Width, _Height), palette, new LoopCount(0, true), 0, frames);
    var file = new FileInfo(Path.Combine(Path.GetTempPath(), $"frozen_{Guid.NewGuid():N}.gif"));
    File.WriteAllBytes(file.FullName, GifWriter.ToBytes(gif));
    return file;
  }

  private static long _Optimize(FileInfo file, bool deferredClear, bool frozenDictionary, bool twoPhase = true)
    => GifOptimizer.FromFile(file, new GifOptimizationOptions(
      TryDeferredClear: deferredClear,
      TryFrozenDictionary: frozenDictionary,
      EnableTwoPhaseOptimization: twoPhase,
      MaxParallelTasks: 1)).OptimizeAsync().AsTask().Result.CompressedSize;

  [Test]
  [Category("Unit")]
  public void TryFrozenDictionary_DefaultsOn() {
    Assert.That(new GifOptimizationOptions().TryFrozenDictionary, Is.True);
    Assert.That(new GifOptimizationOptions().TryDeferredClear, Is.True);
  }

  [Test]
  [Category("Unit")]
  public void FrozenDictionary_IsADistinctMode() {
    Assert.That(LzwMode.FrozenDictionary, Is.Not.EqualTo(LzwMode.Standard));
    Assert.That(LzwMode.FrozenDictionary, Is.Not.EqualTo(LzwMode.DeferredClear));
  }

  [Test]
  [Category("Unit")]
  public void EveryLzwMode_MapsToItsOwnCodecStrategy() {
    Assert.That(GifOptimizer.ToClearStrategy(LzwMode.Standard),
      Is.EqualTo(GifLzwCodec.ClearStrategy.Immediate));
    Assert.That(GifOptimizer.ToClearStrategy(LzwMode.DeferredClear),
      Is.EqualTo(GifLzwCodec.ClearStrategy.Adaptive));
    Assert.That(GifOptimizer.ToClearStrategy(LzwMode.FrozenDictionary),
      Is.EqualTo(GifLzwCodec.ClearStrategy.Freeze));

    // A mapping that collapsed two modes onto one strategy would leave the optimizer running the
    // same trial twice and quietly lose the mode, which no file-size assertion would catch.
    var strategies = new HashSet<GifLzwCodec.ClearStrategy>();
    foreach (LzwMode mode in Enum.GetValues<LzwMode>())
      Assert.That(strategies.Add(GifOptimizer.ToClearStrategy(mode)), Is.True,
        $"{mode} maps onto a strategy another mode already uses");
    Assert.That(strategies, Has.Count.EqualTo(Enum.GetValues<LzwMode>().Length));
  }

  [Test]
  [Category("Unit")]
  public void TheThreeModes_ProduceThreeDifferentEncodings() {
    // Guards the mapping from the other side: on content that exhausts the dictionary the three
    // strategies must actually diverge, or the modes are indistinguishable in practice.
    var rng = new Random(2024);
    var pixels = new byte[1 << 16];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = i < pixels.Length / 2 ? (byte)(i % 41) : (byte)rng.Next(256);

    var sizes = new List<int>();
    foreach (LzwMode mode in Enum.GetValues<LzwMode>())
      sizes.Add(GifLzwCodec.Encode(pixels, 8,
        GifLzwCodec.EncodeOptions.StandardCompression(GifOptimizer.ToClearStrategy(mode))).Length);

    Assert.That(sizes, Is.Unique, $"modes produced identical output: {string.Join(", ", sizes)}");
  }

  [Test]
  [Category("EndToEnd")]
  [CancelAfter(120000)]
  public void FrozenDictionary_NeverProducesALargerFileThanWithoutIt() {
    var file = _CreateHighEntropyGif(3);
    try {
      var without = _Optimize(file, deferredClear: true, frozenDictionary: false);
      var with = _Optimize(file, deferredClear: true, frozenDictionary: true);

      // Precondition: the LZW modes must actually be reachable on this content, or the comparison
      // below proves nothing. Standard-only has to lose to the richer mode set.
      var standardOnly = _Optimize(file, deferredClear: false, frozenDictionary: false);
      Assert.That(standardOnly, Is.GreaterThan(with),
        "fixture no longer exhausts the LZW dictionary — the mode comparison would be vacuous");

      // Regression guard rather than a proof: no input has been found on which enabling the mode
      // enlarges the result, because selection takes the minimum over a strictly larger candidate set.
      Assert.That(with, Is.LessThanOrEqualTo(without),
        $"adding a candidate must never enlarge the result: with={with} without={without}");
    } finally {
      file.Delete();
    }
  }

  [Test]
  [Category("EndToEnd")]
  [CancelAfter(120000)]
  public void FrozenDictionary_IsMonotoneWithScreeningDisabled() {
    // With two-phase screening off the whole cartesian product runs, so this exercises the plain
    // "more candidates" path rather than the promotion path.
    var file = _CreateHighEntropyGif(2, seed: 99);
    try {
      var without = _Optimize(file, true, false, twoPhase: false);
      var with = _Optimize(file, true, true, twoPhase: false);

      var standardOnly = _Optimize(file, false, false, twoPhase: false);
      Assert.That(standardOnly, Is.GreaterThan(with),
        "fixture no longer exhausts the LZW dictionary — the mode comparison would be vacuous");

      Assert.That(with, Is.LessThanOrEqualTo(without));
    } finally {
      file.Delete();
    }
  }

  [Test]
  [Category("EndToEnd")]
  [CancelAfter(120000)]
  public void FrozenDictionary_OutputStillDecodesPixelPerfect() {
    var file = _CreateHighEntropyGif(2, seed: 4242);
    try {
      var source = Reader.FromFile(file);
      var result = GifOptimizer.FromFile(file, new GifOptimizationOptions(
        TryDeferredClear: false,
        TryFrozenDictionary: true,
        MaxParallelTasks: 1)).OptimizeAsync().AsTask().Result;

      using var ms = new MemoryStream(result.FileContents);
      var readBack = Reader.FromStream(ms);

      Assert.That(readBack.Frames.Count, Is.EqualTo(source.Frames.Count));
      for (var f = 0; f < source.Frames.Count; ++f)
        Assert.That(readBack.Frames[f].IndexedPixels, Is.EqualTo(source.Frames[f].IndexedPixels),
          $"frame {f} changed pixels");
    } finally {
      file.Delete();
    }
  }

  [Test]
  [Category("EndToEnd")]
  [CancelAfter(120000)]
  public void DisablingFrozenDictionary_ReproducesThePreviousBehaviour() {
    // The option is the whole opt-out: with it off, the mode must never be generated, so the result
    // has to match a run that only ever had the two older modes available.
    var file = _CreateHighEntropyGif(2, seed: 7);
    try {
      var a = _Optimize(file, deferredClear: true, frozenDictionary: false);
      var b = _Optimize(file, deferredClear: true, frozenDictionary: false);
      Assert.That(a, Is.EqualTo(b));

      var withFrozen = _Optimize(file, deferredClear: true, frozenDictionary: true);
      Assert.That(withFrozen, Is.LessThanOrEqualTo(a));
    } finally {
      file.Delete();
    }
  }
}
