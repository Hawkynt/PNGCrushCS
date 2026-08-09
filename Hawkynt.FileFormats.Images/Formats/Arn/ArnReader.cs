using System;
using System.IO;

namespace FileFormat.Arn;

/// <summary>Reads Astronomical Research Network pictures (.arn) from bytes, streams, or file paths.</summary>
public static class ArnReader {

  public static ArnFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Astronomical Research Network picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static ArnFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.CanSeek) {
      var buffer = new byte[stream.Length - stream.Position];
      stream.ReadExactly(buffer);
      return FromBytes(buffer);
    }

    using var memory = new MemoryStream();
    stream.CopyTo(memory);
    return FromBytes(memory.ToArray());
  }

  public static ArnFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  /// <summary>Whether the first label line is a <c>SIMPLE</c> carrying this format's own value.</summary>
  public static bool HasArnSimpleLine(ReadOnlySpan<byte> data) {
    var at = 0;
    if (!_NextLine(data, ref at, out var line))
      return false;

    _SplitLine(line, out var keyword, out var value);
    return _Is(keyword, ArnFile.SimpleKeyword) && _StartsWith(value, ArnFile.SimpleValuePrefix);
  }

  public static ArnFile FromSpan(ReadOnlySpan<byte> data) {
    if (!HasArnSimpleLine(data))
      throw new InvalidDataException($"Not an Astronomical Research Network picture: its first line is not a {ArnFile.SimpleKeyword} whose value begins \"{ArnFile.SimpleValuePrefix}\".");

    var recordBytes = 0;
    var labelRecords = 0;
    var width = 0;
    var height = 0;
    var sampleBits = 0;
    var insideImage = false;

    var at = 0;
    while (_NextLine(data, ref at, out var line)) {
      _SplitLine(line, out var keyword, out var value);

      if (_Is(keyword, "RECORD_BYTES"))
        recordBytes = _Number(value);
      else if (_Is(keyword, "LABEL_RECORDS"))
        labelRecords = _Number(value);
      else if (_Is(keyword, "OBJECT"))
        insideImage = _Is(value, "IMAGE");
      else if (_Is(keyword, "END_OBJECT"))
        insideImage = false;
      else if (insideImage && _Is(keyword, "LINES"))
        height = _Number(value);
      else if (insideImage && _Is(keyword, "LINE_SAMPLES"))
        width = _Number(value);
      else if (insideImage && _Is(keyword, "SAMPLE_BITS"))
        sampleBits = _Number(value);

      // XnView reads lines to the end of the file and would let the picture's own bytes overwrite a
      // keyword; stopping at the end of the label is the same for a well-formed file and safer.
      if (recordBytes > 0 && labelRecords > 0 && at >= (long)recordBytes * labelRecords)
        break;
    }

    if (recordBytes < 1 || labelRecords < 1)
      throw new InvalidDataException($"Astronomical Research Network: the label states RECORD_BYTES {recordBytes} and LABEL_RECORDS {labelRecords}, which do not say where it ends.");

    if (sampleBits != ArnFile.SupportedSampleBits)
      throw new InvalidDataException($"ARN: Bad BitsPerSample ({sampleBits}).");

    if (width < 1 || height < 1)
      throw new InvalidDataException($"Invalid Astronomical Research Network dimensions: {width}x{height}.");

    var labelEnd = (long)recordBytes * labelRecords;
    var gap = ((ArnFile.GapBeforePalette + recordBytes - 1) / recordBytes) * (long)recordBytes;
    var planeStride = ((ArnFile.PaletteEntries + recordBytes - 1) / recordBytes) * (long)recordBytes;
    var paletteStart = labelEnd + gap;
    var pixelStart = paletteStart + planeStride * 3;
    var needed = (long)width * height;

    if (pixelStart + needed > data.Length)
      throw new InvalidDataException($"A {width}x{height} Astronomical Research Network picture puts its rows at {pixelStart} and needs {needed} bytes, which a file of {data.Length} bytes does not hold.");

    var palette = new byte[ArnFile.PaletteEntries * 3];
    for (var plane = 0; plane < 3; ++plane) {
      var from = paletteStart + planeStride * plane;
      for (var i = 0; i < ArnFile.PaletteEntries; ++i)
        palette[i * 3 + plane] = data[(int)from + i];
    }

    return new() {
      Width = width,
      Height = height,
      RecordBytes = recordBytes,
      LabelRecords = labelRecords,
      Palette = palette,
      PixelData = data.Slice((int)pixelStart, (int)needed).ToArray(),
    };
  }

  /// <summary>Takes the next line, ending it at a carriage return, a line feed or a zero byte.</summary>
  private static bool _NextLine(ReadOnlySpan<byte> data, ref int at, out ReadOnlySpan<byte> line) {
    line = default;
    while (at < data.Length && (data[at] == (byte)'\r' || data[at] == (byte)'\n'))
      ++at;

    if (at >= data.Length)
      return false;

    var start = at;
    while (at < data.Length && data[at] != (byte)'\r' && data[at] != (byte)'\n' && data[at] != 0)
      ++at;

    line = data[start..at];
    while (at < data.Length && (data[at] == (byte)'\r' || data[at] == (byte)'\n' || data[at] == 0))
      ++at;

    return true;
  }

  /// <summary>Splits a label line the way XnView does: the keyword ends at a space, the value begins past any spaces, tabs and the equals sign.</summary>
  private static void _SplitLine(ReadOnlySpan<byte> line, out ReadOnlySpan<byte> keyword, out ReadOnlySpan<byte> value) {
    var i = 0;
    while (i < line.Length && (line[i] == (byte)' ' || line[i] == (byte)'\t'))
      ++i;

    var start = i;
    while (i < line.Length && line[i] != (byte)' ')
      ++i;

    keyword = line[start..i];

    while (i < line.Length && (line[i] == (byte)' ' || line[i] == (byte)'\t' || line[i] == (byte)'='))
      ++i;

    value = line[i..];
  }

  private static bool _Is(ReadOnlySpan<byte> text, string other) {
    if (text.Length != other.Length)
      return false;

    for (var i = 0; i < text.Length; ++i)
      if (text[i] != (byte)other[i])
        return false;

    return true;
  }

  private static bool _StartsWith(ReadOnlySpan<byte> text, string prefix) {
    if (text.Length < prefix.Length)
      return false;

    for (var i = 0; i < prefix.Length; ++i)
      if (text[i] != (byte)prefix[i])
        return false;

    return true;
  }

  /// <summary>Reads the leading decimal of a value the way the converter's own strtol does.</summary>
  private static int _Number(ReadOnlySpan<byte> value) {
    var i = 0;
    while (i < value.Length && (value[i] == (byte)' ' || value[i] == (byte)'\t'))
      ++i;

    var negative = i < value.Length && value[i] == (byte)'-';
    if (negative || (i < value.Length && value[i] == (byte)'+'))
      ++i;

    long result = 0;
    var any = false;
    while (i < value.Length && value[i] is >= (byte)'0' and <= (byte)'9') {
      result = result * 10 + (value[i] - (byte)'0');
      if (result > int.MaxValue)
        return int.MaxValue;

      ++i;
      any = true;
    }

    if (!any)
      return 0;

    return (int)(negative ? -result : result);
  }
}
