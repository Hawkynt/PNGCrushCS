using System;
using FileFormat.Core;

namespace FileFormat.ShapeTable;

/// <summary>What a .shp file holds, which four unrelated programs disagree about.</summary>
public enum ShapeTableKind {

  /// <summary>A C64 hi-res screen, packed.</summary>
  C64Hires,

  /// <summary>A C64 multicolour screen, packed.</summary>
  C64Multicolor,

  /// <summary>Blazing Paddles' shapes, stored as drawing instructions rather than pixels.</summary>
  Vectors,

  /// <summary>An Atari Graphics 7 screen with its colours after it.</summary>
  AtariGraphics7,

  /// <summary>An unpacked C64 multicolour screen with its planes in an unusual order.</summary>
  Loadstar,
}
