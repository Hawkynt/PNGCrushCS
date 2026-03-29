using System;
using System.IO;

namespace FileFormat.Cals;

/// <summary>Reads CALS raster files from bytes, streams, or file paths.</summary>
public static class CalsReader {

  public static CalsFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("CALS file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static CalsFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.CanSeek) {
      var data = new byte[stream.Length - stream.Position];
      stream.ReadExactly(data);
      return FromBytes(data);
    }
    using var ms = new MemoryStream();
    stream.CopyTo(ms);
    return FromBytes(ms.ToArray());
  }

  public static CalsFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < CalsHeaderParser.HeaderSize)
      throw new InvalidDataException($"Data too small for a valid CALS file: expected at least {CalsHeaderParser.HeaderSize} bytes, got {data.Length}.");

    var headerData = new byte[CalsHeaderParser.HeaderSize];
    data.AsSpan(0, CalsHeaderParser.HeaderSize).CopyTo(headerData.AsSpan(0));

    var fields = CalsHeaderParser.ParseAll(headerData);

    // Validate rtype
    if (fields.TryGetValue("rtype", out var rtype) && rtype != "1")
      throw new InvalidDataException($"Unsupported CALS raster type: {rtype}.");

    // Extract dimensions from rpelcnt
    if (!fields.TryGetValue("rpelcnt", out var rpelcnt))
      throw new InvalidDataException("CALS header missing rpelcnt field.");

    var dimParts = rpelcnt.Split(',');
    if (dimParts.Length < 2 || !int.TryParse(dimParts[0].Trim(), out var width) || !int.TryParse(dimParts[1].Trim(), out var height))
      throw new InvalidDataException($"Invalid rpelcnt value: {rpelcnt}.");

    if (width <= 0)
      throw new InvalidDataException($"Invalid CALS width: {width}.");
    if (height <= 0)
      throw new InvalidDataException($"Invalid CALS height: {height}.");

    // Extract optional fields
    var dpi = 200;
    if (fields.TryGetValue("rdensty", out var densityStr) && int.TryParse(densityStr.Trim(), out var parsedDpi))
      dpi = parsedDpi;

    var orientation = "portrait";
    if (fields.TryGetValue("orient", out var orientStr) && !string.IsNullOrWhiteSpace(orientStr))
      orientation = orientStr.Trim().ToLowerInvariant();

    var srcDocId = "NONE";
    if (fields.TryGetValue("srcdocid", out var srcId) && !string.IsNullOrWhiteSpace(srcId))
      srcDocId = srcId;

    var dstDocId = "NONE";
    if (fields.TryGetValue("dstdocid", out var dstId) && !string.IsNullOrWhiteSpace(dstId))
      dstDocId = dstId;

    // Read pixel data
    var bytesPerRow = (width + 7) / 8;
    var expectedPixelBytes = bytesPerRow * height;
    var available = data.Length - CalsHeaderParser.HeaderSize;
    var copyLen = Math.Min(expectedPixelBytes, available);

    var pixelData = new byte[expectedPixelBytes];
    data.AsSpan(CalsHeaderParser.HeaderSize, copyLen).CopyTo(pixelData.AsSpan(0));

    return new CalsFile {
      Width = width,
      Height = height,
      Dpi = dpi,
      Orientation = orientation,
      PixelData = pixelData,
      SrcDocId = srcDocId,
      DstDocId = dstDocId
    };
  }
}
