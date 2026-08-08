using System.Collections.Generic;

namespace FileFormat.Core;

/// <summary>One PNG-style text annotation (tEXt/zTXt/iTXt keyword + text), also used as the
/// landing spot for a JPEG COM segment (keyword left empty).</summary>
/// <param name="Keyword">PNG "official" keyword (Title, Author, Comment, ...) or empty for a bare
/// JPEG COM segment that carries no keyword of its own.</param>
/// <param name="Text">The annotation body, already decoded to a managed string.</param>
/// <param name="LanguageTag">RFC 3066 language tag from an iTXt chunk, or <c>null</c> for
/// tEXt/zTXt/COM entries which have no language concept.</param>
/// <param name="TranslatedKeyword">UTF-8 translated keyword from an iTXt chunk, or <c>null</c>.</param>
/// <param name="PreferCompression">Write-side hint: emit as zTXt/compressed-iTXt rather than
/// tEXt/plain-iTXt. Ignored by formats without a compression concept (JPEG COM).</param>
public readonly record struct TextMetadataEntry(
  string Keyword,
  string Text,
  string? LanguageTag = null,
  string? TranslatedKeyword = null,
  bool PreferCompression = false);

/// <summary>
/// Platform-independent metadata carried alongside a <see cref="RawImage"/>. Every field is optional —
/// a format that can't hold a given facet simply never populates it, and a writer that can't emit a
/// given facet drops it explicitly (see the per-format metadata codecs) rather than inventing a
/// substitute representation.
/// </summary>
/// <remarks>
/// This is an interchange model, not a byte-exact container: round-tripping a single format through
/// its own codec (e.g. PNG chunks &lt;-&gt; <see cref="ImageMetadata"/> &lt;-&gt; PNG chunks) may
/// reorder or re-encode fields (fixed little-endian EXIF output, tags sorted ascending, etc.) even
/// though no information was lost. Byte-exact preservation for a single format is handled separately,
/// beneath this model, by each reader's raw-chunk passthrough.
/// </remarks>
public sealed class ImageMetadata {

  /// <summary>Parsed EXIF tag data (TIFF IFD0 + optional Exif/GPS sub-IFDs). Carried by PNG's
  /// <c>eXIf</c> chunk and JPEG's EXIF-flavoured APP1 segment.</summary>
  public ExifData? Exif { get; init; }

  /// <summary>Raw XMP packet bytes (UTF-8 XML, as-is — XMP is deliberately not parsed, since any
  /// partial re-serialization risks silently dropping fields our model doesn't know about). Carried
  /// by PNG's <c>XML:com.adobe.xmp</c> iTXt convention and JPEG's XMP-flavoured APP1 segment.</summary>
  public byte[]? XmpPacket { get; init; }

  /// <summary>Parsed IPTC-IIM datasets. Carried by JPEG's Photoshop APP13 segment. PNG has no
  /// standard IPTC carrier, so this never survives a PNG hop.</summary>
  public IptcData? Iptc { get; init; }

  /// <summary>Embedded ICC colour profile bytes (decompressed). Carried by PNG's <c>iCCP</c> chunk.
  /// JPEG APP2 ICC_PROFILE carriage is out of scope here, so this never survives a JPEG hop.</summary>
  public byte[]? IccProfile { get; init; }

  /// <summary>The profile name from PNG's <c>iCCP</c> chunk (Latin-1, 1-79 bytes per spec). Meaningless
  /// without <see cref="IccProfile"/>.</summary>
  public string? IccProfileName { get; init; }

  /// <summary>Horizontal pixel density in dots per inch, when the source format declared an absolute
  /// (not merely aspect-ratio) physical unit. <c>null</c> when the source had no density chunk, or
  /// declared a unit-less aspect ratio only — we do not fabricate a DPI value in that case.</summary>
  public double? DpiX { get; init; }

  /// <summary>Vertical pixel density in dots per inch. See <see cref="DpiX"/>.</summary>
  public double? DpiY { get; init; }

  /// <summary>Free-text annotations: PNG tEXt/zTXt/iTXt entries (keyword-tagged) or JPEG COM segments
  /// (keyword left empty). A codec writing to a format without a keyword concept collapses every
  /// entry into that format's plain-text carrier; see each format's metadata codec for the exact
  /// mapping it uses.</summary>
  public IReadOnlyList<TextMetadataEntry> TextEntries { get; init; } = [];

  /// <summary>True when every facet is absent — a convenience for callers deciding whether it's worth
  /// attaching this instance to a <see cref="RawImage"/> at all.</summary>
  public bool IsEmpty
    => this.Exif == null && this.XmpPacket == null && this.Iptc == null && this.IccProfile == null
       && this.DpiX == null && this.DpiY == null && this.TextEntries.Count == 0;
}
