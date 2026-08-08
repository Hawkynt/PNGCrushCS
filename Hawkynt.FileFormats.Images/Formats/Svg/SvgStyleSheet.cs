using System;
using System.Collections.Generic;
using System.Xml.Linq;

namespace FileFormat.Svg;

/// <summary>The rules a drawing's own <c>&lt;style&gt;</c> elements set out.</summary>
/// <remarks>
/// Only the three simple selector forms are read: an element name, a class and an identifier, in
/// comma-separated groups. That is what drawings written by a program use — a converter emitting
/// <c>.brush1{fill:#c0e0ff}</c> and putting <c>class="brush1"</c> on every shape — and the
/// descendant and attribute selectors a hand-written stylesheet might use are left rather than half
/// implemented, because a selector matched by accident paints the wrong shape.
/// </remarks>
public sealed class SvgStyleSheet {

  private readonly record struct Rule(char Kind, string Key, Dictionary<string, string> Declarations);

  private readonly List<Rule> _rules = [];

  /// <summary>Whether the drawing set out no rules at all.</summary>
  public bool IsEmpty => this._rules.Count == 0;

  /// <summary>Reads every <c>&lt;style&gt;</c> element in the document.</summary>
  public static SvgStyleSheet From(XElement root) {
    ArgumentNullException.ThrowIfNull(root);

    var sheet = new SvgStyleSheet();
    foreach (var element in root.DescendantsAndSelf())
      if (element.Name.LocalName == "style")
        sheet._Add(element.Value);

    return sheet;
  }

  /// <summary>Every declaration that applies to an element, outermost rule first.</summary>
  public void Apply(XElement element, Dictionary<string, string> into) {
    if (this._rules.Count == 0)
      return;

    var name = element.Name.LocalName;
    var id = element.Attribute("id")?.Value;
    var classes = (element.Attribute("class")?.Value ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

    // Element, then class, then identifier: the order the cascade gives them, so the more specific
    // rule is written last and wins.
    foreach (var pass in stackalloc[] { 'e', '.', '#' })
    foreach (var rule in this._rules) {
      if (rule.Kind != pass)
        continue;

      var matches = pass switch {
        'e' => rule.Key == name,
        '#' => rule.Key == id,
        _ => Array.IndexOf(classes, rule.Key) >= 0
      };

      if (!matches)
        continue;

      foreach (var (property, value) in rule.Declarations)
        into[property] = value;
    }
  }

  private void _Add(string text) {
    var at = 0;
    while (at < text.Length) {
      var open = text.IndexOf('{', at);
      if (open < 0)
        break;

      var close = text.IndexOf('}', open);
      if (close < 0)
        break;

      var declarations = SvgPresentation.ParseDeclarations(text[(open + 1)..close]);
      foreach (var selector in text[at..open].Split(',')) {
        var key = selector.Trim();
        if (key.Length == 0)
          continue;

        // Anything with a space, a bracket or a colon in it is a selector this does not read, and
        // guessing which elements it meant is exactly what would paint the wrong shape.
        if (key.IndexOfAny([' ', '\t', '\n', '\r', '[', ':', '>', '+', '~', '*']) >= 0)
          continue;

        if (key[0] == '.')
          this._rules.Add(new('.', key[1..], declarations));
        else if (key[0] == '#')
          this._rules.Add(new('#', key[1..], declarations));
        else
          this._rules.Add(new('e', key, declarations));
      }

      at = close + 1;
    }
  }
}
