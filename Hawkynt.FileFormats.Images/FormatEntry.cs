using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;

namespace Hawkynt.FileFormats.Images;

/// <summary>
/// All metadata and operations registered for a single image format.
/// Returned by <see cref="FormatRegistry.GetEntry"/> and produced by the source-generated
/// <c>FormatRegistration.RegisterAll()</c> at startup. All function-pointer fields are typed
/// (no reflection) so they trim cleanly under PublishTrimmed and AOT.
/// </summary>
public sealed record FormatEntry(
  ImageFormat Format,
  string Name,
  string PrimaryExtension,
  string[] AllExtensions,
  string[] MimeTypes,
  FormatCapability Capabilities,
  MagicSignature[] MagicSignatures,
  Func<byte[], bool?>? MatchesSignature,
  int DetectionPriority,
  Func<FileInfo, RawImage?> LoadRawImage,
  Func<byte[], RawImage?> LoadRawImageFromBytes,
  Func<RawImage, byte[]>? ConvertFromRawImage,
  Func<byte[], ImageInfo?>? ReadImageInfo = null,
  Func<FileInfo, int>? GetImageCount = null,
  Func<FileInfo, int, RawImage?>? LoadRawImageAtIndex = null,
  Func<FileInfo, IReadOnlyList<RawImage>?>? LoadAllRawImages = null,
  IntegerRange[]? AllowedPaletteRanges = null,
  FixedPalette[]? FixedPalettes = null,
  (IntegerRange Width, IntegerRange Height)[]? AllowedDimensions = null
) {

  /// <summary>The first/preferred MIME type, or <c>"application/octet-stream"</c> if none is registered.</summary>
  public string PrimaryMimeType => this.MimeTypes.Length > 0 ? this.MimeTypes[0] : "application/octet-stream";

  /// <summary>True if this format supports both reading AND writing.</summary>
  public bool SupportsRead => true; // an entry exists only if at least a reader was registered

  /// <summary>True if this format can encode from a <see cref="RawImage"/>.</summary>
  public bool SupportsWrite => this.ConvertFromRawImage != null;

  /// <summary>True if this format exposes multiple sub-images (animated GIF, multi-page TIFF, ICO sets, etc.).</summary>
  public bool SupportsMultiImage => this.GetImageCount != null;
}
