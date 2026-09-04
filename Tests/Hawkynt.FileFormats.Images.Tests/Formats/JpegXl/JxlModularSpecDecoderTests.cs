using System;
using System.IO;
using FileFormat.JpegXl.Codec;

namespace FileFormat.JpegXl.Tests;

/// <summary>
/// Skeleton-level conformance tests for <see cref="JxlModularSpecDecoder"/>.
///
/// <para>
/// The decoder under test is intentionally a thin orchestrator while companion
/// pieces (<c>JxlMaTreeDecoder</c>, <c>JxlWeightedPredictor</c>,
/// <c>JxlModularTransforms</c>) are being implemented in parallel. These tests
/// cover only the integration paths the skeleton can answer correctly:
/// </para>
/// <list type="bullet">
///   <item>Argument validation (null reader, non-positive dimensions / channel
///         counts / bit depth).</item>
///   <item>"Empty modular section" hand-crafted bit stream — 1 channel of
///         dimensions 1x1, 0 transforms, trivial 1-leaf MA tree, single
///         residual = 0 token. Verifies the channel count, dimensions, and
///         that the decode does not crash.</item>
///   <item>The same with multiple channels and a larger image, to verify
///         channel set construction respects <c>numChannels</c> and
///         <c>width</c>/<c>height</c> exactly.</item>
/// </list>
/// </summary>
[TestFixture]
public sealed class JxlModularSpecDecoderTests {

  // -------------------------------------------------------------
  // Argument validation
  // -------------------------------------------------------------

  [Test]
  public void Decode_NullReader_Throws() {
    Assert.Throws<ArgumentNullException>(() =>
      JxlModularSpecDecoder.Decode(reader: null!, width: 1, height: 1, numChannels: 1, bitDepth: 8));
  }

  [Test]
  public void Decode_ZeroWidth_Throws() {
    var reader = new JxlBitReader(new byte[16], 0);
    Assert.Throws<ArgumentOutOfRangeException>(() =>
      JxlModularSpecDecoder.Decode(reader, width: 0, height: 1, numChannels: 1, bitDepth: 8));
  }

  [Test]
  public void Decode_NegativeHeight_Throws() {
    var reader = new JxlBitReader(new byte[16], 0);
    Assert.Throws<ArgumentOutOfRangeException>(() =>
      JxlModularSpecDecoder.Decode(reader, width: 1, height: -1, numChannels: 1, bitDepth: 8));
  }

  [Test]
  public void Decode_ZeroChannels_Throws() {
    var reader = new JxlBitReader(new byte[16], 0);
    Assert.Throws<ArgumentOutOfRangeException>(() =>
      JxlModularSpecDecoder.Decode(reader, width: 1, height: 1, numChannels: 0, bitDepth: 8));
  }

  [Test]
  public void Decode_BitDepthTooLarge_Throws() {
    var reader = new JxlBitReader(new byte[16], 0);
    Assert.Throws<ArgumentOutOfRangeException>(() =>
      JxlModularSpecDecoder.Decode(reader, width: 1, height: 1, numChannels: 1, bitDepth: 33));
  }

  [Test]
  public void Decode_BitDepthZero_Throws() {
    var reader = new JxlBitReader(new byte[16], 0);
    Assert.Throws<ArgumentOutOfRangeException>(() =>
      JxlModularSpecDecoder.Decode(reader, width: 1, height: 1, numChannels: 1, bitDepth: 0));
  }

  // -------------------------------------------------------------
  // Non-conformant-section tests.
  //
  // These fixtures are hand-built and, as the comment below has always said,
  // are not spec-conformant modular sections. They used to assert that the
  // decoder returned a correctly-shaped image for them anyway. That is the
  // behaviour this file now tests against: a section the decoder cannot follow
  // has to be refused, because the alternative is a picture assembled from
  // whatever the fallbacks happened to leave in the buffers. Measured against
  // libjxl, that alternative was wrong for real files as well as these.
  //
  // The skeleton recognises a 1-bit "transforms all_default = 1" prefix on
  // the modular section, then synthesises a trivial 1-leaf MA tree
  // (predictor=Zero, offset=0, multiplier=×1, context=0). It then reads
  // the entropy decoder via JxlEntropyDecoder.Read(reader, numContexts=1,
  // disallowLz77=false), and finally decodes width*height pixels.
  //
  // To make the test self-contained we hand-craft a bitstream that, after
  // the leading "all_default" bit, contains a complete and trivially
  // decodable entropy block followed by enough 0-token residuals to fill
  // the channel.
  //
  // We exploit JxlEntropyDecoder's prefix-code path with a SIMPLE
  // single-symbol alphabet (alphabet_size=1, length=0) so each ReadInt
  // returns 0 without consuming bits. That requires this prefix:
  //
  //   bit 0  : all_default = 1                          (transforms empty)
  //   bit 1  : lz77_enabled = 0
  //   bit 2  : use_prefix_code = 1                      (no log_alpha_size bits)
  //   bits 3..: per-cluster: alphabet_size = DecodeVarLenUint16() + 1
  //              For alphabet_size=1 we need the U16 to encode 0 → first
  //              bit of DecodeVarLenUint16 = 0 (returns 0 immediately).
  //              So bit 3 = 0 → alphabet_size = 1. The single-symbol code
  //              has length 0 — no further table bits.
  //   per-cluster hybrid-int config: DecodeUintConfig with log_alpha_size=15.
  //              CeilLog2Nonzero(15+1) = 4 bits for split_exponent.
  //              We pick split_exponent=15 (= log_alpha_size) → msb,lsb
  //              skipped per the short-circuit. 4 bits = 1111.
  //
  // -------------------------------------------------------------

  /// <summary>Build the prefix described above. Returns a byte array whose
  /// first ~10 bits form the empty-section preamble; bits beyond are zero
  /// so that any subsequent ReadInt returns the single-symbol 0.</summary>
  private static byte[] BuildEmptyModularSectionBits() {
    // Bit positions (LSB-first within bytes):
    //   bit 0: all_default = 1                                  -> 0b...........1
    //   bit 1: lz77_enabled = 0                                 -> 0b..........01
    //   bit 2: numContexts > 1? With numContexts=1 (one leaf, no LZ77 inflation),
    //          libjxl skips ReadU32 for numClusters and the cluster map. Then
    //          we go straight to use_prefix_code = 1            -> 0b.........101
    //   bit 3: DecodeVarLenUint16: 1-bit prefix = 0 → returns 0. alphabet_size=1.
    //                                                            -> 0b........0101
    //   bits 4..7: split_exponent = 15 (4 bits, value 0xF, LSB-first = 1,1,1,1)
    //                                                            -> 0b....11110101 = 0xF5
    //   The remaining bytes can be 0 — the prefix-code "single symbol with
    //   length 0" returns 0 without reading any further bit.
    var bytes = new byte[64];
    bytes[0] = 0xF5;
    return bytes;
  }

  [Test]
  public void Decode_NonConformantSection_1x1_1Channel_IsRefused() {
    var bits = BuildEmptyModularSectionBits();
    var reader = new JxlBitReader(bits, 0);
    Assert.Throws<InvalidDataException>(() =>
      JxlModularSpecDecoder.Decode(reader, width: 1, height: 1, numChannels: 1, bitDepth: 8));
  }

  [Test]
  public void Decode_NonConformantSection_IsRefusedWhateverTheChannelCount() {
    var bits = BuildEmptyModularSectionBits();
    var reader = new JxlBitReader(bits, 0);
    Assert.Throws<InvalidDataException>(() =>
      JxlModularSpecDecoder.Decode(reader, width: 2, height: 2, numChannels: 3, bitDepth: 8));
  }

  [Test]
  public void Decode_NonConformantSection_IsRefusedWhateverTheDimensions() {
    var bits = BuildEmptyModularSectionBits();
    var reader = new JxlBitReader(bits, 0);
    Assert.Throws<InvalidDataException>(() =>
      JxlModularSpecDecoder.Decode(reader, width: 8, height: 5, numChannels: 1, bitDepth: 8));
  }

  [Test]
  public void Decode_AllZeroBits_IsRefused() {
    // Sixteen zero bytes carry no section at all, so the reader runs out of bits
    // rather than reaching the end-of-stream check.
    var bits = new byte[16];
    var reader = new JxlBitReader(bits, 0);
    Assert.Throws<InvalidOperationException>(() =>
      JxlModularSpecDecoder.Decode(reader, width: 1, height: 1, numChannels: 1, bitDepth: 8));
  }
}
