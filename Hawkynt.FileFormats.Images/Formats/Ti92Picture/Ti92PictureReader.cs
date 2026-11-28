using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace FileFormat.Ti92Picture;

/// <summary>Reads TI-89 / TI-92 / TI-92 Plus / Voyage 200 picture variables from bytes, streams, or file paths.</summary>
public static class Ti92PictureReader {

  public static Ti92PictureFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("TI-92 picture not found.", file.FullName);
    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static Ti92PictureFile FromStream(Stream stream) => FromBytes(StreamBytes.ReadAll(stream));

  public static Ti92PictureFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  /// <summary>The calculator a signature names, or <c>null</c> where it is not one of these four.</summary>
  public static string? ModelOf(ReadOnlySpan<byte> header) {
    if (header.Length < 8 || header[0] != '*' || header[1] != '*' || header[2] != 'T' || header[3] != 'I')
      return null;
    return Encoding.ASCII.GetString(header[4..8]) switch {
      "92**" => "92",
      "89**" => "89",
      "92P*" => "92P",
      "V2**" => "V2",
      _ => null,
    };
  }

  public static Ti92PictureFile FromSpan(ReadOnlySpan<byte> data) {
    var model = ModelOf(data) ?? throw new InvalidDataException("Not a TI-89/92 transfer file: it does not open with **TI92**, **TI89**, **TI92P* or **TIV2**.");
    if (data.Length < Ti92PictureFile.HeaderSize + 2)
      throw new InvalidDataException("A TI-89/92 transfer file ends inside its header.");

    var count = BinaryPrimitives.ReadUInt16LittleEndian(data[Ti92PictureFile.HeaderSize..]);
    var directory = Ti92PictureFile.HeaderSize + 2;
    if (count == 0 || directory + count * Ti92PictureFile.EntrySize > data.Length)
      throw new InvalidDataException($"A TI-89/92 transfer file states {count} variables, which its directory does not hold.");

    var pictures = new List<Ti92Picture>();
    for (var i = 0; i < count; ++i) {
      var entry = data.Slice(directory + i * Ti92PictureFile.EntrySize, Ti92PictureFile.EntrySize);
      if (entry[12] != Ti92PictureFile.PictureType)
        continue; // a folder entry, or a variable of some other kind

      var offset = BinaryPrimitives.ReadUInt32LittleEndian(entry);
      if (offset + 6L > data.Length)
        throw new InvalidDataException($"Picture variable {i} of this TI-89/92 file starts past its end.");
      var at = (int)offset + 4;
      var length = BinaryPrimitives.ReadUInt16BigEndian(data[at..]);
      if (at + 2 + length > data.Length || length < 5)
        throw new InvalidDataException($"Picture variable {i} of this TI-89/92 file states {length} bytes, which the file does not hold.");

      var height = BinaryPrimitives.ReadUInt16BigEndian(data[(at + 2)..]);
      var width = BinaryPrimitives.ReadUInt16BigEndian(data[(at + 4)..]);
      var bitmap = (width + 7) / 8 * height;
      if (width == 0 || height == 0 || 4 + bitmap + 1 != length || data[at + 2 + length - 1] != Ti92PictureFile.PictureTag)
        throw new InvalidDataException($"Picture variable {i} of this TI-89/92 file states {width} by {height}, which its {length} bytes do not hold.");

      pictures.Add(new(_Name(entry[4..12]), width, height, data.Slice(at + 6, bitmap).ToArray()));
    }

    if (pictures.Count == 0)
      throw new InvalidDataException("This TI-89/92 transfer file holds no picture variable.");

    return new() { Model = model, Folder = _Name(data.Slice(10, 8)), Pictures = pictures };
  }

  private static string _Name(ReadOnlySpan<byte> field) {
    var end = field.IndexOf((byte)0);
    return Encoding.ASCII.GetString(end < 0 ? field : field[..end]);
  }
}
