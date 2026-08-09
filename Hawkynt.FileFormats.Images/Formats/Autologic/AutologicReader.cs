using System;
using System.IO;

namespace FileFormat.Autologic;

/// <summary>Reads Autologic bitmaps (.gm, .gm2, .gm4) from bytes, streams, or file paths.</summary>
public static class AutologicReader {

  /// <summary>The largest side taken, so a corrupt header cannot ask for an absurd allocation.</summary>
  private const int _MaximumSide = 32767;

  /// <summary>What a plain-form picture is filled with once the data runs out, which is what
  /// XnView ends up with: its byte reader hands back -1 at the end and the low byte is stored.</summary>
  private const byte _PadSample = 0xFF;

  public static AutologicFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Autologic bitmap not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static AutologicFile FromStream(Stream stream) {
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

  public static AutologicFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  public static AutologicFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < AutologicFile.HeaderSize)
      throw new InvalidDataException($"Data too small for an Autologic bitmap (need at least {AutologicFile.HeaderSize} bytes, got {data.Length}).");

    if (!data[..AutologicFile.Magic.Length].SequenceEqual(AutologicFile.Magic))
      throw new InvalidDataException("Not an Autologic bitmap: it does not open with the record tag 0xFF04 and a length of seven words.");

    var width = _ReadWord(data, 4);
    var height = _ReadWord(data, 6);
    // Bytes 8 to 16 are the rest of the opening record and the reader steps straight over them.
    var levels = data[17];

    if (width is < 1 or > _MaximumSide || height is < 1 or > _MaximumSide)
      throw new InvalidDataException($"Invalid Autologic dimensions: {width}x{height}.");

    var pixels = new byte[width * height];
    var at = AutologicFile.HeaderSize;
    if (levels == AutologicFile.RawLevels)
      _DecodePlain(data, ref at, pixels);
    else
      _DecodeCoded(data, ref at, pixels, width, height);

    return new() {
      Width = width,
      Height = height,
      Levels = levels,
      PixelData = pixels,
    };
  }

  /// <summary>The 255 form: records of raw eight bit samples, the record tag not looked at.</summary>
  private static void _DecodePlain(ReadOnlySpan<byte> data, ref int at, byte[] pixels) {
    var remaining = 0;
    for (var i = 0; i < pixels.Length; ++i) {
      if (remaining <= 0) {
        if (at + 4 > data.Length) {
          pixels.AsSpan(i).Fill(_PadSample);
          return;
        }

        at += 2;
        remaining = _ReadWord(data, at) * 2;
        at += 2;
      }

      --remaining;
      pixels[i] = at < data.Length ? data[at] : _PadSample;
      ++at;
    }
  }

  /// <summary>The line-art form: a sample byte has its top bit clear, a repeat count has it set.</summary>
  private static void _DecodeCoded(ReadOnlySpan<byte> data, ref int at, byte[] pixels, int width, int height) {
    var remaining = 0;
    byte last = 0;

    for (var y = 0; y < height; ++y) {
      var row = pixels.AsSpan(y * width, width);
      var x = 0;
      while (x < width) {
        // A record that is not tagged 0xFF08 is still decoded, but it may not leave the row
        // unfinished: XnView raises its error the moment the pair that followed such a header ends
        // short of the row's end, and clears it again at every row it does complete. Files were
        // built both ways and the converter drew one and refused the other.
        var faultyRecord = false;
        if (remaining <= 0) {
          if (at + 4 > data.Length)
            throw new InvalidDataException("The Autologic data ran out before the picture was full.");

          faultyRecord = _ReadWord(data, at) != AutologicFile.DataRecordTag;
          remaining = _ReadWord(data, at + 2) * 2;
          at += 4;
        }

        if (at >= data.Length)
          throw new InvalidDataException("The Autologic data ran out before the picture was full.");

        var b = data[at];
        ++at;
        --remaining;

        int count;
        if (b >= 0x80) {
          // A count on its own repeats whatever was written last.
          count = (b & 0x7F) + 1;
        } else {
          last = b;
          count = 1;
          // A count may follow, but only inside the same record: at the record's end the sample
          // stands for one pixel and any count opening the next record repeats it again.
          if (remaining > 0 && at < data.Length && data[at] >= 0x80) {
            count = (data[at] & 0x7F) + 1;
            ++at;
            --remaining;
          }
        }

        var run = Math.Min(count, width - x);
        row.Slice(x, run).Fill(last);
        x += run;

        if (x < width && faultyRecord)
          throw new InvalidDataException($"An Autologic record that is not tagged 0x{AutologicFile.DataRecordTag:X4} left a row unfinished, which is where the picture stops.");
      }
    }
  }

  private static int _ReadWord(ReadOnlySpan<byte> data, int at) => (data[at] << 8) | data[at + 1];
}
