using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace FileFormat.Core;

/// <summary>One IPTC-IIM dataset: a (record, dataset-number) pair identifying the field, plus its raw
/// value bytes. Kept raw rather than eagerly decoded to string, since IPTC's declared character set
/// (dataset 1:90) is optional and we shouldn't guess wrong and mangle non-ASCII text.</summary>
public readonly record struct IptcDataSet(byte Record, byte DataSet, byte[] Value) {
  /// <summary>Decodes <see cref="Value"/> as UTF-8 — the de facto default for IPTC written by modern
  /// tools when no explicit CodedCharacterSet (1:90) dataset says otherwise.</summary>
  public string AsString() => Encoding.UTF8.GetString(this.Value);
}

/// <summary>Parsed IPTC-IIM (IIM4) datasets, as carried inside a JPEG APP13 Photoshop resource block.
/// PNG has no standard IPTC carrier chunk, so this facet never survives a PNG hop.</summary>
public sealed class IptcData {
  public IReadOnlyList<IptcDataSet> DataSets { get; init; } = [];

  // ---- common record-2 (application) dataset numbers ----
  public const byte RecordApplication = 2;
  public const byte DataSetObjectName = 5;
  public const byte DataSetKeywords = 25;
  public const byte DataSetByLine = 80;
  public const byte DataSetCaptionAbstract = 120;
  public const byte DataSetCopyrightNotice = 116;
  public const byte DataSetCity = 90;

  /// <summary>First dataset matching (record, dataSet), decoded as a string, or <c>null</c> if absent.</summary>
  public string? GetString(byte record, byte dataSet) {
    foreach (var ds in this.DataSets)
      if (ds.Record == record && ds.DataSet == dataSet)
        return ds.AsString();
    return null;
  }

  /// <summary>Every dataset matching (record, dataSet), decoded as strings — IPTC allows repeatable
  /// fields such as Keywords (2:25), one dataset per value.</summary>
  public IEnumerable<string> GetStrings(byte record, byte dataSet)
    => this.DataSets.Where(ds => ds.Record == record && ds.DataSet == dataSet).Select(ds => ds.AsString());
}
