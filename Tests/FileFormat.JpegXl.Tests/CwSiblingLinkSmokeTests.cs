#if CW_SIBLING
using System.IO;
using Compression.Core.BitIO;
using Compression.Core.Entropy.Huffman;

namespace FileFormat.JpegXl.Tests;

/// <summary>
/// Smoke tests that confirm the CompressionWorkbench source-link is wired
/// correctly. These tests only compile when the sibling repo is checked out
/// at <c>..\..\CompressionWorkbench</c>; otherwise they are excluded by the
/// <c>CW_SIBLING</c> conditional define.
///
/// <para>The intent is to serve as a foothold for future cross-pollination —
/// e.g. replacing the in-house canonical-Huffman path in
/// <see cref="FileFormat.JpegXl.Codec.JxlEntropyDecoder"/> with CW's
/// <see cref="CanonicalHuffman"/>, or layering a stream-based bit reader on
/// top of CW's <see cref="BitReader{TOrder}"/> for non-buffered inputs.</para>
/// </summary>
[TestFixture]
[Category("CwSibling")]
public sealed class CwSiblingLinkSmokeTests {

  /// <summary>The CW BitReader can read LSB-first bits from a stream — the
  /// same convention JPEG XL uses inside the codestream.</summary>
  [Test]
  public void CwBitReader_LsbFirst_ReadsBitsInExpectedOrder() {
    // 0b10110100 = 0xB4. Reading LSB-first: 0,0,1,0,1,1,0,1.
    using var ms = new MemoryStream(new byte[] { 0xB4 });
    var br = new BitReader<LsbBitOrder>(ms);
    var bits = new int[8];
    for (var i = 0; i < 8; ++i)
      bits[i] = br.ReadBit();
    Assert.That(bits, Is.EqualTo(new[] { 0, 0, 1, 0, 1, 1, 0, 1 }).AsCollection);
  }

  /// <summary>CW's CanonicalHuffman builds an encode/decode table from
  /// code lengths. JPEG XL's prefix-code section uses the same canonical
  /// Huffman convention; this verifies the type is accessible from our
  /// project.</summary>
  [Test]
  public void CwCanonicalHuffman_FromCodeLengths_AssignsCanonicalCodes() {
    // 4 symbols with code lengths {2, 1, 3, 3}: a textbook canonical case.
    // Canonical assignment yields codes 10, 0, 110, 111.
    var huff = new CanonicalHuffman(new[] { 2, 1, 3, 3 });
    Assert.That(huff.MaxCodeLength, Is.EqualTo(3));
    Assert.That(huff.MaxSymbol, Is.EqualTo(4));
  }
}
#endif
