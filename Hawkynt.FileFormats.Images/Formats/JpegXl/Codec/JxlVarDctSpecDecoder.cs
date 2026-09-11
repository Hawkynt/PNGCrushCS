using System;

namespace FileFormat.JpegXl.Codec;

// =====================================================================================
// Top-level VarDCT decoder orchestrator (ISO/IEC 18181-1 §G.1; libjxl
// `lib/jxl/dec_frame.cc::DecodeFrame` + `lib/jxl/dec_group.cc::DecodeGroupImpl`).
// =====================================================================================

/// <summary>
/// Top-level orchestrator for a VarDCT frame. The bit reader must be
/// positioned immediately after the FrameHeader (see
/// <see cref="JxlSpecFrameHeader"/>). Returns the decoded image as 3 XYB
/// channel float planes; callers that need sRGB should run
/// <c>JxlXybColorTransform</c> on the result.
/// </summary>
internal static class JxlVarDctSpecDecoder {

  /// <summary>The frame flag that asks for the low frequencies to be left as
  /// they are (libjxl <c>kSkipAdaptiveDCSmoothing</c>).</summary>
  private const ulong _kSkipAdaptiveDcSmoothing = 0x80;

  /// <summary>Default VarDCT group size.</summary>
  private const int _DefaultGroupSizeLog2 = 8;

  private const int _BlockDim = 8;
  private const int _NumXybChannels = 3;
  private const int _QuantTableCount = 17;
  private const int _MaxPasses = 11;

  /// <summary>Decode a VarDCT frame from the bitstream.</summary>
  public static JxlVarDctImage Decode(
    JxlBitReader reader,
    int width,
    int height,
    int bitDepth,
    GaborishParams? gaborishParams = null,
    EpfParams? epfParams = null,
    float[]? dcQuant = null,
    uint xQmScale = 3,
    uint bQmScale = 2,
    byte[]? codestream = null,
    JxlFrameToc? toc = null,
    int frameBody = 0,
    int groupSizeOverride = 0,
    int numDcGroups = 1,
    int numExtraChannels = 0,
    ulong frameFlags = 0,
    int numPasses = 1,
    uint[]? passShifts = null
  ) {
    ArgumentNullException.ThrowIfNull(reader);
    if (width <= 0)
      throw new ArgumentOutOfRangeException(nameof(width), "Width must be positive.");
    if (height <= 0)
      throw new ArgumentOutOfRangeException(nameof(height), "Height must be positive.");
    if (bitDepth <= 0 || bitDepth > 32)
      throw new ArgumentOutOfRangeException(nameof(bitDepth), "Bit depth must be in (0, 32].");
    if (numPasses is < 1 or > _MaxPasses)
      throw new ArgumentOutOfRangeException(nameof(numPasses), $"JPEG XL allows 1..{_MaxPasses} AC passes.");

    passShifts ??= new uint[numPasses];
    if (passShifts.Length != numPasses)
      throw new ArgumentException(
        $"Pass-shift array has {passShifts.Length} entries but the frame has {numPasses} passes.",
        nameof(passShifts));
    for (var pass = 0; pass < passShifts.Length; ++pass)
      if (passShifts[pass] > 3)
        throw new ArgumentOutOfRangeException(nameof(passShifts), "JPEG XL pass shifts are two-bit values.");
    if (passShifts[^1] != 0)
      throw new InvalidDataException("The final JPEG XL AC pass must have shift zero.");

    var groupSize = groupSizeOverride > 0 ? groupSizeOverride : 1 << _DefaultGroupSizeLog2;
    var numGroupsW = (width + groupSize - 1) / groupSize;
    var numGroupsH = (height + groupSize - 1) / groupSize;
    var numGroups = checked(numGroupsW * numGroupsH);

    // A frame in more than one section is addressed through its TOC. AC group
    // sections are pass-major: pass 0 group 0..N-1, then pass 1, and so on.
    var sections = toc?.SectionSizes.Length ?? 1;
    var seeks = codestream is not null && toc is { Permuted: false } && sections > 1;

    JxlBitReader SectionReader(int index) {
      if (!seeks || index >= sections)
        return reader;

      var at = checked(frameBody + toc!.SectionOffsets[index]);
      if (at < 0 || at > codestream!.Length)
        throw new InvalidDataException($"This frame's section {index} sits past the end of the codestream.");

      return new JxlBitReader(codestream, at);
    }

    var quantParams = JxlFrameQuantizer.ReadQuantizerParams(reader);
    var dcQuantUsed = dcQuant ?? JxlFrameQuantizer.DefaultDcQuant;
    var mulDc = new float[_NumXybChannels];
    for (var c = 0; c < _NumXybChannels; ++c)
      mulDc[c] = quantParams.InvQuantDc * dcQuantUsed[c];

    var blockCtxMap = JxlBlockContextMap.Decode(reader);
    var dcCorrelation = JxlColorCorrelationMap.DecodeDc(reader);

    var dcWidth = (width + _BlockDim - 1) / _BlockDim;
    var dcHeight = (height + _BlockDim - 1) / _BlockDim;
    var (modularGlobalTree, modularGlobalEntropy) = JxlModularSpecDecoder.DecodeGlobalInfo(
      reader, distanceMultiplierHint: (uint)Math.Max(dcWidth, _NumXybChannels));

    // VarDCT extra channels use the modular side-stream. A single-pass frame can
    // carry large planes group-by-group (PR #403). Progressive extra-channel
    // partitioning additionally depends on the pass shift interval; until the
    // modular decoder exposes min/max shift filtering, refuse that combination
    // instead of consuming the wrong stream.
    var extraChannels = Array.Empty<JxlChannel>();
    var extraChannelTransforms = Array.Empty<JxlModularTransform>();
    var firstGroupedExtraChannel = 0;
    if (numExtraChannels > 0) {
      extraChannels = new JxlChannel[numExtraChannels];
      for (var i = 0; i < numExtraChannels; ++i)
        extraChannels[i] = new JxlChannel {
          Width = width,
          Height = height,
          HShift = 0,
          VShift = 0,
          Pixels = new int[checked(width * height)],
        };

      var stream = JxlModularSpecDecoder.DecodeStream(
        reader, extraChannels, bitDepth, modularGlobalTree, modularGlobalEntropy,
        new JxlModularStreamOptions {
          MaxChannelSize = groupSize,
          StreamId = 0,
          UndoTransforms = false,
        });

      extraChannels = stream.Image.Channels;
      extraChannelTransforms = stream.Transforms;

      var metaChannels = 0;
      foreach (var transform in extraChannelTransforms)
        if (transform.Type == JxlModularTransformType.Palette)
          ++metaChannels;

      firstGroupedExtraChannel = metaChannels;
      while (firstGroupedExtraChannel < extraChannels.Length
             && extraChannels[firstGroupedExtraChannel].Width <= groupSize
             && extraChannels[firstGroupedExtraChannel].Height <= groupSize)
        ++firstGroupedExtraChannel;

      if (numPasses > 1 && firstGroupedExtraChannel < extraChannels.Length)
        throw new NotSupportedException(
          "Progressive VarDCT with group-carried extra channels needs pass-shift filtering in the modular side-stream decoder.");
    }

    // ProcessDCGroup for VarDCT.
    var dcGroupReader = SectionReader(1);
    var extraPrecision = (int)dcGroupReader.ReadBits(2);
    var extraPrecisionMul = 1f / (1 << extraPrecision);

    const int dcGroupIndex = 0;
    var numDcGroupsForStreams = Math.Max(1, numDcGroups);
    var dcStreamId = 1 + dcGroupIndex;
    var acMetadataStreamId = 1 + 2 * numDcGroupsForStreams + dcGroupIndex;

    var dcImage = JxlModularSpecDecoder.DecodeGroup(
      dcGroupReader,
      width: dcWidth,
      height: dcHeight,
      numChannels: _NumXybChannels,
      bitDepth: bitDepth,
      globalTree: modularGlobalTree,
      globalEntropy: modularGlobalEntropy,
      streamId: dcStreamId);

    var dcGroupBlocksX = (width + _BlockDim - 1) / _BlockDim;
    var dcGroupBlocksY = (height + _BlockDim - 1) / _BlockDim;
    var upperBound = dcGroupBlocksX * dcGroupBlocksY;
    var countBits = upperBound <= 1 ? 0 : (int)Math.Ceiling(Math.Log2(upperBound));
    var count = (int)dcGroupReader.ReadBits(countBits) + 1;
    var crX = (dcGroupBlocksX + 7) >> 3;
    var crY = (dcGroupBlocksY + 7) >> 3;
    var acMetaChannels = new JxlChannel[] {
      new() { Width = crX, Height = crY, HShift = 3, VShift = 3, Pixels = new int[crX * crY] },
      new() { Width = crX, Height = crY, HShift = 3, VShift = 3, Pixels = new int[crX * crY] },
      new() { Width = count, Height = 2, HShift = 0, VShift = 0, Pixels = new int[count * 2] },
      new() { Width = dcGroupBlocksX, Height = dcGroupBlocksY, HShift = 0, VShift = 0, Pixels = new int[dcGroupBlocksX * dcGroupBlocksY] },
    };
    var acMetadata = JxlModularSpecDecoder.DecodeGroupChannels(
      dcGroupReader, acMetaChannels, bitDepth, modularGlobalTree, modularGlobalEntropy, acMetadataStreamId);

    // Build per-block AC strategy, quant field, and transform-origin planes.
    var strategyPlane = new JxlAcStrategyType[dcGroupBlocksX * dcGroupBlocksY];
    var perBlockQuant = new int[dcGroupBlocksX * dcGroupBlocksY];
    var covered = new bool[dcGroupBlocksX * dcGroupBlocksY];
    var originPlane = new bool[dcGroupBlocksX * dcGroupBlocksY];
    var packed = acMetadata.Channels[2];
    var taken = 0;
    for (var by = 0; by < dcGroupBlocksY; ++by)
    for (var bx = 0; bx < dcGroupBlocksX; ++bx) {
      if (covered[by * dcGroupBlocksX + bx])
        continue;
      if (taken >= count)
        throw new InvalidDataException("This frame states fewer transforms than it has blocks to cover.");

      var raw = packed.Pixels[taken];
      if (!JxlAcStrategyGeometry.IsValid(raw))
        throw new InvalidDataException($"This frame states transform {raw}, which the format does not define.");

      var strategy = (JxlAcStrategyType)raw;
      var wide = JxlAcStrategyGeometry.BlocksWide(strategy);
      var high = JxlAcStrategyGeometry.BlocksHigh(strategy);
      if (bx + wide > dcGroupBlocksX || by + high > dcGroupBlocksY)
        throw new InvalidDataException(
          $"A {wide}x{high}-block transform at ({bx}, {by}) reaches past the picture.");

      var quant = 1 + Math.Max(0, Math.Min(255, packed.Pixels[count + taken]));
      for (var cy = 0; cy < high; ++cy)
      for (var cx = 0; cx < wide; ++cx) {
        var at = (by + cy) * dcGroupBlocksX + bx + cx;
        covered[at] = true;
        strategyPlane[at] = strategy;
        perBlockQuant[at] = quant;
        originPlane[at] = cy == 0 && cx == 0;
      }

      ++taken;
    }

    // AC global: one shared histogram-count selector followed by coefficient
    // orders and entropy tables for every pass.
    var hfGlobalReader = SectionReader(1 + numDcGroups);
    var quantTableSet = JxlFrameQuantizer.ReadDequantMatrices(hfGlobalReader);

    var numHistoBits = numGroups <= 1 ? 0 : (int)Math.Ceiling(Math.Log2(numGroups));
    var numHistograms = 1 + (int)hfGlobalReader.ReadBits(numHistoBits);
    var histogramSelectorBits = numHistograms <= 1 ? 0 : (int)Math.Ceiling(Math.Log2(numHistograms));

    var acEntropies = new JxlEntropyDecoder[numPasses];
    var coeffOrdersByPass = new int[numPasses][][][];
    var acContexts = checked(numHistograms * blockCtxMap.NumACContexts);
    for (var pass = 0; pass < numPasses; ++pass) {
      // frame_header.h kOrderEnc = U32(Val(0x5F), Val(0x13), Val(0), Bits(13)).
      var usedOrders = hfGlobalReader.ReadU32(0x5Fu, 0u, 0x13u, 0u, 0u, 0u, 0u, 13u);
      coeffOrdersByPass[pass] = JxlCoeffOrderDecoder.DecodeCoeffOrders(hfGlobalReader, usedOrders);
      acEntropies[pass] = JxlEntropyDecoder.Read(
        hfGlobalReader, acContexts, disallowLz77: false,
        distanceMultiplier: (uint)Math.Max(width, height));
    }

    // Slice frame-level AC metadata into each spatial group.
    var blocksPerGroup = groupSize / _BlockDim;
    var groupStrategies = new JxlAcStrategyType[numGroups][][];
    var groupOrigins = new bool[numGroups][][];
    var groupQuant = new int[numGroups][][];
    for (var gy = 0; gy < numGroupsH; ++gy) {
      for (var gx = 0; gx < numGroupsW; ++gx) {
        var groupIdx = gy * numGroupsW + gx;
        var (blocksX, blocksY) = _GroupBlockDims(gx, gy, width, height, groupSize);
        groupStrategies[groupIdx] = new JxlAcStrategyType[blocksY][];
        groupOrigins[groupIdx] = new bool[blocksY][];
        groupQuant[groupIdx] = new int[blocksY][];
        for (var by = 0; by < blocksY; ++by) {
          groupStrategies[groupIdx][by] = new JxlAcStrategyType[blocksX];
          groupOrigins[groupIdx][by] = new bool[blocksX];
          groupQuant[groupIdx][by] = new int[blocksX];
          for (var bx = 0; bx < blocksX; ++bx) {
            var at = (gy * blocksPerGroup + by) * dcGroupBlocksX + gx * blocksPerGroup + bx;
            groupStrategies[groupIdx][by][bx] = strategyPlane[at];
            groupOrigins[groupIdx][by][bx] = originPlane[at];
            groupQuant[groupIdx][by][bx] = perBlockQuant[at];
          }
        }
      }
    }

    // The low frequencies belong to the frame, not an AC pass.
    var frameDcCount = dcGroupBlocksX * dcGroupBlocksY;
    var frameDc = new float[_NumXybChannels * frameDcCount];
    for (var i = 0; i < frameDcCount; ++i) {
      var qY = dcImage.Channels[0].Pixels[i];
      var qX = dcImage.Channels[1].Pixels[i];
      var qB = dcImage.Channels[2].Pixels[i];
      var yDc = qY * mulDc[1] * extraPrecisionMul;
      frameDc[1 * frameDcCount + i] = yDc;
      frameDc[0 * frameDcCount + i] = qX * mulDc[0] * extraPrecisionMul + yDc * dcCorrelation.YtoX;
      frameDc[2 * frameDcCount + i] = qB * mulDc[2] * extraPrecisionMul + yDc * dcCorrelation.YtoB;
    }

    if ((frameFlags & _kSkipAdaptiveDcSmoothing) == 0)
      JxlAdaptiveDcSmoothing.Apply(frameDc, dcGroupBlocksX, dcGroupBlocksY, mulDc);

    var groups = new JxlVarDctGroup[numGroups];
    for (var gy = 0; gy < numGroupsH; ++gy) {
      for (var gx = 0; gx < numGroupsW; ++gx) {
        var groupIdx = gy * numGroupsW + gx;
        var (blocksX, blocksY) = _GroupBlockDims(gx, gy, width, height, groupSize);
        var strategies = groupStrategies[groupIdx];
        var lfBlocks = _SliceLfBlocksFromDc(
          dcImage, gx, gy, groupSize / _BlockDim, blocksX, blocksY);

        JxlDctBlock[][]? acBlocks = null;
        for (var pass = 0; pass < numPasses; ++pass) {
          var section = checked(2 + numDcGroups + pass * numGroups + groupIdx);
          var acGroupReader = SectionReader(section);

          var selectedHistogram = histogramSelectorBits == 0
            ? 0
            : (int)acGroupReader.ReadBits(histogramSelectorBits);
          if (selectedHistogram >= numHistograms)
            throw new InvalidDataException(
              $"AC group {groupIdx}, pass {pass} selects histogram {selectedHistogram}, but only {numHistograms} exist.");

          var contextOffset = checked(selectedHistogram * blockCtxMap.NumACContexts);
          acBlocks = JxlAcDecoder.DecodeGroup(
            acGroupReader, acEntropies[pass], strategies, blockCtxMap,
            blocksX, blocksY, _NumXybChannels,
            groupQuant[groupIdx], groupOrigins[groupIdx], coeffOrdersByPass[pass],
            contextOffset: contextOffset,
            coefficientShift: checked((int)passShifts[pass]),
            destination: acBlocks);

          // Single-pass grouped extra channels continue after that pass's AC
          // coefficients on the same section reader (PR #403).
          if (numPasses == 1 && firstGroupedExtraChannel < extraChannels.Length)
            _DecodeGroupedExtraChannels(
              acGroupReader, extraChannels, firstGroupedExtraChannel,
              gx, gy, groupIdx, groupSize, bitDepth,
              modularGlobalTree, modularGlobalEntropy,
              numDcGroupsForStreams, numGroups, pass);
        }

        if (acBlocks is null)
          throw new InvalidDataException("A VarDCT frame contained no AC pass data.");

        // DC is injected only after all progressive AC contributions have been
        // accumulated; pass data never overwrites coefficient zero.
        for (var c = 0; c < _NumXybChannels; ++c)
          for (var i = 0; i < lfBlocks[c].Coefficients.Length; ++i)
            acBlocks[c][i].Coefficients[0] = lfBlocks[c].Coefficients[i];

        groups[groupIdx] = new JxlVarDctGroup {
          X = gx * groupSize,
          Y = gy * groupSize,
          Width = Math.Min(groupSize, width - gx * groupSize),
          Height = Math.Min(groupSize, height - gy * groupSize),
          AcBlocks = acBlocks,
          LfBlocks = lfBlocks,
        };
      }
    }

    // Dequantize → inverse transform → spatial XYB planes.
    var channels = new float[_NumXybChannels][];
    for (var c = 0; c < _NumXybChannels; ++c)
      channels[c] = new float[width * height];

    for (var groupIdx = 0; groupIdx < groups.Length; ++groupIdx) {
      var group = groups[groupIdx];
      var (blocksX, blocksY) = _GroupBlockDims(
        groupIdx % numGroupsW, groupIdx / numGroupsW, width, height, groupSize);
      var strategies = groupStrategies[groupIdx];
      var origins = groupOrigins[groupIdx];
      var correlationX = acMetadata.Channels[0];
      var correlationB = acMetadata.Channels[1];
      var xDmMul = MathF.Pow(1f / 1.25f, (int)xQmScale - 2);
      var bDmMul = MathF.Pow(1f / 1.25f, (int)bQmScale - 2);
      _RenderGroup(group, strategies, origins, correlationX, correlationB, frameDc, dcGroupBlocksX, blocksX, blocksY, quantTableSet,
        mulDc, extraPrecisionMul, dcCorrelation,
        quantParams.InvGlobalScale, perBlockQuant, xDmMul, bDmMul,
        channels, width, height);
    }

    var sharpnessPlane = acMetadata.Channels.Length > 3
      ? acMetadata.Channels[3].Pixels
      : new int[dcGroupBlocksX * dcGroupBlocksY];
    try {
      if (gaborishParams is null || !gaborishParams.Enabled) {
        // disabled
      } else {
        var weightsX = gaborishParams.WeightsX.Length == 2
          ? gaborishParams.WeightsX
          : new[] { JxlGaborish.DefaultWeights(0).A, JxlGaborish.DefaultWeights(0).B };
        var weightsY = gaborishParams.WeightsY.Length == 2
          ? gaborishParams.WeightsY
          : new[] { JxlGaborish.DefaultWeights(1).A, JxlGaborish.DefaultWeights(1).B };
        var weightsB = gaborishParams.WeightsB.Length == 2
          ? gaborishParams.WeightsB
          : new[] { JxlGaborish.DefaultWeights(2).A, JxlGaborish.DefaultWeights(2).B };
        JxlGaborish.ApplyInPlace(channels[0], width, height, weightsX);
        JxlGaborish.ApplyInPlace(channels[1], width, height, weightsY);
        JxlGaborish.ApplyInPlace(channels[2], width, height, weightsB);
      }

      if (epfParams is { Iters: > 0 })
        JxlEdgePreservingFilter.Apply(
          channels, width, height,
          perBlockQuant, sharpnessPlane, dcGroupBlocksX, dcGroupBlocksY,
          quantParams.InvGlobalScale, epfParams.Iters);
    } catch (System.NotImplementedException) {
      // Some sub-feature isn't ready; skip.
    } catch (System.ArgumentException) {
      // Degenerate dimensions; skip.
    }

    if (extraChannels.Length > 0)
      extraChannels = JxlModularTransforms.InvertAll(extraChannels, extraChannelTransforms);

    return new JxlVarDctImage {
      Width = width,
      Height = height,
      Channels = channels,
      ExtraChannels = extraChannels,
    };
  }

  private static void _DecodeGroupedExtraChannels(
    JxlBitReader reader,
    JxlChannel[] extraChannels,
    int firstGroupedExtraChannel,
    int gx,
    int gy,
    int groupIdx,
    int groupSize,
    int bitDepth,
    JxlMaTree? modularGlobalTree,
    JxlEntropyDecoder? modularGlobalEntropy,
    int numDcGroups,
    int numGroups,
    int pass
  ) {
    var originX = gx * groupSize;
    var originY = gy * groupSize;
    var windows = new System.Collections.Generic.List<(int Channel, JxlChannel Piece, int X, int Y)>();
    for (var c = firstGroupedExtraChannel; c < extraChannels.Length; ++c) {
      var channel = extraChannels[c];
      var x = originX >> channel.HShift;
      var y = originY >> channel.VShift;
      var pieceWidth = Math.Min(groupSize >> channel.HShift, channel.Width - x);
      var pieceHeight = Math.Min(groupSize >> channel.VShift, channel.Height - y);
      if (pieceWidth <= 0 || pieceHeight <= 0)
        continue;

      windows.Add((c, new JxlChannel {
        Width = pieceWidth,
        Height = pieceHeight,
        HShift = channel.HShift,
        VShift = channel.VShift,
        Pixels = new int[checked(pieceWidth * pieceHeight)],
      }, x, y));
    }

    if (windows.Count == 0)
      return;

    var pieces = new JxlChannel[windows.Count];
    for (var i = 0; i < windows.Count; ++i)
      pieces[i] = windows[i].Piece;

    var streamId = checked(1 + 3 * numDcGroups + _QuantTableCount + numGroups * pass + groupIdx);
    var decodedExtra = JxlModularSpecDecoder.DecodeStream(
      reader, pieces, bitDepth, modularGlobalTree, modularGlobalEntropy,
      new JxlModularStreamOptions {
        MaxChannelSize = int.MaxValue,
        StreamId = streamId,
        UndoTransforms = true,
      });

    for (var i = 0; i < windows.Count; ++i) {
      var (channelIndex, _, x, y) = windows[i];
      var piece = decodedExtra.Image.Channels[i];
      var target = extraChannels[channelIndex];
      for (var row = 0; row < piece.Height; ++row)
        Array.Copy(
          piece.Pixels, row * piece.Width,
          target.Pixels, checked((y + row) * target.Width + x),
          piece.Width);
    }
  }

  /// <summary>Extract this group's per-channel DC slice from the frame's DC
  /// modular sub-image.</summary>
  private static JxlLfBlock[] _SliceLfBlocksFromDc(
    JxlModularImage dcImage,
    int gx,
    int gy,
    int groupBlocks,
    int blocksX,
    int blocksY
  ) {
    var lfBlocks = new JxlLfBlock[_NumXybChannels];
    var x0 = gx * groupBlocks;
    var y0 = gy * groupBlocks;
    for (var c = 0; c < _NumXybChannels; ++c) {
      var srcChannel = c < 2 ? c ^ 1 : c;
      var ch = dcImage.Channels[srcChannel];
      var coeffs = new short[blocksX * blocksY];
      for (var by = 0; by < blocksY; ++by)
        for (var bx = 0; bx < blocksX; ++bx) {
          var v = ch.Pixels[(y0 + by) * ch.Width + (x0 + bx)];
          coeffs[by * blocksX + bx] = v switch {
            > short.MaxValue => short.MaxValue,
            < short.MinValue => short.MinValue,
            _ => (short)v,
          };
        }
      lfBlocks[c] = new JxlLfBlock {
        Width = blocksX,
        Height = blocksY,
        Coefficients = coeffs,
      };
    }
    return lfBlocks;
  }

  private static void _RenderGroup(
    JxlVarDctGroup group,
    JxlAcStrategyType[][] strategies,
    bool[][] origins,
    JxlChannel correlationX,
    JxlChannel correlationB,
    float[] frameDc,
    int frameStride,
    int blocksX,
    int blocksY,
    JxlQuantTableSet quantTableSet,
    float[] mulDc,
    float extraPrecisionMul,
    JxlColorCorrelationMap.DcCorrelationFactors dcCorrelation,
    float invGlobalScale,
    int[] frameQuant,
    float xDmMultiplier,
    float bDmMultiplier,
    float[][] channels,
    int imageWidth,
    int imageHeight
  ) {
    var totalBlocks = blocksX * blocksY;
    var frameCount = frameStride * (frameDc.Length / (_NumXybChannels * frameStride));
    var dequantDc = new float[_NumXybChannels * totalBlocks];
    const int xCh = 0, yCh = 1, bCh = 2;
    var blockX0 = group.X / _BlockDim;
    var blockY0 = group.Y / _BlockDim;

    var perBlockQuant = new int[totalBlocks];
    for (var by = 0; by < blocksY; ++by)
    for (var bx = 0; bx < blocksX; ++bx) {
      var from = (blockY0 + by) * frameStride + blockX0 + bx;
      var to = by * blocksX + bx;
      perBlockQuant[to] = from < frameQuant.Length ? frameQuant[from] : 1;
      for (var c = 0; c < _NumXybChannels; ++c)
        dequantDc[c * totalBlocks + to] = frameDc[c * frameCount + from];
    }

    var dequantedY = new float[totalBlocks][];
    for (var by = 0; by < blocksY; ++by)
      for (var bx = 0; bx < blocksX; ++bx) {
        var blockIdx = by * blocksX + bx;
        var strategy = strategies[by][bx];
        if (!_ReconstructsFromOrigin(origins, bx, by, strategy))
          continue;

        var coeffBlock = group.AcBlocks[yCh][blockIdx];
        var (blockW, blockH) = JxlVarDctIdct.BlockSize(strategy);
        var quantValue = perBlockQuant[blockIdx];
        if (quantValue <= 0) quantValue = 1;

        dequantedY[blockIdx] = _Dequantize(
          coeffBlock.Coefficients, strategy, blockW, blockH,
          _StrategyTables(strategy, quantTableSet).Tables[yCh], quantTableSet.Tables[yCh],
          invGlobalScale / quantValue, yCh);
      }

    for (var c = 0; c < _NumXybChannels; ++c) {
      for (var by = 0; by < blocksY; ++by) {
        for (var bx = 0; bx < blocksX; ++bx) {
          var blockIdx = by * blocksX + bx;
          var strategy = strategies[by][bx];
          if (!_ReconstructsFromOrigin(origins, bx, by, strategy))
            continue;
          var coeffBlock = group.AcBlocks[c][blockIdx];
          var (blockW, blockH) = JxlVarDctIdct.BlockSize(strategy);
          var blockArea = blockW * blockH;

          var quantValue = perBlockQuant[blockIdx];
          if (quantValue <= 0) quantValue = 1;
          var scaledDequant = invGlobalScale / quantValue
            * (c == xCh ? xDmMultiplier : c == bCh ? bDmMultiplier : 1f);
          var dequantized = _Dequantize(
            coeffBlock.Coefficients, strategy, blockW, blockH,
            _StrategyTables(strategy, quantTableSet).Tables[c], quantTableSet.Tables[c],
            scaledDequant, c);

          if ((c == xCh || c == bCh) && dequantedY[blockIdx] != null) {
            var yDeq = dequantedY[blockIdx];
            var mix = c == xCh
              ? _CorrelationAt(correlationX, group, bx, by, dcCorrelation.YtoX)
              : _CorrelationAt(correlationB, group, bx, by, dcCorrelation.YtoB);
            if (mix != 0f) {
              for (var i = 0; i < blockArea; ++i)
                dequantized[i] += mix * yDeq[i];
            }
          }

          var toCoefficients = JxlLowestFrequencies.DcToCoefficients(strategy);
          if (toCoefficients is null) {
            dequantized[0] = dequantDc[c * totalBlocks + blockIdx];
          } else {
            var order = JxlNaturalCoeffOrder.For(strategy);
            var coveredWide = blockW / _BlockDim;
            var coveredHigh = blockH / _BlockDim;
            var dcOfCovered = new float[coveredWide * coveredHigh];
            for (var dy = 0; dy < coveredHigh; ++dy)
            for (var dx = 0; dx < coveredWide; ++dx) {
              var at = (by + dy) * blocksX + bx + dx;
              dcOfCovered[dy * coveredWide + dx] = at < totalBlocks
                ? dequantDc[c * totalBlocks + at]
                : dequantDc[c * totalBlocks + blockIdx];
            }

            for (var i = 0; i < toCoefficients.Length; ++i) {
              var value = 0f;
              for (var j = 0; j < dcOfCovered.Length; ++j)
                value += toCoefficients[i][j] * dcOfCovered[j];

              dequantized[order[i]] = value;
            }
          }

          var spatial = new float[blockArea];
          JxlVarDctIdct.InverseAcStrategy(strategy, dequantized, spatial);

          var pixelX = group.X + bx * _BlockDim;
          var pixelY = group.Y + by * _BlockDim;
          for (var y = 0; y < blockH; ++y) {
            var dstY = pixelY + y;
            if (dstY >= imageHeight) break;
            for (var x = 0; x < blockW; ++x) {
              var dstX = pixelX + x;
              if (dstX >= imageWidth) continue;
              channels[c][dstY * imageWidth + dstX] = spatial[y * blockW + x];
            }
          }
        }
      }
    }
  }

  internal static float _CorrelationAt(
    JxlChannel map,
    JxlVarDctGroup group,
    int bx,
    int by,
    float baseCorrelation
  ) {
    if (map.Width <= 0 || map.Height <= 0)
      return baseCorrelation;

    const int blocksPerTile = 8;
    var tileX = (group.X / _BlockDim + bx) / blocksPerTile;
    var tileY = (group.Y / _BlockDim + by) / blocksPerTile;
    if (tileX >= map.Width || tileY >= map.Height)
      return baseCorrelation;

    return baseCorrelation
           + map.Pixels[tileY * map.Width + tileX] / (float)JxlColorCorrelationMap.DefaultColorFactor;
  }

  private static bool _ReconstructsFromOrigin(
    bool[][] origins,
    int bx,
    int by,
    JxlAcStrategyType strategy
  ) {
    if (JxlLowestFrequencies.DcToCoefficients(strategy) is null)
      return true;

    return origins[by][bx];
  }

  private static JxlQuantTableSet _StrategyTables(JxlAcStrategyType strategy, JxlQuantTableSet fallback)
    => strategy == JxlAcStrategyType.Dct8x8
      ? fallback
      : JxlVarDctQuant.DefaultsForStrategy(strategy) ?? fallback;

  private static float[] _Dequantize(
    short[] coefficients,
    JxlAcStrategyType strategy,
    int blockW,
    int blockH,
    JxlQuantTable table,
    JxlQuantTable fallback,
    float scale,
    int channel
  ) {
    var area = blockW * blockH;
    var result = new float[area];
    if (table.Width * table.Height == area) {
      for (var i = 0; i < area; ++i)
        result[i] = JxlVarDctQuant.AdjustQuantBias(coefficients[i], channel) * scale * table.Weights[i];

      return result;
    }

    for (var i = 0; i < area; ++i) {
      var ty = i / blockW * 8 / Math.Max(1, blockH);
      var tx = i % blockW * 8 / Math.Max(1, blockW);
      result[i] = JxlVarDctQuant.AdjustQuantBias(coefficients[i], channel) * scale * fallback.Weights[ty * 8 + tx];
    }

    return result;
  }

  private static (int blocksX, int blocksY) _GroupBlockDims(
    int gx,
    int gy,
    int width,
    int height,
    int groupSize
  ) {
    var pixelsW = Math.Min(groupSize, width - gx * groupSize);
    var pixelsH = Math.Min(groupSize, height - gy * groupSize);
    var blocksX = (pixelsW + _BlockDim - 1) / _BlockDim;
    var blocksY = (pixelsH + _BlockDim - 1) / _BlockDim;
    return (blocksX, blocksY);
  }

}
