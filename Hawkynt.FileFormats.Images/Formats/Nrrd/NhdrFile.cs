using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using FileFormat.Core;

namespace FileFormat.Nrrd;

/// <summary>A detached NRRD header (.nhdr) whose encoded payload lives in one or more companion files.</summary>
[FormatDetectionPriority(90)]
public sealed class NhdrFile :
  IImageFormatReader<NhdrFile>, IImageToRawImage<NhdrFile>,
  IImageFromRawImage<NhdrFile>, IImageFormatWriter<NhdrFile> {

  static string IImageFormatMetadata<NhdrFile>.PrimaryExtension => ".nhdr";
  static string[] IImageFormatMetadata<NhdrFile>.FileExtensions => [".nhdr"];
  static NhdrFile IImageFormatReader<NhdrFile>.FromSpan(ReadOnlySpan<byte> data) => NhdrReader.FromSpan(data);
  static NhdrFile IImageFormatReader<NhdrFile>.FromFile(FileInfo file) => NhdrReader.FromFile(file);
  static byte[] IImageFormatWriter<NhdrFile>.ToBytes(NhdrFile file) => NhdrWriter.ToBytes(file);
  static void IImageFormatWriter<NhdrFile>.WriteCompanions(NhdrFile file, FileInfo target) => NhdrWriter.WriteCompanions(file, target);

  /// <summary>The ordinary NRRD data model after detached payloads have been assembled.</summary>
  public NrrdFile Nrrd { get; init; } = new();

  /// <summary>Relative or absolute detached payload name. Writers use one companion file.</summary>
  public string DataFile { get; init; } = "data.raw";

  /// <summary>Recognises a detached header by its required <c>data file:</c> field.</summary>
  public static bool? MatchesSignature(ReadOnlySpan<byte> header) {
    if (header.Length < 4 || !header[..4].SequenceEqual("NRRD"u8))
      return false;

    var text = Encoding.ASCII.GetString(header);
    return text.IndexOf("\ndata file:", StringComparison.OrdinalIgnoreCase) >= 0
      || text.StartsWith("data file:", StringComparison.OrdinalIgnoreCase)
        ? true
        : null;
  }

  public static RawImage ToRawImage(NhdrFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return NrrdFile.ToRawImage(file.Nrrd);
  }

  public static NhdrFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    return new() { Nrrd = NrrdFile.FromRawImage(image), DataFile = "data.raw" };
  }

  public static NhdrFile FromRawImage(RawImage image, FileInfo target) {
    ArgumentNullException.ThrowIfNull(image);
    ArgumentNullException.ThrowIfNull(target);

    return new() {
      Nrrd = NrrdFile.FromRawImage(image),
      DataFile = Path.GetFileNameWithoutExtension(target.Name) + ".raw",
    };
  }
}

/// <summary>Reads detached NRRD headers, including single-file, LIST, and integer printf-pattern payload declarations.</summary>
public static class NhdrReader {

  public static NhdrFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < 8 || !data[..4].SequenceEqual("NRRD"u8))
      throw new InvalidDataException("Data is not a NRRD header.");

    var bytes = data.ToArray();
    var offset = NrrdHeaderParser.FindDataOffset(bytes);
    var header = Encoding.ASCII.GetString(bytes, 0, offset);
    var fields = NrrdHeaderParser.Parse(header);
    if (!fields.ContainsKey("data file"))
      throw new InvalidDataException("Detached NRRD header is missing the required 'data file' field.");

    throw new InvalidDataException("Detached NRRD payloads require a file path so companion files can be resolved.");
  }

  public static NhdrFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Detached NRRD header not found.", file.FullName);

    var headerBytes = File.ReadAllBytes(file.FullName);
    var dataOffset = NrrdHeaderParser.FindDataOffset(headerBytes);
    var headerText = Encoding.ASCII.GetString(headerBytes, 0, dataOffset);
    var fields = NrrdHeaderParser.Parse(headerText);
    if (!fields.TryGetValue("data file", out var dataFileSpec) || string.IsNullOrWhiteSpace(dataFileSpec))
      throw new InvalidDataException("Detached NRRD header is missing the required 'data file' field.");

    var payloadNames = _ResolvePayloadNames(headerText, dataFileSpec);
    if (payloadNames.Count == 0)
      throw new InvalidDataException("Detached NRRD header does not name any payload files.");

    var lineSkip = _ParseOptionalInt(fields, "line skip", 0);
    var byteSkip = _ParseOptionalInt(fields, "byte skip", 0);
    var expectedBytes = _ExpectedRawBytes(fields);

    using var payload = new MemoryStream();
    foreach (var payloadName in payloadNames) {
      var path = _ResolvePath(file.DirectoryName, payloadName);
      if (!File.Exists(path))
        throw new FileNotFoundException($"Detached NRRD payload '{payloadName}' was not found.", path);

      var bytes = File.ReadAllBytes(path);
      var sliced = _ApplySkips(bytes, lineSkip, byteSkip, expectedBytes, payloadNames.Count);
      payload.Write(sliced, 0, sliced.Length);
    }

    // The ordinary reader already owns all encoding/endian/type semantics. Re-use it by appending
    // the detached encoded payload to the original header; unknown detached-only fields are ignored.
    var combined = new byte[dataOffset + checked((int)payload.Length)];
    Buffer.BlockCopy(headerBytes, 0, combined, 0, dataOffset);
    payload.Position = 0;
    payload.ReadExactly(combined.AsSpan(dataOffset));

    return new() {
      Nrrd = NrrdReader.FromBytes(combined),
      DataFile = dataFileSpec,
    };
  }

  private static List<string> _ResolvePayloadNames(string headerText, string spec) {
    if (spec.StartsWith("LIST", StringComparison.OrdinalIgnoreCase))
      return _ParseList(headerText);

    var tokens = spec.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    if (tokens.Length >= 4 && tokens[0].Contains('%')
        && int.TryParse(tokens[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var min)
        && int.TryParse(tokens[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var max)
        && int.TryParse(tokens[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var step)) {
      if (step == 0)
        throw new InvalidDataException("NRRD detached data-file sequence step cannot be zero.");
      if (step > 0 && min > max || step < 0 && min < max)
        throw new InvalidDataException("NRRD detached data-file sequence bounds contradict its step.");

      var result = new List<string>();
      for (var value = min; step > 0 ? value <= max : value >= max; value += step)
        result.Add(_FormatSequenceName(tokens[0], value));
      return result;
    }

    return [_Unquote(spec.Trim())];
  }

  private static List<string> _ParseList(string headerText) {
    var lines = headerText.Replace("\r\n", "\n").Split('\n');
    var result = new List<string>();
    var afterDeclaration = false;

    foreach (var raw in lines) {
      var line = raw.Trim();
      if (!afterDeclaration) {
        if (line.StartsWith("data file:", StringComparison.OrdinalIgnoreCase)
            && line.AsSpan("data file:".Length).TrimStart().StartsWith("LIST", StringComparison.OrdinalIgnoreCase))
          afterDeclaration = true;
        continue;
      }

      if (line.Length == 0)
        break;
      if (line.StartsWith('#'))
        continue;
      result.Add(_Unquote(line));
    }

    return result;
  }

  private static string _FormatSequenceName(string pattern, int value) {
    var percent = pattern.IndexOf('%');
    if (percent < 0)
      return pattern;

    var i = percent + 1;
    var zeroPad = i < pattern.Length && pattern[i] == '0';
    if (zeroPad)
      ++i;
    var widthStart = i;
    while (i < pattern.Length && char.IsDigit(pattern[i]))
      ++i;
    var width = i > widthStart
      ? int.Parse(pattern.AsSpan(widthStart, i - widthStart), CultureInfo.InvariantCulture)
      : 0;
    if (i >= pattern.Length || pattern[i] != 'd')
      throw new InvalidDataException($"Unsupported NRRD data-file sequence format '{pattern}'; expected an integer %d conversion.");

    var number = width > 0
      ? value.ToString((zeroPad ? "D" : "").PadRight(zeroPad ? 1 : 0) + (zeroPad ? width.ToString(CultureInfo.InvariantCulture) : ""), CultureInfo.InvariantCulture)
      : value.ToString(CultureInfo.InvariantCulture);
    if (!zeroPad && width > number.Length)
      number = number.PadLeft(width, ' ');

    return pattern[..percent] + number + pattern[(i + 1)..];
  }

  private static string _Unquote(string value)
    => value.Length >= 2 && value[0] == '"' && value[^1] == '"' ? value[1..^1] : value;

  private static string _ResolvePath(string? directory, string payloadName) {
    if (Path.IsPathRooted(payloadName))
      return Path.GetFullPath(payloadName);
    return Path.GetFullPath(Path.Combine(directory ?? ".", payloadName));
  }

  private static int _ParseOptionalInt(Dictionary<string, string> fields, string name, int defaultValue) {
    if (!fields.TryGetValue(name, out var text))
      return defaultValue;
    if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
      throw new InvalidDataException($"Invalid NRRD {name} value '{text}'.");
    return value;
  }

  private static long _ExpectedRawBytes(Dictionary<string, string> fields) {
    if (!fields.TryGetValue("sizes", out var sizesText) || !fields.TryGetValue("type", out var typeText))
      return 0;

    long count = 1;
    foreach (var size in NrrdHeaderParser.ParseSizes(sizesText))
      count = checked(count * size);

    var bytesPerSample = NrrdHeaderParser.ParseType(typeText) switch {
      NrrdType.Int8 or NrrdType.UInt8 => 1,
      NrrdType.Int16 or NrrdType.UInt16 => 2,
      NrrdType.Int32 or NrrdType.UInt32 or NrrdType.Float => 4,
      NrrdType.Double => 8,
      _ => 0,
    };
    return checked(count * bytesPerSample);
  }

  private static byte[] _ApplySkips(byte[] bytes, int lineSkip, int byteSkip, long expectedBytes, int fileCount) {
    var start = 0;
    for (var line = 0; line < lineSkip; ++line) {
      var newline = Array.IndexOf(bytes, (byte)'\n', start);
      if (newline < 0)
        throw new InvalidDataException("NRRD line skip exceeds detached payload length.");
      start = newline + 1;
    }

    if (byteSkip >= 0)
      start = checked(start + byteSkip);
    else if (byteSkip == -1 && expectedBytes > 0 && fileCount == 1)
      start = checked(bytes.Length - (int)expectedBytes);
    else if (byteSkip < -1)
      throw new InvalidDataException("NRRD byte skip values below -1 are invalid.");

    if ((uint)start > (uint)bytes.Length)
      throw new InvalidDataException("NRRD detached payload skip exceeds file length.");

    return bytes[start..];
  }
}

/// <summary>Writes a detached NRRD header plus its encoded payload companion.</summary>
public static class NhdrWriter {

  public static byte[] ToBytes(NhdrFile file) {
    ArgumentNullException.ThrowIfNull(file);
    if (string.IsNullOrWhiteSpace(file.DataFile))
      throw new InvalidDataException("Detached NRRD requires a payload file name.");
    if (file.DataFile.IndexOfAny(['\r', '\n']) >= 0)
      throw new InvalidDataException("Detached NRRD payload name cannot contain a line break.");

    var inline = NrrdWriter.ToBytes(file.Nrrd);
    var dataOffset = NrrdHeaderParser.FindDataOffset(inline);
    var header = Encoding.ASCII.GetString(inline, 0, dataOffset);
    var separator = header.LastIndexOf("\n\n", StringComparison.Ordinal);
    if (separator < 0)
      throw new InvalidDataException("NRRD writer did not produce a valid header separator.");

    var detached = header[..separator] + "\ndata file: " + file.DataFile + "\n\n";
    return Encoding.ASCII.GetBytes(detached);
  }

  public static void WriteCompanions(NhdrFile file, FileInfo target) {
    ArgumentNullException.ThrowIfNull(file);
    ArgumentNullException.ThrowIfNull(target);

    var inline = NrrdWriter.ToBytes(file.Nrrd);
    var dataOffset = NrrdHeaderParser.FindDataOffset(inline);
    var payload = inline[dataOffset..];
    var path = Path.IsPathRooted(file.DataFile)
      ? Path.GetFullPath(file.DataFile)
      : Path.GetFullPath(Path.Combine(target.DirectoryName ?? ".", file.DataFile));

    if (string.Equals(path, target.FullName, StringComparison.OrdinalIgnoreCase))
      throw new InvalidDataException("Detached NRRD payload cannot overwrite its own header.");

    var directory = Path.GetDirectoryName(path);
    if (!string.IsNullOrEmpty(directory))
      Directory.CreateDirectory(directory);
    File.WriteAllBytes(path, payload);
  }
}
