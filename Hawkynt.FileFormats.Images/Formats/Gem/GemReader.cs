using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.Gem;

/// <summary>Reads a GEM metafile's header and the records after it.</summary>
public static class GemReader {

  /// <summary>Longer than any header the format defines, and it keeps a false match cheap.</summary>
  private const int _MaxHeaderWords = 512;

  /// <summary>More than any of these files holds, and it bounds the work a corrupt count can ask for.</summary>
  private const int _MaxRecords = 1 << 20;

  public static GemFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("GEM metafile not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static GemFile FromStream(Stream stream) {
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

  public static GemFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  public static GemFile FromSpan(ReadOnlySpan<byte> data) {
    var words = data.Length / 2;
    if (words < GemFile.StandardHeaderWords)
      throw new InvalidDataException($"A GEM metafile needs at least {GemFile.StandardHeaderWords} words of header and this has {words}.");

    if (_Word(data, 0) != GemFile.Magic)
      throw new InvalidDataException("Not a GEM metafile: it does not open with 0xFFFF.");

    var headerWords = _Word(data, 1);
    if (headerWords < GemFile.StandardHeaderWords || headerWords > _MaxHeaderWords || headerWords > words)
      throw new InvalidDataException($"A GEM metafile states a header of {headerWords} words, which the file cannot hold.");

    var records = new List<GemRecord>();
    var at = (int)headerWords;
    var terminated = false;

    while (at < words) {
      var opcode = _Word(data, at);
      if (opcode == GemFile.Magic) {
        terminated = true;
        break;
      }

      if (at + GemFile.RecordHeaderWords > words)
        throw new InvalidDataException($"A GEM record at word {at} is cut off by the end of the file.");

      var pointCount = _Word(data, at + 1);
      var integerCount = _Word(data, at + 2);
      var subOpcode = _Word(data, at + 3);
      if (pointCount < 0 || integerCount < 0)
        throw new InvalidDataException($"A GEM record at word {at} states {pointCount} points and {integerCount} integers.");

      var length = GemFile.RecordHeaderWords + pointCount * 2 + integerCount;
      if (at + length > words)
        throw new InvalidDataException($"A GEM record at word {at} runs {length} words past the end of the file.");

      if (records.Count >= _MaxRecords)
        throw new InvalidDataException($"A GEM metafile of more than {_MaxRecords} records is not one of these.");

      records.Add(new(
        opcode,
        subOpcode,
        _Words(data, at + GemFile.RecordHeaderWords, pointCount * 2),
        _Words(data, at + GemFile.RecordHeaderWords + pointCount * 2, integerCount)
      ));

      at += length;
    }

    // Closing the workstation writes the terminator, so a file without one was never finished — and
    // more to the point, a run of bytes that walked to the end without hitting one has not been
    // shown to be a record list at all.
    if (!terminated)
      throw new InvalidDataException("A GEM metafile ends with 0xFFFF and this one does not.");

    return new() {
      Version = _Word(data, 2),
      CoordinateFlag = _Word(data, 3),
      Extent = (_Word(data, 4), _Word(data, 5), _Word(data, 6), _Word(data, 7)),
      PageSize = (_Word(data, 8), _Word(data, 9)),
      Window = (_Word(data, 10), _Word(data, 11), _Word(data, 12), _Word(data, 13)),
      Records = records
    };
  }

  /// <summary>One signed word, low byte first, which is how the format stores every one of them.</summary>
  private static short _Word(ReadOnlySpan<byte> data, int index) => BinaryPrimitives.ReadInt16LittleEndian(data[(index * 2)..]);

  private static short[] _Words(ReadOnlySpan<byte> data, int index, int count) {
    var words = new short[count];
    for (var i = 0; i < count; ++i)
      words[i] = _Word(data, index + i);

    return words;
  }
}
