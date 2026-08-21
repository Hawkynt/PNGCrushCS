using System;
using System.Collections.Generic;

namespace FileFormat.Codecs.Vp3;

/// <summary>
/// One of VP3's variable-length code tables, held as the binary tree the codes describe.
/// </summary>
/// <remarks>
/// The codes are given as the strings the Theora specification prints them as — <c>"01001110"</c> —
/// rather than as a length and a number, so a table can be read straight across from the page it was
/// transcribed from.
/// <para/>
/// Construction is a check on the transcription. Walking each code into the tree fails if the walk
/// passes through a leaf or ends on one already taken, which is what one code being a prefix of
/// another looks like; and when every code is in, any node still missing a child means the tree is
/// not full, so some sequence of bits would decode to nothing. VP3's codebooks are all full and
/// prefix-free, so a code that lost or gained a bit in transcription breaks one test or the other.
/// Neither test catches a code that is unique, keeps the tree full, and names the wrong value.
/// <para/>
/// Decoding walks the tree one bit at a time rather than peeking a fixed width and indexing a flat
/// table. The widest code VP3 has is fifteen bits, so a flat table would be thirty-two thousand cells
/// per codebook and there are eighty codebooks; the tree is sixty-three nodes.
/// </remarks>
internal sealed class Vp3VlcTable {

  /// <summary>A child link that ends the code rather than continuing it; the value is beside it.</summary>
  private const int _LEAF = -1;

  /// <summary>A child link no code has reached, which node zero can never be because it is the root.</summary>
  private const int _UNSET = 0;

  private readonly string _name;

  /// <summary>
  /// Two entries per node, the zero branch then the one: a node index, <see cref="_UNSET"/> or
  /// <see cref="_LEAF"/>.
  /// </summary>
  private readonly int[] _children;

  /// <summary>The value each leaf names, beside its entry in <see cref="_children"/>.</summary>
  /// <remarks>
  /// Beside rather than folded into the link, because the values include negative ones — a motion
  /// vector component of minus one is a value like any other — and there is no arithmetic that packs
  /// a signed value and two markers into one number without one of them meaning two things.
  /// </remarks>
  private readonly int[] _values;

  /// <summary>
  /// Builds a table from entries written as <c>"code:value code:value …"</c>.
  /// </summary>
  internal Vp3VlcTable(string name, string entries) : this(name, _Parse(entries)) { }

  internal Vp3VlcTable(string name, params (string Code, int Value)[] entries) {
    this._name = name;

    var children = new List<int> { _UNSET, _UNSET };
    var values = new List<int> { 0, 0 };
    foreach (var (code, value) in entries) {
      if (code.Length is 0 or > 32)
        throw new ArgumentException($"{name}: the code '{code}' is not between one and thirty-two bits long.");

      var node = 0;
      for (var i = 0; i < code.Length; ++i) {
        var slot = node + (code[i] == '1' ? 1 : 0);
        var last = i == code.Length - 1;
        var link = children[slot];

        if (last) {
          if (link != _UNSET)
            throw new ArgumentException(
              $"{name}: the code '{code}' collides with one already in the table, so the two are not a prefix code.");

          children[slot] = _LEAF;
          values[slot] = value;
          break;
        }

        if (link == _LEAF)
          throw new ArgumentException(
            $"{name}: the code '{code}' continues past a shorter code, so the two are not a prefix code.");

        if (link == _UNSET) {
          link = children.Count;
          children[slot] = link;
          children.Add(_UNSET);
          children.Add(_UNSET);
          values.Add(0);
          values.Add(0);
        }

        node = link;
      }
    }

    for (var i = 0; i < children.Count; ++i)
      if (children[i] == _UNSET)
        throw new ArgumentException(
          $"{name}: the codes do not fill their tree, so some sequence of bits decodes to nothing. "
          + $"A code ending in '{((i & 1) == 0 ? '0' : '1')}' is missing.");

    this._children = [.. children];
    this._values = [.. values];
  }

  /// <summary>Reads one code and returns the value it names.</summary>
  internal int Read(Vp3BitReader reader) {
    var node = 0;
    for (var depth = 0; depth <= 32; ++depth) {
      var slot = node + reader.ReadBit();
      var link = this._children[slot];
      if (link == _LEAF)
        return this._values[slot];

      node = link;
    }

    throw new InvalidOperationException($"{this._name}: the tree is deeper than any code in it.");
  }

  private static (string Code, int Value)[] _Parse(string entries) {
    var parts = entries.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    var parsed = new (string, int)[parts.Length];
    for (var i = 0; i < parts.Length; ++i) {
      var split = parts[i].IndexOf(':');
      parsed[i] = (parts[i][..split], int.Parse(parts[i][(split + 1)..]));
    }

    return parsed;
  }
}
