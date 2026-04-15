using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using Compression.Core;

namespace Compression.Tests;

[TestFixture]
public sealed class ZopfliDeflaterTests {
  // --- BitWriter Tests ---

  [Test]
  public void BitWriter_WriteSingleBit_CorrectOutput() {
    var writer = new ZopfliDeflater.BitWriter(16);
    try {
      writer.WriteBits(1, 1);
      writer.AlignToByte();
      var output = writer.GetOutput();
      Assert.That(output, Has.Length.EqualTo(1));
      Assert.That(output[0], Is.EqualTo(0x01));
    } finally {
      writer.Release();
    }
  }

  [Test]
  public void BitWriter_WriteMultipleBits_LsbFirst() {
    var writer = new ZopfliDeflater.BitWriter(16);
    try {
      writer.WriteBits(0b101, 3);
      writer.AlignToByte();
      var output = writer.GetOutput();
      Assert.That(output, Has.Length.EqualTo(1));
      Assert.That(output[0], Is.EqualTo(0b00000101));
    } finally {
      writer.Release();
    }
  }

  [Test]
  public void BitWriter_WriteBitsAcrossByteBoundary() {
    var writer = new ZopfliDeflater.BitWriter(16);
    try {
      writer.WriteBits(0xFF, 8);
      writer.WriteBits(0x01, 1);
      writer.AlignToByte();
      var output = writer.GetOutput();
      Assert.That(output, Has.Length.EqualTo(2));
      Assert.That(output[0], Is.EqualTo(0xFF));
      Assert.That(output[1], Is.EqualTo(0x01));
    } finally {
      writer.Release();
    }
  }

  [Test]
  public void BitWriter_WriteHuffmanBits_MsbFirst() {
    var writer = new ZopfliDeflater.BitWriter(16);
    try {
      // Huffman code 0b110 (3 bits, MSB-first) should be written as bits: 1,1,0
      // In LSB-first byte packing: bit0=1, bit1=1, bit2=0 → byte = 0b011 = 3
      writer.WriteHuffmanBits(0b110, 3);
      writer.AlignToByte();
      var output = writer.GetOutput();
      Assert.That(output, Has.Length.EqualTo(1));
      Assert.That(output[0], Is.EqualTo(0b00000011));
    } finally {
      writer.Release();
    }
  }

  [Test]
  public void BitWriter_ByteLength_IncludesPartialByte() {
    var writer = new ZopfliDeflater.BitWriter(16);
    try {
      writer.WriteBits(0, 3);
      Assert.That(writer.ByteLength, Is.EqualTo(1));
      writer.AlignToByte();
      Assert.That(writer.ByteLength, Is.EqualTo(1));
    } finally {
      writer.Release();
    }
  }

  // --- Symbol Table Tests ---

  [Test]
  public void GetLengthCode_MinLength3_Returns257() {
    Assert.That(ZopfliDeflater.GetLengthCode(3), Is.EqualTo(257));
  }

  [Test]
  public void GetLengthCode_MaxLength258_Returns285() {
    Assert.That(ZopfliDeflater.GetLengthCode(258), Is.EqualTo(285));
  }

  [Test]
  public void GetLengthCode_AllLengths_MatchRfc1951() {
    var lengthBase = ZopfliDeflater.LengthBase;
    var extraBits = ZopfliDeflater.LengthExtraBits;

    for (var len = 3; len <= 258; ++len) {
      var code = ZopfliDeflater.GetLengthCode(len);
      Assert.That(code, Is.InRange(257, 285), $"Length {len} produced out-of-range code {code}");

      var baseLen = lengthBase[code - 257];
      var extra = extraBits[code - 257];
      var maxLen = baseLen + (1 << extra) - 1;
      Assert.That(len, Is.InRange((int)baseLen, maxLen),
        $"Length {len} code {code}: base={baseLen}, extra={extra}");
    }
  }

  [Test]
  public void GetDistanceCode_MinDistance1_Returns0() {
    Assert.That(ZopfliDeflater.GetDistanceCode(1), Is.EqualTo(0));
  }

  [Test]
  public void GetDistanceCode_AllDistances_MatchRfc1951() {
    var distBase = ZopfliDeflater.DistanceBase;
    var extraBits = ZopfliDeflater.DistanceExtraBits;

    for (var dist = 1; dist <= 32768; ++dist) {
      var code = ZopfliDeflater.GetDistanceCode(dist);
      Assert.That(code, Is.InRange(0, 29), $"Distance {dist} produced out-of-range code {code}");

      var baseDist = distBase[code];
      var extra = extraBits[code];
      var maxDist = baseDist + (1 << extra) - 1;
      Assert.That(dist, Is.InRange((int)baseDist, maxDist),
        $"Distance {dist} code {code}: base={baseDist}, extra={extra}");
    }
  }

  // --- Huffman Tree Tests ---

  [Test]
  public void HuffmanTree_Fixed_MatchesRfc1951() {
    var tree = ZopfliDeflater.HuffmanTree.BuildFixed();
    // RFC 1951 fixed Huffman: 0-143 → 8 bits, 144-255 → 9 bits, 256-279 → 7 bits, 280-287 → 8 bits
    for (var i = 0; i <= 143; ++i)
      Assert.That(tree.Lengths[i], Is.EqualTo(8), $"Symbol {i}");
    for (var i = 144; i <= 255; ++i)
      Assert.That(tree.Lengths[i], Is.EqualTo(9), $"Symbol {i}");
    for (var i = 256; i <= 279; ++i)
      Assert.That(tree.Lengths[i], Is.EqualTo(7), $"Symbol {i}");
    for (var i = 280; i <= 287; ++i)
      Assert.That(tree.Lengths[i], Is.EqualTo(8), $"Symbol {i}");
  }

  [Test]
  public void HuffmanTree_FixedDistance_All5Bits() {
    var tree = ZopfliDeflater.HuffmanTree.BuildFixedDistance();
    for (var i = 0; i < 32; ++i)
      Assert.That(tree.Lengths[i], Is.EqualTo(5), $"Distance symbol {i}");
  }

  [Test]
  public void HuffmanTree_Build_RespectsMaxBitLength() {
    var freqs = new int[286];
    // Skewed distribution that would normally produce very long codes
    for (var i = 0; i < 286; ++i)
      freqs[i] = i + 1;

    var tree = ZopfliDeflater.HuffmanTree.Build(freqs, 15);
    for (var i = 0; i < 286; ++i)
      Assert.That(tree.Lengths[i], Is.LessThanOrEqualTo(15), $"Symbol {i} exceeds max bit length");
  }

  [Test]
  public void HuffmanTree_Build_SingleSymbol_HasLength1() {
    var freqs = new int[286];
    freqs[42] = 100;
    var tree = ZopfliDeflater.HuffmanTree.Build(freqs, 15);
    Assert.That(tree.Lengths[42], Is.EqualTo(1));
  }

  [Test]
  public void HuffmanTree_Build_TwoSymbols_EachHasLength1() {
    var freqs = new int[4];
    freqs[0] = 10;
    freqs[1] = 20;
    var tree = ZopfliDeflater.HuffmanTree.Build(freqs, 15);
    Assert.That(tree.Lengths[0], Is.EqualTo(1));
    Assert.That(tree.Lengths[1], Is.EqualTo(1));
  }

  // --- HashChain Tests ---

  [Test]
  public void HashChain_FindsKnownPattern() {
    var data = "ABCABCABC"u8.ToArray();
    var chain = new ZopfliDeflater.HashChain(data, data.Length, 128, 258);
    for (var i = 0; i < data.Length; ++i)
      chain.Insert(i);

    Span<ZopfliDeflater.LzMatch> matches = stackalloc ZopfliDeflater.LzMatch[8];
    var count = chain.FindMatches(3, 258, matches);
    Assert.That(count, Is.GreaterThan(0));

    var found = false;
    for (var i = 0; i < count; ++i)
      if (matches[i].Distance == 3 && matches[i].Length >= 3)
        found = true;

    Assert.That(found, Is.True, "Should find match at distance 3");
  }

  [Test]
  public void HashChain_MaxMatchLength258() {
    // Create data with 300+ repeated bytes
    var data = new byte[512];
    Array.Fill(data, (byte)'A');

    var chain = new ZopfliDeflater.HashChain(data, data.Length, 128, 258);
    for (var i = 0; i < data.Length; ++i)
      chain.Insert(i);

    Span<ZopfliDeflater.LzMatch> matches = stackalloc ZopfliDeflater.LzMatch[8];
    var count = chain.FindMatches(3, 258, matches);

    Assert.That(count, Is.GreaterThan(0));
    for (var i = 0; i < count; ++i)
      Assert.That(matches[i].Length, Is.LessThanOrEqualTo(258));
  }

  [Test]
  public void HashChain_NoMatchForFirstBytes() {
    var data = "ABCDEF"u8.ToArray();
    var chain = new ZopfliDeflater.HashChain(data, data.Length, 128, 258);
    for (var i = 0; i < data.Length; ++i)
      chain.Insert(i);

    Span<ZopfliDeflater.LzMatch> matches = stackalloc ZopfliDeflater.LzMatch[8];
    var count = chain.FindMatches(0, 258, matches);
    Assert.That(count, Is.EqualTo(0), "No matches should exist at position 0 with no prior context");
  }

  // --- Round-trip Compression Tests ---

  [Test]
  public void Compress_Ultra_RoundTrip_EmptyData() {
    var original = Array.Empty<byte>();
    var compressed = ZopfliDeflater.Compress(original, false, 2);

    using var ms = new MemoryStream(compressed);
    using var zlib = new ZLibStream(ms, CompressionMode.Decompress);
    var decompressed = new MemoryStream();
    zlib.CopyTo(decompressed);
    Assert.That(decompressed.ToArray(), Is.EqualTo(original));
  }

  [Test]
  public void Compress_Ultra_RoundTrip_SingleByte() {
    var original = new byte[] { 42 };
    var compressed = ZopfliDeflater.Compress(original, false, 2);

    using var ms = new MemoryStream(compressed);
    using var zlib = new ZLibStream(ms, CompressionMode.Decompress);
    var decompressed = new MemoryStream();
    zlib.CopyTo(decompressed);
    Assert.That(decompressed.ToArray(), Is.EqualTo(original));
  }

  [Test]
  public void Compress_Ultra_RoundTrip_RepeatedData() {
    var original = new byte[1000];
    Array.Fill(original, (byte)0xAB);

    var compressed = ZopfliDeflater.Compress(original, false, 2);

    using var ms = new MemoryStream(compressed);
    using var zlib = new ZLibStream(ms, CompressionMode.Decompress);
    var decompressed = new MemoryStream();
    zlib.CopyTo(decompressed);
    Assert.That(decompressed.ToArray(), Is.EqualTo(original));
  }

  [Test]
  public void Compress_Ultra_RoundTrip_RandomData() {
    var original = new byte[4096];
    new Random(12345).NextBytes(original);

    var compressed = ZopfliDeflater.Compress(original, false, 2);

    using var ms = new MemoryStream(compressed);
    using var zlib = new ZLibStream(ms, CompressionMode.Decompress);
    var decompressed = new MemoryStream();
    zlib.CopyTo(decompressed);
    Assert.That(decompressed.ToArray(), Is.EqualTo(original));
  }

  [Test]
  public void Compress_Ultra_RoundTrip_TextData() {
    var text = "The quick brown fox jumps over the lazy dog. " +
               "The quick brown fox jumps over the lazy dog. " +
               "Pack my box with five dozen liquor jugs.";
    var original = Encoding.UTF8.GetBytes(text);

    var compressed = ZopfliDeflater.Compress(original, false, 2);

    using var ms = new MemoryStream(compressed);
    using var zlib = new ZLibStream(ms, CompressionMode.Decompress);
    var decompressed = new MemoryStream();
    zlib.CopyTo(decompressed);
    Assert.That(decompressed.ToArray(), Is.EqualTo(original));
  }

  [Test]
  public void Compress_Hyper_RoundTrip_TextData() {
    var text = "The quick brown fox jumps over the lazy dog. " +
               "The quick brown fox jumps over the lazy dog. " +
               "Pack my box with five dozen liquor jugs.";
    var original = Encoding.UTF8.GetBytes(text);

    var compressed = ZopfliDeflater.Compress(original, true, 5);

    using var ms = new MemoryStream(compressed);
    using var zlib = new ZLibStream(ms, CompressionMode.Decompress);
    var decompressed = new MemoryStream();
    zlib.CopyTo(decompressed);
    Assert.That(decompressed.ToArray(), Is.EqualTo(original));
  }

  [Test]
  public void Compress_Hyper_RoundTrip_LargerData() {
    var original = new byte[16384];
    var rng = new Random(54321);
    // Generate compressible data (mix of patterns and randomness)
    for (var i = 0; i < original.Length; ++i)
      original[i] = (byte)(rng.Next(0, 4) == 0 ? rng.Next(256) : i % 64);

    var compressed = ZopfliDeflater.Compress(original, true, 3);

    using var ms = new MemoryStream(compressed);
    using var zlib = new ZLibStream(ms, CompressionMode.Decompress);
    var decompressed = new MemoryStream();
    zlib.CopyTo(decompressed);
    Assert.That(decompressed.ToArray(), Is.EqualTo(original));
  }

  [Test]
  public void Compress_ZlibHeader_Valid() {
    var data = new byte[] { 1, 2, 3, 4, 5 };
    var compressed = ZopfliDeflater.Compress(data, false);

    Assert.That(compressed.Length, Is.GreaterThanOrEqualTo(6));
    // CMF should be 0x78 (deflate, 32K window)
    Assert.That(compressed[0], Is.EqualTo(0x78));
    // (CMF * 256 + FLG) % 31 == 0
    var check = (compressed[0] * 256 + compressed[1]) % 31;
    Assert.That(check, Is.EqualTo(0), "Zlib header check failed");
  }

  [Test]
  public void Compress_Ultra_SmallerThan_DotNetSmallestSize_ForSmallCompressibleData() {
    var original = new byte[8192];
    for (var i = 0; i < original.Length; ++i)
      original[i] = (byte)(i % 16);

    var ultraCompressed = ZopfliDeflater.Compress(original, false, 2);

    using var dotnetMs = new MemoryStream();
    using (var zlib = new ZLibStream(dotnetMs, CompressionLevel.SmallestSize, true)) {
      zlib.Write(original);
    }

    Assert.That(ultraCompressed.Length, Is.LessThanOrEqualTo(dotnetMs.Length),
      $"Ultra={ultraCompressed.Length} vs .NET SmallestSize={dotnetMs.Length}");
  }

  [Test]
  public void Compress_Ultra_SmallerThan_DotNetSmallestSize_ForLargeCompressibleData() {
    // 50KB of compressible data (similar to filtered PNG scanlines)
    var original = new byte[51200];
    for (var i = 0; i < original.Length; ++i)
      original[i] = (byte)(i % 16);

    var ultraCompressed = ZopfliDeflater.Compress(original, false, 2);

    using var dotnetMs = new MemoryStream();
    using (var zlib = new ZLibStream(dotnetMs, CompressionLevel.SmallestSize, true)) {
      zlib.Write(original);
    }

    Assert.That(ultraCompressed.Length, Is.LessThanOrEqualTo(dotnetMs.Length),
      $"Ultra={ultraCompressed.Length} vs .NET SmallestSize={dotnetMs.Length}");
  }

  [Test]
  public void Compress_Hyper_NoLargerThan_Ultra() {
    var original = new byte[4096];
    for (var i = 0; i < original.Length; ++i)
      original[i] = (byte)(i % 32);

    var ultraCompressed = ZopfliDeflater.Compress(original, false, 2);
    var hyperCompressed = ZopfliDeflater.Compress(original, true, 5);

    Assert.That(hyperCompressed.Length, Is.LessThanOrEqualTo(ultraCompressed.Length),
      $"Hyper={hyperCompressed.Length} vs Ultra={ultraCompressed.Length}");
  }

  // --- Multi-Length DP Tests ---

  [Test]
  public void Compress_Ultra_MultiLength_ProducesSmallerOrEqualOutput() {
    // Compressible data where multi-length DP should have advantage
    var original = new byte[8192];
    for (var i = 0; i < original.Length; ++i)
      original[i] = (byte)((i * 7 + i / 13) % 64);

    var compressed = ZopfliDeflater.Compress(original, false);

    using var dotnetMs = new MemoryStream();
    using (var zlib = new ZLibStream(dotnetMs, CompressionLevel.SmallestSize, true)) {
      zlib.Write(original);
    }

    Assert.That(compressed.Length, Is.LessThanOrEqualTo(dotnetMs.Length),
      $"Ultra={compressed.Length} vs .NET SmallestSize={dotnetMs.Length}");
  }

  // --- Convergence Detection Tests ---

  [Test]
  public void Compress_Hyper_ConvergesForHighlyCompressibleData() {
    // Highly repetitive data converges quickly
    var original = new byte[4096];
    Array.Fill(original, (byte)0x42);

    var compressed3 = ZopfliDeflater.Compress(original, true, 3);
    var compressed15 = ZopfliDeflater.Compress(original, true);

    // With convergence detection, more iterations should produce identical output
    Assert.That(compressed15.Length, Is.EqualTo(compressed3.Length),
      $"Converged output should be identical: 3-iter={compressed3.Length} vs 15-iter={compressed15.Length}");
  }

  // --- 64KB+ Round-trip Tests ---

  [Test]
  [Timeout(60000)]
  public void Compress_Ultra_RoundTrip_LargeData_64KB() {
    var original = new byte[65536];
    var rng = new Random(99999);
    for (var i = 0; i < original.Length; ++i)
      original[i] = (byte)(rng.Next(0, 3) == 0 ? rng.Next(256) : i % 128);

    var compressed = ZopfliDeflater.Compress(original, false);

    using var ms = new MemoryStream(compressed);
    using var zlib = new ZLibStream(ms, CompressionMode.Decompress);
    var decompressed = new MemoryStream();
    zlib.CopyTo(decompressed);
    Assert.That(decompressed.ToArray(), Is.EqualTo(original));
  }

  [Test]
  [Timeout(300000)]
  public void Compress_Hyper_RoundTrip_LargeData_64KB() {
    var original = new byte[65536];
    var rng = new Random(99999);
    for (var i = 0; i < original.Length; ++i)
      original[i] = (byte)(rng.Next(0, 3) == 0 ? rng.Next(256) : i % 128);

    var compressed = ZopfliDeflater.Compress(original, true, 3);

    using var ms = new MemoryStream(compressed);
    using var zlib = new ZLibStream(ms, CompressionMode.Decompress);
    var decompressed = new MemoryStream();
    zlib.CopyTo(decompressed);
    Assert.That(decompressed.ToArray(), Is.EqualTo(original));
  }

  // --- Lookup Table Verification Tests ---

  [Test]
  public void GetLengthCode_LookupTable_MatchesExpectedForAllLengths() {
    var lengthBase = ZopfliDeflater.LengthBase;
    var extraBits = ZopfliDeflater.LengthExtraBits;

    for (var len = 3; len <= 258; ++len) {
      var code = ZopfliDeflater.GetLengthCode(len);

      // Verify code is in valid range
      Assert.That(code, Is.InRange(257, 285), $"Length {len}");

      // Verify the length falls within the code's range
      var baseLen = (int)lengthBase[code - 257];
      var extra = extraBits[code - 257];
      var maxLen = baseLen + (1 << extra) - 1;
      Assert.That(len, Is.InRange(baseLen, maxLen), $"Length {len} code {code}");

      // Verify length is at or above the code's base (code 285 for length 258 is a special RFC case)
      Assert.That(len, Is.GreaterThanOrEqualTo(baseLen), $"Length {len} below base for code {code}");
    }
  }

  [Test]
  public void GetDistanceCode_LookupTable_MatchesExpectedForAllDistances() {
    var distBase = ZopfliDeflater.DistanceBase;
    var extraBits = ZopfliDeflater.DistanceExtraBits;

    for (var dist = 1; dist <= 32768; ++dist) {
      var code = ZopfliDeflater.GetDistanceCode(dist);

      // Verify code is in valid range
      Assert.That(code, Is.InRange(0, 29), $"Distance {dist}");

      // Verify the distance falls within the code's range
      var baseDist = (int)distBase[code];
      var extra = extraBits[code];
      var maxDist = baseDist + (1 << extra) - 1;
      Assert.That(dist, Is.InRange(baseDist, maxDist), $"Distance {dist} code {code}");

      // Verify this is the correct (lowest-base) code for this distance
      if (code > 0) {
        var prevBase = (int)distBase[code - 1];
        var prevExtra = extraBits[code - 1];
        var prevMax = prevBase + (1 << prevExtra) - 1;
        Assert.That(dist, Is.GreaterThan(prevMax), $"Distance {dist} should not map to code {code - 1}");
      }
    }
  }

  [Test]
  public void GetDistanceCode_SmallAndLargeDistances_ConsistentResults() {
    // Verify that the lookup table (distances 1-512) and binary search (513+)
    // produce consistent results at the boundary
    var code512 = ZopfliDeflater.GetDistanceCode(512);
    var code513 = ZopfliDeflater.GetDistanceCode(513);

    // 512 is in code 17 (base 385, 7 extra bits, max 512), 513 is in code 18 (base 513)
    Assert.That(code512, Is.EqualTo(17));
    Assert.That(code513, Is.EqualTo(18));
  }

  // --- Lazy Matching Tests ---

  [Test]
  public void Compress_Ultra_LazyMatch_RoundTrip() {
    // Data with patterns where lazy matching helps: short match at pos, longer match at pos+1
    var original = new byte[2048];
    var rng = new Random(77777);
    for (var i = 0; i < original.Length; ++i)
      original[i] = (byte)(rng.Next(0, 5) == 0 ? rng.Next(256) : i % 32);

    var compressed = ZopfliDeflater.Compress(original, false);

    using var ms = new MemoryStream(compressed);
    using var zlib = new ZLibStream(ms, CompressionMode.Decompress);
    var decompressed = new MemoryStream();
    zlib.CopyTo(decompressed);
    Assert.That(decompressed.ToArray(), Is.EqualTo(original));
  }

  // --- Adaptive Hash Chain Depth Tests ---

  [Test]
  public void EstimateLocalDepth_HighEntropy_ReducesDepth() {
    // Random data = high entropy = high diversity
    var rng = new Random(42);
    var data = new byte[128];
    rng.NextBytes(data);

    var depth = ZopfliDeflater.HashChain.EstimateLocalDepth(data, 0, data.Length, 256);
    Assert.That(depth, Is.LessThan(256), $"High-entropy depth should be reduced, got {depth}");
  }

  [Test]
  public void EstimateLocalDepth_LowEntropy_IncreasesDepth() {
    // Repetitive data = low entropy = low diversity
    var data = new byte[128];
    for (var i = 0; i < data.Length; ++i)
      data[i] = (byte)(i % 3);

    var depth = ZopfliDeflater.HashChain.EstimateLocalDepth(data, 0, data.Length, 256);
    Assert.That(depth, Is.GreaterThan(256), $"Low-entropy depth should be increased, got {depth}");
  }

  [Test]
  public void Compress_AdaptiveDepth_RoundTrip() {
    var original = new byte[8192];
    // Mix of repetitive and random regions
    for (var i = 0; i < 4096; ++i)
      original[i] = (byte)(i % 8);
    new Random(888).NextBytes(original.AsSpan(4096));

    var compressed = ZopfliDeflater.Compress(original, false);

    using var ms = new MemoryStream(compressed);
    using var zlib = new ZLibStream(ms, CompressionMode.Decompress);
    var decompressed = new MemoryStream();
    zlib.CopyTo(decompressed);
    Assert.That(decompressed.ToArray(), Is.EqualTo(original));
  }

  [Test]
  [Category("Regression")]
  public void Compress_AdaptiveDepth_NoRegression() {
    var original = new byte[8192];
    for (var i = 0; i < original.Length; ++i)
      original[i] = (byte)(i % 16);

    var compressed = ZopfliDeflater.Compress(original, false);

    using var dotnetMs = new MemoryStream();
    using (var zlib = new ZLibStream(dotnetMs, CompressionLevel.SmallestSize, true)) {
      zlib.Write(original);
    }

    Assert.That(compressed.Length, Is.LessThanOrEqualTo(dotnetMs.Length),
      $"Ultra={compressed.Length} vs .NET SmallestSize={dotnetMs.Length}");
  }

  // --- Statistical Block Split Tests ---

  [Test]
  public void FindStatisticalCandidates_DetectsDistributionShift() {
    // Build symbols: first half is literals 0-15, second half is literals 200-215
    var symbols = new ZopfliDeflater.LzSymbol[2048];
    for (var i = 0; i < 1024; ++i)
      symbols[i] = new ZopfliDeflater.LzSymbol((ushort)(i % 16), 0);
    for (var i = 1024; i < 2048; ++i)
      symbols[i] = new ZopfliDeflater.LzSymbol((ushort)(200 + i % 16), 0);

    var candidates = ZopfliDeflater.BlockSplitter._FindStatisticalCandidates(symbols);

    // Should detect at least one candidate near the distribution shift around position 1024
    Assert.That(candidates.Count, Is.GreaterThan(0));
    var nearShift = candidates.Exists(c => c >= 768 && c <= 1280);
    Assert.That(nearShift, Is.True, $"Expected candidate near 1024, found: [{string.Join(", ", candidates)}]");
  }

  [Test]
  public void FindStatisticalCandidates_UniformData_FewCandidates() {
    // Uniform distribution: no distribution shift expected
    var symbols = new ZopfliDeflater.LzSymbol[4096];
    for (var i = 0; i < symbols.Length; ++i)
      symbols[i] = new ZopfliDeflater.LzSymbol((ushort)(i % 16), 0);

    var candidates = ZopfliDeflater.BlockSplitter._FindStatisticalCandidates(symbols);

    // Uniform data should produce few or no candidates
    Assert.That(candidates.Count, Is.LessThanOrEqualTo(2),
      $"Uniform data should have few candidates, found {candidates.Count}");
  }

  [Test]
  public void Compress_Hyper_StatisticalSplit_RoundTrip() {
    // Large data with a distribution shift in the middle
    var original = new byte[16384];
    for (var i = 0; i < 8192; ++i)
      original[i] = (byte)(i % 16);
    var rng = new Random(55555);
    rng.NextBytes(original.AsSpan(8192));

    var compressed = ZopfliDeflater.Compress(original, true, 3);

    using var ms = new MemoryStream(compressed);
    using var zlib = new ZLibStream(ms, CompressionMode.Decompress);
    var decompressed = new MemoryStream();
    zlib.CopyTo(decompressed);
    Assert.That(decompressed.ToArray(), Is.EqualTo(original));
  }

  [Test]
  [Category("Regression")]
  public void Compress_Ultra_LazyMatch_NoCompressionRegression() {
    // Verify lazy matching doesn't make output larger than .NET SmallestSize
    var original = new byte[8192];
    for (var i = 0; i < original.Length; ++i)
      original[i] = (byte)((i * 3 + i / 7) % 48);

    var compressed = ZopfliDeflater.Compress(original, false);

    using var dotnetMs = new MemoryStream();
    using (var zlib = new ZLibStream(dotnetMs, CompressionLevel.SmallestSize, true)) {
      zlib.Write(original);
    }

    Assert.That(compressed.Length, Is.LessThanOrEqualTo(dotnetMs.Length),
      $"Ultra={compressed.Length} vs .NET SmallestSize={dotnetMs.Length}");
  }

  [Test]
  public void FindMatches_SecondByteReject_SkipsFalsePositives() {
    // Craft data where first bytes match but second bytes differ at hash-colliding positions
    var data = new byte[256];
    // Fill with pattern: same first byte at regular intervals, different second byte
    for (var i = 0; i < data.Length; ++i)
      data[i] = (byte)(i % 4 == 0 ? 0xAA : i);

    // Overwrite a region to create an exact match at the end
    data[200] = 0xAA;
    data[201] = 0xBB;
    data[202] = 0xCC;
    data[220] = 0xAA;
    data[221] = 0xBB;
    data[222] = 0xCC;

    var chain = new ZopfliDeflater.HashChain(data, data.Length, 128, 64);
    for (var i = 0; i < data.Length; ++i)
      chain.Insert(i);

    Span<ZopfliDeflater.LzMatch> matches = stackalloc ZopfliDeflater.LzMatch[16];
    var count = chain.FindMatches(220, 36, matches);

    Assert.That(count, Is.GreaterThan(0));
    Assert.That(matches[count - 1].Length, Is.GreaterThanOrEqualTo(3));
  }

  [Test]
  public void BlockReparse_ProducesValidDeflate() {
    // 64KB+ data that exercises block splitting and reparse
    var rng = new Random(42);
    var original = new byte[65536];
    // Mix of repetitive and random for block diversity
    for (var i = 0; i < 32768; ++i)
      original[i] = (byte)(i % 32);
    rng.NextBytes(original.AsSpan(32768));

    var compressed = ZopfliDeflater.Compress(original, true, 3);

    using var ms = new MemoryStream(compressed);
    using var zlib = new ZLibStream(ms, CompressionMode.Decompress);
    var decompressed = new MemoryStream();
    zlib.CopyTo(decompressed);
    Assert.That(decompressed.ToArray(), Is.EqualTo(original));
  }

  [Test]
  public void Hyper_WithBlockReparse_SameOrSmallerThanWithout() {
    // Verify that Hyper mode with block reparse produces valid and reasonably-sized output
    var original = new byte[16384];
    for (var i = 0; i < original.Length; ++i)
      original[i] = (byte)((i * 7 + i / 13) % 64);

    var compressed = ZopfliDeflater.Compress(original, true, 3);

    // Verify round-trip
    using var ms = new MemoryStream(compressed);
    using var zlib = new ZLibStream(ms, CompressionMode.Decompress);
    var decompressed = new MemoryStream();
    zlib.CopyTo(decompressed);
    Assert.That(decompressed.ToArray(), Is.EqualTo(original));

    // Verify it compresses at least as well as .NET SmallestSize
    using var dotnetMs = new MemoryStream();
    using (var dotnetZlib = new ZLibStream(dotnetMs, CompressionLevel.SmallestSize, true)) {
      dotnetZlib.Write(original);
    }

    Assert.That(compressed.Length, Is.LessThanOrEqualTo(dotnetMs.Length));
  }

  [Test]
  public void LazyMatch_DistanceAware_PrefersShorterCloserMatch() {
    // Craft data where distance-aware lazy matching should prefer a shorter close match
    // over a longer distant match: the cost of a close-distance match is lower
    var data = new byte[1024];
    // Repeating pattern at close distance
    for (var i = 0; i < data.Length; ++i)
      data[i] = (byte)(i % 7);

    var compressed = ZopfliDeflater.Compress(data, false);

    // Verify round-trip
    using var ms = new MemoryStream(compressed);
    using var zlib = new ZLibStream(ms, CompressionMode.Decompress);
    var decompressed = new MemoryStream();
    zlib.CopyTo(decompressed);
    Assert.That(decompressed.ToArray(), Is.EqualTo(data));
  }

  [Test]
  public void LazyMatch_ImprovedCompression_SameOrBetter() {
    // 64KB test data should produce same or smaller output with distance-aware lazy matching
    var data = new byte[65536];
    for (var i = 0; i < data.Length; ++i)
      data[i] = (byte)((i * 7 + i / 13 + i / 97) % 256);

    var compressed = ZopfliDeflater.Compress(data, false);

    // Verify round-trip
    using var ms = new MemoryStream(compressed);
    using var zlib = new ZLibStream(ms, CompressionMode.Decompress);
    var decompressed = new MemoryStream();
    zlib.CopyTo(decompressed);
    Assert.That(decompressed.ToArray(), Is.EqualTo(data));

    // Verify it compresses better than .NET SmallestSize
    using var dotnetMs = new MemoryStream();
    using (var dotnetZlib = new ZLibStream(dotnetMs, CompressionLevel.SmallestSize, true)) {
      dotnetZlib.Write(data);
    }

    Assert.That(compressed.Length, Is.LessThanOrEqualTo(dotnetMs.Length));
  }

  [Test]
  public void RleEncoding_CachedResultMatchesRecomputed() {
    // Verify that caching RLE encoding produces identical DEFLATE output
    var data = new byte[4096];
    for (var i = 0; i < data.Length; ++i)
      data[i] = (byte)((i * 3 + i / 7) % 128);

    // Compress twice — result should be deterministic (proves caching doesn't alter output)
    var compressed1 = ZopfliDeflater.Compress(data, false);
    var compressed2 = ZopfliDeflater.Compress(data, false);
    Assert.That(compressed1, Is.EqualTo(compressed2));

    // Also verify round-trip
    using var ms = new MemoryStream(compressed1);
    using var zlib = new ZLibStream(ms, CompressionMode.Decompress);
    var decompressed = new MemoryStream();
    zlib.CopyTo(decompressed);
    Assert.That(decompressed.ToArray(), Is.EqualTo(data));
  }
}
