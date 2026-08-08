using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace FileFormat.Dxf;

/// <summary>One entity: what it is, the groups that describe it, and anything that follows it.</summary>
/// <remarks>
/// Two entities carry others after them rather than inside them. A POLYLINE is followed by its
/// VERTEX entities and a SEQEND that ends them, and an INSERT with the attributes-follow flag set is
/// followed by its ATTRIB entities and the same SEQEND. Both runs are gathered here so the caller
/// sees one entity rather than a stream it has to re-join.
/// </remarks>
public sealed class DxfEntity {

  /// <summary>What the entity is, as the 0 group names it.</summary>
  public string Type { get; init; } = string.Empty;

  /// <summary>Every group between this entity's 0 group and the next one.</summary>
  public List<DxfPair> Pairs { get; } = [];

  /// <summary>The VERTEX entities of a POLYLINE, in order.</summary>
  public List<DxfEntity> Vertices { get; } = [];

  /// <summary>The value of the first group with this code, or nothing.</summary>
  public string? Text(int code) {
    foreach (var pair in this.Pairs)
      if (pair.Code == code)
        return pair.Value;

    return null;
  }

  /// <summary>The first group with this code as a number, or the fallback when it is absent.</summary>
  /// <exception cref="InvalidDataException">The group is there but its value is not a number.</exception>
  public double Number(int code, double fallback) {
    var text = this.Text(code);
    if (text == null)
      return fallback;

    if (!double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || !double.IsFinite(value))
      throw new InvalidDataException($"Group {code} of a {this.Type} is \"{text}\", which is not a number.");

    return value;
  }

  /// <summary>The first group with this code as a whole number, or the fallback.</summary>
  /// <exception cref="InvalidDataException">The group is there but its value is not a whole number.</exception>
  public int Integer(int code, int fallback) {
    var text = this.Text(code);
    if (text == null)
      return fallback;

    if (!int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
      throw new InvalidDataException($"Group {code} of a {this.Type} is \"{text}\", which is not a whole number.");

    return value;
  }
}

/// <summary>A block definition: its name, where its own origin sits, and what is in it.</summary>
public sealed class DxfBlock {

  /// <summary>The name an INSERT refers to it by.</summary>
  public string Name { get; init; } = string.Empty;

  /// <summary>The point in the block's own coordinates that an INSERT lands on.</summary>
  public double BaseX { get; init; }

  /// <summary>The other coordinate of that point.</summary>
  public double BaseY { get; init; }

  /// <summary>What the block draws.</summary>
  public List<DxfEntity> Entities { get; } = [];
}

/// <summary>
/// The parts of a drawing that say what it looks like: its header variables, its layers, its blocks
/// and its entities.
/// </summary>
/// <remarks>
/// Built by walking the pairs once. The sections are the ones Autodesk's reference names — HEADER
/// carries variables introduced by a 9 group, TABLES carries the LAYER table an entity's BYLAYER
/// colour resolves through, BLOCKS carries the definitions INSERT places, and ENTITIES carries the
/// drawing itself.
/// </remarks>
public sealed class DxfDrawing {

  /// <summary>Each header variable's groups, by the name its 9 group gives.</summary>
  public Dictionary<string, DxfEntity> Header { get; } = new(StringComparer.Ordinal);

  /// <summary>Each layer's colour index, by layer name.</summary>
  public Dictionary<string, int> LayerColours { get; } = new(StringComparer.OrdinalIgnoreCase);

  /// <summary>Each block, by name.</summary>
  public Dictionary<string, DxfBlock> Blocks { get; } = new(StringComparer.OrdinalIgnoreCase);

  /// <summary>What the drawing draws.</summary>
  public List<DxfEntity> Entities { get; } = [];

  /// <summary>Reads the drawing out of its pairs.</summary>
  public static DxfDrawing From(DxfFile file) {
    if (file.Pairs == null)
      throw new InvalidDataException("A drawing with no group codes cannot be read.");

    var drawing = new DxfDrawing();
    var pairs = file.Pairs;

    for (var i = 0; i < pairs.Count; ++i) {
      if (pairs[i].Code != 0 || pairs[i].Value != "SECTION")
        continue;

      // The reader has already checked that a 2 group names the section and that an ENDSEC closes
      // it, so both are known to be there.
      var name = pairs[i + 1].Value;
      var start = i + 2;
      var end = start;
      while (pairs[end].Code != 0 || pairs[end].Value != "ENDSEC")
        ++end;

      switch (name) {
        case "HEADER":
          _ReadHeader(drawing, pairs, start, end);
          break;

        case "TABLES":
          _ReadLayers(drawing, pairs, start, end);
          break;

        case "BLOCKS":
          _ReadBlocks(drawing, pairs, start, end);
          break;

        case "ENTITIES":
          _ReadEntities(drawing.Entities, pairs, start, end, "the ENTITIES section");
          break;
      }

      i = end;
    }

    return drawing;
  }

  /// <summary>A header variable's groups, or nothing when the drawing does not state it.</summary>
  public DxfEntity? Variable(string name) => this.Header.GetValueOrDefault(name);

  private static void _ReadHeader(DxfDrawing drawing, IReadOnlyList<DxfPair> pairs, int start, int end) {
    DxfEntity? current = null;
    for (var i = start; i < end; ++i) {
      if (pairs[i].Code == 9) {
        current = new() { Type = pairs[i].Value };
        drawing.Header[pairs[i].Value] = current;
        continue;
      }

      current?.Pairs.Add(pairs[i]);
    }
  }

  /// <summary>
  /// Picks the LAYER entries out of the TABLES section, which is where an entity's BYLAYER colour
  /// comes from.
  /// </summary>
  private static void _ReadLayers(DxfDrawing drawing, IReadOnlyList<DxfPair> pairs, int start, int end) {
    string? name = null;
    var colour = 7;
    var inLayer = false;

    void Close() {
      if (inLayer && name != null)
        drawing.LayerColours[name] = colour;

      inLayer = false;
      name = null;
      colour = 7;
    }

    for (var i = start; i < end; ++i) {
      if (pairs[i].Code == 0) {
        Close();
        inLayer = pairs[i].Value == "LAYER";
        continue;
      }

      if (!inLayer)
        continue;

      switch (pairs[i].Code) {
        case 2:
          name = pairs[i].Value;
          break;

        // A layer that is turned off is written with its colour negated, and it is still that
        // colour; whether it is drawn is a question about the layer, not about the number.
        case 62 when int.TryParse(pairs[i].Value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value):
          colour = Math.Abs(value);
          break;
      }
    }

    Close();
  }

  private static void _ReadBlocks(DxfDrawing drawing, IReadOnlyList<DxfPair> pairs, int start, int end) {
    var i = start;
    while (i < end) {
      if (pairs[i].Code != 0 || pairs[i].Value != "BLOCK") {
        ++i;
        continue;
      }

      var header = new DxfEntity { Type = "BLOCK" };
      ++i;
      while (i < end && pairs[i].Code != 0)
        header.Pairs.Add(pairs[i++]);

      var block = new DxfBlock {
        Name = header.Text(2) ?? string.Empty,
        BaseX = header.Number(10, 0),
        BaseY = header.Number(20, 0)
      };

      // ENDBLK closes the definition. A block that never reaches one has run into the next block or
      // off the end of the section, and reading it either way would put the wrong shapes in it.
      var body = i;
      var close = body;
      while (close < end && (pairs[close].Code != 0 || pairs[close].Value != "ENDBLK"))
        ++close;

      if (close >= end)
        throw new InvalidDataException($"Block \"{block.Name}\" is never closed by an ENDBLK.");

      _ReadEntities(block.Entities, pairs, body, close, $"block \"{block.Name}\"");
      if (block.Name.Length > 0)
        drawing.Blocks[block.Name] = block;

      i = close + 1;
    }
  }

  /// <summary>Gathers a run of pairs into entities, joining up the ones that carry a trailer.</summary>
  private static void _ReadEntities(List<DxfEntity> into, IReadOnlyList<DxfPair> pairs, int start, int end, string where) {
    var i = start;
    while (i < end) {
      if (pairs[i].Code != 0) {
        ++i;
        continue;
      }

      var entity = new DxfEntity { Type = pairs[i].Value };
      ++i;
      while (i < end && pairs[i].Code != 0)
        entity.Pairs.Add(pairs[i++]);

      // A POLYLINE's vertices follow it, and so do an INSERT's attributes when it says they do.
      // Either run is ended by a SEQEND and by nothing else.
      var trailing = entity.Type == "POLYLINE" || (entity.Type == "INSERT" && entity.Integer(66, 0) == 1);
      if (trailing) {
        var closed = false;
        while (i < end) {
          var follower = new DxfEntity { Type = pairs[i].Value };
          ++i;
          while (i < end && pairs[i].Code != 0)
            follower.Pairs.Add(pairs[i++]);

          if (follower.Type == "SEQEND") {
            closed = true;
            break;
          }

          if (follower.Type == "VERTEX")
            entity.Vertices.Add(follower);
        }

        if (!closed)
          throw new InvalidDataException($"A {entity.Type} in {where} is never ended by a SEQEND.");
      }

      into.Add(entity);
    }
  }
}
