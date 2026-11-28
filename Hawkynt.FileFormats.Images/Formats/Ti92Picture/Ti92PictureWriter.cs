using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace FileFormat.Ti92Picture;

/// <summary>Writes a TI-89 / TI-92 / TI-92 Plus / Voyage 200 transfer file of picture variables.</summary>
/// <remarks>
/// The layout of the files the calculators' own link software writes, to the byte: the header with a
/// blank comment, the directory, the file's size and <c>A5 5A</c>, and each variable with its
/// checksum after it.
/// </remarks>
public static class Ti92PictureWriter {

  public static byte[] ToBytes(Ti92PictureFile file) {
    var pictures = file.Pictures ?? throw new ArgumentException("A TI-89/92 transfer file needs its pictures.", nameof(file));
    if (pictures.Count is 0 or > 0xFFFF)
      throw new ArgumentException($"A TI-89/92 transfer file holds 1 to 65535 variables, not {pictures.Count}.", nameof(file));
    var signature = file.Model switch {
      "92" => "**TI92**",
      "89" => "**TI89**",
      "V2" => "**TIV2**",
      "92P" or null => "**TI92P*",
      _ => throw new ArgumentException($"A TI-89/92 transfer file is for the 89, 92, 92P or V2, not {file.Model}.", nameof(file)),
    };

    var bodies = new byte[pictures.Count][];
    for (var i = 0; i < pictures.Count; ++i) {
      var picture = pictures[i];
      if (picture.Width is < 1 or > 0xFFFF || picture.Height is < 1 or > 0xFFFF)
        throw new ArgumentException($"Picture {i} is {picture.Width} by {picture.Height}.", nameof(file));
      var bitmap = (picture.Width + 7) / 8 * picture.Height;
      var length = 4 + bitmap + 1;
      if (length > 0xFFFF || picture.PixelData.Length < bitmap)
        throw new ArgumentException($"Picture {i} of {picture.Width} by {picture.Height} does not fit a variable, or lacks its {bitmap} bytes.", nameof(file));

      var body = new byte[4 + 2 + length + 2];
      BinaryPrimitives.WriteUInt16BigEndian(body.AsSpan(4), (ushort)length);
      BinaryPrimitives.WriteUInt16BigEndian(body.AsSpan(6), (ushort)picture.Height);
      BinaryPrimitives.WriteUInt16BigEndian(body.AsSpan(8), (ushort)picture.Width);
      picture.PixelData.AsSpan(0, bitmap).CopyTo(body.AsSpan(10));
      body[10 + bitmap] = Ti92PictureFile.PictureTag;
      var sum = 0;
      for (var b = 4; b < 6 + length; ++b)
        sum += body[b];
      BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(6 + length), (ushort)sum);
      bodies[i] = body;
    }

    var directory = Ti92PictureFile.HeaderSize + 2;
    var offset = directory + pictures.Count * Ti92PictureFile.EntrySize + 4 + 2;
    using var output = new MemoryStream();
    var header = new byte[offset];
    Encoding.ASCII.GetBytes(signature).CopyTo(header, 0);
    header[8] = 1;
    _Name(file.Folder ?? "main", header.AsSpan(10, 8));
    BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(Ti92PictureFile.HeaderSize), (ushort)pictures.Count);
    for (var i = 0; i < pictures.Count; ++i) {
      var entry = header.AsSpan(directory + i * Ti92PictureFile.EntrySize, Ti92PictureFile.EntrySize);
      BinaryPrimitives.WriteUInt32LittleEndian(entry, (uint)offset);
      _Name(pictures[i].Name ?? $"pic{i + 1}", entry[4..12]);
      entry[12] = Ti92PictureFile.PictureType;
      offset += bodies[i].Length;
    }

    BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(directory + pictures.Count * Ti92PictureFile.EntrySize), (uint)offset);
    header[^2] = 0xA5;
    header[^1] = 0x5A;
    output.Write(header);
    foreach (var body in bodies)
      output.Write(body);
    return output.ToArray();
  }

  private static void _Name(string name, Span<byte> field) {
    var bytes = Encoding.ASCII.GetBytes(name);
    if (bytes.Length > field.Length)
      throw new ArgumentException($"A TI-89/92 name is at most eight characters; '{name}' is not.");
    bytes.CopyTo(field);
  }
}
