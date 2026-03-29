using System;
using System.Collections.Generic;

namespace FileFormat.Jpeg2000.Codec;

/// <summary>Describes a subband's dimensions and position within the wavelet decomposition.</summary>
internal readonly record struct SubbandInfo(
  /// <summary>Resolution level (0 = lowest, DecompLevels = full resolution).</summary>
  int ResolutionLevel,
  /// <summary>Subband type: 0=LL, 1=HL, 2=LH, 3=HH.</summary>
  int Type,
  /// <summary>Width of the subband in coefficients.</summary>
  int Width,
  /// <summary>Height of the subband in coefficients.</summary>
  int Height,
  /// <summary>X offset into the coefficient plane.</summary>
  int OffsetX,
  /// <summary>Y offset into the coefficient plane.</summary>
  int OffsetY,
  /// <summary>Sequential index across all subbands.</summary>
  int Index
) {

  /// <summary>Computes all subbands for the given image dimensions and decomposition levels.</summary>
  internal static SubbandInfo[] ComputeSubbands(int width, int height, int levels) {
    var result = new List<SubbandInfo>();
    var index = 0;

    // Compute dimensions at each decomposition level
    var widths = new int[levels + 1];
    var heights = new int[levels + 1];
    widths[0] = width;
    heights[0] = height;
    for (var i = 1; i <= levels; ++i) {
      widths[i] = (widths[i - 1] + 1) / 2;
      heights[i] = (heights[i - 1] + 1) / 2;
    }

    // LL subband at the deepest level
    result.Add(new(levels, 0, widths[levels], heights[levels], 0, 0, index++));

    // For each level from deepest to shallowest, add HL, LH, HH subbands
    for (var level = levels; level >= 1; --level) {
      var llW = widths[level];
      var llH = heights[level];
      var fullW = widths[level - 1];
      var fullH = heights[level - 1];
      var hlW = fullW - llW;
      var hlH = llH;
      var lhW = llW;
      var lhH = fullH - llH;
      var hhW = fullW - llW;
      var hhH = fullH - llH;

      result.Add(new(level, 1, hlW, hlH, llW, 0, index++));       // HL
      result.Add(new(level, 2, lhW, lhH, 0, llH, index++));       // LH
      result.Add(new(level, 3, hhW, hhH, llW, llH, index++));      // HH
    }

    return result.ToArray();
  }

  /// <summary>Compute the number of code-blocks in this subband given the nominal code-block size.</summary>
  internal void GetCodeBlockGrid(int cbWidth, int cbHeight, out int numCbX, out int numCbY) {
    numCbX = Width > 0 ? (Width + cbWidth - 1) / cbWidth : 0;
    numCbY = Height > 0 ? (Height + cbHeight - 1) / cbHeight : 0;
  }
}
