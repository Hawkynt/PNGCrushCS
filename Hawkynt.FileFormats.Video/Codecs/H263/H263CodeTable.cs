using System.Collections.Generic;
using System.IO;

namespace FileFormat.Codecs.H263;

/// <summary>The write direction of one decoder VLC table, derived from the table itself.</summary>
internal sealed class H263CodeTable {

  private readonly Dictionary<int, string> _codes = [];
  private readonly string _name;

  internal H263CodeTable(H263VlcTable table) {
    this._name = table.Name;
    foreach (var (code, value) in table.Entries)
      this._codes[value] = code;
  }

  internal string this[int value] => this._codes.TryGetValue(value, out var code)
    ? code
    : throw new InvalidDataException($"{this._name} has no code for the value {value}.");
}