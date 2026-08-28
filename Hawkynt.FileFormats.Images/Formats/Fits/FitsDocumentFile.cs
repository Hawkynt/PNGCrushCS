using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using FileFormat.Core;

namespace FileFormat.Fits;

/// <summary>One FITS header/data unit, including arbitrary N-dimensional image arrays and non-image extensions.</summary>
public sealed class FitsHdu {
  public bool IsPrimary { get; init; }
  public string ExtensionType { get; init; } = "IMAGE";
  public long[] Axes { get; init; } = [];
  public FitsBitpix Bitpix { get; init; }
  public long ParameterCount { get; init; }
  public long GroupCount { get; init; } = 1;
  public IReadOnlyList<FitsKeyword> Keywords { get; init; } = [];
  public byte[] Data { get; init; } = [];

  /// <summary>
  /// Whether the HDU can be projected as ordinary raster planes. Random-groups HDUs and extensions
  /// with heap/parameter payloads are retained losslessly but are not misrepresented as pictures.
  /// </summary>
  public bool IsImage
    => ParameterCount == 0
       && GroupCount == 1
       && (IsPrimary || string.Equals(ExtensionType.Trim(), "IMAGE", StringComparison.OrdinalIgnoreCase));
}

/// <summary>Full FITS document model retaining every HDU and exposing every 2D plane through the multi-image contract.</summary>
/// <remarks>
/// <see cref="FitsFile"/> remains the compact single-image API. This document model is the lossless
/// container view for primary arrays, IMAGE extensions, arbitrary higher dimensions, random groups,
/// tables, and extension HDUs that an image conversion does not understand but must not discard.
/// </remarks>
public sealed class FitsDocumentFile :
  IImageFormatReader<FitsDocumentFile>, IImageToRawImage<FitsDocumentFile>,
  IImageFromRawImage<FitsDocumentFile>, IImageFormatWriter<FitsDocumentFile>,
  IMultiImageFileFormat<FitsDocumentFile> {

  static string IImageFormatMetadata<FitsDocumentFile>.PrimaryExtension => ".fits";
  static string[] IImageFormatMetadata<FitsDocumentFile>.FileExtensions => [".fits", ".fit", ".fts"];
  static FormatCapability IImageFormatMetadata<FitsDocumentFile>.Capabilities => FormatCapability.MultiImage;
  static FitsDocumentFile IImageFormatReader<FitsDocumentFile>.FromSpan(ReadOnlySpan<byte> data) => FitsDocumentReader.FromSpan(data);
  static byte[] IImageFormatWriter<FitsDocumentFile>.ToBytes(FitsDocumentFile file) => FitsDocumentWriter.ToBytes(file);

  public IReadOnlyList<FitsHdu> Hdus { get; init; } = [];

  public static bool? MatchesSignature(ReadOnlySpan<byte> header) {
    if (header.Length < 8)
      return null;
    return header[..8].SequenceEqual("SIMPLE  "u8) ? true : null;
  }

  public static FitsDocumentFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    var single = FitsFile.FromRawImage(image);
    var axes = single.Channels is 3 or 4
      ? new long[] { single.Width, single.Height, single.Channels }
      : new long[] { single.Width, single.Height };
    return new() {
      Hdus = [new FitsHdu {
        IsPrimary = true,
        Axes = axes,
        Bitpix = single.Bitpix,
        ParameterCount = 0,
        GroupCount = 1,
        Keywords = single.Keywords ?? [],
        Data = single.PixelData ?? [],
      }],
    };
  }

  public static int ImageCount(FitsDocumentFile file) {
    ArgumentNullException.ThrowIfNull(file);
    var count = 0L;
    foreach (var hdu in file.Hdus)
      count = checked(count + _PlaneCount(hdu));
    return count > int.MaxValue
      ? throw new NotSupportedException("FITS document contains more image planes than the API can index.")
      : (int)count;
  }

  public static RawImage ToRawImage(FitsDocumentFile file)
    => ToRawImage(file, 0);

  public static RawImage ToRawImage(FitsDocumentFile file, int index) {
    ArgumentNullException.ThrowIfNull(file);
    if (index < 0)
      throw new ArgumentOutOfRangeException(nameof(index));

    var remaining = (long)index;
    foreach (var hdu in file.Hdus) {
      var count = _PlaneCount(hdu);
      if (remaining >= count) {
        remaining -= count;
        continue;
      }
      return _DecodePlane(hdu, remaining);
    }

    throw new ArgumentOutOfRangeException(nameof(index), index, "FITS document does not contain that image plane.");
  }

  private static long _PlaneCount(FitsHdu hdu) {
    if (!hdu.IsImage || hdu.Axes.Length < 2 || hdu.Axes[0] < 1 || hdu.Axes[1] < 1)
      return 0;

    var colour = hdu.Axes.Length >= 3 && hdu.Axes[2] is 3 or 4;
    long result = 1;
    var start = colour ? 3 : 2;
    for (var i = start; i < hdu.Axes.Length; ++i) {
      if (hdu.Axes[i] < 1)
        return 0;
      result = checked(result * hdu.Axes[i]);
    }
    return result;
  }

  private static RawImage _DecodePlane(FitsHdu hdu, long planeIndex) {
    var width = checked((int)hdu.Axes[0]);
    var height = checked((int)hdu.Axes[1]);
    var channels = hdu.Axes.Length >= 3 && hdu.Axes[2] is 3 or 4 ? checked((int)hdu.Axes[2]) : 1;
    var bytesPerSample = FitsFile.BytesPerSample(hdu.Bitpix);
    var planeBytes = checked((long)width * height * channels * bytesPerSample);
    var offset = checked(planeIndex * planeBytes);
    if (offset + planeBytes > hdu.Data.LongLength)
      throw new InvalidDataException("FITS HDU is shorter than its declared image axes require.");
    if (planeBytes > int.MaxValue)
      throw new NotSupportedException("FITS image plane exceeds the supported in-memory size.");

    var pixels = new byte[(int)planeBytes];
    Buffer.BlockCopy(hdu.Data, checked((int)offset), pixels, 0, pixels.Length);
    return FitsFile.ToRawImage(new FitsFile {
      Width = width,
      Height = height,
      Channels = channels,
      Bitpix = hdu.Bitpix,
      Keywords = hdu.Keywords,
      PixelData = pixels,
    });
  }
}

public static class FitsDocumentReader {
  private const int BlockSize = 2880;

  public static FitsDocumentFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < BlockSize)
      throw new InvalidDataException("Data too small for a valid FITS document.");

    var hdus = new List<FitsHdu>();
    var offset = 0;
    var primary = true;

    while (offset + BlockSize <= data.Length) {
      var first = Encoding.ASCII.GetString(data.Slice(offset, 8));
      if (primary) {
        if (!string.Equals(first, "SIMPLE  ", StringComparison.Ordinal))
          throw new InvalidDataException("FITS primary HDU does not begin with SIMPLE.");
      } else if (!string.Equals(first, "XTENSION", StringComparison.Ordinal)) {
        if (_IsPadding(data[offset..]))
          break;
        throw new InvalidDataException($"FITS extension at byte {offset} does not begin with XTENSION.");
      }

      var remaining = data[offset..].ToArray();
      var (keywords, headerLength) = FitsHeaderParser.Parse(remaining);
      if (headerLength <= 0 || offset + headerLength > data.Length)
        throw new InvalidDataException("FITS HDU header is truncated.");

      var bitpix = FitsHeaderParser.GetBitpix(keywords);
      var naxis = FitsHeaderParser.GetIntValue(keywords, "NAXIS");
      if (naxis < 0 || naxis > 999)
        throw new InvalidDataException($"Invalid FITS NAXIS value {naxis}.");

      var axes = new long[naxis];
      for (var axis = 0; axis < naxis; ++axis) {
        axes[axis] = _GetLong(keywords, $"NAXIS{axis + 1}", required: true, 0);
        if (axes[axis] < 0)
          throw new InvalidDataException($"FITS NAXIS{axis + 1} cannot be negative.");
      }

      var pcount = _GetLong(keywords, "PCOUNT", required: false, 0);
      var gcount = _GetLong(keywords, "GCOUNT", required: false, 1);
      if (pcount < 0 || gcount < 1)
        throw new InvalidDataException("FITS PCOUNT/GCOUNT values are invalid.");

      long elements = naxis == 0 ? 0 : 1;
      foreach (var axis in axes)
        elements = checked(elements * axis);
      var dataBytes = checked((long)FitsFile.BytesPerSample(bitpix) * gcount * checked(pcount + elements));
      if (dataBytes > int.MaxValue)
        throw new NotSupportedException("FITS HDU payload exceeds the supported in-memory size.");

      var dataStart = checked(offset + headerLength);
      if (dataStart + dataBytes > data.Length)
        throw new InvalidDataException("FITS HDU payload is truncated.");
      var payload = data.Slice(dataStart, (int)dataBytes).ToArray();
      var extensionType = primary ? "PRIMARY" : _GetString(keywords, "XTENSION") ?? "";

      hdus.Add(new FitsHdu {
        IsPrimary = primary,
        ExtensionType = extensionType,
        Axes = axes,
        Bitpix = bitpix,
        ParameterCount = pcount,
        GroupCount = gcount,
        Keywords = keywords,
        Data = payload,
      });

      offset = checked(dataStart + _Pad((int)dataBytes));
      primary = false;
      if (offset == data.Length)
        break;
    }

    if (hdus.Count == 0)
      throw new InvalidDataException("FITS document contains no HDU.");
    return new() { Hdus = hdus };
  }

  private static long _GetLong(IReadOnlyList<FitsKeyword> keywords, string name, bool required, long defaultValue) {
    foreach (var keyword in keywords)
      if (string.Equals(keyword.Name, name, StringComparison.OrdinalIgnoreCase)) {
        if (keyword.Value != null && long.TryParse(keyword.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
          return value;
        throw new InvalidDataException($"FITS keyword {name} is not a valid integer.");
      }
    if (required)
      throw new InvalidDataException($"Required FITS keyword {name} is missing.");
    return defaultValue;
  }

  private static string? _GetString(IReadOnlyList<FitsKeyword> keywords, string name) {
    foreach (var keyword in keywords)
      if (string.Equals(keyword.Name, name, StringComparison.OrdinalIgnoreCase))
        return keyword.Value;
    return null;
  }

  private static bool _IsPadding(ReadOnlySpan<byte> data) {
    foreach (var value in data)
      if (value is not 0 and not (byte)' ')
        return false;
    return true;
  }

  private static int _Pad(int length) {
    var remainder = length % BlockSize;
    return remainder == 0 ? length : checked(length + BlockSize - remainder);
  }
}

public static class FitsDocumentWriter {
  private const int CardSize = 80;
  private const int BlockSize = 2880;

  public static byte[] ToBytes(FitsDocumentFile file) {
    ArgumentNullException.ThrowIfNull(file);
    if (file.Hdus.Count == 0)
      throw new InvalidDataException("FITS document must contain a primary HDU.");
    if (!file.Hdus[0].IsPrimary)
      throw new InvalidDataException("The first FITS HDU must be primary.");

    using var output = new MemoryStream();
    for (var i = 0; i < file.Hdus.Count; ++i)
      _WriteHdu(output, file.Hdus[i], i == 0);
    return output.ToArray();
  }

  private static void _WriteHdu(Stream output, FitsHdu hdu, bool primary) {
    if (primary != hdu.IsPrimary)
      throw new InvalidDataException(primary ? "First HDU must be primary." : "Only the first HDU may be primary.");
    if (hdu.ParameterCount < 0 || hdu.GroupCount < 1)
      throw new InvalidDataException("FITS PCOUNT/GCOUNT values are invalid.");

    var cards = new List<string>();
    if (primary)
      cards.Add(_Card("SIMPLE", "T", "conforms to FITS standard"));
    else {
      var extension = string.IsNullOrWhiteSpace(hdu.ExtensionType) ? "IMAGE" : hdu.ExtensionType.Trim();
      cards.Add(_Card("XTENSION", $"'{extension.PadRight(8)[..8]}'", "extension type"));
    }
    cards.Add(_Card("BITPIX", ((int)hdu.Bitpix).ToString(CultureInfo.InvariantCulture), "bits per data value"));
    cards.Add(_Card("NAXIS", hdu.Axes.Length.ToString(CultureInfo.InvariantCulture), "number of data axes"));
    for (var axis = 0; axis < hdu.Axes.Length; ++axis) {
      if (hdu.Axes[axis] < 0)
        throw new InvalidDataException($"FITS NAXIS{axis + 1} cannot be negative.");
      cards.Add(_Card($"NAXIS{axis + 1}", hdu.Axes[axis].ToString(CultureInfo.InvariantCulture), null));
    }

    // Extension HDUs require both keywords. Random-groups primary HDUs use them too; keeping the
    // explicit values is what makes a parse/write pass preserve tables and grouped data verbatim.
    if (!primary || hdu.ParameterCount != 0 || hdu.GroupCount != 1) {
      cards.Add(_Card("PCOUNT", hdu.ParameterCount.ToString(CultureInfo.InvariantCulture), "parameter count"));
      cards.Add(_Card("GCOUNT", hdu.GroupCount.ToString(CultureInfo.InvariantCulture), "group count"));
    }

    var mandatory = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "SIMPLE", "XTENSION", "BITPIX", "NAXIS", "PCOUNT", "GCOUNT", "END" };
    for (var axis = 0; axis < hdu.Axes.Length; ++axis)
      mandatory.Add($"NAXIS{axis + 1}");
    foreach (var keyword in hdu.Keywords)
      if (!mandatory.Contains(keyword.Name))
        cards.Add(_Card(keyword.Name, keyword.Value, keyword.Comment));
    cards.Add("END".PadRight(CardSize));

    var headerLength = _Pad(cards.Count * CardSize);
    var header = new byte[headerLength];
    Array.Fill(header, (byte)' ');
    for (var i = 0; i < cards.Count; ++i)
      Encoding.ASCII.GetBytes(cards[i], header.AsSpan(i * CardSize, CardSize));
    output.Write(header);

    long elements = hdu.Axes.Length == 0 ? 0 : 1;
    foreach (var axis in hdu.Axes)
      elements = checked(elements * axis);
    var expected = checked((long)FitsFile.BytesPerSample(hdu.Bitpix) * hdu.GroupCount * checked(hdu.ParameterCount + elements));
    if (hdu.Data.LongLength < expected)
      throw new InvalidDataException($"FITS HDU declares {expected} payload bytes but contains {hdu.Data.LongLength}.");
    if (expected > int.MaxValue)
      throw new NotSupportedException("FITS HDU payload exceeds the supported in-memory size.");
    output.Write(hdu.Data, 0, (int)expected);
    var padded = _Pad((int)expected);
    if (padded > expected)
      output.Write(new byte[padded - (int)expected]);
  }

  private static string _Card(string name, string? value, string? comment) {
    var keyword = name.Length > 8 ? name[..8] : name;
    var text = keyword.PadRight(8);
    if (value != null) {
      text += "= " + value.PadLeft(20);
      if (!string.IsNullOrEmpty(comment))
        text += " / " + comment;
    }
    return text.Length < CardSize ? text.PadRight(CardSize) : text[..CardSize];
  }

  private static int _Pad(int length) {
    var remainder = length % BlockSize;
    return remainder == 0 ? length : checked(length + BlockSize - remainder);
  }
}
