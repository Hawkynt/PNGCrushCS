using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using FileFormat.Codecs.FlashSv2;

namespace FileFormat.Codecs.FlashSv2.Tests;

/// <summary>
/// RFC 1951 read directly, since neither .NET's zlib wrapper nor its <see cref="DeflateStream"/>
/// exposes the preset dictionary Screen Video v2's "priming" needs. These tests carry no dictionary at
/// all — they check the decoder against <see cref="ZLibStream"/>, a completely independent
/// implementation that owes this one nothing, over the raw DEFLATE payload an ordinary zlib stream
/// already carries once its two-byte header and four-byte Adler-32 trailer are stripped. Only once this
/// agreed byte for byte was <see cref="RawDeflate"/> trusted for the case <c>ZLibStream</c> cannot do at
/// all — a preset dictionary, exercised below and again by <c>FlashSv2VideoDecoderTests</c> against a
/// real FSV2 block — with bytes Python's zlib produced, since nothing in this package compresses
/// against one.
/// </summary>
[TestFixture]
public class RawDeflateTests {

  /// <summary>Compresses with the BCL's own zlib and returns the raw DEFLATE payload — the header and
  /// trailer sliced away, exactly what a primed FSV2 block carries instead of a complete zlib stream.</summary>
  private static byte[] RawDeflatePayload(byte[] raw, CompressionLevel level) {
    using var ms = new MemoryStream();
    using (var z = new ZLibStream(ms, level, leaveOpen: true))
      z.Write(raw);

    var wrapped = ms.ToArray();
    // Two-byte zlib header, four-byte Adler-32 trailer.
    return wrapped[2..^4];
  }

  private static void AssertMatchesZLibStream(byte[] raw, CompressionLevel level) {
    var payload = RawDeflatePayload(raw, level);

    var decoded = RawDeflate.Decode(payload, []);

    Assert.That(decoded, Is.EqualTo(raw));
  }

  [Test]
  [Category("Unit")]
  public void DecodesAFixedHuffmanBlockTheSameAsZLibStream() {
    // Short and varied enough that zlib's own encoder picks the fixed Huffman block type.
    AssertMatchesZLibStream("The quick brown fox jumps over the lazy dog."u8.ToArray(), CompressionLevel.Fastest);
  }

  [Test]
  [Category("Unit")]
  public void DecodesADynamicHuffmanBlockTheSameAsZLibStream() {
    // Large and skewed enough in its byte frequencies that zlib's encoder builds a dynamic table.
    var raw = new byte[8192];
    var rng = new Random(12345);
    for (var i = 0; i < raw.Length; ++i)
      raw[i] = (byte)(rng.Next(6) == 0 ? rng.Next(256) : rng.Next(4));

    AssertMatchesZLibStream(raw, CompressionLevel.Optimal);
  }

  [Test]
  [Category("Unit")]
  public void DecodesLongRunsThatForceLengthDistanceMatchesTheSameAsZLibStream() {
    // Long repeated runs push the length/distance extra-bit tables through their full range.
    var raw = new byte[70000];
    for (var i = 0; i < raw.Length; ++i)
      raw[i] = (byte)((i / 257) % 5);

    AssertMatchesZLibStream(raw, CompressionLevel.Optimal);
  }

  [Test]
  [Category("Unit")]
  public void DecodesAStoredBlockTheSameAsZLibStream() {
    // Incompressible data, which a real encoder falls back to a stored (uncompressed) block for.
    var raw = new byte[600];
    new Random(7).NextBytes(raw);

    AssertMatchesZLibStream(raw, CompressionLevel.NoCompression);
  }

  [Test]
  [Category("Unit")]
  public void DecodesEmptyInputTheSameAsZLibStream() {
    AssertMatchesZLibStream([], CompressionLevel.Optimal);
  }

  [Test]
  [Category("Unit")]
  public void DecodesAgainstAPresetDictionaryTheSameAsPythonsZlibDoes() {
    // Thirty-two distinct bytes, primed with a dictionary of the same thirty-two bytes -- the encoder
    // reduces the whole payload to a single dictionary back-reference. Produced once with Python's
    // zlib against a preset dictionary, since nothing in this package or the BCL compresses against
    // one; this is the one thing about the bytes that could not be produced here, and the whole reason
    // RawDeflate reads RFC 1951 directly rather than asking ZLibStream for it.
    byte[] dictionary = [.. Enumerable.Range(0, 32).Select(i => (byte)i)];
    byte[] payload = [0x63, 0x20, 0x20, 0x0F, 0x00];

    var decoded = RawDeflate.Decode(payload, dictionary);

    Assert.That(decoded, Is.EqualTo(dictionary));
  }

  [Test]
  [Category("Unit")]
  public void RefusesAMatchReachingBeforeTheStartOfTheOutputAndAnyDictionary() {
    // The same bytes as the test above, decoded with no dictionary at all. Their single symbol is one
    // match copying the whole thirty-two-byte dictionary, so its distance reaches before anything has
    // been produced the moment output is empty -- exactly the diagnostic that first proved FSV2's
    // "priming" needs a genuine preset dictionary rather than a continued stream: an ordinary DEFLATE
    // decoder given these bytes and no history fails outright, where a stream continuation would not
    // have anything to fail against.
    byte[] payload = [0x63, 0x20, 0x20, 0x0F, 0x00];

    var failure = Assert.Throws<InvalidDataException>(() => RawDeflate.Decode(payload, []));
    Assert.That(failure!.Message, Does.Contain("reaches"));
  }
}
