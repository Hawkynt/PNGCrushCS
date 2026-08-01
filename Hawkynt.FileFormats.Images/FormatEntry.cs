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
  VideoMode[]? VideoModes = null,
  Func<byte[], IReadOnlyList<ChunkSpan>>? EnumerateChunks = null,
  Func<byte[], IReadOnlyList<ChunkRewriteRule>, byte[]>? RewriteChunks = null,
  Func<byte[], ChunkRewritePlan, ChunkRewriteResult>? ApplyChunkPlan = null,

  /// <summary>Reads a file and lets the reason for a failure out.</summary>
  /// <remarks>
  /// <see cref="LoadRawImage"/> answers null for every kind of failure, which is convenient for a
  /// caller that only wants a picture and useless for one asking why there isn't one. A wrong
  /// length, an unsupported depth and a file that is not this format at all are the same answer,
  /// and telling them apart is most of the work of fixing a reader.
  /// </remarks>
  Func<FileInfo, RawImage?>? LoadRawImageOrThrow = null,

  /// <summary>Writes a picture to a file, with whatever else that format keeps beside it.</summary>
  /// <remarks>
  /// A few formats keep their palette in a separate file and nothing can open the main one without
  /// it. Encoding returns a single array of bytes and so has nowhere to put that, which is why the
  /// write that names a file comes through here instead — and why it encodes once rather than
  /// letting the two halves each read the picture again.
  /// </remarks>
  Action<RawImage, FileInfo>? WriteToFile = null
) {

  /// <summary>The first/preferred MIME type, or <c>"application/octet-stream"</c> if none is registered.</summary>
  public string PrimaryMimeType => this.MimeTypes.Length > 0 ? this.MimeTypes[0] : "application/octet-stream";

  /// <summary>True if this format supports both reading AND writing.</summary>
  public bool SupportsRead => true; // an entry exists only if at least a reader was registered

  /// <summary>True if this format can encode from a <see cref="RawImage"/>.</summary>
  public bool SupportsWrite => this.ConvertFromRawImage != null;


  /// <summary>True if this format exposes multiple sub-images (animated GIF, multi-page TIFF, ICO sets, etc.).</summary>
  public bool SupportsMultiImage => this.GetImageCount != null;

  /// <summary>True if this format exposes its byte-level chunk structure via <see cref="EnumerateChunks"/>.</summary>
  public bool SupportsChunkLayout => this.EnumerateChunks != null;

  /// <summary>True if this format can rewrite its chunk arrangement (re-order, remove, fuse) via <see cref="RewriteChunks"/>.</summary>
  public bool SupportsChunkRewrite => this.RewriteChunks != null;

  /// <summary>True if this format accepts concrete placement plans with validation via <see cref="ApplyChunkPlan"/>.</summary>
  public bool SupportsChunkPlanRewrite => this.ApplyChunkPlan != null;
}
