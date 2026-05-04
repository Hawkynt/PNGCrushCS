using System;

namespace FileFormat.Core;

/// <summary>Declares one or more MIME types associated with this image format.
/// The first entry is the primary/preferred MIME type; subsequent entries are accepted aliases
/// (e.g. <c>image/png</c> primary, <c>image/x-png</c> legacy alias).
/// Extracted at compile time by <c>FileFormat.Registry.Generator</c> and emitted into the
/// generated registry so consumers can look up <c>format → mime</c> and <c>mime → format</c>.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
public sealed class FormatMimeTypeAttribute(params string[] mimeTypes) : Attribute {
  public string[] MimeTypes { get; } = mimeTypes;
  public string PrimaryMimeType => mimeTypes.Length > 0 ? mimeTypes[0] : "application/octet-stream";
}
