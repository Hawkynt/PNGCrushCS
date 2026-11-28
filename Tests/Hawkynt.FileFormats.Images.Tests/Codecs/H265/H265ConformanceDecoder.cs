using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using FileFormat.Codecs.H265;

namespace FileFormat.Codecs.H265.Tests;

/// <summary>
/// Decodes an H.265 elementary stream the way a conformance check needs it decoded: every picture
/// held against the decoded picture hash its encoder wrote beside it, and the cropped output
/// collected in output order as the planar file a reference decoder writes.
/// </summary>
/// <remarks>
/// The hash is the oracle that travels with the stream. Clause D.3.19 has the encoder hash every
/// picture it reconstructed — MD5, CRC or checksum, one per colour plane, over the whole decoded
/// picture before cropping — so a decoder that reproduces all of them reproduced the encoder's own
/// reconstruction, sample for sample, without anybody's output file being needed. That matters for
/// this suite because its <c>.yuv</c> checksums were written by more than one tool in more than one
/// layout, and several of them describe a file no decoder writes by default.
/// </remarks>
internal static class H265ConformanceDecoder {

  /// <summary>What decoding one stream produced.</summary>
  internal sealed class Result {
    /// <summary>How many pictures carried a hash, and how many of those matched it.</summary>
    internal int HashedPictures { get; set; }
    internal int MatchingPictures { get; set; }

    /// <summary>The first picture whose hash did not match, described for a failure message.</summary>
    internal string? FirstMismatch { get; set; }

    /// <summary>
    /// The MD5 of every output picture, cropped, in output order: one byte per sample at eight bits,
    /// two little-endian otherwise — the file a reference decoder writes.
    /// </summary>
    internal IncrementalHash Output { get; } = IncrementalHash.CreateHash(HashAlgorithmName.MD5);

    internal int OutputPictures { get; set; }

    /// <summary>
    /// The same output with a 4:4:4 picture's planes in the order R, G, B — the coded G, B, R
    /// reordered, which is the file the reference decoder writes when asked to convert GBR to RGB.
    /// Some conformance archives state only that file's checksum.
    /// </summary>
    internal IncrementalHash OutputRgb { get; } = IncrementalHash.CreateHash(HashAlgorithmName.MD5);

    internal string OutputRgbMd5 => this._rgbMd5 ??= Convert.ToHexString(this.OutputRgb.GetHashAndReset()).ToLowerInvariant();

    private string? _rgbMd5;

    /// <summary>The range extension and screen content tools the stream switched on, by syntax element name.</summary>
    internal SortedSet<string> Tools { get; } = new(StringComparer.Ordinal);

    internal H265SequenceParameterSet? Sequence { get; set; }

    internal string OutputMd5 => this._md5 ??= Convert.ToHexString(this.Output.GetHashAndReset()).ToLowerInvariant();

    private string? _md5;
  }

  internal static Result Decode(byte[] annexB) {
    var result = new Result();
    var decoder = new H265StreamDecoder();
    var pending = new List<(int Type, byte[][] Hashes)>();
    var sequenceSets = new Dictionary<int, H265SequenceParameterSet>();
    var pictureSets = new Dictionary<int, H265PictureParameterSet>();
    H265SliceHeader? lastIndependent = null;
    H265Picture? checkedPicture = null;

    void CheckFinished() {
      var picture = decoder.LastFinishedPicture;
      if (picture == null || ReferenceEquals(picture, checkedPicture) || pending.Count == 0)
        return;

      checkedPicture = picture;
      var (type, hashes) = pending[0];
      pending.RemoveAt(0);
      ++result.HashedPictures;

      var sps = decoder.Sequence!;
      var planes = sps.ChromaArrayType == 0 ? 1 : 3;
      for (var c = 0; c < planes; ++c) {
        var plane = c == 0 ? picture.Luma : picture.Chroma(c - 1);
        var width = c == 0 ? picture.Width : picture.ChromaWidth;
        var height = c == 0 ? picture.Height : picture.ChromaHeight;
        var depth = c == 0 ? sps.BitDepthLuma : sps.BitDepthChroma;
        var actual = PictureHash(type, plane, width, height, depth);
        if (actual.AsSpan().SequenceEqual(hashes[c]))
          continue;

        result.FirstMismatch ??=
          $"picture {result.HashedPictures - 1} (POC {picture.PictureOrderCount}) plane {c}: expected "
          + $"{Convert.ToHexString(hashes[c])}, decoded {Convert.ToHexString(actual)}";
        return;
      }

      ++result.MatchingPictures;
    }

    var buffer = new MemoryStream();

    void Drain() {
      while (decoder.TryTakePicture(out var picture, out var sps)) {
        result.Sequence = sps;
        if (!picture.IsOutput)
          continue;

        buffer.SetLength(0);
        WriteCropped(buffer, picture, sps);
        result.Output.AppendData(buffer.GetBuffer(), 0, (int)buffer.Length);
        if (sps.ChromaArrayType == 3) {
          var plane = (int)buffer.Length / 3;
          result.OutputRgb.AppendData(buffer.GetBuffer(), 2 * plane, plane);
          result.OutputRgb.AppendData(buffer.GetBuffer(), 0, 2 * plane);
        }

        ++result.OutputPictures;
      }
    }

    foreach (var nal in H265NalReader.SplitAnnexB(annexB)) {
      _NoteTools(nal, result.Tools, sequenceSets, pictureSets, ref lastIndependent);

      if (nal.Type == H265NalUnitType.SuffixSupplementalEnhancementInformation) {
        // The suffix message closes the picture it follows; finishing the picture here rather than
        // at the next slice is what lets its hash be checked before anything else is decoded.
        var hash = _ReadPictureHash(nal.Payload, decoder.Sequence);
        if (hash != null) {
          decoder.EndOfAccessUnit();
          pending.Add(hash.Value);
          CheckFinished();
        }

        continue;
      }

      if (nal.Type == H265NalUnitType.PrefixSupplementalEnhancementInformation) {
        var hash = _ReadPictureHash(nal.Payload, decoder.Sequence);
        if (hash != null) {
          decoder.EndOfAccessUnit();
          CheckFinished();
          pending.Add(hash.Value);
        }

        continue;
      }

      try {
        decoder.Decode([nal]);
      } catch (Exception e) when (e is InvalidDataException or IndexOutOfRangeException) {
        throw new InvalidDataException(
          $"Decoding failed after {result.HashedPictures} hashed pictures, {result.MatchingPictures} of them "
          + $"matching{(result.FirstMismatch == null ? "" : $" (first mismatch: {result.FirstMismatch})")}: "
          + e.Message, e);
      }

      CheckFinished();
      Drain();
    }

    decoder.Flush();
    CheckFinished();
    Drain();
    return result;
  }

  /// <summary>Records which range extension and screen content tools a parameter set or slice enables.</summary>
  private static void _NoteTools(
    H265NalUnit nal, SortedSet<string> tools, Dictionary<int, H265SequenceParameterSet> sequenceSets,
    Dictionary<int, H265PictureParameterSet> pictureSets, ref H265SliceHeader? lastIndependent) {
    if (nal.IsSlice && sequenceSets.Count > 0 && pictureSets.Count > 0) {
      var header = H265SliceHeader.Parse(nal, sequenceSets, pictureSets, lastIndependent);
      if (!header.DependentSliceSegment)
        lastIndependent = header;

      if (header.PredictionWeights is { } weights) {
        for (var list = 0; list < 2; ++list)
          for (var i = 0; i < (list == 0 ? weights.LumaWeightL0.Length : weights.LumaWeightL1.Length); ++i) {
            if (weights.LumaWeight(list, i) != 1 << weights.LumaLog2WeightDenom || weights.LumaOffset(list, i) != 0)
              tools.Add("weights_used");
            for (var c = 0; c < 2; ++c)
              if (weights.ChromaOffset(list, i, c) != 0 && header.Sps.ChromaArrayType != 0)
                tools.Add("chroma_offsets_used");
          }
      }

      if (header.CuChromaQpOffsetEnabled)
        tools.Add("cu_chroma_qp_offset_enabled");
      if (header.UseIntegerMv && !header.IsIntra)
        tools.Add("use_integer_mv");
      if (header.SliceActQpOffsetY != 0 || header.SliceActQpOffsetCb != 0 || header.SliceActQpOffsetCr != 0)
        tools.Add("slice_act_qp_offsets_used");
      if (header.Nal.IsRandomAccessPoint && !header.IsIntra)
        tools.Add("predicted_random_access_point");
      return;
    }

    if (nal.Type == H265NalUnitType.SequenceParameterSet) {
      var sps = H265SequenceParameterSet.Parse(nal.Payload);
      sequenceSets[sps.Id] = sps;
      var range = sps.RangeExtension;
      void Note(bool on, string name) {
        if (on)
          tools.Add(name);
      }

      Note(range.TransformSkipRotationEnabled, "transform_skip_rotation");
      Note(range.TransformSkipContextEnabled, "transform_skip_context");
      Note(range.ImplicitRdpcmEnabled, "implicit_rdpcm");
      Note(range.ExplicitRdpcmEnabled, "explicit_rdpcm");
      Note(range.ExtendedPrecisionProcessing, "extended_precision");
      Note(range.IntraSmoothingDisabled, "intra_smoothing_disabled");
      Note(range.HighPrecisionOffsetsEnabled, "high_precision_offsets");
      Note(range.PersistentRiceAdaptationEnabled, "persistent_rice");
      Note(range.CabacBypassAlignmentEnabled, "cabac_bypass_alignment");
      Note(sps.BitDepthLuma > 12 || sps.BitDepthChroma > 12, "depth>12");
      Note(sps.BitDepthLuma != sps.BitDepthChroma && sps.ChromaArrayType != 0, "mixed_depth");
      Note(sps.PcmEnabled, "pcm");
      Note(sps.ScalingListEnabled, "scaling_list");
      var screen = sps.ScreenContent;
      Note(screen.CurrentPictureReferenceEnabled, "sps_curr_pic_ref");
      Note(screen.PaletteModeEnabled, $"palette(max {screen.PaletteMaxSize}, predictor {screen.PaletteMaxPredictorSize})");
      Note(screen.PalettePredictorInitializers != null, "sps_palette_predictor_initializers");
      Note(screen.MotionVectorResolutionControlIdc != 0, $"mv_resolution_control_idc={screen.MotionVectorResolutionControlIdc}");
      Note(screen.IntraBoundaryFilteringDisabled, "intra_boundary_filtering_disabled");
    } else if (nal.Type == H265NalUnitType.PictureParameterSet) {
      var pps = H265PictureParameterSet.Parse(nal.Payload);
      pictureSets[pps.Id] = pps;
      if (pps.TransformSkipEnabled && pps.Log2MaxTransformSkipBlockSize > 2)
        tools.Add("log2_max_transform_skip_block_size");
      if (pps.CrossComponentPredictionEnabled)
        tools.Add("cross_component_prediction");
      if (pps.ChromaQpOffsetListEnabled)
        tools.Add("chroma_qp_offset_list" + (pps.CbQpOffsetList.Length > 1 ? "(len>1)" : ""));
      if (pps.Log2SaoOffsetScaleLuma > 0 || pps.Log2SaoOffsetScaleChroma > 0)
        tools.Add("log2_sao_offset_scale");
      if (pps.WeightedPred || pps.WeightedBipred)
        tools.Add("weighted_prediction");
      if (pps.TilesEnabled)
        tools.Add("tiles");
      if (pps.EntropyCodingSyncEnabled)
        tools.Add("wavefronts");
      if (pps.TransquantBypassEnabled)
        tools.Add("transquant_bypass");
      if (pps.CurrentPictureReferenceEnabled)
        tools.Add("pps_curr_pic_ref");
      if (pps.AdaptiveColourTransformEnabled)
        tools.Add("adaptive_colour_transform");
      if (pps.SliceActQpOffsetsPresent)
        tools.Add("slice_act_qp_offsets_present");
      if (pps.PalettePredictorInitializers != null)
        tools.Add("pps_palette_predictor_initializers");
    }
  }

  /// <summary>Writes a picture as a reference decoder writes its output file: cropped planes, Y then Cb then Cr.</summary>
  /// <remarks>
  /// One sample width for the whole file, as the reference decoder writes it: two bytes for every
  /// plane as soon as any plane is deeper than eight bits, with each plane keeping its own depth.
  /// </remarks>
  internal static void WriteCropped(Stream output, H265Picture picture, H265SequenceParameterSet sps) {
    var depth = sps.ChromaArrayType == 0 ? sps.BitDepthLuma : Math.Max(sps.BitDepthLuma, sps.BitDepthChroma);
    _WritePlane(output, picture.Luma, picture.Width, sps.CropOffsetX, sps.CropOffsetY,
      sps.DisplayWidth, sps.DisplayHeight, depth);
    if (sps.ChromaArrayType == 0)
      return;

    var width = sps.DisplayWidth / sps.SubWidthC;
    var height = sps.DisplayHeight / sps.SubHeightC;
    for (var c = 0; c < 2; ++c)
      _WritePlane(output, picture.Chroma(c), picture.ChromaWidth, sps.CropOffsetX / sps.SubWidthC,
        sps.CropOffsetY / sps.SubHeightC, width, height, depth);
  }

  private static void _WritePlane(
    Stream output, ushort[] plane, int stride, int left, int top, int width, int height, int depth) {
    var row = new byte[width * (depth > 8 ? 2 : 1)];
    for (var y = 0; y < height; ++y) {
      var at = (top + y) * stride + left;
      for (var x = 0; x < width; ++x)
        if (depth > 8) {
          row[2 * x] = (byte)plane[at + x];
          row[2 * x + 1] = (byte)(plane[at + x] >> 8);
        } else
          row[x] = (byte)plane[at + x];

      output.Write(row);
    }
  }

  /// <summary>
  /// The picture hash of one decoded plane — clause D.3.19.
  /// </summary>
  /// <param name="type">0 MD5, 1 CRC, 2 checksum, as <c>hash_type</c> numbers them.</param>
  internal static byte[] PictureHash(int type, ushort[] plane, int width, int height, int depth) {
    var wide = depth > 8;
    switch (type) {
      case 0: {
        var bytes = new byte[width * height * (wide ? 2 : 1)];
        for (int i = 0, o = 0; i < width * height; ++i) {
          bytes[o++] = (byte)plane[i];
          if (wide)
            bytes[o++] = (byte)(plane[i] >> 8);
        }

        return MD5.HashData(bytes);
      }

      case 1: {
        var crc = 0xFFFF;

        void Feed(int value) {
          for (var bit = 0; bit < 8; ++bit) {
            var msb = (crc >> 15) & 1;
            crc = (((crc << 1) + ((value >> (7 - bit)) & 1)) & 0xFFFF) ^ (msb * 0x1021);
          }
        }

        for (var i = 0; i < width * height; ++i) {
          Feed(plane[i] & 0xFF);
          if (wide)
            Feed(plane[i] >> 8);
        }

        for (var bit = 0; bit < 16; ++bit) {
          var msb = (crc >> 15) & 1;
          crc = ((crc << 1) & 0xFFFF) ^ (msb * 0x1021);
        }

        return [(byte)(crc >> 8), (byte)crc];
      }

      default: {
        var sum = 0u;
        for (var y = 0; y < height; ++y)
          for (var x = 0; x < width; ++x) {
            var mask = (uint)((x & 0xFF) ^ (y & 0xFF) ^ (x >> 8) ^ (y >> 8));
            var sample = plane[y * width + x];
            sum += (uint)(sample & 0xFF) ^ mask;
            if (wide)
              sum += (uint)(sample >> 8) ^ mask;
          }

        return [(byte)(sum >> 24), (byte)(sum >> 16), (byte)(sum >> 8), (byte)sum];
      }
    }
  }

  /// <summary>Finds a decoded picture hash message among the SEI messages of one NAL unit.</summary>
  private static (int Type, byte[][] Hashes)? _ReadPictureHash(byte[] payload, H265SequenceParameterSet? sps) {
    var at = 0;
    while (at < payload.Length && !(at == payload.Length - 1 && payload[at] == 0x80)) {
      var payloadType = 0;
      while (at < payload.Length && payload[at] == 0xFF) {
        payloadType += 255;
        ++at;
      }

      if (at >= payload.Length)
        return null;
      payloadType += payload[at++];

      var size = 0;
      while (at < payload.Length && payload[at] == 0xFF) {
        size += 255;
        ++at;
      }

      if (at >= payload.Length)
        return null;
      size += payload[at++];

      if (at + size > payload.Length)
        return null;

      if (payloadType == 132) {
        var type = payload[at];
        var length = type switch { 0 => 16, 1 => 2, _ => 4 };
        var planes = sps is { ChromaArrayType: 0 } ? 1 : 3;
        planes = Math.Min(planes, (size - 1) / length);
        var hashes = new byte[3][];
        for (var c = 0; c < planes; ++c)
          hashes[c] = payload.AsSpan(at + 1 + c * length, length).ToArray();
        return (type, hashes);
      }

      at += size;
    }

    return null;
  }
}
