using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using FileFormat.Core;
using FileFormat.Ilbm;

namespace FileFormat.IffAnim;

/// <summary>Reads IFF ANIM files from bytes, streams, or file paths.</summary>
public static class IffAnimReader {

  private const int _MIN_SIZE = 12; // "FORM" + uint32 size + "ANIM"

  public static IffAnimFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("IFF ANIM file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static IffAnimFile FromStream(Stream stream) {
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

  public static IffAnimFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < _MIN_SIZE)
      throw new InvalidDataException("Data too small for a valid IFF ANIM file.");

    var span = data.AsSpan();

    var formTag = Encoding.ASCII.GetString(data, 0, 4);
    if (formTag != "FORM")
      throw new InvalidDataException($"Invalid IFF magic: expected 'FORM', got '{formTag}'.");

    var formType = Encoding.ASCII.GetString(data, 8, 4);
    if (formType != "ANIM")
      throw new InvalidDataException($"Invalid IFF form type: expected 'ANIM', got '{formType}'.");

    var formSize = BinaryPrimitives.ReadInt32BigEndian(span[4..]);
    var endOffset = Math.Min(8 + formSize, data.Length);

    // Scan for first embedded FORM ILBM
    var offset = 12;
    while (offset + 12 <= endOffset) {
      var chunkId = Encoding.ASCII.GetString(data, offset, 4);
      var chunkSize = BinaryPrimitives.ReadInt32BigEndian(span[(offset + 4)..]);

      if (chunkId == "FORM" && offset + 12 <= endOffset) {
        var subFormType = Encoding.ASCII.GetString(data, offset + 8, 4);
        if (subFormType == "ILBM") {
          var ilbmTotalSize = 8 + chunkSize; // "FORM" + size + data
          if (offset + ilbmTotalSize > data.Length)
            ilbmTotalSize = data.Length - offset;

          var ilbmBytes = new byte[ilbmTotalSize];
          data.AsSpan(offset, ilbmTotalSize).CopyTo(ilbmBytes.AsSpan(0));

          var ilbmFile = IlbmReader.FromBytes(ilbmBytes);
          var rawImage = IlbmFile.ToRawImage(ilbmFile);

          // Convert to RGB24 if needed
          var rgb24 = rawImage.Format == PixelFormat.Rgb24
            ? rawImage.PixelData
            : rawImage.ToRgb24();

          return new IffAnimFile {
            Width = ilbmFile.Width,
            Height = ilbmFile.Height,
            PixelData = rgb24,
          };
        }
      }

      // Advance past this chunk (2-byte aligned)
      var dataSize = chunkSize + (chunkSize & 1);
      offset += 8 + dataSize;
    }

    throw new InvalidDataException("No FORM ILBM frame found in the ANIM container.");
  }
}
