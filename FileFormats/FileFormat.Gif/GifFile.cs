using System;
using System.Collections.Generic;
using FileFormat.Core;

namespace FileFormat.Gif;

/// <summary>In-memory representation of a GIF file with full GIF87a / GIF89a support, including
/// all standard extensions (NETSCAPE2.0 loop, XMP, ICC, comment, plain-text). Implements the standard
/// FileFormat interface family plus the chunk-layout / chunk-rewrite APIs for metadata rearrangement.</summary>
[FormatMagicBytes([(byte)'G', (byte)'I', (byte)'F', (byte)'8'])]
[FormatMimeType("image/gif")]
public sealed class GifFile :
  IImageFormatReader<GifFile>, IImageToRawImage<GifFile>, IImageFromRawImage<GifFile>,
  IImageFormatWriter<GifFile>, IMultiImageFileFormat<GifFile>,
  IFormatChunkLayout<GifFile>, IFormatChunkRewriter<GifFile>, IFormatChunkPlanRewriter<GifFile> {

  public required GifVersion Version { get; init; }
  public required GifLogicalScreenDescriptor LogicalScreenDescriptor { get; init; }

  /// <summary>Global colour table — packed RGB triplets. <c>null</c> when the LSD's
  /// <see cref="GifLogicalScreenDescriptor.HasGlobalColorTable"/> is false.</summary>
  public byte[]? GlobalColorTable { get; init; }

  /// <summary>Animation loop count parsed from the NETSCAPE2.0 application extension (if present).</summary>
  public LoopCount LoopCount { get; init; }

  public IReadOnlyList<Frame> Frames { get; init; } = Array.Empty<Frame>();

  /// <summary>Default constructor.</summary>
  public GifFile() { }

  /// <summary>Positional constructor matching the external API. <paramref name="version"/> is one of
  /// "87a" or "89a"; anything else is treated as 89a.</summary>
  [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
  public GifFile(
    string version,
    Dimensions logicalScreenSize,
    byte[]? globalColorTable,
    LoopCount loopCount,
    byte backgroundColorIndex,
    IReadOnlyList<Frame> frames) {
    this.Version = version == "87a" ? GifVersion.Gif87a : GifVersion.Gif89a;
    var gctSize = globalColorTable != null ? _SizeExp(globalColorTable.Length / 3) : (byte)0;
    this.LogicalScreenDescriptor = new GifLogicalScreenDescriptor(
      Width: logicalScreenSize.Width,
      Height: logicalScreenSize.Height,
      HasGlobalColorTable: globalColorTable != null,
      ColorResolution: 8,
      GlobalColorTableSorted: false,
      GlobalColorTableSize: gctSize,
      BackgroundColorIndex: backgroundColorIndex,
      PixelAspectRatio: 0);
    this.GlobalColorTable = globalColorTable;
    this.LoopCount = loopCount;
    this.Frames = frames;
  }

  private static byte _SizeExp(int entries) {
    for (byte e = 0; e < 7; ++e)
      if (entries <= 1 << (e + 1)) return e;
    return 7;
  }

  /// <summary>Convenience shortcut for the LSD canvas size matching the external API.</summary>
  public Dimensions LogicalScreenSize =>
    new(this.LogicalScreenDescriptor.Width, this.LogicalScreenDescriptor.Height);

  /// <summary>Convenience shortcut for the LSD background-colour index matching the external API.</summary>
  public byte BackgroundColorIndex => this.LogicalScreenDescriptor.BackgroundColorIndex;
  public IReadOnlyList<GifCommentExtension> Comments { get; init; } = Array.Empty<GifCommentExtension>();
  public IReadOnlyList<GifApplicationExtension> ApplicationExtensions { get; init; } = Array.Empty<GifApplicationExtension>();
  public IReadOnlyList<GifPlainTextExtension> PlainTextExtensions { get; init; } = Array.Empty<GifPlainTextExtension>();

  // ============================================================
  // IImageFormatMetadata
  // ============================================================

  static string IImageFormatMetadata<GifFile>.PrimaryExtension => ".gif";
  static string[] IImageFormatMetadata<GifFile>.FileExtensions => [".gif", ".giff"];
  static FormatCapability IImageFormatMetadata<GifFile>.Capabilities
    => FormatCapability.HasDedicatedOptimizer | FormatCapability.MultiImage;

  static bool? IImageFormatMetadata<GifFile>.MatchesSignature(ReadOnlySpan<byte> header)
    => header.Length >= 6
       && header[0] == 'G' && header[1] == 'I' && header[2] == 'F'
       && header[3] == '8' && (header[4] == '7' || header[4] == '9') && header[5] == 'a'
      ? true : null;

  // ============================================================
  // IImageFormatReader / Writer / ToRaw / FromRaw
  // ============================================================

  static GifFile IImageFormatReader<GifFile>.FromSpan(ReadOnlySpan<byte> data) => GifReader.FromSpan(data);

  static byte[] IImageFormatWriter<GifFile>.ToBytes(GifFile file) => GifWriter.ToBytes(file);

  public static RawImage ToRawImage(GifFile file) => ToRawImage(file, 0);

  public static GifFile FromRawImage(RawImage image) {
    // Single-frame GIF from an indexed source (or quantised through Optimizer.Gif for full-colour input).
    ArgumentNullException.ThrowIfNull(image);
    var indexed = image.Format == PixelFormat.Indexed8 ? image : PixelConverter.Convert(image, PixelFormat.Indexed8);
    var pixels = indexed.PixelData ?? Array.Empty<byte>();
    var palette = indexed.Palette ?? Array.Empty<byte>();
    var paletteEntries = palette.Length / 3;
    if (paletteEntries == 0) throw new ArgumentException("GIF requires a palette.", nameof(image));

    // Pad palette to the next power of two (GIF spec requirement).
    var paddedSize = 1;
    while (paddedSize < paletteEntries) paddedSize <<= 1;
    paddedSize = Math.Max(2, paddedSize);
    var paddedPalette = palette;
    if (paddedSize * 3 != palette.Length) {
      paddedPalette = new byte[paddedSize * 3];
      Array.Copy(palette, paddedPalette, palette.Length);
    }
    var gctSizeExp = 0;
    while (1 << (gctSizeExp + 1) < paddedSize) ++gctSizeExp;

    return new GifFile {
      Version = GifVersion.Gif89a,
      LogicalScreenDescriptor = new GifLogicalScreenDescriptor(
        Width: (ushort)image.Width,
        Height: (ushort)image.Height,
        HasGlobalColorTable: true,
        ColorResolution: 8,
        GlobalColorTableSorted: false,
        GlobalColorTableSize: (byte)gctSizeExp,
        BackgroundColorIndex: 0,
        PixelAspectRatio: 0),
      GlobalColorTable = paddedPalette,
      LoopCount = LoopCount.PlayOnce,
      Frames = [new Frame {
        Left = 0, Top = 0, Width = (ushort)image.Width, Height = (ushort)image.Height,
        PixelData = pixels,
      }],
    };
  }

  // ============================================================
  // IMultiImageFileFormat
  // ============================================================

  public static int ImageCount(GifFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return file.Frames.Count;
  }

  public static RawImage ToRawImage(GifFile file, int index) {
    ArgumentNullException.ThrowIfNull(file);
    if (index < 0 || index >= file.Frames.Count) throw new ArgumentOutOfRangeException(nameof(index));
    var frame = file.Frames[index];
    var palette = frame.LocalColorTable ?? file.GlobalColorTable ?? throw new InvalidOperationException("GIF frame has no palette.");
    var paletteCount = palette.Length / 3;

    byte[]? alphaTable = null;
    if (frame.TransparentColorIndex is { } tIdx) {
      alphaTable = new byte[paletteCount];
      for (var i = 0; i < paletteCount; ++i) alphaTable[i] = 255;
      if (tIdx < paletteCount) alphaTable[tIdx] = 0;
    }

    return new RawImage {
      Width = frame.Width,
      Height = frame.Height,
      Format = PixelFormat.Indexed8,
      PixelData = frame.PixelData,
      Palette = palette,
      PaletteCount = paletteCount,
      AlphaTable = alphaTable,
    };
  }

  public static IReadOnlyList<RawImage> ToRawImages(GifFile file) {
    ArgumentNullException.ThrowIfNull(file);
    var images = new RawImage[file.Frames.Count];
    for (var i = 0; i < images.Length; ++i) images[i] = ToRawImage(file, i);
    return images;
  }

  // ============================================================
  // Chunk-layout / rewrite
  // ============================================================

  static IEnumerable<ChunkSpan> IFormatChunkLayout<GifFile>.EnumerateChunks(ReadOnlySpan<byte> data)
    => GifChunkLayout.Enumerate(data);

  static byte[] IFormatChunkRewriter<GifFile>.Rewrite(ReadOnlySpan<byte> data, IReadOnlyList<ChunkRewriteRule> rules)
    => GifChunkLayout.Rewrite(data, rules);

  static ChunkRewriteResult IFormatChunkPlanRewriter<GifFile>.ApplyPlan(ReadOnlySpan<byte> data, ChunkRewritePlan plan)
    => GifChunkLayout.ApplyPlan(data, plan);
}
