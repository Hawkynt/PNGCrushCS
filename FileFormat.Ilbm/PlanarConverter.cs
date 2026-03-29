using System;
using FileFormat.Core;

namespace FileFormat.Ilbm;

/// <summary>Converts between interleaved planar and chunky pixel formats.</summary>
internal static class PlanarConverter {

  /// <summary>
  ///   Converts interleaved planar data to chunky (one byte per pixel, each byte is a palette index).
  ///   Each scanline has <paramref name="numPlanes"/> bitplane rows.
  ///   Each bitplane row is ceil(width/16)*2 bytes (word-aligned).
  /// </summary>
  public static byte[] PlanarToChunky(ReadOnlySpan<byte> planarData, int width, int height, int numPlanes)
    => Core.PlanarConverter.IlbmPlanarToChunky(planarData, width, height, numPlanes);

  /// <summary>
  ///   Converts chunky pixel data (one byte per pixel) to interleaved planar format.
  ///   Each scanline produces <paramref name="numPlanes"/> bitplane rows, each word-aligned.
  /// </summary>
  public static byte[] ChunkyToPlanar(ReadOnlySpan<byte> chunkyData, int width, int height, int numPlanes)
    => Core.PlanarConverter.ChunkyToIlbmPlanar(chunkyData, width, height, numPlanes);
}
