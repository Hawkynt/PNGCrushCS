using System;
using System.Collections.Generic;

namespace FileFormat.Pdf.Fonts;

/// <summary>
/// The vertical alternates of an OpenType font: the single substitutions of its GSUB 'vrt2' feature,
/// or of 'vert' where it has no 'vrt2', for one script and language system.
/// </summary>
/// <remarks>
/// Read from the OpenType specification (GSUB table; ScriptList, LangSys, FeatureList, LookupList,
/// lookup type 1 in both formats, coverage formats 1 and 2, and the type 7 extension that may wrap
/// them). The language system named is used when the script has it, the script's default otherwise;
/// a required feature counts like the others. Coverage tables are searched through rather than
/// bisected, since some CJK fonts do not keep them sorted. Anything that does not add up leaves the
/// glyph as it is.
/// </remarks>
internal sealed class OpenTypeVerticalForms {

  private readonly byte[] _gsub;
  private readonly List<int> _lookups = [];
  private readonly Dictionary<int, int> _cache = [];

  private OpenTypeVerticalForms(byte[] gsub) => this._gsub = gsub;

  /// <summary>Reads the alternates from a GSUB table's bytes, or null when the font has none for the script.</summary>
  public static OpenTypeVerticalForms? Read(byte[]? gsub, string script, string language) {
    if (gsub == null || gsub.Length < 10)
      return null;

    try {
      var forms = new OpenTypeVerticalForms(gsub);
      return forms._Setup(_Tag(script), _Tag(language)) ? forms : null;
    } catch (IndexOutOfRangeException) {
      return null;
    }
  }

  /// <summary>The vertical alternate of a glyph, or the glyph itself when it has none.</summary>
  public int Substitute(int glyphId) {
    if (glyphId < 0)
      return glyphId;

    lock (this._cache) {
      if (this._cache.TryGetValue(glyphId, out var cached))
        return cached;
    }

    var result = glyphId;
    try {
      foreach (var lookup in this._lookups)
        if (this._Lookup(lookup, glyphId) is var gid and > 0) {
          result = gid;
          break;
        }
    } catch (IndexOutOfRangeException) {
      result = glyphId;
    }

    lock (this._cache)
      this._cache[glyphId] = result;
    return result;
  }

  private bool _Setup(uint script, uint language) {
    var data = this._gsub;
    var scriptList = _U16(data, 4);
    var featureList = _U16(data, 6);
    var lookupList = _U16(data, 8);

    var scriptTable = 0;
    var scriptCount = _U16(data, scriptList);
    for (var i = 0; i < scriptCount; ++i) {
      var record = scriptList + 2 + i * 6;
      if (_U32(data, record) == script) {
        scriptTable = scriptList + _U16(data, record + 4);
        break;
      }
    }

    if (scriptTable == 0)
      return false;

    var langSys = 0;
    var langCount = _U16(data, scriptTable + 2);
    for (var i = 0; i < langCount && langSys == 0; ++i) {
      var record = scriptTable + 4 + i * 6;
      if (_U32(data, record) == language && _U16(data, record + 4) != 0)
        langSys = scriptTable + _U16(data, record + 4);
    }

    if (langSys == 0) {
      var fallback = _U16(data, scriptTable);
      if (fallback == 0)
        return false;
      langSys = scriptTable + fallback;
    }

    var features = new List<int>();
    var required = _U16(data, langSys + 2);
    if (required != 0xFFFF)
      features.Add(required);
    var featureCount = _U16(data, langSys + 4);
    for (var i = 0; i < featureCount; ++i)
      features.Add(_U16(data, langSys + 6 + i * 2));

    var vrt2 = 0;
    var vert = 0;
    var totalFeatures = _U16(data, featureList);
    foreach (var index in features) {
      if (index >= totalFeatures)
        continue;
      var record = featureList + 2 + index * 6;
      var tag = _U32(data, record);
      var table = featureList + _U16(data, record + 4);
      if (tag == _Tag("vrt2") && vrt2 == 0)
        vrt2 = table;
      else if (tag == _Tag("vert") && vert == 0)
        vert = table;
    }

    var feature = vrt2 != 0 ? vrt2 : vert;
    if (feature == 0)
      return false;

    var lookupCount = _U16(data, lookupList);
    var count = _U16(data, feature + 2);
    for (var i = 0; i < count; ++i) {
      var index = _U16(data, feature + 4 + i * 2);
      if (index < lookupCount)
        this._lookups.Add(lookupList + _U16(data, lookupList + 2 + index * 2));
    }

    return this._lookups.Count > 0;
  }

  private int _Lookup(int lookup, int glyphId) {
    var data = this._gsub;
    var type = _U16(data, lookup);
    var subtables = _U16(data, lookup + 4);
    for (var i = 0; i < subtables; ++i) {
      var subtable = lookup + _U16(data, lookup + 6 + i * 2);
      var kind = type;
      if (kind == 7) {
        // Extension: format 1, the wrapped lookup type, a 32-bit offset to the real subtable.
        kind = _U16(data, subtable + 2);
        subtable += (int)_U32(data, subtable + 4);
      }

      if (kind != 1)
        continue;

      var format = _U16(data, subtable);
      var index = _CoverageIndex(subtable + _U16(data, subtable + 2), glyphId);
      if (index < 0)
        continue;

      if (format == 1)
        return (glyphId + (short)_U16(data, subtable + 4)) & 0xFFFF;
      if (format == 2 && index < _U16(data, subtable + 4))
        return _U16(data, subtable + 6 + index * 2);
    }

    return 0;
  }

  private int _CoverageIndex(int coverage, int glyphId) {
    var data = this._gsub;
    var format = _U16(data, coverage);
    var count = _U16(data, coverage + 2);
    if (format == 1) {
      for (var i = 0; i < count; ++i)
        if (_U16(data, coverage + 4 + i * 2) == glyphId)
          return i;
    } else if (format == 2) {
      for (var i = 0; i < count; ++i) {
        var record = coverage + 4 + i * 6;
        int start = _U16(data, record), end = _U16(data, record + 2);
        if (glyphId >= start && glyphId <= end)
          return _U16(data, record + 4) + glyphId - start;
      }
    }

    return -1;
  }

  private static uint _Tag(string tag) => (uint)((tag[0] << 24) | (tag[1] << 16) | (tag[2] << 8) | tag[3]);

  private static ushort _U16(byte[] data, int at) => (ushort)((data[at] << 8) | data[at + 1]);

  private static uint _U32(byte[] data, int at) => (uint)((data[at] << 24) | (data[at + 1] << 16) | (data[at + 2] << 8) | data[at + 3]);
}
