using System;
using FileFormat.JpegXl.Codec;

namespace FileFormat.JpegXl.Tests;

/// <summary>
/// Tests for <see cref="JxlPatches"/> — the patch dictionary reader/applier
/// (ISO/IEC 18181-1 §G.11 / libjxl <c>lib/jxl/dec_patch_dictionary.cc</c>).
///
/// <para>First-wave scope:
/// <list type="bullet">
///   <item><see cref="JxlPatches.ReadDictionary"/> with <c>has_patches=0</c>
///     returns null without further bit consumption.</item>
///   <item><see cref="JxlPatches.ReadDictionary"/> with <c>has_patches=1</c>
///     throws <see cref="NotImplementedException"/> identifying the missing
///     recursive-entropy path.</item>
///   <item><see cref="JxlPatches.Apply"/> with an empty
///     <see cref="PatchDictionary"/> is a no-op — input channels and
///     reference frame are untouched.</item>
///   <item><see cref="JxlPatches.Apply"/> with a non-empty dictionary
///     correctly performs the per-channel blend, including out-of-bounds
///     clipping and null-reference-frame handling.</item>
/// </list></para>
/// </summary>
[TestFixture]
internal sealed class JxlPatchesTests {

  // ============================================================
  // ReadHasPatchesFlag — single-bit gate
  // ============================================================

  /// <summary>The <c>has_patches</c> flag is the first bit on the wire.
  /// A 0 byte returns false; a 1 byte returns true.</summary>
  [Test]
  public void ReadHasPatchesFlag_ZeroBit_ReturnsFalse() {
    var reader = new JxlBitReader([0x00, 0x00, 0x00, 0x00], 0);
    Assert.That(JxlPatches.ReadHasPatchesFlag(reader), Is.False);
  }

  [Test]
  public void ReadHasPatchesFlag_OneBit_ReturnsTrue() {
    // LSB-first bit reader: byte 0x01 → first bit on the wire is 1.
    var reader = new JxlBitReader([0x01, 0x00, 0x00, 0x00], 0);
    Assert.That(JxlPatches.ReadHasPatchesFlag(reader), Is.True);
  }

  [Test]
  public void ReadHasPatchesFlag_NullReader_Throws() {
    Assert.Throws<ArgumentNullException>(
      () => JxlPatches.ReadHasPatchesFlag(null!));
  }

  // ============================================================
  // ReadDictionary — gate behaviour
  // ============================================================

  /// <summary>
  /// When the bitstream's <c>has_patches</c> flag is 0, ReadDictionary
  /// returns null and consumes exactly one bit. This is the "patches
  /// disabled" common case and must never throw.
  /// </summary>
  [Test]
  public void ReadDictionary_HasPatchesZero_ReturnsNull() {
    var bytes = new byte[16];
    var reader = new JxlBitReader(bytes, 0);
    var entropy = JxlEntropyDecoder.CreateSimple(reader, numContexts: 1, maxSymbol: 0);

    var result = JxlPatches.ReadDictionary(reader, entropy);

    Assert.Multiple(() => {
      Assert.That(result, Is.Null);
      Assert.That(reader.BitsRead, Is.EqualTo(1L),
        "Exactly one bit (the has_patches flag) should have been consumed.");
    });
  }

  /// <summary>
  /// When <c>has_patches</c> is 1, the first-wave implementation throws
  /// NotImplementedException naming the missing recursive-entropy work.
  /// The exception message must be load-bearing for downstream debugging.
  /// </summary>
  [Test]
  public void ReadDictionary_HasPatchesOne_ThrowsNotImplemented() {
    var reader = new JxlBitReader([0x01, 0x00, 0x00, 0x00], 0);
    var entropy = JxlEntropyDecoder.CreateSimple(reader, numContexts: 1, maxSymbol: 0);

    var ex = Assert.Throws<NotImplementedException>(
      () => JxlPatches.ReadDictionary(reader, entropy));
    Assert.That(ex!.Message, Does.Contain("kNumPatchDictionaryContexts").IgnoreCase
      .Or.Contain("patch dictionary").IgnoreCase);
  }

  /// <summary>Null reader is rejected.</summary>
  [Test]
  public void ReadDictionary_NullReader_Throws() {
    var reader = new JxlBitReader(new byte[16], 0);
    var entropy = JxlEntropyDecoder.CreateSimple(reader, numContexts: 1, maxSymbol: 0);
    Assert.Throws<ArgumentNullException>(
      () => JxlPatches.ReadDictionary(null!, entropy));
  }

  /// <summary>Null entropy decoder is rejected.</summary>
  [Test]
  public void ReadDictionary_NullEntropy_Throws() {
    var reader = new JxlBitReader(new byte[16], 0);
    Assert.Throws<ArgumentNullException>(
      () => JxlPatches.ReadDictionary(reader, null!));
  }

  // ============================================================
  // Apply — empty dictionary is a no-op
  // ============================================================

  /// <summary>
  /// Applying a default-constructed (empty) <see cref="PatchDictionary"/>
  /// must not modify the input image planes. This is the common path on
  /// every patches-disabled VarDCT frame.
  /// </summary>
  [Test]
  public void Apply_EmptyDictionary_DoesNotModifyChannels() {
    const int width = 4;
    const int height = 4;
    var channels = new float[3][];
    for (var c = 0; c < 3; ++c) {
      channels[c] = new float[width * height];
      for (var i = 0; i < width * height; ++i)
        channels[c][i] = 0.5f + 0.1f * c + 0.01f * i;
    }
    // Snapshot for comparison.
    var snapshots = new float[3][];
    for (var c = 0; c < 3; ++c)
      snapshots[c] = (float[])channels[c].Clone();

    var dictionary = new PatchDictionary(); // Patches = []

    JxlPatches.Apply(channels, width, height, dictionary, referenceFrame: null!);

    Assert.Multiple(() => {
      for (var c = 0; c < 3; ++c)
        for (var i = 0; i < width * height; ++i)
          Assert.That(channels[c][i], Is.EqualTo(snapshots[c][i]),
            $"Channel {c} index {i} should be untouched.");
    });
  }

  // ============================================================
  // Apply — argument validation
  // ============================================================

  [Test]
  public void Apply_NullChannels_Throws() {
    Assert.Throws<ArgumentNullException>(
      () => JxlPatches.Apply(null!, 4, 4, new PatchDictionary(), referenceFrame: null!));
  }

  [Test]
  public void Apply_NullDictionary_Throws() {
    var channels = new[] { new float[16], new float[16], new float[16] };
    Assert.Throws<ArgumentNullException>(
      () => JxlPatches.Apply(channels, 4, 4, null!, referenceFrame: null!));
  }

  [Test]
  public void Apply_FewerThanThreeChannels_Throws() {
    var channels = new[] { new float[16], new float[16] }; // only 2
    Assert.Throws<ArgumentException>(
      () => JxlPatches.Apply(channels, 4, 4, new PatchDictionary(), referenceFrame: null!));
  }

  [Test]
  public void Apply_NegativeWidth_Throws() {
    var channels = new[] { new float[16], new float[16], new float[16] };
    Assert.Throws<ArgumentOutOfRangeException>(
      () => JxlPatches.Apply(channels, -1, 4, new PatchDictionary(), referenceFrame: null!));
  }

  [Test]
  public void Apply_NegativeHeight_Throws() {
    var channels = new[] { new float[16], new float[16], new float[16] };
    Assert.Throws<ArgumentOutOfRangeException>(
      () => JxlPatches.Apply(channels, 4, -1, new PatchDictionary(), referenceFrame: null!));
  }

  [Test]
  public void Apply_ChannelTooSmall_Throws() {
    // Width*Height = 16 but channel 2 only has 8 floats.
    var channels = new[] { new float[16], new float[16], new float[8] };
    Assert.Throws<ArgumentException>(
      () => JxlPatches.Apply(channels, 4, 4, new PatchDictionary(), referenceFrame: null!));
  }

  // ============================================================
  // Apply — blend semantics with a non-empty dictionary
  // ============================================================

  /// <summary>
  /// A single Replace-mode patch copies the source rectangle verbatim onto
  /// the destination. Verify the per-channel walk and the row-major index
  /// math both match the spec.
  /// </summary>
  [Test]
  public void Apply_ReplaceMode_CopiesSourceRectangle() {
    const int width = 4;
    const int height = 4;
    var channels = new float[3][];
    for (var c = 0; c < 3; ++c)
      channels[c] = new float[width * height]; // all zero

    var refFrame = new float[3][];
    for (var c = 0; c < 3; ++c) {
      refFrame[c] = new float[width * height];
      for (var i = 0; i < width * height; ++i)
        refFrame[c][i] = 1.0f + c;
    }

    var dictionary = new PatchDictionary {
      Patches = [
        new PatchEntry {
          RefIdx = 0,
          X0 = 1, Y0 = 1,
          Width = 2, Height = 2,
          Positions = [
            new PatchPosition {
              X = 0, Y = 0,
              BlendModeX = PatchBlendMode.Replace,
              BlendModeY = PatchBlendMode.Replace,
              BlendModeB = PatchBlendMode.Replace,
            },
          ],
        },
      ],
    };

    JxlPatches.Apply(channels, width, height, dictionary, refFrame);

    // The 2x2 region at dst (0,0) should now hold refFrame values from src
    // rectangle (1,1)-(2,2). Channel c has constant value (1+c) so the
    // copy result is just (1+c) at the four target cells.
    Assert.Multiple(() => {
      for (var c = 0; c < 3; ++c) {
        Assert.That(channels[c][0 * width + 0], Is.EqualTo(1.0f + c).Within(1e-6),
          $"Channel {c} (0,0) should be replaced.");
        Assert.That(channels[c][0 * width + 1], Is.EqualTo(1.0f + c).Within(1e-6),
          $"Channel {c} (1,0) should be replaced.");
        Assert.That(channels[c][1 * width + 0], Is.EqualTo(1.0f + c).Within(1e-6),
          $"Channel {c} (0,1) should be replaced.");
        Assert.That(channels[c][1 * width + 1], Is.EqualTo(1.0f + c).Within(1e-6),
          $"Channel {c} (1,1) should be replaced.");
        // (2..3, 2..3) is outside the 2x2 patch — should still be zero.
        Assert.That(channels[c][2 * width + 2], Is.EqualTo(0f),
          $"Channel {c} (2,2) should be untouched (outside patch area).");
      }
    });
  }

  /// <summary>
  /// Add-mode patch sums source onto destination. Confirms the blend-mode
  /// switch dispatches Add correctly.
  /// </summary>
  [Test]
  public void Apply_AddMode_AccumulatesSource() {
    const int width = 2;
    const int height = 2;
    var channels = new[] {
      new[] { 0.1f, 0.2f, 0.3f, 0.4f },
      new[] { 0.5f, 0.6f, 0.7f, 0.8f },
      new[] { 0.9f, 1.0f, 1.1f, 1.2f },
    };
    var refFrame = new[] {
      new[] { 1f, 1f, 1f, 1f },
      new[] { 2f, 2f, 2f, 2f },
      new[] { 3f, 3f, 3f, 3f },
    };
    var dictionary = new PatchDictionary {
      Patches = [
        new PatchEntry {
          RefIdx = 0,
          X0 = 0, Y0 = 0,
          Width = 2, Height = 2,
          Positions = [
            new PatchPosition {
              X = 0, Y = 0,
              BlendModeX = PatchBlendMode.Add,
              BlendModeY = PatchBlendMode.Add,
              BlendModeB = PatchBlendMode.Add,
            },
          ],
        },
      ],
    };

    JxlPatches.Apply(channels, width, height, dictionary, refFrame);

    Assert.Multiple(() => {
      Assert.That(channels[0][0], Is.EqualTo(1.1f).Within(1e-6));
      Assert.That(channels[1][3], Is.EqualTo(2.8f).Within(1e-6));
      Assert.That(channels[2][2], Is.EqualTo(4.1f).Within(1e-6));
    });
  }

  /// <summary>
  /// None blend mode skips the channel entirely. Mixed blend modes (None
  /// for channel X, Replace for channel Y, Add for channel B) must be
  /// dispatched independently per channel.
  /// </summary>
  [Test]
  public void Apply_PerChannelBlendModesAreIndependent() {
    const int width = 1;
    const int height = 1;
    var channels = new[] {
      new[] { 7f },  // X
      new[] { 8f },  // Y
      new[] { 9f },  // B
    };
    var refFrame = new[] {
      new[] { 100f },
      new[] { 200f },
      new[] { 300f },
    };
    var dictionary = new PatchDictionary {
      Patches = [
        new PatchEntry {
          RefIdx = 0,
          X0 = 0, Y0 = 0,
          Width = 1, Height = 1,
          Positions = [
            new PatchPosition {
              X = 0, Y = 0,
              BlendModeX = PatchBlendMode.None,
              BlendModeY = PatchBlendMode.Replace,
              BlendModeB = PatchBlendMode.Add,
            },
          ],
        },
      ],
    };

    JxlPatches.Apply(channels, width, height, dictionary, refFrame);

    Assert.Multiple(() => {
      Assert.That(channels[0][0], Is.EqualTo(7f),       "Channel X (None) should be untouched.");
      Assert.That(channels[1][0], Is.EqualTo(200f),     "Channel Y (Replace) should equal source.");
      Assert.That(channels[2][0], Is.EqualTo(309f),     "Channel B (Add) should be original + source.");
    });
  }

  /// <summary>
  /// Null reference frame is a legal no-op-source (treat as zero) — libjxl
  /// allows this for unmaterialised reference slots in early decode
  /// stages. Replace-from-null must zero out the destination.
  /// </summary>
  [Test]
  public void Apply_NullReferenceFrame_ZeroesReplaceTargets() {
    const int width = 2;
    const int height = 2;
    var channels = new[] {
      new[] { 1f, 2f, 3f, 4f },
      new[] { 5f, 6f, 7f, 8f },
      new[] { 9f, 10f, 11f, 12f },
    };
    var dictionary = new PatchDictionary {
      Patches = [
        new PatchEntry {
          RefIdx = 0,
          X0 = 0, Y0 = 0,
          Width = 1, Height = 1,
          Positions = [
            new PatchPosition {
              X = 0, Y = 0,
              BlendModeX = PatchBlendMode.Replace,
              BlendModeY = PatchBlendMode.Replace,
              BlendModeB = PatchBlendMode.Replace,
            },
          ],
        },
      ],
    };

    JxlPatches.Apply(channels, width, height, dictionary, referenceFrame: null!);

    Assert.Multiple(() => {
      Assert.That(channels[0][0], Is.EqualTo(0f), "X (0,0) should be zeroed (null ref).");
      Assert.That(channels[1][0], Is.EqualTo(0f), "Y (0,0) should be zeroed (null ref).");
      Assert.That(channels[2][0], Is.EqualTo(0f), "B (0,0) should be zeroed (null ref).");
      // Everything else untouched.
      Assert.That(channels[0][1], Is.EqualTo(2f));
      Assert.That(channels[1][3], Is.EqualTo(8f));
    });
  }

  /// <summary>
  /// Patches whose target rectangle extends outside the destination image
  /// must clip silently — libjxl's <c>AddOneRow</c> performs the same
  /// per-row clipping. Verify we don't crash and that the in-bounds part
  /// is still applied.
  /// </summary>
  [Test]
  public void Apply_TargetOutOfBounds_ClipsSilently() {
    const int width = 2;
    const int height = 2;
    var channels = new[] {
      new[] { 0f, 0f, 0f, 0f },
      new[] { 0f, 0f, 0f, 0f },
      new[] { 0f, 0f, 0f, 0f },
    };
    var refFrame = new[] {
      new[] { 1f, 2f, 3f, 4f, 5f, 6f, 7f, 8f, 9f },
      new[] { 1f, 2f, 3f, 4f, 5f, 6f, 7f, 8f, 9f },
      new[] { 1f, 2f, 3f, 4f, 5f, 6f, 7f, 8f, 9f },
    };
    // Reference frame is logically 3x3 but we still pass width=2,height=2
    // for the destination. The patch wants a 4x4 source starting at (0,0)
    // pasted at destination (1,1) — clipped, only (1,1) of the destination
    // can receive a pixel (since the patch width spills past width=2).
    var dictionary = new PatchDictionary {
      Patches = [
        new PatchEntry {
          RefIdx = 0,
          X0 = 0, Y0 = 0,
          Width = 4, Height = 4,
          Positions = [
            new PatchPosition {
              X = 1, Y = 1,
              BlendModeX = PatchBlendMode.Replace,
              BlendModeY = PatchBlendMode.Replace,
              BlendModeB = PatchBlendMode.Replace,
            },
          ],
        },
      ],
    };

    Assert.DoesNotThrow(() => JxlPatches.Apply(channels, width, height, dictionary, refFrame));
    // The single in-bounds destination cell (1,1) receives source pixel
    // (0,0) of the reference-frame plane, which is ref[0]=1f.
    Assert.That(channels[0][1 * width + 1], Is.EqualTo(1f).Within(1e-6),
      "Destination (1,1) should receive the source rectangle's (0,0) value.");
    // Out-of-bounds cells of the destination weren't even attempted; the
    // four destination cells include (0,0), (1,0), (0,1) which weren't
    // touched (the patch starts at dst (1,1)). They stay zero.
    Assert.That(channels[0][0], Is.EqualTo(0f));
    Assert.That(channels[0][1], Is.EqualTo(0f));
    Assert.That(channels[0][2], Is.EqualTo(0f));
  }

  /// <summary>
  /// Clamp flag clamps blend output to [0, 1]. Verify with an Add that
  /// would otherwise overflow.
  /// </summary>
  [Test]
  public void Apply_ClampFlag_ClampsToUnitRange() {
    const int width = 1;
    const int height = 1;
    var channels = new[] { new[] { 0.7f }, new[] { 0.7f }, new[] { 0.7f } };
    var refFrame = new[] { new[] { 0.6f }, new[] { 0.6f }, new[] { 0.6f } };
    var dictionary = new PatchDictionary {
      Patches = [
        new PatchEntry {
          RefIdx = 0,
          X0 = 0, Y0 = 0,
          Width = 1, Height = 1,
          Positions = [
            new PatchPosition {
              X = 0, Y = 0,
              BlendModeX = PatchBlendMode.Add,
              BlendModeY = PatchBlendMode.Add,
              BlendModeB = PatchBlendMode.Add,
              Clamp = [true, true, true],
            },
          ],
        },
      ],
    };

    JxlPatches.Apply(channels, width, height, dictionary, refFrame);

    Assert.Multiple(() => {
      Assert.That(channels[0][0], Is.EqualTo(1f).Within(1e-6),
        "0.7 + 0.6 = 1.3 should clamp down to 1.0.");
      Assert.That(channels[1][0], Is.EqualTo(1f).Within(1e-6));
      Assert.That(channels[2][0], Is.EqualTo(1f).Within(1e-6));
    });
  }

  /// <summary>
  /// Multiple positions within a single patch are applied in order. After
  /// two Replace positions copying different source areas, both target
  /// cells should reflect their own source.
  /// </summary>
  [Test]
  public void Apply_MultiplePositions_AppliedInOrder() {
    const int width = 4;
    const int height = 1;
    var channels = new[] {
      new[] { 0f, 0f, 0f, 0f },
      new[] { 0f, 0f, 0f, 0f },
      new[] { 0f, 0f, 0f, 0f },
    };
    var refFrame = new[] {
      new[] { 11f, 22f, 33f, 44f },
      new[] { 11f, 22f, 33f, 44f },
      new[] { 11f, 22f, 33f, 44f },
    };
    var dictionary = new PatchDictionary {
      Patches = [
        new PatchEntry {
          RefIdx = 0,
          X0 = 0, Y0 = 0,
          Width = 1, Height = 1,
          Positions = [
            new PatchPosition {
              X = 1, Y = 0,
              BlendModeX = PatchBlendMode.Replace,
              BlendModeY = PatchBlendMode.Replace,
              BlendModeB = PatchBlendMode.Replace,
            },
            new PatchPosition {
              X = 3, Y = 0,
              BlendModeX = PatchBlendMode.Replace,
              BlendModeY = PatchBlendMode.Replace,
              BlendModeB = PatchBlendMode.Replace,
            },
          ],
        },
      ],
    };

    JxlPatches.Apply(channels, width, height, dictionary, refFrame);

    Assert.Multiple(() => {
      Assert.That(channels[0][0], Is.EqualTo(0f),  "(0,0) untouched.");
      Assert.That(channels[0][1], Is.EqualTo(11f), "(1,0) replaced by ref(0,0)=11.");
      Assert.That(channels[0][2], Is.EqualTo(0f),  "(2,0) untouched.");
      Assert.That(channels[0][3], Is.EqualTo(11f), "(3,0) replaced by ref(0,0)=11.");
    });
  }
}
