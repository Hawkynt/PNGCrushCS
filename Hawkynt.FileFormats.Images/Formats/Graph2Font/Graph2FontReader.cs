using System;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace FileFormat.Graph2Font;

/// <summary>Reads Graph2Font projects from bytes, streams, or file paths.</summary>
public static class Graph2FontReader {

  /// <summary>Smallest project the format can describe.</summary>
  private const int _MINIMUM_LENGTH = 155711;

  /// <summary>Largest a project unpacks to.</summary>
  private const int _MAXIMUM_LENGTH = 327078;

  public static Graph2FontFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Project not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static Graph2FontFile FromStream(Stream stream) {
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

  public static Graph2FontFile FromSpan(ReadOnlySpan<byte> data) {
    var project = Unwrap(data);
    Describe(project);

    return new() { Data = project };
  }

  /// <summary>Uncompresses a project if it was compressed, and returns it either way.</summary>
  public static byte[] Unwrap(ReadOnlySpan<byte> data) {
    if (data.Length < Graph2FontFile.CompressedSignature.Length
        || Encoding.ASCII.GetString(data[..Graph2FontFile.CompressedSignature.Length])
          != Graph2FontFile.CompressedSignature)
      return data.ToArray();

    using var source = new MemoryStream(data[Graph2FontFile.CompressedSignature.Length..].ToArray());
    using var inflate = new ZLibStream(source, CompressionMode.Decompress);
    using var target = new MemoryStream(_MINIMUM_LENGTH);

    var buffer = new byte[65536];
    for (int read; (read = inflate.Read(buffer, 0, buffer.Length)) > 0;) {
      target.Write(buffer, 0, read);
      if (target.Length > _MAXIMUM_LENGTH)
        throw new InvalidDataException("A compressed Graph2Font project unpacks to more than one can be.");
    }

    return target.ToArray();
  }

  /// <summary>
  /// Works out where a project's tables sit, which follows from its width and how many character
  /// sets it carries.
  /// </summary>
  public static Graph2FontLayout Describe(ReadOnlySpan<byte> data) {
    if (data.Length < _MINIMUM_LENGTH)
      throw new InvalidDataException($"Not a Graph2Font project: {data.Length} bytes.");

    var columns = data[0] & 127;
    if (columns is not (32 or 40 or 48))
      throw new InvalidDataException($"A Graph2Font project is 32, 40 or 48 characters wide, not {columns}.");

    var fontsOffset = 3 + 30 * columns;
    var fontNumberOffset = fontsOffset + (((data[2] & 127) + 1) * Graph2FontFile.FontSize);
    if (data.Length < fontNumberOffset + 153724)
      throw new InvalidDataException("A Graph2Font project's tables run past the end of the file.");

    var vbxeOffset = fontNumberOffset + 155231;
    var inverse2Offset = -1;
    bool characterMode;

    switch (data[fontNumberOffset + 147679] & 127) {
      case 1:
      case 3:
        // These two may carry a raster program, and one that does is an animation rather than a
        // picture — the display changes as it is drawn, so there is no frame to produce.
        if (_HasRaster(data, fontNumberOffset + 147934, data[0] < 128 ? (byte)22 : (byte)30))
          throw new InvalidDataException("A Graph2Font project with a raster program has no single frame.");

        characterMode = (data[fontNumberOffset + 147679] & 127) == 1;
        break;

      case 2:
        characterMode = true;
        break;

      case 66:
        characterMode = true;
        inverse2Offset = vbxeOffset + 138244;
        if (data.Length < inverse2Offset + 30 * columns)
          throw new InvalidDataException("A Graph2Font project's second inverse table is not there.");

        break;

      default:
        throw new InvalidDataException("A Graph2Font project names no character arrangement.");
    }

    if (data.Length < vbxeOffset + 138243)
      vbxeOffset = -1;
    else
      switch (data[vbxeOffset]) {
        case 0:
          vbxeOffset = -1;
          break;

        case 1:
          if (data[vbxeOffset + 1] != 8 || data[vbxeOffset + 2] == 0)
            throw new InvalidDataException("A Graph2Font project's video upgrade block is malformed.");

          break;

        default:
          throw new InvalidDataException("A Graph2Font project's video upgrade block is neither on nor off.");
      }

    return new() {
      Columns = columns,
      FontsOffset = fontsOffset,
      FontNumberOffset = fontNumberOffset,
      CharacterMode = characterMode,
      Inverse2Offset = inverse2Offset,
      VbxeOffset = vbxeOffset,
    };
  }

  /// <summary>Whether the raster block does anything beyond waiting and clearing collisions.</summary>
  private static bool _HasRaster(ReadOnlySpan<byte> data, int offset, byte collisionClear) {
    for (var i = 0; i < 2880; ++i, offset += 2) {
      if (offset + 1 >= data.Length)
        return true;

      switch (data[offset]) {
        case 0 or 1 or 2 or 3 or 65 or 66 or 67 or 97 or 98 or 99:
          break;

        case 129 or 130 or 131:
          if (data[offset + 1] != collisionClear)
            return true;

          break;

        default:
          return true;
      }
    }

    return false;
  }

  public static Graph2FontFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
