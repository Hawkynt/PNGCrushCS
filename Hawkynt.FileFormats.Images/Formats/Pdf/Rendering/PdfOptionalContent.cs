using System;
using System.Collections.Generic;
using FileFormat.Pdf.Document;

namespace FileFormat.Pdf.Rendering;

/// <summary>
/// Which optional content is shown (PDF 32000-1 §8.11): the default configuration's ON and OFF
/// lists over its base state, and membership dictionaries with their visibility policies.
/// </summary>
internal sealed class PdfOptionalContent {

  private readonly PdfDocument _document;
  private readonly HashSet<object> _off = new(ReferenceEqualityComparer.Instance);
  private readonly HashSet<object> _on = new(ReferenceEqualityComparer.Instance);
  private readonly bool _baseOn = true;

  public PdfOptionalContent(PdfDocument document) {
    this._document = document;
    var properties = document.Dict(document.Catalog.Raw("OCProperties"));
    var defaults = document.Dict(properties?.Raw("D"));
    if (defaults == null)
      return;

    this._baseOn = document.Name(defaults, "BaseState") is not "OFF";
    foreach (var item in document.Array(defaults.Raw("OFF")) ?? [])
      if (document.Dict(item) is { } group)
        this._off.Add(group);
    foreach (var item in document.Array(defaults.Raw("ON")) ?? [])
      if (document.Dict(item) is { } group)
        this._on.Add(group);
  }

  /// <summary>Whether content marked with this group or membership dictionary is drawn.</summary>
  public bool IsVisible(PdfDict dict, int depth = 0) {
    if (depth > 16)
      return true;

    var type = this._document.Name(dict, "Type");
    if (type == "OCMD") {
      // A visibility expression is evaluated when present; otherwise the policy over the groups.
      if (this._document.Array(dict.Raw("VE")) is { } expression)
        return this._Expression(expression, depth);

      var groups = new List<PdfDict>();
      var ocgs = this._document.Resolve(dict.Raw("OCGs"));
      if (ocgs is PdfDict single)
        groups.Add(single);
      else if (ocgs is List<object?> list)
        foreach (var item in list)
          if (this._document.Dict(item) is { } g)
            groups.Add(g);

      if (groups.Count == 0)
        return true;

      var policy = this._document.Name(dict, "P") ?? "AnyOn";
      var on = 0;
      foreach (var g in groups)
        if (this._GroupOn(g))
          ++on;

      return policy switch {
        "AllOn" => on == groups.Count,
        "AnyOff" => on < groups.Count,
        "AllOff" => on == 0,
        _ => on > 0,
      };
    }

    return this._GroupOn(dict);
  }

  private bool _GroupOn(PdfDict group) {
    if (this._off.Contains(group))
      return false;
    if (this._on.Contains(group))
      return true;
    return this._baseOn;
  }

  private bool _Expression(List<object?> expression, int depth) {
    if (expression.Count == 0)
      return true;

    var op = this._document.Resolve(expression[0]) as string;
    var results = new List<bool>();
    for (var i = 1; i < expression.Count; ++i) {
      var operand = this._document.Resolve(expression[i]);
      if (operand is List<object?> nested)
        results.Add(this._Expression(nested, depth + 1));
      else if (operand is PdfDict group)
        results.Add(this.IsVisible(group, depth + 1));
    }

    return op switch {
      "And" => results.TrueForAll(static r => r),
      "Or" => results.Exists(static r => r),
      "Not" => results.Count == 0 || !results[0],
      _ => true,
    };
  }
}
