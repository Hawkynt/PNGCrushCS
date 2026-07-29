using System;

namespace FileFormat.Gif;

/// <summary>A Comment Extension (label 0xFE). One or more 1..255-byte sub-blocks of arbitrary text.
/// Typically ASCII or UTF-8 application notes.</summary>
public sealed record GifCommentExtension(byte[] Data);

/// <summary>A Plain-Text Extension (label 0x01) — the original "burn text into the canvas" spec feature
/// most decoders ignore. Preserved for byte-exact round-trips.</summary>
/// <param name="GridLeft">Text grid left position.</param>
/// <param name="GridTop">Text grid top position.</param>
/// <param name="GridWidth">Text grid width.</param>
/// <param name="GridHeight">Text grid height.</param>
/// <param name="CellWidth">Character cell width.</param>
/// <param name="CellHeight">Character cell height.</param>
/// <param name="ForegroundColorIndex">Foreground colour index into the global colour table.</param>
/// <param name="BackgroundColorIndex">Background colour index.</param>
/// <param name="Text">Plain text payload (concatenated sub-blocks).</param>
public sealed record GifPlainTextExtension(
  ushort GridLeft,
  ushort GridTop,
  ushort GridWidth,
  ushort GridHeight,
  byte CellWidth,
  byte CellHeight,
  byte ForegroundColorIndex,
  byte BackgroundColorIndex,
  byte[] Text);

/// <summary>An Application Extension (label 0xFF) — 8-byte ASCII identifier + 3-byte authentication code +
/// arbitrary sub-block payload. Well-known identifiers: <c>NETSCAPE2.0</c> (animation loop),
/// <c>XMP DataXMP</c> (Adobe XMP packet), <c>ICCRGBG1012</c> (ICC profile), <c>ANIMEXTS1.0</c>.</summary>
public sealed record GifApplicationExtension(
  string Identifier,
  byte[] AuthenticationCode,
  byte[] Data) {

  /// <summary>True when this is the NETSCAPE2.0 (or ANIMEXTS1.0) animation-loop extension.
  /// The 8-byte identifier carries the "NETSCAPE"/"ANIMEXTS" portion; the version digits live in
  /// the 3-byte <see cref="AuthenticationCode"/> field per spec.</summary>
  public bool IsNetscapeLoop =>
    (this.Identifier is "NETSCAPE" or "ANIMEXTS")
    && this.Data.Length >= 3
    && this.Data[0] == 0x01;

  /// <summary>For a NETSCAPE2.0 loop extension, the loop count (0 = infinite). Returns null when this is
  /// some other extension.</summary>
  public ushort? NetscapeLoopCount =>
    this.IsNetscapeLoop
      ? (ushort)(this.Data[1] | (this.Data[2] << 8))
      : null;

  /// <summary>True when this is the Adobe XMP application extension.</summary>
  public bool IsXmp => this.Identifier == "XMP Data"; // XMP packs id "XMP DataXMP" - first 8 chars = identifier

  /// <summary>True when this is the ICC profile application extension.</summary>
  public bool IsIcc => this.Identifier == "ICCRGBG1";  // "ICCRGBG1012" - first 8 chars
}
