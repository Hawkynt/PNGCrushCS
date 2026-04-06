using System;
using System.IO;
using System.Text;

namespace FileFormat.Pds;

/// <summary>Reads PDS (NASA Planetary Data System) files from bytes, streams, or file paths.</summary>
public static class PdsReader {

  private const string _MAGIC = "PDS_VERSION_ID";
  private const int _MIN_HEADER_SIZE = 32;

  public static PdsFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("PDS file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static PdsFile FromStream(Stream stream) {
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

  public static PdsFile FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static PdsFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < _MIN_HEADER_SIZE)
      throw new InvalidDataException("Data too small for a valid PDS file.");

    var prefix = Encoding.ASCII.GetString(data, 0, Math.Min(_MAGIC.Length, data.Length));
    if (!prefix.Equals(_MAGIC, StringComparison.Ordinal))
      throw new InvalidDataException("Invalid PDS signature: header must start with 'PDS_VERSION_ID'.");

    var (labels, imageOffset) = PdsHeaderParser.Parse(data);

    var width = _GetIntLabel(labels, "LINE_SAMPLES");
    var height = _GetIntLabel(labels, "LINES");
    var sampleBits = _GetIntLabel(labels, "SAMPLE_BITS");
    var bands = labels.ContainsKey("BANDS") ? _GetIntLabel(labels, "BANDS") : 1;

    var sampleType = _ParseSampleType(_GetStringLabel(labels, "SAMPLE_TYPE", "UNSIGNED_INTEGER"), sampleBits);
    var bandStorage = bands > 1
      ? _ParseBandStorage(_GetStringLabel(labels, "BAND_STORAGE_TYPE", "BAND_SEQUENTIAL"))
      : PdsBandStorage.BandSequential;

    var bytesPerSample = sampleBits / 8;
    var expectedPixelBytes = width * height * bands * bytesPerSample;
    var available = data.Length - imageOffset;
    var copyLen = Math.Min(expectedPixelBytes, Math.Max(0, available));

    var pixelData = new byte[expectedPixelBytes];
    if (copyLen > 0)
      data.AsSpan(imageOffset, copyLen).CopyTo(pixelData.AsSpan(0));

    return new PdsFile {
      Width = width,
      Height = height,
      SampleBits = sampleBits,
      Bands = bands,
      SampleType = sampleType,
      BandStorage = bandStorage,
      PixelData = pixelData,
      Labels = labels
    };
  }

  private static int _GetIntLabel(System.Collections.Generic.Dictionary<string, string> labels, string key) {
    if (!labels.TryGetValue(key, out var val) || !int.TryParse(val, out var result))
      throw new InvalidDataException($"Missing or invalid PDS label: {key}.");

    return result;
  }

  private static string _GetStringLabel(System.Collections.Generic.Dictionary<string, string> labels, string key, string defaultValue)
    => labels.TryGetValue(key, out var val) ? val : defaultValue;

  private static PdsSampleType _ParseSampleType(string sampleTypeStr, int sampleBits) {
    var upper = sampleTypeStr.ToUpperInvariant();
    if (sampleBits == 8)
      return PdsSampleType.UnsignedByte;

    return upper switch {
      "MSB_UNSIGNED_INTEGER" => PdsSampleType.MsbUnsigned16,
      "LSB_UNSIGNED_INTEGER" => PdsSampleType.LsbUnsigned16,
      "UNSIGNED_INTEGER" => PdsSampleType.MsbUnsigned16,
      _ => sampleBits <= 8 ? PdsSampleType.UnsignedByte : PdsSampleType.MsbUnsigned16
    };
  }

  private static PdsBandStorage _ParseBandStorage(string bandStorageStr) => bandStorageStr.ToUpperInvariant() switch {
    "BAND_SEQUENTIAL" or "BSQ" => PdsBandStorage.BandSequential,
    "LINE_INTERLEAVED" or "BIL" => PdsBandStorage.LineInterleaved,
    "SAMPLE_INTERLEAVED" or "BIP" => PdsBandStorage.SampleInterleaved,
    _ => throw new InvalidDataException($"Unsupported PDS BAND_STORAGE_TYPE: '{bandStorageStr}'.")
  };
}
