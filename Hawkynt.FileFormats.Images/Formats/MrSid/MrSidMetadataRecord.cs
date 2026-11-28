using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace FileFormat.MrSid;

/// <summary>One record of a MrSID metadata database, such as <c>IMAGE::WKT</c> or <c>GEOTIFF_NUM::1024::GTModelTypeGeoKey</c>.</summary>
/// <param name="Key">The record's name as stored.</param>
/// <param name="Type">The stored value type.</param>
/// <param name="Dimensions">The shape of the value array; every record seen is one-dimensional.</param>
/// <param name="Numbers">The values of a numeric record, widened to <see cref="double"/>; empty for a string record.</param>
/// <param name="Strings">The values of a string record; empty for a numeric record.</param>
public sealed record MrSidMetadataRecord(
  string Key,
  MrSidMetadataType Type,
  IReadOnlyList<int> Dimensions,
  IReadOnlyList<double> Numbers,
  IReadOnlyList<string> Strings
) {

  /// <summary>The key and its values, the values joined by commas.</summary>
  public override string ToString()
    => this.Type == MrSidMetadataType.Ascii
      ? $"{this.Key} = {string.Join(", ", this.Strings)}"
      : $"{this.Key} = {string.Join(", ", this.Numbers.Select(static n => n.ToString("R", CultureInfo.InvariantCulture)))}";
}
