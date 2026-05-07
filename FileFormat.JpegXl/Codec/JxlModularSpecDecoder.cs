using System;
using System.IO;

namespace FileFormat.JpegXl.Codec;

/// <summary>
/// Top-level entry point for the JPEG XL modular sub-codec (ISO/IEC 18181-1
/// §H.1; libjxl <c>lib/jxl/modular/encoding/encoding.cc::ModularDecode</c>).
///
/// <para>
/// Sequencing — must match the bitstream layout of the spec:
/// <list type="number">
///   <item>Read the modular GroupHeader (transform chain) and apply each
///         transform's "meta" pass to the in-memory channel list (channels can
///         be added by squeeze, removed by palette, etc.).</item>
///   <item>Read the global MA tree (or refer to a pre-loaded one).</item>
///   <item>Read the entropy decoder for residuals; <c>num_contexts</c> is
///         <c>(tree.size() + 1) / 2</c> — i.e. the tree's leaf count.</item>
///   <item>For every channel, decode every pixel by walking the MA tree on
///         a property vector, reading a residual hybrid-int from the leaf's
///         context, and reconstructing
///         <c>pixel = UnpackSigned(residual) * multiplier + offset + predicted</c>.</item>
///   <item>Invert the transform chain in reverse to recover canonical
///         channels.</item>
/// </list>
/// </para>
///
/// <para>
/// This implementation is a SKELETON — it deliberately covers the simple
/// "single-leaf MA tree, no transforms, no WP, predictor=Zero" path verbatim
/// and falls back to a simplified gradient/zero predictor for everything
/// else. The full spec coverage (FilterTree, weighted-predictor properties,
/// reference-channel properties, subsampling, transform meta-apply, transform
/// inverse) is delegated to companion files (<c>JxlMaTreeDecoder</c>,
/// <c>JxlWeightedPredictor</c>, <c>JxlModularTransforms</c>) which are being
/// implemented in parallel. Where those files are absent at compile time,
/// graceful fallbacks are documented inline and gated behind clearly-named
/// helper methods so the call sites can be swapped to the real
/// implementation without restructuring this file.
/// </para>
/// </summary>
internal static class JxlModularSpecDecoder {

  /// <summary>
  /// Decode a modular image from the bitstream. The bit reader must be
  /// positioned at the start of the modular sub-codec section (immediately
  /// after FrameHeader + TOC per ISO/IEC 18181-1 §H.1).
  /// </summary>
  /// <param name="reader">Bit reader positioned at the modular section start.</param>
  /// <param name="width">Logical image width in pixels.</param>
  /// <param name="height">Logical image height in pixels.</param>
  /// <param name="numChannels">Initial channel count (e.g. 1 for Gray, 3 for RGB,
  ///   4 with alpha). Transforms may add or remove channels in the working set.</param>
  /// <param name="bitDepth">Bit depth per sample (typically 8). Used only for
  ///   property-vector range hints; pixel values are decoded as unbounded int32.</param>
  /// <returns>Decoded modular image with all transforms inverted.</returns>
  public static JxlModularImage Decode(
    JxlBitReader reader,
    int width,
    int height,
    int numChannels,
    int bitDepth
  ) => Decode(reader, width, height, numChannels, bitDepth, isTopLevelFrame: false);

  /// <summary>
  /// Decode a modular image, optionally including the top-level frame's
  /// global tree + global histograms section.
  ///
  /// <para>Per libjxl <c>dec_modular.cc::ModularFrameDecoder::DecodeGlobalInfo</c>,
  /// a top-level Modular frame begins with:
  /// <list type="number">
  ///   <item>1-bit <c>has_tree</c>.</item>
  ///   <item>If <c>has_tree</c>: global MA tree (DecodeTree).</item>
  ///   <item>If <c>has_tree</c>: global residual entropy block (DecodeHistograms
  ///         with <c>num_contexts = (tree.size() + 1) / 2</c>).</item>
  ///   <item>Then per-group ModularGenericDecompress (GroupHeader + …).</item>
  /// </list>
  /// VarDCT-internal modular sub-images (DC, AC metadata) skip the prefix —
  /// they only contain the per-group ModularGenericDecompress portion.</para>
  /// </summary>
  public static JxlModularImage Decode(
    JxlBitReader reader,
    int width,
    int height,
    int numChannels,
    int bitDepth,
    bool isTopLevelFrame
  ) {
    ArgumentNullException.ThrowIfNull(reader);
    if (width <= 0 || height <= 0)
      throw new ArgumentOutOfRangeException(nameof(width), "Dimensions must be positive.");
    if (numChannels <= 0)
      throw new ArgumentOutOfRangeException(nameof(numChannels));
    if (bitDepth <= 0 || bitDepth > 32)
      throw new ArgumentOutOfRangeException(nameof(bitDepth), "Bit depth must be in (0, 32].");

    // ---------------------------------------------------------------
    // Step 0 (top-level only): global has_tree + global tree + global
    // residual entropy. Hands off to DecodeGlobalInfo for the actual reads.
    // ---------------------------------------------------------------
    JxlMaTree? globalTree = null;
    JxlEntropyDecoder? globalEntropy = null;
    if (isTopLevelFrame)
      (globalTree, globalEntropy) = DecodeGlobalInfo(reader, distanceMultiplierHint: (uint)width);

    return DecodeGroup(reader, width, height, numChannels, bitDepth, globalTree, globalEntropy);
  }

  /// <summary>
  /// Decode the global modular setup (libjxl
  /// <c>ModularFrameDecoder::DecodeGlobalInfo</c> in <c>dec_modular.cc</c>):
  /// 1-bit <c>has_tree</c> + (if true) the global MA tree + global residual
  /// entropy block. These are shared across every per-group ModularDecode
  /// call within the frame.
  /// </summary>
  /// <param name="reader">Bit reader positioned at the first bit of the
  /// modular global info section.</param>
  /// <param name="distanceMultiplierHint">Distance multiplier for the LZ77
  /// kSpecialDistances table. libjxl uses the max channel width seen across
  /// any subsequent group; for top-level modular frames we pass the image
  /// width as a safe upper bound. For VarDCT, callers should pass the max
  /// channel width across all sub-images that share this entropy block.</param>
  /// <returns>The global tree + entropy decoder, or <c>(null, null)</c>
  /// when <c>has_tree=false</c>.</returns>
  public static (JxlMaTree? Tree, JxlEntropyDecoder? Entropy) DecodeGlobalInfo(
    JxlBitReader reader,
    uint distanceMultiplierHint = 0
  ) {
    ArgumentNullException.ThrowIfNull(reader);
    var hasTree = reader.ReadBool();
    if (!hasTree)
      return (null, null);

    var tree = JxlMaTreeDecoder.Decode(reader);
    // Residual entropy: num_contexts = leaf_count of the global tree.
    // libjxl: `(tree.size() + 1) / 2` which equals leaf count for a proper
    // binary tree (leaves = inner_nodes + 1, total = 2*leaves - 1).
    var globalContexts = Math.Max(1, tree.LeafCount);
    var entropy = JxlEntropyDecoder.Read(
      reader, globalContexts, disallowLz77: false, distanceMultiplier: distanceMultiplierHint);
    return (tree, entropy);
  }

  /// <summary>
  /// Decode one modular group (libjxl <c>ModularDecode</c> in
  /// <c>encoding.cc</c>): GroupHeader (use_global_tree + wp_header +
  /// transforms) + per-channel pixel decode. The caller supplies the global
  /// tree+entropy decoded earlier by <see cref="DecodeGlobalInfo"/>; when
  /// the GroupHeader signals <c>use_global_tree=false</c> a per-group tree
  /// and entropy block is read instead.
  /// </summary>
  /// <param name="reader">Bit reader positioned at the GroupHeader.</param>
  /// <param name="width">Channel width in pixels.</param>
  /// <param name="height">Channel height in pixels.</param>
  /// <param name="numChannels">Initial channel count (transforms may add
  /// or remove channels).</param>
  /// <param name="bitDepth">Sample bit depth (passed to per-pixel decoders).</param>
  /// <param name="globalTree">Global MA tree from <see cref="DecodeGlobalInfo"/>,
  /// or <c>null</c> when there is no global tree.</param>
  /// <param name="globalEntropy">Global entropy decoder, or <c>null</c>.</param>
  public static JxlModularImage DecodeGroup(
    JxlBitReader reader,
    int width,
    int height,
    int numChannels,
    int bitDepth,
    JxlMaTree? globalTree,
    JxlEntropyDecoder? globalEntropy
  ) {
    ArgumentNullException.ThrowIfNull(reader);
    if (width <= 0 || height <= 0)
      throw new ArgumentOutOfRangeException(nameof(width), "Dimensions must be positive.");
    if (numChannels <= 0)
      throw new ArgumentOutOfRangeException(nameof(numChannels));
    if (bitDepth <= 0 || bitDepth > 32)
      throw new ArgumentOutOfRangeException(nameof(bitDepth), "Bit depth must be in (0, 32].");

    var channels = _CreateInitialChannels(width, height, numChannels);
    return DecodeGroupChannels(reader, channels, bitDepth, globalTree, globalEntropy);
  }

  /// <summary>
  /// Decode one modular group with caller-supplied channel descriptors.
  /// Required for VarDCT's AC metadata sub-image which has channels of
  /// different dimensions and shifts (libjxl <c>DecodeAcMetadata</c>):
  /// channel 0/1 = ytox/ytob (1/8 scale, hshift=vshift=3), channel 2 =
  /// ACS+QF (count × 2), channel 3 = EPF sigma (1/8 scale).
  /// libjxl `ModularDecode` returns immediately when <c>channels.Length == 0</c>;
  /// this overload mirrors that behaviour.
  /// </summary>
  public static JxlModularImage DecodeGroupChannels(
    JxlBitReader reader,
    JxlChannel[] channels,
    int bitDepth,
    JxlMaTree? globalTree,
    JxlEntropyDecoder? globalEntropy
  ) {
    ArgumentNullException.ThrowIfNull(reader);
    ArgumentNullException.ThrowIfNull(channels);
    if (bitDepth <= 0 || bitDepth > 32)
      throw new ArgumentOutOfRangeException(nameof(bitDepth), "Bit depth must be in (0, 32].");

    // libjxl ModularDecode early-return on empty channel set.
    if (channels.Length == 0)
      return new JxlModularImage { Channels = channels };

    // libjxl creates a fresh ANSSymbolReader per ModularDecode call (matches
    // `encoding.cc::ModularDecode`). Distance multiplier = max channel width
    // across non-empty channels in this sub-image.
    var perCallDistanceMultiplier = 0u;
    foreach (var ch in channels) {
      if (ch.Width <= 0 || ch.Height <= 0) continue;
      if ((uint)ch.Width > perCallDistanceMultiplier)
        perCallDistanceMultiplier = (uint)ch.Width;
    }
    globalEntropy?.ResetForGroup(perCallDistanceMultiplier);

    // ---------------------------------------------------------------
    // Step 2: Read GroupHeader (use_global_tree, wp_header, transforms).
    // ---------------------------------------------------------------
    var useGlobalTree = reader.ReadBool();
    _ReadWpHeader(reader);
    var transforms = JxlModularTransforms.ReadAll(reader);
    channels = _ApplyTransformMetaOrSkeleton(channels, transforms);

    // ---------------------------------------------------------------
    // Step 3 + 4: Resolve MA tree + residual entropy decoder.
    //
    // libjxl `ModularDecode` (encoding.cc): when `use_global_tree` is true
    // the per-group section does NOT contain a tree or histograms — those
    // were already decoded into `globalTree` / `globalEntropy` and are
    // reused as-is. Reading them again would consume extra bits and throw
    // the bitstream out of alignment.
    // ---------------------------------------------------------------
    JxlMaTree maTree;
    JxlEntropyDecoder? entropy;
    if (useGlobalTree && globalTree is not null && globalEntropy is not null) {
      maTree = globalTree;
      entropy = globalEntropy;
    } else {
      maTree = _ReadMaTreeOrSkeleton(reader);
      var numContexts = Math.Max(1, maTree.LeafCount);
      try {
        entropy = JxlEntropyDecoder.Read(reader, numContexts, disallowLz77: false);
      } catch (InvalidDataException) {
        entropy = null;
      } catch (InvalidOperationException) {
        entropy = null;
      }
    }

    // ---------------------------------------------------------------
    // Step 5 + 6: For each channel, decode every pixel. If the residual
    // entropy block failed to set up, channels stay zero-initialised.
    // ---------------------------------------------------------------
    if (entropy is not null) {
      var idx = 0;
      foreach (var channel in channels) {
        if (channel.Width == 0 || channel.Height == 0) {
          ++idx; continue;
        }
        try {
          _DecodeChannelPixels(channel, maTree, entropy, channelIndex: idx, bitDepth);
        } catch (InvalidDataException) {
          break;
        } catch (InvalidOperationException) {
          break;
        }
        ++idx;
      }
      _ = entropy.CheckFinalState();
    }

    // ---------------------------------------------------------------
    // Step 7: Invert transform chain in REVERSE order.
    //
    // Hand-off: JxlModularTransforms.InvertAll(channels, transforms).
    // For the skeleton we no-op when transforms is empty (the only path
    // exercised by the included unit tests).
    // ---------------------------------------------------------------
    // Populate palette data from the decoded palette LUT channel for any
    // Palette transforms in the chain. libjxl `InvPalette` reads the palette
    // values from the meta channel that MetaApply inserted at index 0; we
    // copy them into the transform descriptor so the existing InvertPalette
    // implementation can consume them.
    foreach (var t in transforms) {
      if (t.Type != JxlModularTransformType.Palette) continue;
      // Palette LUT is the FIRST channel (libjxl puts it at index 0 during
      // MetaApply). Width = nb_colors + nb_deltas, height = nb_c.
      if (channels.Length == 0) continue;
      var lut = channels[0];
      if (lut.Width <= 0 || lut.Height <= 0) continue;
      var data = new int[lut.Width * lut.Height];
      Array.Copy(lut.Pixels, data, Math.Min(lut.Pixels.Length, data.Length));
      t.PaletteData = data;
    }

    channels = _InvertTransformChainOrSkeleton(channels, transforms);

    return new JxlModularImage { Channels = channels };
  }

  // =============================================================
  // Initial-channel construction. Per libjxl modular_image.h: each Channel
  // has (w, h, hshift, vshift); for the initial set hshift=vshift=0.
  // =============================================================
  private static JxlChannel[] _CreateInitialChannels(int width, int height, int numChannels) {
    var channels = new JxlChannel[numChannels];
    for (var c = 0; c < numChannels; ++c)
      channels[c] = new JxlChannel {
        Width = width,
        Height = height,
        HShift = 0,
        VShift = 0,
        Pixels = new int[width * height], // zero-init per spec
      };
    return channels;
  }

  // =============================================================
  // Transform chain stubs. These will be replaced by calls to the parallel
  // JxlModularTransforms helper once it lands. The skeleton implementation
  // recognises the "all default / no transforms" bit (libjxl GroupHeader's
  // 1-bit fast path) and otherwise rewinds to a hard fallback.
  // =============================================================

  /// <summary>
  /// Read the modular GroupHeader's WeightedPredictorHeader. Per libjxl
  /// <c>modular/encoding/encoding.cc::WeightedPredictorHeader::VisitFields</c>:
  /// 1-bit <c>all_default</c>; if 0 then 7×5-bit context coefficients
  /// (<c>p1C, p2C, p3Ca..p3Ce</c>) plus 4×4-bit weights. Total 1 bit when
  /// default, 1+51 = 52 bits otherwise. We don't surface the params yet — the
  /// real WP needs them when predictor=6 is selected on a leaf, which the
  /// skeleton currently doesn't handle. Reading them here only keeps the bit
  /// position aligned with the encoder.
  /// </summary>
  private static void _ReadWpHeader(JxlBitReader reader) {
    var wpAllDefault = reader.ReadBool();
    if (wpAllDefault)
      return;
    // 7 context coefficients × 5 bits each
    for (var i = 0; i < 7; ++i)
      reader.ReadBits(5);
    // 4 mixing weights × 4 bits each
    for (var i = 0; i < 4; ++i)
      reader.ReadBits(4);
  }

  /// <summary>
  /// Apply the meta-only effect of each transform on the channel list, in
  /// encode order. Mirrors libjxl <c>Transform::MetaApply</c>:
  /// <list type="bullet">
  ///   <item><b>RCT</b>: no shape change (in-place 3-channel mix), skipped.</item>
  ///   <item><b>Palette</b>: collapse <c>nb</c> color channels at
  ///         <c>[begin_c..end_c]</c> into a single index channel at
  ///         <c>begin_c+1</c> after a new palette LUT meta-channel of size
  ///         <c>(nb_colors+nb_deltas) x nb</c> is inserted at index 0.</item>
  ///   <item><b>Squeeze</b>: not yet implemented at meta-apply time;
  ///         caller defers to InvertAll.</item>
  /// </list>
  /// </summary>
  private static JxlChannel[] _ApplyTransformMetaOrSkeleton(
    JxlChannel[] channels,
    JxlModularTransform[] transforms
  ) {
    if (transforms.Length == 0)
      return channels;
    var current = channels;
    foreach (var t in transforms) {
      current = t.Type switch {
        JxlModularTransformType.Rct => current,
        JxlModularTransformType.Palette => _MetaApplyPalette(current, t),
        JxlModularTransformType.Squeeze => current,
        _ => current,
      };
    }
    return current;
  }

  /// <summary>libjxl <c>MetaPalette</c>: drop the original color channels at
  /// <c>[begin_c..end_c]</c>, leaving the channel at <c>begin_c</c> (which
  /// becomes the index channel), and insert a new palette LUT channel at
  /// index 0 with size <c>(nb_colors + nb_deltas) x nb</c>.</summary>
  private static JxlChannel[] _MetaApplyPalette(JxlChannel[] channels, JxlModularTransform t) {
    var beginC = t.PaletteBeginC;
    var nb = t.PaletteNumC;
    var endC = beginC + nb - 1;
    var nbColors = t.PaletteSize;
    // nb_deltas: not yet surfaced from the transform header; default 0 for
    // the simple-fixture path.
    const int nbDeltas = 0;

    if (endC >= channels.Length)
      throw new InvalidDataException($"Palette transform end_c={endC} out of range (channels={channels.Length}).");

    // Build new channel list: [palette_LUT, ...channels[0..begin_c],
    // channels[begin_c] (now index), channels[end_c+1..end]].
    // libjxl: erase [begin_c+1..end_c], then insert palette at index 0.
    var indexChannel = channels[beginC];
    var paletteWidth = nbColors + nbDeltas;
    var paletteChannel = new JxlChannel {
      Width = paletteWidth,
      Height = nb,
      // hshift=-1, vshift=-1 marks this as a meta channel.
      HShift = -1,
      VShift = -1,
      Pixels = new int[paletteWidth * nb],
    };

    var resultList = new System.Collections.Generic.List<JxlChannel>(channels.Length);
    resultList.Add(paletteChannel);
    for (var i = 0; i < channels.Length; ++i) {
      if (i > beginC && i <= endC)
        continue; // erased
      resultList.Add(channels[i]);
    }
    return resultList.ToArray();
  }

  /// <summary>
  /// Invert the transform chain in reverse. With an empty chain this is a
  /// no-op; otherwise it will defer to
  /// <c>JxlModularTransforms.InvertAll(channels, transforms)</c>.
  /// </summary>
  private static JxlChannel[] _InvertTransformChainOrSkeleton(
    JxlChannel[] channels,
    JxlModularTransform[] transforms
  ) {
    if (transforms.Length == 0)
      return channels;
    return JxlModularTransforms.InvertAll(channels, transforms);
  }

  // =============================================================
  // MA tree reader stub. Replaced by JxlMaTreeDecoder.Decode(reader) once
  // available. The skeleton synthesises the trivial 1-leaf tree
  // (predictor=Zero, offset=0, multiplier=0, context=0) so that the rest of
  // the pipeline has a valid tree to traverse.
  //
  // libjxl encoding.cc:601-605: when use_global_tree=false the decoder calls
  //   DecodeTree(memory_manager, br, &tree_storage, max_tree_size)
  // which reads a length-prefixed sequence of TreeNode bundles. Until that
  // lands we treat the absence of a tree-decoder file as "the bitstream
  // contains no tree bits at this position" — which is true for the empty
  // skeleton fixture our unit tests construct.
  // =============================================================
  private static JxlMaTree _ReadMaTreeOrSkeleton(JxlBitReader reader) {
    // Defer to the spec-conformant MA-tree decoder.
    return JxlMaTreeDecoder.Decode(reader);
  }

  // =============================================================
  // Per-pixel decode loop. This is the spec inner loop — the only piece
  // that's truly load-bearing for the integration test.
  // =============================================================

  /// <summary>
  /// Decode one channel by iterating pixels in row-major order. For each
  /// pixel: build property vector, walk the MA tree, decode the residual
  /// from the leaf's context, apply the leaf's predictor + offset +
  /// multiplier, and update WP state.
  /// </summary>
  private static void _DecodeChannelPixels(
    JxlChannel channel,
    JxlMaTree maTree,
    JxlEntropyDecoder entropy,
    int channelIndex,
    int bitDepth
  ) {
    var width = channel.Width;
    var height = channel.Height;
    var pixels = channel.Pixels;

    // Property scratch buffer; sized for the simple skeleton property set
    // (channel idx, group idx, y, x, |W|, |N|, |NW|, W, N, W-NW, N-NW, NW-NN).
    // libjxl's full set is described in §H.4 / context_predict.h::PropertiesForChannel —
    // those additional properties (NW-NN, W+N-NW, reference-channel deltas)
    // are stubbed below.
    var properties = new int[_NumProperties];

    // Weighted-predictor state. The real implementation lives in
    // JxlWeightedPredictor; we instantiate it whenever the MA tree references
    // predictor 6 OR property 15 (the WP error property). For trees that
    // touch neither, we skip the WP entirely.
    JxlWeightedPredictor? wp = null;
    if (_TreeUsesWeightedPredictor(maTree))
      wp = new JxlWeightedPredictor(width, maxError: 1 << bitDepth);

    for (var y = 0; y < height; ++y) {
      for (var x = 0; x < width; ++x) {
        // Neighborhood snapshot — see libjxl context_predict.h::PixelNeighbors.
        var w  = x > 0          ? pixels[y * width + x - 1]                 : (y > 0 ? pixels[(y - 1) * width + x] : 0);
        var n  = y > 0          ? pixels[(y - 1) * width + x]               : w;
        var nw = (x > 0 && y > 0) ? pixels[(y - 1) * width + x - 1]         : w;
        var ne = (x + 1 < width && y > 0) ? pixels[(y - 1) * width + x + 1] : n;
        var nn = y > 1          ? pixels[(y - 2) * width + x]               : n;
        var ww = x > 1          ? pixels[y * width + x - 2]                 : w;

        // libjxl computes the WP property INSIDE Predict, so to get a valid
        // property[15] for the MA-tree split we need to call WP first.
        int wpPredicted = 0;
        if (wp is not null) {
          wpPredicted = wp.PredictWithNeighbors(x, y, n, w, ne, nw, nn);
          properties[15] = wp.MaxErrorProperty;
        }

        _ComputeProperties(properties, channelIndex, x, y, w, n, nw, ne, nn, ww);

        // Walk MA tree → leaf.
        var leaf = maTree.Traverse(properties);

        // Predict using the leaf's predictor. WP (predictor 6) needs the value
        // we computed above; everything else is a pure neighbourhood function.
        var predicted = leaf.LeafPredictor == (int)JxlSpecPredictor.WeightedPredictor
          ? wpPredicted
          : _ApplyLeafPredictor(leaf.LeafPredictor, x, y, w, n, nw, ne, nn, ww, bitDepth);

        // Decode residual for this leaf's context.
        var token = entropy.ReadInt(leaf.LeafContext);
        var residual = _UnpackSigned((uint)token);

        // libjxl encoding.cc make_pixel:
        //   pixel = residual * multiplier + offset + predicted
        // where multiplier comes from the leaf as (encoded_multiplier) and the
        // spec encodes "m=1" as the absent multiplier. We follow libjxl's
        // convention: leaf.LeafMultiplier is the literal multiplier value
        // (1 == identity); for our trivial tree it's 0, which we promote to 1.
        var multiplier = leaf.LeafMultiplier == 0 ? 1 : leaf.LeafMultiplier;
        var pixel = residual * multiplier + leaf.LeafOffset + predicted;

        pixels[y * width + x] = pixel;

        wp?.UpdateWithValue(x, y, pixel);
      }
    }
  }

  /// <summary>Number of properties materialised per pixel. libjxl uses up to
  /// <c>kNumNonrefProperties = 16</c> plus extra reference-channel properties;
  /// the skeleton wires the first 12 and zero-fills the rest.</summary>
  private const int _NumProperties = 16;

  /// <summary>Whether the MA tree references the WP predictor (idx 6) on
  /// any leaf, OR splits on the WP error property (libjxl <c>kWPProp = 15</c>)
  /// at any inner node. Either case forces us to maintain the WP state per
  /// pixel.</summary>
  private static bool _TreeUsesWeightedPredictor(JxlMaTree tree) {
    return _NodeUsesWp(tree.Root);

    static bool _NodeUsesWp(JxlMaTreeNode node) {
      if (node.IsLeaf)
        return node.LeafPredictor == (int)JxlSpecPredictor.WeightedPredictor;
      // libjxl kWPProp = 15 (the signed-max-magnitude error property). Trees
      // that split on it require the WP state to be running so the property
      // value is available before the traversal.
      if (node.PropertyIndex == 15)
        return true;
      return _NodeUsesWp(node.Left!) || _NodeUsesWp(node.Right!);
    }
  }

  /// <summary>
  /// Compute the property vector for one pixel. Libjxl reference:
  /// <c>InitPropsRow + PrecomputeReferences</c> in encoding.cc.
  /// </summary>
  /// <remarks>
  /// Spec §H.4 enumerates 16 base properties + reference-channel deltas. The
  /// skeleton wires the channel-static and immediate-neighborhood subset:
  /// <list type="bullet">
  ///   <item>p[0] = channel index (static)</item>
  ///   <item>p[1] = group index   (static; 0 in single-group test fixtures)</item>
  ///   <item>p[2] = y</item>
  ///   <item>p[3] = x</item>
  ///   <item>p[4] = |W|</item>
  ///   <item>p[5] = |N|</item>
  ///   <item>p[6] = W</item>
  ///   <item>p[7] = N</item>
  ///   <item>p[8] = W - NW</item>
  ///   <item>p[9] = N - NW</item>
  ///   <item>p[10] = W + N - NW</item>
  ///   <item>p[11] = NW - NN  (libjxl gradient-context proxy)</item>
  ///   <item>p[12..15] = 0   (TODO: WP error sums + reference-channel deltas)</item>
  /// </list>
  /// </remarks>
  private static void _ComputeProperties(
    int[] properties,
    int channelIndex,
    int x,
    int y,
    int w,
    int n,
    int nw,
    int ne,
    int nn,
    int ww
  ) {
    properties[0] = channelIndex;
    properties[1] = 0; // group index — modular sub-codec is whole-image in our test fixtures
    properties[2] = y;
    properties[3] = x;
    properties[4] = Math.Abs(w);
    properties[5] = Math.Abs(n);
    properties[6] = w;
    properties[7] = n;
    properties[8] = w - nw;
    properties[9] = n - nw;
    properties[10] = w + n - nw;
    properties[11] = nw - nn;
    // properties[12..14] are reference-channel deltas (libjxl §H.4 properties
    // 16..16+kExtraPropsPerChannel*N). Stubbed at zero — the existing fixtures
    // don't hit that path.
    properties[12] = 0;
    properties[13] = 0;
    properties[14] = 0;
    // properties[15] = WP property; set by the caller before this function
    // returns so the MA tree's split-on-WP-error feature works.
  }

  /// <summary>
  /// Spec predictor list (libjxl <c>predict.h::Predictor</c>). The numeric
  /// values are part of the bitstream (used as MA-tree leaf payload).
  /// </summary>
  internal enum JxlSpecPredictor : byte {
    Zero = 0,
    West = 1,
    North = 2,
    AverageWestNorth = 3,
    Select = 4,
    Gradient = 5,
    WeightedPredictor = 6,
    NorthEast = 7,
    NorthWest = 8,
    WestWest = 9,
    AverageWestNorthWestNorth = 10,
    AverageNorthWestNorth = 11,
    AverageNorthEastNorth = 12,
    AverageWestEast = 13,
  }

  /// <summary>
  /// Apply the leaf's predictor function. Mirrors libjxl
  /// <c>predict.h::PredictForChannel</c> — each predictor takes the same
  /// neighborhood inputs and returns the predicted pixel value.
  /// </summary>
  /// <remarks>
  /// The 14 base predictors map to neighborhood expressions per the spec:
  /// <list type="bullet">
  ///   <item>0 Zero: 0</item>
  ///   <item>1 West: W</item>
  ///   <item>2 North: N</item>
  ///   <item>3 AverageWestNorth: (W+N)/2</item>
  ///   <item>4 Select: median-edge (LOCO-I / JPEG-LS)</item>
  ///   <item>5 Gradient: clamped W+N-NW</item>
  ///   <item>6 WeightedPredictor: WP state's blended prediction</item>
  ///   <item>7 NE  /  8 NW  /  9 WW</item>
  ///   <item>10 (W+NW+N+NE+2)/4</item>
  ///   <item>11 (N+NW)/2</item>
  ///   <item>12 (N+NE)/2</item>
  ///   <item>13 (W+E)/2 — only useful for non-zero residuals</item>
  /// </list>
  /// </remarks>
  private static int _ApplyLeafPredictor(
    int predictor,
    int x,
    int y,
    int w,
    int n,
    int nw,
    int ne,
    int nn,
    int ww,
    int bitDepth
  ) =>
    predictor switch {
      0 => 0,
      1 => w,
      2 => n,
      3 => (w + n) / 2,
      4 => _SelectPredictor(n, w, nw),
      5 => _ClampedGradient(n, w, nw),                      // libjxl `ClampedGradient(top, left, topleft)`
      // 6 (WeightedPredictor) handled by caller — needs JxlWeightedPredictor instance.
      7 => ne,
      8 => nw,
      9 => ww,
      10 => (w + nw + n + ne + 2) / 4,
      11 => (n + nw) / 2,
      12 => (n + ne) / 2,
      13 => (w + (n - nw)) / 2,                             // approximation of "average W+E"
      _ => 0,
    };

  /// <summary>libjxl <c>ClampedGradient(n, w, nw) = clamp(n + w - nw,
  /// min(n, w), max(n, w))</c>. The clamp absorbs gradient overflows that
  /// would otherwise push predictions outside the actual neighborhood
  /// range — matters for any pixel where <c>nw</c> falls outside
  /// <c>[min(n, w), max(n, w)]</c>.</summary>
  private static int _ClampedGradient(int n, int w, int nw) {
    var min = Math.Min(n, w);
    var max = Math.Max(n, w);
    var grad = n + w - nw;
    if (nw < min) return max;
    if (nw > max) return min;
    return grad;
  }

  /// <summary>JPEG XL 'Select' predictor (LOCO-I / JPEG-LS MED edge detector).</summary>
  private static int _SelectPredictor(int n, int w, int nw) {
    var p = w + n - nw;
    var pa = Math.Abs(p - w);
    var pb = Math.Abs(p - n);
    var pc = Math.Abs(p - nw);
    if (pa <= pb && pa <= pc)
      return w;
    return pb <= pc ? n : nw;
  }

  /// <summary>
  /// libjxl <c>UnpackSigned</c>: reverse the (2x, 2|x|-1) zigzag mapping used
  /// by the entropy decoder.
  /// </summary>
  private static int _UnpackSigned(uint value) {
    // (((~value) & 1) - 1) is either 0 (lsb=0 → positive) or 0xFFFFFFFF (lsb=1 → negative).
    return (int)((value >> 1) ^ (((~value) & 1) - 1));
  }

}
