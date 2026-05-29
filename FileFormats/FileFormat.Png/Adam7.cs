using System.Runtime.CompilerServices;

namespace FileFormat.Png;

/// <summary>Adam7 interlace pass definitions per PNG specification</summary>
public static class Adam7 {
  public const int PassCount = 7;

  private static readonly int[] _xStart = [0, 4, 0, 2, 0, 1, 0];
  private static readonly int[] _yStart = [0, 0, 4, 0, 2, 0, 1];
  private static readonly int[] _xStep = [8, 8, 4, 4, 2, 2, 1];
  private static readonly int[] _yStep = [8, 8, 8, 4, 4, 2, 2];

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public static int XStart(int pass) => _xStart[pass];

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public static int YStart(int pass) => _yStart[pass];

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public static int XStep(int pass) => _xStep[pass];

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public static int YStep(int pass) => _yStep[pass];

  /// <summary>Get the sub-image dimensions for a given pass</summary>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public static (int width, int height) GetPassDimensions(int pass, int imageWidth, int imageHeight) {
    var w = (imageWidth - _xStart[pass] + _xStep[pass] - 1) / _xStep[pass];
    var h = (imageHeight - _yStart[pass] + _yStep[pass] - 1) / _yStep[pass];
    return (w > 0 ? w : 0, h > 0 ? h : 0);
  }
}
