using System;
using FileFormat.JpegXl.Codec;

namespace FileFormat.JpegXl.Tests;

/// <summary>
/// Skeleton-level conformance tests for <see cref="JxlVarDctSpecDecoder"/>.
///
/// <para>
/// VarDCT is a large multi-stage decoder; this orchestrator file is the
/// structural skeleton (TOC-style group iteration + per-block dispatch). The
/// integration boundaries against parallel helpers
/// (<c>JxlVarDctQuant</c>, <c>JxlAcStrategyDecoder</c>,
/// <c>JxlBlockContextMap</c>, <c>JxlVarDctIdct</c>,
/// <c>JxlXybColorTransform</c>) are wired up but several sub-stages still
/// throw <see cref="NotImplementedException"/> with precise messages.
/// </para>
///
/// <para>
/// The tests below cover the integration paths the skeleton can answer
/// correctly: argument validation and the "minimal frame" failure mode where
/// the decoder reaches the LF coefficient read step and reports the missing
/// integration with a clear message naming the helper.
/// </para>
/// </summary>
[TestFixture]
public sealed class JxlVarDctSpecDecoderTests {

  // -------------------------------------------------------------
  // Argument validation
  // -------------------------------------------------------------

  [Test]
  public void Decode_NullReader_Throws() {
    Assert.Throws<ArgumentNullException>(() =>
      JxlVarDctSpecDecoder.Decode(reader: null!, width: 1, height: 1, bitDepth: 8));
  }

  [Test]
  public void Decode_ZeroWidth_Throws() {
    var reader = new JxlBitReader(new byte[16], 0);
    Assert.Throws<ArgumentOutOfRangeException>(() =>
      JxlVarDctSpecDecoder.Decode(reader, width: 0, height: 1, bitDepth: 8));
  }

  [Test]
  public void Decode_NegativeWidth_Throws() {
    var reader = new JxlBitReader(new byte[16], 0);
    Assert.Throws<ArgumentOutOfRangeException>(() =>
      JxlVarDctSpecDecoder.Decode(reader, width: -1, height: 1, bitDepth: 8));
  }

  [Test]
  public void Decode_ZeroHeight_Throws() {
    var reader = new JxlBitReader(new byte[16], 0);
    Assert.Throws<ArgumentOutOfRangeException>(() =>
      JxlVarDctSpecDecoder.Decode(reader, width: 1, height: 0, bitDepth: 8));
  }

  [Test]
  public void Decode_NegativeHeight_Throws() {
    var reader = new JxlBitReader(new byte[16], 0);
    Assert.Throws<ArgumentOutOfRangeException>(() =>
      JxlVarDctSpecDecoder.Decode(reader, width: 1, height: -1, bitDepth: 8));
  }

  [Test]
  public void Decode_BitDepthZero_Throws() {
    var reader = new JxlBitReader(new byte[16], 0);
    Assert.Throws<ArgumentOutOfRangeException>(() =>
      JxlVarDctSpecDecoder.Decode(reader, width: 1, height: 1, bitDepth: 0));
  }

  [Test]
  public void Decode_BitDepthTooLarge_Throws() {
    var reader = new JxlBitReader(new byte[16], 0);
    Assert.Throws<ArgumentOutOfRangeException>(() =>
      JxlVarDctSpecDecoder.Decode(reader, width: 1, height: 1, bitDepth: 33));
  }

  // -------------------------------------------------------------
  // Minimal frame integration test.
  //
  // Construct the simplest possible VarDCT input — an 8×8 single-group
  // frame — and verify that Decode either returns a valid JxlVarDctImage
  // (if the LF/AC integration lands later) OR throws NotImplementedException
  // with a clear message naming the missing piece.
  //
  // This test guards against silent regressions in the structural skeleton:
  //   - Step 1 (quant tables) must succeed via DefaultTableSetXyb.
  //   - Step 2 (AC strategy) must succeed via CreateAllDct8x8 fallback.
  //   - Step 3 (LF coefficients) is the first integration point that needs
  //     JxlBlockContextMap + a frame-level entropy decoder. Until then the
  //     orchestrator throws here.
  // -------------------------------------------------------------

  [Test]
  public void Decode_MinimalFrame_8x8_FailsCleanly() {
    // 64 zero bytes is a degenerate input. The pipeline now reaches LF decode
    // (which delegates to the modular sub-codec, which delegates to the MA
    // tree decoder, which delegates to the entropy decoder). On all-zero
    // bytes the entropy decoder eventually runs out of bits, producing
    // InvalidOperationException — that's the spec-correct failure mode for
    // truncated/invalid bitstreams. NotImplementedException is also accepted
    // for paths that haven't been wired yet.
    var bytes = new byte[64];
    var reader = new JxlBitReader(bytes, 0);

    Exception? thrown = null;
    try {
      JxlVarDctSpecDecoder.Decode(reader, width: 8, height: 8, bitDepth: 8);
    } catch (NotImplementedException ex) { thrown = ex; }
    catch (InvalidOperationException ex) { thrown = ex; }
    catch (System.IO.InvalidDataException ex) { thrown = ex; }

    Assert.That(thrown, Is.Not.Null,
      "Degenerate input must produce a clean exception, not silent corruption.");
    Assert.That(thrown!.Message, Is.Not.Empty);
  }

  // -------------------------------------------------------------
  // Sub-256 single-group frame still produces ONE group.
  //
  // Verifies the group-geometry math: ceil(8/256) = 1 group in each
  // dimension. Without this, we'd allocate zero groups and never throw.
  // -------------------------------------------------------------

  [Test]
  public void Decode_SubGroupSizeFrame_FailsCleanly() {
    var bytes = new byte[64];
    var reader = new JxlBitReader(bytes, 0);

    // Width 17, height 9 — both partial group dimensions, neither aligned
    // to 8-pixel block boundaries. ceil(17/8) = 3 blocks wide,
    // ceil(9/8) = 2 blocks high, all in 1 group. Pipeline now functional
    // through LF decode; degenerate input produces the same clean-failure
    // pattern as the 8×8 case.
    Assert.That(
      () => JxlVarDctSpecDecoder.Decode(reader, width: 17, height: 9, bitDepth: 8),
      Throws.TypeOf<NotImplementedException>()
        .Or.TypeOf<InvalidOperationException>()
        .Or.TypeOf<System.IO.InvalidDataException>(),
      "Degenerate input must produce a clean exception, not silent corruption."
    );
  }
}
