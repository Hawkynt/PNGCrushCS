using System;
using System.Collections.Generic;
using System.Text;

namespace FileFormat.Vicar;

/// <summary>Assembles NASA JPL VICAR file bytes from pixel data.</summary>
public static class VicarWriter {

  public static byte[] ToBytes(VicarFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return Assemble(file.PixelData, file.Width, file.Height, file.Bands, file.PixelType, file.Organization, file.IntFormat, file.RealFormat, file.Labels);
  }

  internal static byte[] Assemble(
    byte[] pixelData,
    int width,
    int height,
    int bands,
    VicarPixelType pixelType,
    VicarOrganization organization,
    string intFormat,
    string realFormat,
    Dictionary<string, string>? extraLabels
  ) {
    var bytesPerPixel = VicarReader._BytesPerPixel(pixelType);
    var recSize = width * bytesPerPixel;
    var formatStr = _FormatString(pixelType);
    var orgStr = _OrganizationString(organization);

    var labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    if (extraLabels != null)
      foreach (var kvp in extraLabels)
        if (!_IsReservedKey(kvp.Key))
          labels[kvp.Key] = kvp.Value;

    var coreLabels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
      ["LBLSIZE"] = "0",
      ["FORMAT"] = formatStr,
      ["TYPE"] = "IMAGE",
      ["ORG"] = orgStr,
      ["NL"] = height.ToString(),
      ["NS"] = width.ToString(),
      ["NB"] = bands.ToString(),
      ["RECSIZE"] = recSize.ToString(),
      ["INTFMT"] = intFormat,
      ["REALFMT"] = realFormat
    };

    var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    foreach (var kvp in coreLabels)
      merged[kvp.Key] = kvp.Value;
    foreach (var kvp in labels)
      merged[kvp.Key] = kvp.Value;

    var tempHeader = VicarHeaderParser.Format(merged, 0);
    var rawLen = Encoding.ASCII.GetByteCount(tempHeader);

    var lblSize = recSize > 0
      ? ((rawLen / recSize) + 2) * recSize
      : rawLen + 64;

    merged["LBLSIZE"] = lblSize.ToString();
    var checkHeader = VicarHeaderParser.Format(merged, 0);
    var checkLen = Encoding.ASCII.GetByteCount(checkHeader);
    if (checkLen > lblSize)
      lblSize = recSize > 0
        ? ((checkLen / recSize) + 1) * recSize
        : checkLen + 64;

    merged["LBLSIZE"] = lblSize.ToString();

    var headerText = VicarHeaderParser.Format(merged, lblSize);
    var headerBytes = Encoding.ASCII.GetBytes(headerText);

    var expectedPixelBytes = width * height * bands * bytesPerPixel;
    var result = new byte[lblSize + expectedPixelBytes];

    headerBytes.AsSpan(0, Math.Min(headerBytes.Length, lblSize)).CopyTo(result.AsSpan(0));

    var copyLen = Math.Min(expectedPixelBytes, pixelData.Length);
    if (copyLen > 0)
      pixelData.AsSpan(0, copyLen).CopyTo(result.AsSpan(lblSize));

    return result;
  }

  private static string _FormatString(VicarPixelType pixelType) => pixelType switch {
    VicarPixelType.Byte => "BYTE",
    VicarPixelType.Half => "HALF",
    VicarPixelType.Full => "FULL",
    VicarPixelType.Real => "REAL",
    VicarPixelType.Doub => "DOUB",
    _ => throw new ArgumentOutOfRangeException(nameof(pixelType))
  };

  private static string _OrganizationString(VicarOrganization org) => org switch {
    VicarOrganization.Bsq => "BSQ",
    VicarOrganization.Bil => "BIL",
    VicarOrganization.Bip => "BIP",
    _ => throw new ArgumentOutOfRangeException(nameof(org))
  };

  private static bool _IsReservedKey(string key) => key.ToUpperInvariant() switch {
    "LBLSIZE" or "FORMAT" or "TYPE" or "ORG" or "NL" or "NS" or "NB" or "RECSIZE" or "INTFMT" or "REALFMT" => true,
    _ => false
  };
}
