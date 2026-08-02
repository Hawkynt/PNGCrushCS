using System;
using FileFormat.CrackArt;

namespace FileFormat.CrackArt.Tests;

[TestFixture]
public sealed class CrackArtCompressorTests {

  [Test]
  [Category("Unit")]
  public void Compress_Decompress_RoundTrip() {
    var original = new byte[256];
    for (var i = 0; i < original.Length; ++i)
      original[i] = (byte)(i * 7 % 256);

    var compressed = CrackArtCompressor.Compress(original);
    var decompressed = CrackArtCompressor.Decompress(compressed, original.Length);

    Assert.That(decompressed, Is.EqualTo(original));
  }

  [Test]
  [Category("Unit")]
  public void Compress_AllSame_ProducesSmallOutput() {
    var original = new byte[1000];
    Array.Fill(original, (byte)0xAB);

    var compressed = CrackArtCompressor.Compress(original);

    Assert.Multiple(() => {
      Assert.That(compressed.Length, Is.LessThan(original.Length));
      var decompressed = CrackArtCompressor.Decompress(compressed, original.Length);
      Assert.That(decompressed, Is.EqualTo(original));
    });
  }

  [Test]
  [Category("Unit")]
  public void Compress_AllDifferent_RoundTrips() {
    var original = new byte[200];
    for (var i = 0; i < original.Length; ++i)
      original[i] = (byte)i;

    var compressed = CrackArtCompressor.Compress(original);
    var decompressed = CrackArtCompressor.Decompress(compressed, original.Length);

    Assert.That(decompressed, Is.EqualTo(original));
  }

  [Test]
  [Category("Unit")]
  public void Decompress_Empty_ReturnsZeroFilled() {
    var result = CrackArtCompressor.Decompress([], 100);

    Assert.That(result, Has.Length.EqualTo(100));
    Assert.That(result, Is.All.EqualTo((byte)0));
  }

  [Test]
  [Category("Unit")]
  /// <summary>
  /// A packed stream always carries its escape byte, even with nothing to say.
  /// </summary>
  /// <remarks>
  /// This used to expect nothing at all, which came from reading the coding as PackBits: that one
  /// has no preamble and can be empty, and CrackArt's names its escape byte before anything else.
  /// A stream with no preamble cannot be read back.
  /// </remarks>
  public void Compress_Empty_StillNamesItsEscape() {
    var result = CrackArtCompressor.Compress([]);

    Assert.That(result, Has.Length.EqualTo(CrackArtCompressor.PreambleSize));
  }

  [Test]
  [Category("Unit")]
  public void Compress_LargeData_RoundTrips() {
    var original = new byte[32000];
    for (var i = 0; i < original.Length; ++i)
      original[i] = (byte)(i * 3 % 256);

    var compressed = CrackArtCompressor.Compress(original);
    var decompressed = CrackArtCompressor.Decompress(compressed, original.Length);

    Assert.That(decompressed, Is.EqualTo(original));
  }

  [Test]
  [Category("Unit")]
  public void Compress_MixedRunsAndLiterals_RoundTrips() {
    var original = new byte[] { 1, 2, 3, 4, 5, 5, 5, 5, 5, 6, 7, 8, 8, 8 };

    var compressed = CrackArtCompressor.Compress(original);
    var decompressed = CrackArtCompressor.Decompress(compressed, original.Length);

    Assert.That(decompressed, Is.EqualTo(original));
  }
}
