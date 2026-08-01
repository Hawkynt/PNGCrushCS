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

  public static CalsFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length < CalsHeaderParser.HeaderSize)
      throw new InvalidDataException($"Data too small for a valid CALS file: expected at least {CalsHeaderParser.HeaderSize} bytes, got {data.Length}.");

    var headerData = new byte[CalsHeaderParser.HeaderSize];
    data.Slice(0, CalsHeaderParser.HeaderSize).CopyTo(headerData.AsSpan(0));

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

    // The keyword is rorient, and its value is the pair of angles the rows and columns run at —
    // not a word. Looking for "orient" found nothing in any file written elsewhere.
    var orientation = CalsFile.DefaultOrientation;
    if (fields.TryGetValue("rorient", out var orientStr) && !string.IsNullOrWhiteSpace(orientStr))
      orientation = orientStr.Trim();

    var srcDocId = "NONE";
    if (fields.TryGetValue("srcdocid", out var srcId) && !string.IsNullOrWhiteSpace(srcId))
      srcDocId = srcId;

    var dstDocId = "NONE";
    if (fields.TryGetValue("dstdocid", out var dstId) && !string.IsNullOrWhiteSpace(dstId))
      dstDocId = dstId;

    // What follows the header is Group 4 fax coding, not a bitmap. Copying it across as though it
    // were one gives a picture of the right size made of the compressed bytes — which is a picture,
    // just not this one, and small enough files even fill the buffer convincingly.
    var pixelData = FileFormat.Ccitt.CcittG4Decoder.Decode(
      data[CalsHeaderParser.HeaderSize..].ToArray(), width, height);

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

  /// <summary>Reads a CALS raster from a byte array.</summary>
  /// <remarks>
  /// This used to be a second copy of the whole parse rather than a call into the first, and the two
  /// drifted: a fix to one left the other reading the compressed bytes as though they were pixels.
  /// </remarks>
  public static CalsFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
