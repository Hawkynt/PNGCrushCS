using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace FileFormat.Pdf.Fonts.Data;

/// <summary>
/// The predefined CMaps of PDF 32000-1 table 118 and the rest of Adobe's cmap-resources, bundled.
/// </summary>
/// <remarks>
/// The data is Adobe's (adobe-type-tools/cmap-resources, BSD-3-Clause; the licence travels beside it
/// as <c>CMaps/LICENSE-cmap-resources.txt</c>), repacked by <c>CMaps/pack_cmaps.py</c> into a compact
/// record form and compressed with Brotli: 138 CMaps — every one of the five current character
/// collections and Adobe-Manga1, all encodings but UTF-8 — in about 260 KB. Every range, single
/// mapping, notdef range, usecmap and WMode of a CMap is kept. The set is read once, the first time a
/// document asks for a predefined CMap.
/// </remarks>
internal static class PdfPredefinedCMaps {

  private const string _Resource = "FileFormat.Pdf.Fonts.Data.CMaps.cmaps.bin.br";

  internal sealed record Record(
    string Name,
    bool Vertical,
    string UseCMap,
    (int Bytes, uint Low, uint High)[] Codespaces,
    (int Bytes, uint Low, uint High, int Cid)[] Mappings,
    (int Bytes, uint Low, uint High, int Cid)[] Notdefs);

  private static readonly Lazy<Dictionary<string, Record>> _All = new(_Load);

  /// <summary>The names of every bundled CMap.</summary>
  public static IReadOnlyCollection<string> Names => _All.Value.Keys;

  /// <summary>A CMap's record by name, or null.</summary>
  public static Record? Find(string name) => _All.Value.TryGetValue(name, out var record) ? record : null;

  private static Dictionary<string, Record> _Load() {
    byte[] data;
    using (var stream = typeof(PdfPredefinedCMaps).Assembly.GetManifestResourceStream(_Resource)
                        ?? throw new InvalidOperationException("The bundled CMaps are missing from the assembly."))
    using (var brotli = new BrotliStream(stream, CompressionMode.Decompress))
    using (var output = new MemoryStream()) {
      brotli.CopyTo(output);
      data = output.ToArray();
    }

    var reader = new _Reader(data);
    if (data.Length < 4 || data[0] != 'P' || data[1] != 'C' || data[2] != 'M' || data[3] != '1')
      throw new InvalidDataException("The bundled CMaps have an unknown format.");

    reader.Position = 4;
    var count = (int)reader.Varint();
    var all = new Dictionary<string, Record>(count, StringComparer.Ordinal);
    for (var i = 0; i < count; ++i) {
      var name = reader.String();
      var vertical = reader.Byte() == 1;
      var use = reader.String();
      var codespaces = new (int, uint, uint)[(int)reader.Varint()];
      for (var c = 0; c < codespaces.Length; ++c)
        codespaces[c] = (reader.Byte(), (uint)reader.Varint(), (uint)reader.Varint());
      var mappings = reader.Entries();
      var notdefs = reader.Entries();
      all[name] = new Record(name, vertical, use, codespaces, mappings, notdefs);
    }

    return all;
  }

  private sealed class _Reader(byte[] data) {
    public int Position;

    public int Byte() => data[this.Position++];

    public ulong Varint() {
      ulong value = 0;
      var shift = 0;
      while (true) {
        var b = data[this.Position++];
        value |= (ulong)(b & 0x7F) << shift;
        if ((b & 0x80) == 0)
          return value;
        shift += 7;
      }
    }

    public long Zigzag() {
      var v = this.Varint();
      return (long)(v >> 1) ^ -(long)(v & 1);
    }

    public string String() {
      var length = (int)this.Varint();
      var s = Encoding.ASCII.GetString(data, this.Position, length);
      this.Position += length;
      return s;
    }

    public (int, uint, uint, int)[] Entries() {
      var entries = new (int, uint, uint, int)[(int)this.Varint()];
      long previousHigh = -1, previousCidEnd = -1;
      for (var i = 0; i < entries.Length; ++i) {
        var bytes = this.Byte();
        var low = previousHigh + 1 + this.Zigzag();
        var high = low + (long)this.Varint();
        var cid = previousCidEnd + 1 + this.Zigzag();
        entries[i] = (bytes, (uint)low, (uint)high, (int)cid);
        previousHigh = high;
        previousCidEnd = cid + (high - low);
      }

      return entries;
    }
  }
}
