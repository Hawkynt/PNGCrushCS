using System;

namespace FileFormat.Core;

/// <summary>
/// Declares a container format's identity: extensions and signature matching. The counterpart of
/// <see cref="IImageFormatMetadata{TSelf}"/> for containers.
/// </summary>
/// <remarks>
/// Separate from the image one rather than shared because the parts of it a container has no meaning
/// for are the larger half. A container has no palette, no dimensions of its own and no video mode
/// in the sense the image side uses the word — those belong to the streams inside it, and a stream
/// is not the file.
/// </remarks>
public interface IVideoFormatMetadata<TSelf> where TSelf : IVideoFormatMetadata<TSelf> {

  /// <summary>The canonical file extension for this container (e.g. ".avi").</summary>
  static abstract string PrimaryExtension { get; }

  /// <summary>All recognised file extensions for this container.</summary>
  static abstract string[] FileExtensions { get; }

  /// <summary>Tests whether the given file header matches this container's signature. Returns
  /// <c>true</c> (match), <c>false</c> (explicitly not this format), or <c>null</c> (no opinion —
  /// fall back to attribute-based matching).</summary>
  static virtual bool? MatchesSignature(ReadOnlySpan<byte> header) => null;
}
