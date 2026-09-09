using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.EmbeddedDib;
using FileFormat.Riff;

namespace FileFormat.Cdr;

/// <summary>Serializes the direct RIFF container used by CorelDRAW X3 and older.</summary>
public static class CdrWriter {

  public static byte[] ToBytes(CdrFile file) {
    ArgumentNullException.ThrowIfNull(file);

    if (!CdrFile.IsDirectRiffCdrForm(file.FormType))
      throw new InvalidDataException($"{file.FormType} is not a direct RIFF CorelDRAW form.");

    if (file.Chunks is null)
      throw new InvalidDataException("The CDR file has no RIFF chunk collection.");

    var replacement = file.ReplacementPreview;
    if (replacement is null)
      return RiffWriter.ToBytes(new RiffFile {
        FormType = file.FormType,
        Chunks = [.. file.Chunks],
      });

    var dib = EmbeddedDibWriter.ToBytes(replacement);
    var chunks = new List<RiffChunk>(file.Chunks.Count);
    var replaced = false;

    foreach (var chunk in file.Chunks) {
      if (!replaced && chunk.Id.ToString() == "DISP") {
        if (chunk.Data.Length < sizeof(uint))
          throw new InvalidDataException("The CDR DISP chunk is too short to preserve its four-byte Corel prefix.");

        var data = new byte[sizeof(uint) + dib.Length];
        chunk.Data.AsSpan(0, sizeof(uint)).CopyTo(data);
        dib.CopyTo(data.AsSpan(sizeof(uint)));
        chunks.Add(new RiffChunk { Id = chunk.Id, Data = data });
        replaced = true;
        continue;
      }

      chunks.Add(chunk);
    }

    if (!replaced)
      throw new InvalidDataException("The CDR file has no DISP chunk to replace.");

    return RiffWriter.ToBytes(new RiffFile {
      FormType = file.FormType,
      Chunks = chunks,
    });
  }

}
