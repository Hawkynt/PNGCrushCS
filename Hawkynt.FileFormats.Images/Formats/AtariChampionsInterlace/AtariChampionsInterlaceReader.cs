using System;
using System.IO;
using System.Text;

namespace FileFormat.AtariChampionsInterlace;

/// <summary>Reads Champions' Interlace pictures from bytes, streams, or file paths.</summary>
public static class AtariChampionsInterlaceReader {

  public static AtariChampionsInterlaceFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static AtariChampionsInterlaceFile FromStream(Stream stream) {
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

  public static AtariChampionsInterlaceFile FromSpan(ReadOnlySpan<byte> data) {
    // The compressed form says so; the uncompressed ones are told apart by length alone.
    if (data.Length >= 24
        && Encoding.ASCII.GetString(data[..AtariChampionsInterlaceFile.CompressedSignature.Length])
          == AtariChampionsInterlaceFile.CompressedSignature)
      return new() { Data = _Unpack(data), Height = 192 };

    var height = data.Length switch {
      AtariChampionsInterlaceFile.BareSize or AtariChampionsInterlaceFile.PerRowSize => 192,
      AtariChampionsInterlaceFile.OneSetSize => 200,
      _ => throw new InvalidDataException($"Not a Champions' Interlace picture: {data.Length} bytes."),
    };

    return new() { Data = data.ToArray(), Height = height };
  }

  /// <summary>
  /// Unpacks the four streams a compressed picture holds, each preceded by four bytes the decoder
  /// does not need.
  /// </summary>
  /// <remarks>
  /// The two bitmap fields are unpacked down their own columns eighty bytes apart, which keeps each
  /// field's runs together even though the file interleaves their rows. The hues and the per-row
  /// registers follow, packed the way they are laid out rather than the way they are stored.
  /// </remarks>
  private static byte[] _Unpack(ReadOnlySpan<byte> data) {
    var unpacked = new byte[AtariChampionsInterlaceFile.PerRowSize];
    var rle = new _Rle(data, 8);

    foreach (var field in (int[])[0, AtariChampionsInterlaceFile.Stride]) {
      rle.Skip(4);
      for (var column = 0; column < AtariChampionsInterlaceFile.Stride; ++column)
        rle.Unpack(unpacked, field + column, AtariChampionsInterlaceFile.HueStride, 7680);
    }

    rle.Skip(4);
    for (var column = 0; column < AtariChampionsInterlaceFile.Stride; ++column)
      rle.Unpack(unpacked, 7680 + column, AtariChampionsInterlaceFile.Stride, 15360);

    rle.Skip(4);
    rle.Unpack(unpacked, 15360, 1, AtariChampionsInterlaceFile.PerRowSize);

    return unpacked;
  }

  /// <summary>
  /// The run-length encoding: a command byte under 128 counts literals and one over it counts
  /// repeats of the byte that follows, both biased so that a count of nothing cannot be written.
  /// </summary>
  private sealed class _Rle {

    private readonly byte[] _data;
    private int _at;
    private int _remaining;
    private int _value;

    public _Rle(ReadOnlySpan<byte> data, int offset) {
      this._data = data.ToArray();
      this._at = offset;
      this._value = -1;
    }

    /// <summary>Steps past bytes between streams, and resets whatever run was in progress.</summary>
    public void Skip(int count) {
      this._at += count;
      this._remaining = 0;
    }

    public void Unpack(Span<byte> target, int offset, int stride, int end) {
      for (var position = offset; position < end; position += stride) {
        while (this._remaining == 0)
          this._ReadCommand();

        --this._remaining;
        target[position] = this._value >= 0 ? (byte)this._value : this._Next();
      }
    }

    private void _ReadCommand() {
      var b = this._Next();
      if (b < 128) {
        this._remaining = b + 1;
        this._value = -1;
        return;
      }

      this._remaining = b - 127;
      this._value = this._Next();
    }

    private byte _Next() {
      if (this._at >= this._data.Length)
        throw new InvalidDataException("A compressed Champions' Interlace picture ends early.");

      return this._data[this._at++];
    }
  }

  public static AtariChampionsInterlaceFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
