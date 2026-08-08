using System;
using System.IO;

namespace FileFormat.CasioQv;

/// <summary>Reads a Casio QV camera file, reassembling the QV-10 generation's stripped stream.</summary>
public static class CasioQvReader {

  public static CasioQvFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Casio QV file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static CasioQvFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.CanSeek) {
      var data = new byte[stream.Length - stream.Position];
      stream.ReadExactly(data);
      return FromBytes(data);
    }

    using var memory = new MemoryStream();
    stream.CopyTo(memory);
    return FromBytes(memory.ToArray());
  }

  public static CasioQvFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  public static CasioQvFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < CasioQvFile.TableOffset)
      throw new InvalidDataException("Data too small to be a Casio QV file.");
    if (!data[..CasioQvFile.Magic.Length].SequenceEqual(CasioQvFile.Magic))
      throw new InvalidDataException("Not a Casio QV file: the four bytes it opens with are not the camera's.");

    var areaCount = (data[4] << 8) | data[5];
    if (areaCount is 0 or > CasioQvFile.MaxAreaCount)
      throw new InvalidDataException($"A Casio QV file describes {areaCount} areas.");

    var tableEnd = CasioQvFile.TableOffset + areaCount * CasioQvFile.DescriptorSize;
    if (tableEnd > data.Length)
      throw new InvalidDataException("A Casio QV file's area table reaches past the end of the file.");

    // Nothing states where an area begins. The offsets are the running sum of the lengths, so the
    // sum landing on the end of the file is what says the table has been read as it was written —
    // one sample carries a single byte of slack past the last area and nothing carries more.
    var at = tableEnd;
    var strippedAt = -1;
    var strippedLength = 0;
    var wholeAt = -1;
    var wholeLength = 0;

    for (var index = 0; index < areaCount; ++index) {
      var descriptor = CasioQvFile.TableOffset + index * CasioQvFile.DescriptorSize;
      var area = (data[descriptor] << 8) | data[descriptor + 1];
      var length = ((long)data[descriptor + 2] << 24) | ((long)data[descriptor + 3] << 16)
                 | ((long)data[descriptor + 4] << 8) | data[descriptor + 5];

      if (length < 0 || at + length > data.Length)
        throw new InvalidDataException($"Area {area} of a Casio QV file states {length} bytes, which the file does not hold.");

      switch (area) {
        case CasioQvFile.AreaStrippedJpeg when length > CasioQvFile.StrippedHeaderSize && strippedAt < 0:
          strippedAt = at;
          strippedLength = (int)length;
          break;
        case CasioQvFile.AreaWholeJpeg when length > 3 && wholeAt < 0:
          wholeAt = at;
          wholeLength = (int)length;
          break;
      }

      at += (int)length;
    }

    if (at > data.Length || data.Length - at > 1)
      throw new InvalidDataException($"A Casio QV file's areas account for {at} of its {data.Length} bytes.");

    // Which of the two the file is is read from the bytes, not from the camera's name: an area that
    // already begins with a start-of-image marker is a whole stream and is handed over untouched.
    if (wholeAt >= 0 && _IsJpeg(data.Slice(wholeAt, wholeLength))) {
      var whole = data.Slice(wholeAt, wholeLength).ToArray();
      var (wholeWidth, wholeHeight) = _FrameSize(whole);
      return new() { Width = wholeWidth, Height = wholeHeight, WasReassembled = false, Jpeg = whole };
    }

    if (strippedAt < 0)
      throw new InvalidDataException("A Casio QV file carries no picture area.");

    var jpeg = _Reassemble(data.Slice(strippedAt, strippedLength));
    var (width, height) = _FrameSize(jpeg);
    return new() { Width = width, Height = height, WasReassembled = true, Jpeg = jpeg };
  }

  private static bool _IsJpeg(ReadOnlySpan<byte> payload)
    => payload.Length > 3 && payload[0] == 0xFF && payload[1] == 0xD8 && payload[2] == 0xFF;

  /// <summary>
  /// Puts the markers, the frame and the Huffman tables back around what the camera stored: the two
  /// quantisation tables out of the payload, then the three scans in the order they were written.
  /// </summary>
  private static byte[] _Reassemble(ReadOnlySpan<byte> payload) {
    var area = (payload[0] << 8) | payload[1];
    if (area != CasioQvFile.AreaStrippedJpeg)
      throw new InvalidDataException($"A Casio QV picture area names itself {area} rather than {CasioQvFile.AreaStrippedJpeg}.");

    var luminance = (payload[2] << 8) | payload[3];
    var blue = (payload[4] << 8) | payload[5];
    var red = (payload[6] << 8) | payload[7];

    // The area's own arithmetic: its header, its two tables and its three scans have to be the whole
    // of it. Without that a payload of some other shape would be reassembled into a stream that
    // decoded to noise rather than being refused.
    var stated = CasioQvFile.StrippedHeaderSize + 2 * CasioQvFile.QuantTableSize + luminance + blue + red;
    if (stated != payload.Length)
      throw new InvalidDataException($"A Casio QV picture area of {payload.Length} bytes accounts for {stated}.");

    using var stream = new MemoryStream();
    stream.Write(CasioQvTables.StartOfImage);
    stream.Write(CasioQvTables.Application0);

    var quantAt = CasioQvFile.StrippedHeaderSize;
    stream.Write(CasioQvTables.LuminanceQuantHeader);
    stream.Write(payload.Slice(quantAt, CasioQvFile.QuantTableSize));
    quantAt += CasioQvFile.QuantTableSize;
    stream.Write(CasioQvTables.ChrominanceQuantHeader);
    stream.Write(payload.Slice(quantAt, CasioQvFile.QuantTableSize));
    quantAt += CasioQvFile.QuantTableSize;

    stream.Write(CasioQvTables.StartOfFrame);
    stream.Write(CasioQvTables.HuffmanTables);

    stream.Write(CasioQvTables.LuminanceScan);
    stream.Write(payload.Slice(quantAt, luminance));
    quantAt += luminance;
    stream.Write(CasioQvTables.BlueDifferenceScan);
    stream.Write(payload.Slice(quantAt, blue));
    quantAt += blue;
    stream.Write(CasioQvTables.RedDifferenceScan);
    stream.Write(payload.Slice(quantAt, red));

    stream.Write(CasioQvTables.EndOfImage);
    return stream.ToArray();
  }

  /// <summary>Finds the size in the stream's own frame header rather than assuming the camera's.</summary>
  private static (int Width, int Height) _FrameSize(ReadOnlySpan<byte> jpeg) {
    var at = 2;
    while (at + 4 <= jpeg.Length) {
      if (jpeg[at] != 0xFF) {
        ++at;
        continue;
      }

      var marker = jpeg[at + 1];
      if (marker is 0xD8 or 0x01 or >= 0xD0 and <= 0xD7) {
        at += 2;
        continue;
      }

      var length = (jpeg[at + 2] << 8) | jpeg[at + 3];
      if (length < 2 || at + 2 + length > jpeg.Length)
        break;

      // Every start-of-frame but the two that are not frames at all states the size the same way.
      if (marker is >= 0xC0 and <= 0xCF && marker is not (0xC4 or 0xC8 or 0xCC)) {
        var height = (jpeg[at + 5] << 8) | jpeg[at + 6];
        var width = (jpeg[at + 7] << 8) | jpeg[at + 8];
        return (width, height);
      }

      if (marker == 0xDA)
        break;

      at += 2 + length;
    }

    throw new InvalidDataException("A Casio QV stream states no frame size.");
  }
}
