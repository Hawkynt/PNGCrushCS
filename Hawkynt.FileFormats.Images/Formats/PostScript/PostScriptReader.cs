using System;
using System.IO;
using System.Text;

namespace FileFormat.PostScript;

/// <summary>Opens a PostScript file: finds the program in it and reads its comments.</summary>
public static class PostScriptReader {

  /// <summary>Reads a file from disk.</summary>
  public static PostScriptFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("PostScript file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  /// <summary>Reads a file from a stream.</summary>
  public static PostScriptFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    using var memory = new MemoryStream();
    stream.CopyTo(memory);
    return FromBytes(memory.ToArray());
  }

  /// <summary>Reads a file from bytes.</summary>
  public static PostScriptFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  /// <summary>Reads a file from bytes.</summary>
  public static PostScriptFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < 4)
      throw new InvalidDataException($"A PostScript file of {data.Length} bytes holds no program.");

    // A file whose magic says PDF is a PDF, whatever it is called. Illustrator has written PDF under
    // its own name since version 9, and the reader for that is the PDF one; taking such a file here
    // would run a scanner over a binary object stream and get nothing out of it.
    if (data[0] == '%' && data[1] == 'P' && data[2] == 'D' && data[3] == 'F')
      throw new InvalidDataException("A PDF file, which is not a PostScript program however it is named.");

    var (start, end) = PostScriptStructure.Program(data);
    if (end - start < 2 || data[start] != '%' || data[start + 1] != '!')
      throw new InvalidDataException("A PostScript program begins \"%!\", and this file does not.");

    var copy = data.ToArray();
    return new() {
      Data = copy,
      Start = start,
      End = end,
      Comments = PostScriptStructure.Read(copy, start, end)
    };
  }

  /// <summary>What the file says it is, out of its first line.</summary>
  public static string FirstLine(PostScriptFile file) {
    var end = file.Start;
    while (end < file.End && file.Data[end] != '\r' && file.Data[end] != '\n' && end - file.Start < 128)
      ++end;

    return Encoding.Latin1.GetString(file.Data, file.Start, end - file.Start);
  }
}
