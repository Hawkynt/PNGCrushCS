using System;

namespace FileFormat.Core;

/// <summary>Declares format identity: extensions, capabilities, and signature matching.</summary>
public interface IImageFormatMetadata<TSelf> where TSelf : IImageFormatMetadata<TSelf> {

  /// <summary>The canonical file extension for this format (e.g. ".png").</summary>
  static abstract string PrimaryExtension { get; }

  /// <summary>All recognized file extensions for this format (e.g. [".png"]).</summary>
  static abstract string[] FileExtensions { get; }

  /// <summary>Capability flags for this format (e.g. MonochromeOnly, IndexedOnly). Default: <see cref="FormatCapability.VariableResolution"/>.</summary>
  static virtual FormatCapability Capabilities => FormatCapability.VariableResolution;

  /// <summary>Tests whether the given file header matches this format's signature. Returns <c>true</c> (match), <c>false</c> (explicitly not this format), or <c>null</c> (no opinion — fall back to attribute-based matching).</summary>
  static virtual bool? MatchesSignature(ReadOnlySpan<byte> header) => null;
}
