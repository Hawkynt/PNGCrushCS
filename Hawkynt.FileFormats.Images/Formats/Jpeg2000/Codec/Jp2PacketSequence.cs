using System;
using System.Collections.Generic;

namespace FileFormat.Jpeg2000.Codec;

/// <summary>Identifies one packet inside a tile.</summary>
internal readonly record struct Jp2PacketIndex(int Layer, int Resolution, int Component, int Precinct);

/// <summary>
/// Produces a tile's packets in the order its progression demands (ITU-T T.800 B.12).
/// </summary>
/// <remarks>
/// Layer, resolution and component are plain nested loops. The three position-led orders are not:
/// a packet belongs to a precinct, and precincts of different resolutions cover different areas, so
/// the walk is over the tile's own coordinates in steps of the smallest precinct projected back onto
/// the reference grid, emitting a packet only where that coordinate is the top-left corner of some
/// precinct.
/// </remarks>
internal static class Jp2PacketSequence {

  public static IEnumerable<Jp2PacketIndex> Enumerate(Jp2Image image, Jp2Tile tile) {
    ArgumentNullException.ThrowIfNull(image);
    ArgumentNullException.ThrowIfNull(tile);

    return tile.ProgressionOrder switch {
      0 => _Lrcp(tile),
      1 => _Rlcp(tile),
      2 => _Rpcl(image, tile),
      3 => _Pcrl(image, tile),
      4 => _Cprl(image, tile),
      _ => throw new NotSupportedException($"JPEG 2000 progression order {tile.ProgressionOrder} is not one of the five T.800 defines."),
    };
  }

  private static int _MaxResolutions(Jp2Tile tile) {
    var maximum = 0;
    foreach (var component in tile.Components)
      maximum = Math.Max(maximum, component.Resolutions.Length);
    return maximum;
  }

  private static IEnumerable<Jp2PacketIndex> _Lrcp(Jp2Tile tile) {
    var maxResolutions = _MaxResolutions(tile);
    for (var layer = 0; layer < tile.Layers; ++layer)
      for (var resolution = 0; resolution < maxResolutions; ++resolution)
        for (var component = 0; component < tile.Components.Length; ++component) {
          if (resolution >= tile.Components[component].Resolutions.Length)
            continue;

          var count = tile.Components[component].Resolutions[resolution].PrecinctCount;
          for (var precinct = 0; precinct < count; ++precinct)
            yield return new(layer, resolution, component, precinct);
        }
  }

  private static IEnumerable<Jp2PacketIndex> _Rlcp(Jp2Tile tile) {
    var maxResolutions = _MaxResolutions(tile);
    for (var resolution = 0; resolution < maxResolutions; ++resolution)
      for (var layer = 0; layer < tile.Layers; ++layer)
        for (var component = 0; component < tile.Components.Length; ++component) {
          if (resolution >= tile.Components[component].Resolutions.Length)
            continue;

          var count = tile.Components[component].Resolutions[resolution].PrecinctCount;
          for (var precinct = 0; precinct < count; ++precinct)
            yield return new(layer, resolution, component, precinct);
        }
  }

  private static IEnumerable<Jp2PacketIndex> _Rpcl(Jp2Image image, Jp2Tile tile) {
    _GetStep(image, tile, -1, out var stepX, out var stepY);
    var maxResolutions = _MaxResolutions(tile);

    for (var resolution = 0; resolution < maxResolutions; ++resolution)
      for (var y = tile.Y0; y < tile.Y1; y += stepY - y % stepY)
        for (var x = tile.X0; x < tile.X1; x += stepX - x % stepX)
          for (var component = 0; component < tile.Components.Length; ++component) {
            if (!_TryMapPrecinct(image, tile, component, resolution, x, y, out var precinct))
              continue;

            for (var layer = 0; layer < tile.Layers; ++layer)
              yield return new(layer, resolution, component, precinct);
          }
  }

  private static IEnumerable<Jp2PacketIndex> _Pcrl(Jp2Image image, Jp2Tile tile) {
    _GetStep(image, tile, -1, out var stepX, out var stepY);

    for (var y = tile.Y0; y < tile.Y1; y += stepY - y % stepY)
      for (var x = tile.X0; x < tile.X1; x += stepX - x % stepX)
        for (var component = 0; component < tile.Components.Length; ++component)
          for (var resolution = 0; resolution < tile.Components[component].Resolutions.Length; ++resolution) {
            if (!_TryMapPrecinct(image, tile, component, resolution, x, y, out var precinct))
              continue;

            for (var layer = 0; layer < tile.Layers; ++layer)
              yield return new(layer, resolution, component, precinct);
          }
  }

  private static IEnumerable<Jp2PacketIndex> _Cprl(Jp2Image image, Jp2Tile tile) {
    for (var component = 0; component < tile.Components.Length; ++component) {
      _GetStep(image, tile, component, out var stepX, out var stepY);

      for (var y = tile.Y0; y < tile.Y1; y += stepY - y % stepY)
        for (var x = tile.X0; x < tile.X1; x += stepX - x % stepX)
          for (var resolution = 0; resolution < tile.Components[component].Resolutions.Length; ++resolution) {
            if (!_TryMapPrecinct(image, tile, component, resolution, x, y, out var precinct))
              continue;

            for (var layer = 0; layer < tile.Layers; ++layer)
              yield return new(layer, resolution, component, precinct);
          }
    }
  }

  /// <summary>
  /// The walk's step is the smallest precinct any resolution contributes, measured on the reference
  /// grid rather than in subband coordinates.
  /// </summary>
  private static void _GetStep(Jp2Image image, Jp2Tile tile, int onlyComponent, out int stepX, out int stepY) {
    stepX = 0;
    stepY = 0;

    for (var c = 0; c < tile.Components.Length; ++c) {
      if (onlyComponent >= 0 && c != onlyComponent)
        continue;

      var component = tile.Components[c];
      var grid = image.Components[c];
      var levels = component.Resolutions.Length - 1;

      for (var resolution = 0; resolution < component.Resolutions.Length; ++resolution) {
        var levelNo = levels - resolution;
        var dx = grid.Dx * (1 << (component.Resolutions[resolution].PrecinctWidthExp + levelNo));
        var dy = grid.Dy * (1 << (component.Resolutions[resolution].PrecinctHeightExp + levelNo));
        stepX = stepX == 0 ? dx : Math.Min(stepX, dx);
        stepY = stepY == 0 ? dy : Math.Min(stepY, dy);
      }
    }

    if (stepX <= 0 || stepY <= 0)
      throw new InvalidOperationException("JPEG 2000 progression step collapsed to zero; the precinct sizes are inconsistent.");
  }

  private static bool _TryMapPrecinct(
    Jp2Image image,
    Jp2Tile tile,
    int componentIndex,
    int resolutionIndex,
    int x,
    int y,
    out int precinct
  ) {
    precinct = 0;

    var component = tile.Components[componentIndex];
    if (resolutionIndex >= component.Resolutions.Length)
      return false;

    var grid = image.Components[componentIndex];
    var resolution = component.Resolutions[resolutionIndex];
    var levelNo = component.Resolutions.Length - 1 - resolutionIndex;

    var trx0 = Jp2Math.CeilDiv(tile.X0, grid.Dx << levelNo);
    var try0 = Jp2Math.CeilDiv(tile.Y0, grid.Dy << levelNo);
    var trx1 = Jp2Math.CeilDiv(tile.X1, grid.Dx << levelNo);
    var try1 = Jp2Math.CeilDiv(tile.Y1, grid.Dy << levelNo);

    var spanX = resolution.PrecinctWidthExp + levelNo;
    var spanY = resolution.PrecinctHeightExp + levelNo;

    // A precinct starts here either because the coordinate is a multiple of its projected size, or
    // because it is the tile's own first coordinate and the tile does not begin on that boundary.
    if (!(y % (grid.Dy << spanY) == 0 || (y == tile.Y0 && (try0 << levelNo) % (1 << spanY) != 0)))
      return false;
    if (!(x % (grid.Dx << spanX) == 0 || (x == tile.X0 && (trx0 << levelNo) % (1 << spanX) != 0)))
      return false;

    if (resolution.PrecinctsWide == 0 || resolution.PrecinctsHigh == 0)
      return false;
    if (trx0 == trx1 || try0 == try1)
      return false;

    var column = Jp2Math.FloorDivPow2(Jp2Math.CeilDiv(x, grid.Dx << levelNo), resolution.PrecinctWidthExp)
               - Jp2Math.FloorDivPow2(trx0, resolution.PrecinctWidthExp);
    var row = Jp2Math.FloorDivPow2(Jp2Math.CeilDiv(y, grid.Dy << levelNo), resolution.PrecinctHeightExp)
            - Jp2Math.FloorDivPow2(try0, resolution.PrecinctHeightExp);

    precinct = column + row * resolution.PrecinctsWide;
    return precinct >= 0 && precinct < resolution.PrecinctCount;
  }
}
