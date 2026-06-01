using System;

namespace FileFormat.Core;

/// <summary>Declares format identity: extensions, capabilities, and signature matching.</summary>
public interface IImageFormatMetadata<TSelf> where TSelf : IImageFormatMetadata<TSelf> {

  /// <summary>The canonical file extension for this format (e.g. ".png").</summary>
  static abstract string PrimaryExtension { get; }

  /// <summary>All recognized file extensions for this format (e.g. [".png"]).</summary>
  static abstract string[] FileExtensions { get; }

  /// <summary>Capability flags for this format. Default: <see cref="FormatCapability.None"/>.</summary>
  /// <remarks>
  /// Only flags that don't already follow from <see cref="VideoModes"/> are kept here:
  /// <see cref="FormatCapability.HasDedicatedOptimizer"/> and <see cref="FormatCapability.MultiImage"/>.
  /// Per-mode constraints (dimensions, palette sizes, available palettes) live inside <see cref="VideoModes"/>.
  /// </remarks>
  static virtual FormatCapability Capabilities => FormatCapability.None;

  /// <summary>
  /// The coupled video modes this format supports. Each <see cref="VideoMode"/> is one user-selectable
  /// configuration that binds dimensions, palette-size options, available palettes, and display hints.
  /// Every format declares ≥ 1 <see cref="VideoMode"/>; the default is a single arbitrary-resolution full-colour
  /// mode (used by formats like PNG, BMP, QOI, etc. that impose no dimension or palette constraint).
  /// </summary>
  static virtual VideoMode[] VideoModes => _DefaultVideoModes;

  private static readonly VideoMode[] _DefaultVideoModes = [
    new("Default", [(IntegerRange.Any, IntegerRange.Any)])
  ];

  /// <summary>Tests whether the given file header matches this format's signature. Returns <c>true</c> (match), <c>false</c> (explicitly not this format), or <c>null</c> (no opinion — fall back to attribute-based matching).</summary>
  static virtual bool? MatchesSignature(ReadOnlySpan<byte> header) => null;
}
