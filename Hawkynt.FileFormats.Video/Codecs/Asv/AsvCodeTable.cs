using System.Collections.Generic;
using System.IO;
using FileFormat.Codecs.H263;

namespace FileFormat.Codecs.Asv;

/// <summary>
/// The write side of one of the decoders' variable-length code tables, built from that same table's
/// own entries rather than transcribed a second time.
/// </summary>
/// <remarks>
/// A table written out twice is a table that can disagree with itself, and the disagreement shows up
/// as a picture that decodes to something plausible rather than as an error. So the encoder never
/// holds codes of its own: it inverts the decoder's table at start-up, which also means the
/// construction check that table performs — no code a prefix of another — covers both directions.
/// </remarks>
internal sealed class AsvCodeTable {

  private readonly Dictionary<int, string> _codes = [];
  private readonly string _name;

  internal AsvCodeTable(H263VlcTable table) {
    this._name = table.Name;
    foreach (var (code, value) in table.Entries)
      this._codes[value] = code;
  }

  /// <summary>The code for one value, as the document prints it.</summary>
  /// <exception cref="InvalidDataException">The table has no code for that value.</exception>
  internal string this[int value] => this._codes.TryGetValue(value, out var code)
    ? code
    : throw new InvalidDataException($"{this._name} has no code for the value {value}.");

  /// <summary>Whether the table can state that value at all, which is what selects an escape.</summary>
  internal bool Holds(int value) => this._codes.ContainsKey(value);
}
