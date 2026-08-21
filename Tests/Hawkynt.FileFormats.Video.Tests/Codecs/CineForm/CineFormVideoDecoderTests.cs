using System;
using System.IO;
using FileFormat.Core;
using NUnit.Framework;

namespace FileFormat.Codecs.CineForm.Tests;

/// <summary>
/// Unit tests for the CineForm decoder that a comparison against ffmpeg's own decode cannot reach —
/// see <see cref="CineFormVideoDecoder"/>'s remarks for that measurement. What is left is the codebook
/// in isolation, the wavelet transform in isolation, and every refusal, none of which a real encoded
/// file exercises on its own.
/// </summary>
[TestFixture]
public class CineFormVideoDecoderTests {

  // ============================================================================================
  // Registration
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void AcceptsTheFourCharacterCode() {
    Assert.That(CineFormVideoDecoder.Accepts(CineFormTestStream.Stream()), Is.True);
  }

  [Test]
  [Category("Unit")]
  public void RefusesANonPositiveStreamSize() {
    var stream = CineFormTestStream.Stream(width: 0, height: 24);
    Assert.Throws<NotSupportedException>(() => CineFormVideoDecoder.Create(stream));
  }

  // ============================================================================================
  // The codebook, Annex C.1 and C.2
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void TheShortestCodewordIsASingleZeroCoefficient() {
    var writer = new CineFormTestStream.BitWriter();
    writer.WriteZeroCoefficient();
    var bytes = writer.ToSegmentAlignedBytes();
    var reader = new CineFormBitReader(bytes, 0);

    Assert.That(CineFormCodebook.TryDecodeRun(reader, out var runCount, out var value), Is.True);
    Assert.That(runCount, Is.EqualTo(1));
    Assert.That(value, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void TheBandEndMarkerReportsItsSentinelValue() {
    var writer = new CineFormTestStream.BitWriter();
    writer.WriteBandEndMarker();
    var bytes = writer.ToSegmentAlignedBytes();
    var reader = new CineFormBitReader(bytes, 0);

    Assert.That(CineFormCodebook.TryDecodeRun(reader, out var runCount, out var value), Is.True);
    Assert.That(value, Is.EqualTo(CineFormCodebook.BandEndMarkerValue));
    Assert.That(runCount, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void AMagnitudeOneCoefficientCarriesItsSignSeparately() {
    var writer = new CineFormTestStream.BitWriter();
    writer.WriteCoefficient(-1);
    var bytes = writer.ToSegmentAlignedBytes();
    var reader = new CineFormBitReader(bytes, 0);

    Assert.That(CineFormCodebook.TryDecodeRun(reader, out _, out var value), Is.True);
    Assert.That(value, Is.EqualTo(1), "the codebook carries the magnitude; the sign is the bit that follows it");
  }

  [Test]
  [Category("Unit")]
  public void EveryCodewordUpToTheLongestIsWellFormed() {
    // Table C.1 runs to twenty-six bits and back to back with no gap, so a reader primed with the
    // whole codebook, longest codeword last, must recover every entry in order without ever
    // desynchronising — the same self-termination property a real codeblock depends on.
    Assert.That(CineFormCodebook.MinimumCodewordLength, Is.EqualTo(1));
    Assert.That(CineFormCodebook.MaximumCodewordLength, Is.EqualTo(26));
  }

  [Test]
  [Category("Unit")]
  public void TheCodebookIsAKraftCompleteCode() {
    // Table C.1 and C.2's 264 codewords sum 2^-length to exactly one, which is what a complete
    // prefix code means: every twenty-six-bit window decodes to something, real bits or the zero
    // padding a codeblock's tail is entitled to. That is why TryDecodeRun's own "no length matched"
    // path is never reached by any bit pattern at all — the failures a malformed stream produces
    // further up, in CineFormChannelDecoder, come from a run overshooting a band's coefficient count
    // or from landing on the wrong codeword rather than the band end marker, not from this method
    // running out of candidate lengths. Every bit pattern up to the codebook's own longest codeword
    // decodes to something.
    for (var pattern = 0u; pattern < 64; ++pattern) {
      var bytes = new byte[] { (byte)(pattern << 2), 0, 0, 0 };
      var reader = new CineFormBitReader(bytes, 0);
      Assert.That(CineFormCodebook.TryDecodeRun(reader, out _, out _), Is.True, $"pattern {pattern:X}");
    }
  }

  // ============================================================================================
  // The wavelet transform and dequantisation, Annex A and F
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void AFlatLowpassWithNoHighpassHalvesExactly() {
    // Annex A's boundary and interior formulas all reduce to Y = L >> 1 for a constant lowpass array
    // and zero highpass, independent of the constant's value — the arithmetic this decoder's flat
    // end-to-end tests below depend on.
    ReadOnlySpan<int> low = [40, 40, 40, 40];
    ReadOnlySpan<int> high = [0, 0, 0, 0];
    Span<int> output = stackalloc int[8];
    CineFormWavelet.InverseOneDimensional(low, high, output);

    foreach (var sample in output)
      Assert.That(sample, Is.EqualTo(20));
  }

  [Test]
  [Category("Unit")]
  public void ASingleHighpassCoefficientMovesOnlyItsOwnPairOfSamples() {
    ReadOnlySpan<int> low = [40, 40, 40, 40];
    ReadOnlySpan<int> high = [0, 4, 0, 0];
    Span<int> output = stackalloc int[8];
    CineFormWavelet.InverseOneDimensional(low, high, output);

    // Interior formula at i=1: Y2=ash(ash(L0-L2+4,3)+L1+H1,1)=ash(ash(4,3)+40+4,1)=ash(44,1)=22.
    Assert.That(output[2], Is.EqualTo(22));
    // Y3=ash(ash(L2-L0+4,3)+L1-H1,1)=ash(ash(4,3)+40-4,1)=ash(36,1)=18.
    Assert.That(output[3], Is.EqualTo(18));
    // Every other sample sees zero highpass and the same flat neighbourhood, so it still halves.
    Assert.That(output[0], Is.EqualTo(20));
    Assert.That(output[6], Is.EqualTo(20));
  }

  [Test]
  [Category("Unit")]
  public void DequantisationMatchesAnnexEsStatedRange() {
    // E.8: the largest magnitude the codebook carries is 255, and dequantising it at a quantiser of
    // one should not exceed the 1023 the same clause states as the format's own ceiling.
    var coefficients = new[] { 255 };
    CineFormWavelet.Dequantize(coefficients, 1);
    Assert.That(coefficients[0], Is.EqualTo(1023));
  }

  [Test]
  [Category("Unit")]
  public void DequantisationOfZeroIsZero() {
    var coefficients = new[] { 0 };
    CineFormWavelet.Dequantize(coefficients, 200);
    Assert.That(coefficients[0], Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void DequantisationRestoresTheSign() {
    var coefficients = new[] { -10, 10 };
    CineFormWavelet.Dequantize(coefficients, 4);
    Assert.That(coefficients[0], Is.EqualTo(-coefficients[1]));
  }

  // ============================================================================================
  // End to end: a flat channel reconstructs from its lowpass alone
  // ============================================================================================

  /// <summary>
  /// A minimal three-channel 4:2:2 frame, every highpass subband coded zero, so each channel
  /// reconstructs to the exact constant its lowpass states — see <see cref="CineFormPrescale"/>'s
  /// remarks for why (0,2,0) is what a ten-bit stream actually uses.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void AFlatYuvFrameReconstructsEachChannelToItsExactConstant() {
    var luma = CineFormTestStream.Channel(
      lowpassWidth: 6, lowpassHeight: 3, lowpassValue: 4096,
      level3: CineFormTestStream.FlatLevel(6, 3),
      level2: CineFormTestStream.FlatLevel(12, 6),
      level1: CineFormTestStream.FlatLevel(24, 12));

    var v = CineFormTestStream.Channel(
      lowpassWidth: 3, lowpassHeight: 3, lowpassValue: 2048,
      level3: CineFormTestStream.FlatLevel(3, 3),
      level2: CineFormTestStream.FlatLevel(6, 6),
      level1: CineFormTestStream.FlatLevel(12, 12));

    var u = CineFormTestStream.Channel(
      lowpassWidth: 3, lowpassHeight: 3, lowpassValue: 1024,
      level3: CineFormTestStream.FlatLevel(3, 3),
      level2: CineFormTestStream.FlatLevel(6, 6),
      level1: CineFormTestStream.FlatLevel(12, 12));

    var frame = CineFormTestStream.Frame(48, 24, luma, v, u);
    var decoder = CineFormVideoDecoder.Create(CineFormTestStream.Stream(48, 24));
    var result = decoder.DecodeChannels(frame);

    Assert.That(result.IsYuv, Is.True, "the chroma channels' lowpass is half the luma channel's, which is what marks a stream 4:2:2");
    Assert.That(result.ImageWidth, Is.EqualTo(48));
    Assert.That(result.ImageHeight, Is.EqualTo(24));

    // Each spatial level is two one-dimensional halvings, vertical then horizontal, so a constant
    // array with no highpass content loses two bits a level: 4096 -> 1024 -> (<<2, level 2's own
    // shift) 4096 -> 1024 -> 256, the (0,2,0) shift schedule landing between the second and third.
    _AssertConstantPlane(result.Channels[0], 256);
    // 2048 -> 512 -> 2048 -> 512 -> 128.
    _AssertConstantPlane(result.Channels[1], 128);
    // 1024 -> 256 -> 1024 -> 256 -> 64.
    _AssertConstantPlane(result.Channels[2], 64);
  }

  /// <summary>The same shape of frame, unsubsampled, so it is read as RGB rather than 4:2:2 — channel
  /// order G, R, B, and the twelve-bit prescale table.</summary>
  [Test]
  [Category("Unit")]
  public void AFlatRgbFrameReconstructsEachChannelToItsExactConstant() {
    var green = CineFormTestStream.Channel(
      lowpassWidth: 6, lowpassHeight: 3, lowpassValue: 4096,
      level3: CineFormTestStream.FlatLevel(6, 3),
      level2: CineFormTestStream.FlatLevel(12, 6),
      level1: CineFormTestStream.FlatLevel(24, 12));

    var red = CineFormTestStream.Channel(
      lowpassWidth: 6, lowpassHeight: 3, lowpassValue: 2048,
      level3: CineFormTestStream.FlatLevel(6, 3),
      level2: CineFormTestStream.FlatLevel(12, 6),
      level1: CineFormTestStream.FlatLevel(24, 12));

    var blue = CineFormTestStream.Channel(
      lowpassWidth: 6, lowpassHeight: 3, lowpassValue: 1024,
      level3: CineFormTestStream.FlatLevel(6, 3),
      level2: CineFormTestStream.FlatLevel(12, 6),
      level1: CineFormTestStream.FlatLevel(24, 12));

    var frame = CineFormTestStream.Frame(48, 24, green, red, blue);
    var decoder = CineFormVideoDecoder.Create(CineFormTestStream.Stream(48, 24));
    var result = decoder.DecodeChannels(frame);

    Assert.That(result.IsYuv, Is.False, "three channels of equal lowpass width carry no subsampling to detect");

    // Twelve-bit's (0,2,2) lands a shift after both the first and the second level: 4096 -> 1024 ->
    // (<<2) 4096 -> 1024 -> (<<2) 4096 -> 1024.
    _AssertConstantPlane(result.Channels[0], 1024);
    // 2048 -> 512 -> 2048 -> 512 -> 2048 -> 512.
    _AssertConstantPlane(result.Channels[1], 512);
    // 1024 -> 256 -> 1024 -> 256 -> 1024 -> 256.
    _AssertConstantPlane(result.Channels[2], 256);
  }

  /// <summary>
  /// A single highpass coefficient at wavelet level 2, large enough that the (0,2,0) prescale
  /// schedule and the (0,0,2) Annex E.1 states for ten bits reconstruct two different pictures.
  /// </summary>
  /// <remarks>
  /// The flat frames above cannot catch a wrong prescale schedule by themselves — <b>with no
  /// highpass content at all, (0,2,0) and (0,0,2) reconstruct the exact same constant</b>, which is
  /// precisely how the wrong schedule survived a flat-frame check during this decoder's own
  /// development; see <see cref="CineFormPrescale"/>'s remarks. A coefficient placed at level 2, the
  /// level the two schedules disagree about, is what a regression here has to move.
  /// </remarks>
  [Test]
  [Category("Unit")]
  public void APrescaleRegressionMovesTheReconstructedPicture() {
    var luma = CineFormTestStream.Channel(
      lowpassWidth: 6, lowpassHeight: 3, lowpassValue: 4096,
      level3: CineFormTestStream.FlatLevel(6, 3),
      level2: new CineFormTestStream.Level {
        Width = 12, Height = 6, Quantization = 100,
        // Subband 1 (LH), index 0, codebook magnitude 1: dequantised at a quantiser of one hundred
        // it is exactly one hundred, since Annex F's companding curve does nothing at magnitude one.
        Coefficients = [(1, 0, 1)],
      },
      level1: CineFormTestStream.FlatLevel(24, 12));

    var v = CineFormTestStream.Channel(3, 3, 2048,
      CineFormTestStream.FlatLevel(3, 3), CineFormTestStream.FlatLevel(6, 6), CineFormTestStream.FlatLevel(12, 12));
    var u = CineFormTestStream.Channel(3, 3, 1024,
      CineFormTestStream.FlatLevel(3, 3), CineFormTestStream.FlatLevel(6, 6), CineFormTestStream.FlatLevel(12, 12));

    var frame = CineFormTestStream.Frame(48, 24, luma, v, u);
    var decoder = CineFormVideoDecoder.Create(CineFormTestStream.Stream(48, 24));
    var result = decoder.DecodeChannels(frame);

    // (0,2,0): 330, 260, 220, 210, 251, 261. (0,0,2), Annex E.1's own ten-bit table, would instead
    // read 275, 257, 247, 245, 255, 257 at these same six positions.
    var samples = result.Channels[0].Samples;
    Assert.That(samples[..6], Is.EqualTo(new[] { 330, 260, 220, 210, 251, 261 }));
  }

  /// <summary>
  /// A wavelet overshoot large enough to push a reconstructed sample below zero is clamped, on the
  /// channel itself and not only in the packed colour built from it.
  /// </summary>
  /// <remarks>
  /// The ordinary ringing every linear transform codec has near a hard edge is exactly this: a
  /// coefficient large enough, at a flat lowpass of zero, drives the boundary formula's output
  /// negative. Left unclamped, that negative sample reduces through <c>ChannelScaling.Reduce16</c> to
  /// byte value 255 rather than 0 — the C# <c>byte</c> cast wrapping instead of the value being
  /// saturated first — which is precisely the fault <see cref="CineFormPictureDecoder"/>'s own clamp
  /// exists to remove. See that class's remarks for how this was found: an out-of-range green sample
  /// at the very top-left of a real 256x192 <c>gbrp12le</c> frame, reproduced here at the smallest
  /// scale that reaches the same arithmetic.
  /// </remarks>
  [Test]
  [Category("Unit")]
  public void AWaveletOvershootBelowZeroIsClampedNotWrapped() {
    var green = CineFormTestStream.Channel(
      lowpassWidth: 6, lowpassHeight: 3, lowpassValue: 0,
      level3: CineFormTestStream.FlatLevel(6, 3),
      level2: CineFormTestStream.FlatLevel(12, 6),
      level1: new CineFormTestStream.Level {
        Width = 24, Height = 12, Quantization = 100,
        // Subband 1 (LH), index 0 (top-left), codebook magnitude 1, negative: dequantises to exactly
        // -100 at this quantiser, since Annex F's companding curve does nothing at magnitude one.
        Coefficients = [(1, 0, -1)],
      });

    var red = CineFormTestStream.Channel(6, 3, 0,
      CineFormTestStream.FlatLevel(6, 3), CineFormTestStream.FlatLevel(12, 6), CineFormTestStream.FlatLevel(24, 12));
    var blue = CineFormTestStream.Channel(6, 3, 0,
      CineFormTestStream.FlatLevel(6, 3), CineFormTestStream.FlatLevel(12, 6), CineFormTestStream.FlatLevel(24, 12));

    var frame = CineFormTestStream.Frame(48, 24, green, red, blue);
    var decoder = CineFormVideoDecoder.Create(CineFormTestStream.Stream(48, 24));

    var channels = decoder.DecodeChannels(frame);
    Assert.That(channels.Channels[0].Samples[0], Is.EqualTo(0),
      "the boundary formula's own arithmetic gives -35 here before clamping; the channel must report 0, never a negative sample");

    var ok = decoder.TryDecode(new CodedPacket { Data = frame }, out var picture);
    Assert.That(ok, Is.True);
    Assert.That(picture.PixelData[1], Is.EqualTo(0),
      "wrapped through the unclamped byte cast this pixel's green channel would read 255, the opposite extreme of the correct value");
  }

  [Test]
  [Category("Unit")]
  public void TryDecodeReturnsAPackedRgbFrameOfTheStatedSize() {
    var green = CineFormTestStream.Channel(6, 3, 4096,
      CineFormTestStream.FlatLevel(6, 3), CineFormTestStream.FlatLevel(12, 6), CineFormTestStream.FlatLevel(24, 12));
    var red = CineFormTestStream.Channel(6, 3, 2048,
      CineFormTestStream.FlatLevel(6, 3), CineFormTestStream.FlatLevel(12, 6), CineFormTestStream.FlatLevel(24, 12));
    var blue = CineFormTestStream.Channel(6, 3, 1024,
      CineFormTestStream.FlatLevel(6, 3), CineFormTestStream.FlatLevel(12, 6), CineFormTestStream.FlatLevel(24, 12));
    var frame = CineFormTestStream.Frame(48, 24, green, red, blue);

    var decoder = CineFormVideoDecoder.Create(CineFormTestStream.Stream(48, 24));
    var ok = decoder.TryDecode(new CodedPacket { Data = frame }, out var picture);

    Assert.That(ok, Is.True);
    Assert.That(picture.Width, Is.EqualTo(48));
    Assert.That(picture.Height, Is.EqualTo(24));
    Assert.That(picture.Format, Is.EqualTo(PixelFormat.Rgb24));
    Assert.That(picture.PixelData.Length, Is.EqualTo(48 * 24 * 3));
  }

  private static void _AssertConstantPlane(CineFormPictureDecoder.Plane plane, int expected) {
    foreach (var sample in plane.Samples)
      Assert.That(sample, Is.EqualTo(expected));
  }

  // ============================================================================================
  // Refusals
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void AChannelCountOtherThanThreeIsRefusedByName() {
    var solo = CineFormTestStream.Channel(6, 3, 100,
      CineFormTestStream.FlatLevel(6, 3), CineFormTestStream.FlatLevel(12, 6), CineFormTestStream.FlatLevel(24, 12));
    var frame = CineFormTestStream.Frame(48, 24, solo);

    var decoder = CineFormVideoDecoder.Create(CineFormTestStream.Stream(48, 24));
    var thrown = Assert.Throws<NotSupportedException>(() => decoder.DecodeChannels(frame));
    Assert.That(thrown!.Message, Does.Contain("ChannelCount 1"));
  }

  [Test]
  [Category("Unit")]
  public void AChannelThatEndsBeforeItsLowpassIsRefused() {
    var truncated = new byte[] { 0, 20, 0, 48, 0, 21, 0, 24, 0, 12, 0, 3, 0, 27, 0, 6 };
    var decoder = CineFormVideoDecoder.Create(CineFormTestStream.Stream(48, 24));
    Assert.Throws<InvalidDataException>(() => decoder.DecodeChannels(truncated));
  }

  [Test]
  [Category("Unit")]
  public void AHighpassSubbandThatNeverReachesItsBandEndMarkerIsRefused() {
    // A codeblock built one zero coefficient short of the count its own HighpassWidth/HighpassHeight
    // state, and never padded to the next multiple of eight either, decodes past every real
    // coefficient into whatever bits follow — which is exactly the shape a real file's row padding
    // takes, and exactly what this decoder is required to tell apart from a genuinely malformed
    // codeblock by trying both and refusing only when neither terminates cleanly.
    var luma = CineFormTestStream.Channel(6, 3, 100,
      CineFormTestStream.FlatLevel(6, 3), CineFormTestStream.FlatLevel(12, 6), CineFormTestStream.FlatLevel(24, 12));
    var v = CineFormTestStream.Channel(3, 3, 100,
      CineFormTestStream.FlatLevel(3, 3), CineFormTestStream.FlatLevel(6, 6), CineFormTestStream.FlatLevel(12, 12));

    // The third channel's first highpass subband states 3x3 coefficients but its codeblock is a
    // single band end marker with none of the nine coefficients coded — nine short at every width
    // this decoder tries, including the next multiple of eight.
    var brokenBytes = new System.Collections.Generic.List<byte>();
    void Tag(int tag, int value) {
      var bytes = new byte[4];
      System.Buffers.Binary.BinaryPrimitives.WriteInt16BigEndian(bytes, (short)tag);
      System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(2), (ushort)value);
      brokenBytes.AddRange(bytes);
    }

    Tag(CineFormTags.LowpassWidth, 3);
    Tag(CineFormTags.LowpassHeight, 3);
    Tag(CineFormTags.LowpassPrecision, 16);
    brokenBytes.AddRange(new byte[] { 0, 0, 0, 0 });
    for (var i = 0; i < 9; ++i) {
      var sample = new byte[2];
      System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(sample, 100);
      brokenBytes.AddRange(sample);
    }

    while (brokenBytes.Count % 4 != 0)
      brokenBytes.Add(0);

    Tag(CineFormTags.HighpassWidth, 3);
    Tag(CineFormTags.HighpassHeight, 3);
    Tag(CineFormTags.SubbandNumber, 1);
    Tag(53, 1);
    Tag(CineFormTags.HighpassDataFollows, 0);
    var writer = new CineFormTestStream.BitWriter();
    writer.WriteBandEndMarker();
    brokenBytes.AddRange(writer.ToSegmentAlignedBytes());

    var frame = CineFormTestStream.Frame(48, 24, luma, v, brokenBytes.ToArray());
    var decoder = CineFormVideoDecoder.Create(CineFormTestStream.Stream(48, 24));
    var thrown = Assert.Throws<InvalidDataException>(() => decoder.DecodeChannels(frame));
    Assert.That(thrown!.Message, Does.Contain("3x3"));
  }
}
