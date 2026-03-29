using System;

namespace FileFormat.Bpg.Codec;

/// <summary>Sample Adaptive Offset (SAO) filter: band offset and edge offset modes.</summary>
internal static class HevcSaoFilter {

  /// <summary>SAO type enumeration.</summary>
  public enum SaoType {
    None = 0,
    BandOffset = 1,
    EdgeOffset = 2,
  }

  /// <summary>Edge offset class directions: horizontal, vertical, 135-degree, 45-degree.</summary>
  public enum SaoEdgeClass {
    Horizontal = 0,
    Vertical = 1,
    Diagonal135 = 2,
    Diagonal45 = 3,
  }

  /// <summary>SAO parameters for one CTU and one color component.</summary>
  public sealed class SaoParams {
    public SaoType Type { get; init; }
    public int[] Offsets { get; init; } = new int[4]; // 4 offset values
    public int BandPosition { get; init; } // For band offset: starting band index
    public SaoEdgeClass EdgeClass { get; init; } // For edge offset: direction
  }

  /// <summary>Applies SAO filtering to a CTU region of a plane.</summary>
  /// <param name="samples">Sample buffer (modified in place).</param>
  /// <param name="stride">Row stride.</param>
  /// <param name="ctuX">CTU X position (in samples).</param>
  /// <param name="ctuY">CTU Y position (in samples).</param>
  /// <param name="ctuSize">CTU size in samples.</param>
  /// <param name="frameWidth">Frame width.</param>
  /// <param name="frameHeight">Frame height.</param>
  /// <param name="parameters">SAO parameters for this CTU.</param>
  /// <param name="bitDepth">Bit depth.</param>
  public static void ApplyCtu(
    int[] samples, int stride,
    int ctuX, int ctuY, int ctuSize,
    int frameWidth, int frameHeight,
    SaoParams parameters, int bitDepth
  ) {
    if (parameters.Type == SaoType.None)
      return;

    var maxVal = (1 << bitDepth) - 1;
    var xEnd = Math.Min(ctuX + ctuSize, frameWidth);
    var yEnd = Math.Min(ctuY + ctuSize, frameHeight);

    switch (parameters.Type) {
      case SaoType.BandOffset:
        _ApplyBandOffset(samples, stride, ctuX, ctuY, xEnd, yEnd, parameters, maxVal, bitDepth);
        break;
      case SaoType.EdgeOffset:
        _ApplyEdgeOffset(samples, stride, ctuX, ctuY, xEnd, yEnd, frameWidth, frameHeight, parameters, maxVal);
        break;
    }
  }

  private static void _ApplyBandOffset(
    int[] samples, int stride,
    int xStart, int yStart, int xEnd, int yEnd,
    SaoParams parameters, int maxVal, int bitDepth
  ) {
    // Band offset divides the sample range into 32 bands, applies offsets to 4 consecutive bands
    var bandShift = bitDepth - 5;
    var bandPos = parameters.BandPosition;

    for (var y = yStart; y < yEnd; ++y)
      for (var x = xStart; x < xEnd; ++x) {
        var idx = y * stride + x;
        var sample = samples[idx];
        var band = sample >> bandShift;

        // Check if this sample falls within the offset bands
        var bandIdx = band - bandPos;
        if (bandIdx >= 0 && bandIdx < 4) {
          var offset = parameters.Offsets[bandIdx];
          samples[idx] = Math.Clamp(sample + offset, 0, maxVal);
        }
      }
  }

  private static void _ApplyEdgeOffset(
    int[] samples, int stride,
    int xStart, int yStart, int xEnd, int yEnd,
    int frameWidth, int frameHeight,
    SaoParams parameters, int maxVal
  ) {
    // Direction offsets for the two neighboring samples
    int dx0, dy0, dx1, dy1;
    switch (parameters.EdgeClass) {
      case SaoEdgeClass.Horizontal:
        dx0 = -1; dy0 = 0; dx1 = 1; dy1 = 0;
        break;
      case SaoEdgeClass.Vertical:
        dx0 = 0; dy0 = -1; dx1 = 0; dy1 = 1;
        break;
      case SaoEdgeClass.Diagonal135:
        dx0 = -1; dy0 = -1; dx1 = 1; dy1 = 1;
        break;
      case SaoEdgeClass.Diagonal45:
        dx0 = 1; dy0 = -1; dx1 = -1; dy1 = 1;
        break;
      default:
        return;
    }

    // Edge offset lookup: edgeIdx maps to offset index
    // edgeIdx: 0=valley(both neighbors larger), 1=one larger, 2=flat, 3=one smaller, 4=peak(both smaller)
    // SAO offsets are for classes 0..3 (class 2 always has offset 0)
    var offsets = new int[5];
    offsets[0] = parameters.Offsets[0]; // valley: positive offset
    offsets[1] = parameters.Offsets[1]; // half-valley: positive offset
    offsets[2] = 0;                      // flat: no offset
    offsets[3] = parameters.Offsets[2]; // half-peak: negative offset
    offsets[4] = parameters.Offsets[3]; // peak: negative offset

    // Make a copy to avoid using modified values as neighbors
    var regionWidth = xEnd - xStart;
    var regionHeight = yEnd - yStart;
    var backup = new int[regionWidth * regionHeight];
    for (var y = yStart; y < yEnd; ++y)
      for (var x = xStart; x < xEnd; ++x)
        backup[(y - yStart) * regionWidth + (x - xStart)] = samples[y * stride + x];

    for (var y = yStart; y < yEnd; ++y)
      for (var x = xStart; x < xEnd; ++x) {
        var nx0 = x + dx0;
        var ny0 = y + dy0;
        var nx1 = x + dx1;
        var ny1 = y + dy1;

        // Skip boundary samples where neighbors are outside the frame
        if (nx0 < 0 || nx0 >= frameWidth || ny0 < 0 || ny0 >= frameHeight)
          continue;
        if (nx1 < 0 || nx1 >= frameWidth || ny1 < 0 || ny1 >= frameHeight)
          continue;

        var c = backup[(y - yStart) * regionWidth + (x - xStart)];

        // Get neighbors from the original (unmodified) data
        int n0, n1;
        if (nx0 >= xStart && nx0 < xEnd && ny0 >= yStart && ny0 < yEnd)
          n0 = backup[(ny0 - yStart) * regionWidth + (nx0 - xStart)];
        else
          n0 = samples[ny0 * stride + nx0];

        if (nx1 >= xStart && nx1 < xEnd && ny1 >= yStart && ny1 < yEnd)
          n1 = backup[(ny1 - yStart) * regionWidth + (nx1 - xStart)];
        else
          n1 = samples[ny1 * stride + nx1];

        var signLeft = Math.Sign(c - n0);
        var signRight = Math.Sign(c - n1);
        var edgeIdx = signLeft + signRight + 2; // Map from {-2,-1,0,1,2} to {0,1,2,3,4}

        samples[y * stride + x] = Math.Clamp(c + offsets[edgeIdx], 0, maxVal);
      }
  }
}
