using System.IO;

namespace FileFormat.Core;

/// <summary>Serializes the in-memory file representation to bytes. Use <see cref="FormatIO"/> for Stream overloads.</summary>
public interface IImageFormatWriter<TSelf> where TSelf : IImageFormatWriter<TSelf> {

  /// <summary>Serializes the format to a byte array.</summary>
  static abstract byte[] ToBytes(TSelf file);

  /// <summary>Writes whatever else belongs beside a file that has just been written.</summary>
  /// <param name="file">What was written, so a companion describes exactly that and not a second
  /// reading of the same picture — a reduction done twice can come out twice differently.</param>
  /// <param name="target">The main file, whose name and directory a companion is derived from.</param>
  /// <remarks>
  /// Almost nothing needs this and the default does nothing. A few formats keep part of themselves
  /// beside the file rather than inside it — most often the palette, in a file of the same name with
  /// a different extension — and nothing can open the main file without its companion. Encoding
  /// returns a single array of bytes, which leaves nowhere for that to go, so a writer for one of
  /// those formats would otherwise have to emit a file no tool could read.
  /// <para/>
  /// It is only called by the write that names a file. A caller taking the bytes and putting them
  /// somewhere itself is responsible for the rest, which is why <c>WritesCompanionFiles</c> says
  /// whether there is any.
  /// </remarks>
  static virtual void WriteCompanions(TSelf file, FileInfo target) { }
}
