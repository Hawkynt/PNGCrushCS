using System;
using System.IO;
using System.IO.Compression;
using System.Reflection;

namespace FileFormat.TextMode;

/// <summary>
/// Named accessors for fonts baked into the assembly as deflate-compressed embedded resources.
/// Each property exposes a <see cref="Lazy{T}"/>-wrapped <see cref="BitmapFont"/> that decompresses
/// + constructs on first access and caches thereafter — so the 6-font catalogue costs nothing in
/// memory until the user picks one.
/// <para/>
/// The on-disk format is a raw deflate stream of <c>256 × CellHeight</c> bytes (no header). The
/// resource naming convention is <c>{family}.fnt.dfl</c> at logical name <c>{family}.fnt.dfl</c>.
/// To swap in an authentic ROM dump, replace the corresponding <c>.fnt.dfl</c> file under
/// <c>FileFormat.TextMode/Resources/Fonts/</c> with a deflate-compressed version of the new bytes;
/// no loader changes needed.
/// </summary>
public static class BitmapFontEmbedded {

  /// <summary>IBM PC VGA 8×16 — the canonical "DOS text" look.</summary>
  public static BitmapFont IbmVga8x16 => _ibmVga.Value;

  /// <summary>IBM PC EGA 8×14 — 1984 EGA-era text font.</summary>
  public static BitmapFont IbmEga8x14 => _ibmEga.Value;

  /// <summary>IBM PC CGA 8×8 — chunky 1981 CGA font (text and 40-column modes).</summary>
  public static BitmapFont IbmCga8x8 => _ibmCga.Value;

  /// <summary>Amiga Topaz 8×16 — Workbench 1.x default.</summary>
  public static BitmapFont AmigaTopaz8x16 => _topaz.Value;

  /// <summary>Commodore 64 PETSCII 8×8 — C64 character set.</summary>
  public static BitmapFont C64Petscii8x8 => _petscii.Value;

  /// <summary>Atari 8-bit ATASCII 8×8 — Atari 400/800/XL/XE character set.</summary>
  public static BitmapFont AtariAtascii8x8 => _atascii.Value;

  /// <summary>All embedded fonts in display order, paired with a human-readable label for pickers.</summary>
  public static readonly (string Label, Func<BitmapFont> Get, int CellW, int CellH)[] All = [
    ("IBM VGA 8×16",       () => IbmVga8x16,     8, 16),
    ("IBM EGA 8×14",       () => IbmEga8x14,     8, 14),
    ("IBM CGA 8×8",        () => IbmCga8x8,      8, 8),
    ("Amiga Topaz 8×16",   () => AmigaTopaz8x16, 8, 16),
    ("C64 PETSCII 8×8",    () => C64Petscii8x8,  8, 8),
    ("Atari ATASCII 8×8",  () => AtariAtascii8x8, 8, 8),
  ];

  private static readonly Lazy<BitmapFont> _ibmVga  = new(() => _Load("ibm-vga-8x16.fnt.dfl",      8, 16));
  private static readonly Lazy<BitmapFont> _ibmEga  = new(() => _Load("ibm-ega-8x14.fnt.dfl",      8, 14));
  private static readonly Lazy<BitmapFont> _ibmCga  = new(() => _Load("ibm-cga-8x8.fnt.dfl",       8, 8));
  private static readonly Lazy<BitmapFont> _topaz   = new(() => _Load("amiga-topaz-8x16.fnt.dfl",  8, 16));
  private static readonly Lazy<BitmapFont> _petscii = new(() => _Load("c64-petscii-8x8.fnt.dfl",   8, 8));
  private static readonly Lazy<BitmapFont> _atascii = new(() => _Load("atari-atascii-8x8.fnt.dfl", 8, 8));

  private static BitmapFont _Load(string resourceName, int cellW, int cellH) {
    var asm = typeof(BitmapFontEmbedded).Assembly;
    using var stream = _OpenResource(asm, resourceName)
      ?? throw new InvalidOperationException($"Embedded font resource '{resourceName}' not found in {asm.GetName().Name}.");
    using var deflate = new DeflateStream(stream, CompressionMode.Decompress);
    var expected = 256 * cellH;
    var buf = new byte[expected];
    var read = 0;
    int n;
    while (read < expected && (n = deflate.Read(buf, read, expected - read)) > 0) read += n;
    if (read != expected)
      throw new InvalidDataException($"Embedded font '{resourceName}' decompressed to {read} bytes (expected {expected}).");
    return BitmapFont.FromBytes(cellW, cellH, buf);
  }

  // Resources are embedded with their file name as logical name (see FileFormat.TextMode.csproj).
  // Some build configurations prefix with the default namespace + path; we try a few likely names.
  private static Stream? _OpenResource(Assembly asm, string name) {
    var s = asm.GetManifestResourceStream(name);
    if (s != null) return s;
    // Try fully-qualified prefixed name.
    foreach (var candidate in asm.GetManifestResourceNames())
      if (candidate.EndsWith(name, StringComparison.OrdinalIgnoreCase))
        return asm.GetManifestResourceStream(candidate);
    return null;
  }
}
