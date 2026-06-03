using System;
using System.IO;
using System.IO.Compression;
using FileFormat.TextMode;

namespace FontBaker;

/// <summary>
/// Bakes the embedded font catalogue: looks for raw ROM dumps in <c>Resources/Fonts/sources/</c>;
/// falls back to procedural generators when a real ROM isn't available. Deflate-compresses each
/// to the corresponding <c>.fnt.dfl</c> next to <c>Resources/Fonts/</c>. Run via:
/// <code>dotnet run --project Tools/FontBaker</code>
/// </summary>
public static class Program {

  public static int Main(string[] args) {
    var resourceRoot = args.Length > 0 ? args[0]
      : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "FileFormats", "FileFormat.TextMode", "Resources", "Fonts"));
    var sourcesDir = Path.Combine(resourceRoot, "sources");
    Directory.CreateDirectory(resourceRoot);
    Console.WriteLine($"Output dir : {resourceRoot}");
    Console.WriteLine($"Sources dir: {sourcesDir} ({(Directory.Exists(sourcesDir) ? "exists" : "MISSING — falling back to procedural")})");

    // Each entry: (output filename, source filename in sources/, expected raw size, procedural fallback, bank).
    // Bank picks which 2048-byte slice of the source to use when the source is 4096 bytes (C64 chargen
    // ROMs are two banks: 0 = uppercase + graphics, 1 = lowercase + uppercase).
    var entries = new (string OutName, string SourceName, int Size, Func<byte[]> Fallback, int Bank)[] {
      ("ibm-vga-8x16.fnt.dfl",       "ibm-vga-8x16.bin",       4096, EraFontGenerator.BuildIbmVga8x16,      0),
      ("ibm-ega-8x14.fnt.dfl",       "ibm-vga-8x14.bin",       3584, EraFontGenerator.BuildIbmEga8x14,      0),
      ("ibm-cga-8x8.fnt.dfl",        "ibm-vga-8x8.bin",        2048, EraFontGenerator.BuildIbmCga8x8,       0),
      ("amiga-topaz-8x16.fnt.dfl",   "amiga-topaz-8x16.bin",   4096, EraFontGenerator.BuildAmigaTopaz8x16,  0),
      ("c64-petscii-8x8.fnt.dfl",    "c64-petscii-8x8.bin",    2048, EraFontGenerator.BuildC64Petscii8x8,   0),
      ("c64-petscii-lo-8x8.fnt.dfl", "c64-petscii-8x8.bin",    2048, EraFontGenerator.BuildC64Petscii8x8,   1),
      ("atari-atascii-8x8.fnt.dfl",  "atari-atascii-8x8.bin",  2048, EraFontGenerator.BuildAtariAtascii8x8, 0),
    };

    foreach (var (outName, sourceName, size, fallback, bank) in entries) {
      var sourcePath = Path.Combine(sourcesDir, sourceName);
      byte[] raw;
      string origin;
      if (File.Exists(sourcePath)) {
        raw = File.ReadAllBytes(sourcePath);
        if (raw.Length != size) {
          // Atari ATASCII is 1024 (128 glyphs × 8) — the embedded loader expects 256-glyph buffers, so mirror.
          if (raw.Length == 1024 && size == 2048) {
            var expanded = new byte[2048];
            Buffer.BlockCopy(raw, 0, expanded, 0, 1024);
            Buffer.BlockCopy(raw, 0, expanded, 1024, 1024);
            raw = expanded;
          } else if (raw.Length == 4096 && size == 2048) {
            // Two-bank PETSCII chargen — slice the requested bank.
            var sliced = new byte[2048];
            Buffer.BlockCopy(raw, bank * 2048, sliced, 0, 2048);
            raw = sliced;
          } else {
            Console.Error.WriteLine($"  WARN {sourceName}: expected {size} bytes, got {raw.Length}; using procedural fallback");
            raw = fallback();
            origin = "procedural (size mismatch)";
            goto write;
          }
        }
        origin = $"ROM dump ({sourceName}{(bank > 0 ? $" bank {bank}" : "")})";
      } else {
        raw = fallback();
        origin = "procedural fallback";
      }
write:
      var outPath = Path.Combine(resourceRoot, outName);
      using (var fs = File.Create(outPath))
      using (var deflate = new DeflateStream(fs, CompressionLevel.SmallestSize, leaveOpen: true))
        deflate.Write(raw, 0, raw.Length);
      var compressed = new FileInfo(outPath).Length;
      Console.WriteLine($"  {outName}: {raw.Length} → {compressed} bytes [{origin}]");
    }

    Console.WriteLine("Done.");
    return 0;
  }
}
