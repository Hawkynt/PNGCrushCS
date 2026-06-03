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

    // Each entry: (output filename, source filename in sources/, expected raw size, procedural fallback).
    var entries = new (string OutName, string SourceName, int Size, Func<byte[]> Fallback)[] {
      ("ibm-vga-8x16.fnt.dfl",      "ibm-vga-8x16.bin",      4096, EraFontGenerator.BuildIbmVga8x16),
      ("ibm-ega-8x14.fnt.dfl",      "ibm-vga-8x14.bin",      3584, EraFontGenerator.BuildIbmEga8x14),
      ("ibm-cga-8x8.fnt.dfl",       "ibm-vga-8x8.bin",       2048, EraFontGenerator.BuildIbmCga8x8),
      ("amiga-topaz-8x16.fnt.dfl",  "amiga-topaz-8x16.bin",  4096, EraFontGenerator.BuildAmigaTopaz8x16),
      ("c64-petscii-8x8.fnt.dfl",   "c64-petscii-8x8.bin",   2048, EraFontGenerator.BuildC64Petscii8x8),
      ("atari-atascii-8x8.fnt.dfl", "atari-atascii-8x8.bin", 2048, EraFontGenerator.BuildAtariAtascii8x8),
    };

    foreach (var (outName, sourceName, size, fallback) in entries) {
      var sourcePath = Path.Combine(sourcesDir, sourceName);
      byte[] raw;
      string origin;
      if (File.Exists(sourcePath)) {
        raw = File.ReadAllBytes(sourcePath);
        if (raw.Length != size) {
          // Atari ATASCII is 1024 (128 glyphs × 8) — the embedded loader expects 256-glyph buffers, so pad/extend.
          if (raw.Length == 1024 && size == 2048) {
            var expanded = new byte[2048];
            Buffer.BlockCopy(raw, 0, expanded, 0, 1024);
            Buffer.BlockCopy(raw, 0, expanded, 1024, 1024); // mirror low half into high half as a stub
            raw = expanded;
          } else if (raw.Length == 4096 && size == 2048) {
            // Some PETSCII dumps are 2 banks of 2048 — take the first bank.
            var bank0 = new byte[2048];
            Buffer.BlockCopy(raw, 0, bank0, 0, 2048);
            raw = bank0;
          } else {
            Console.Error.WriteLine($"  WARN {sourceName}: expected {size} bytes, got {raw.Length}; using procedural fallback");
            raw = fallback();
            origin = "procedural (size mismatch)";
            goto write;
          }
        }
        origin = $"ROM dump ({sourceName})";
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
