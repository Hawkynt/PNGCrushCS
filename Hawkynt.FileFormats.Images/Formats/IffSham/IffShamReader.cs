using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Ilbm;

namespace FileFormat.IffSham;

/// <summary>Reads IFF SHAM (Sliced HAM) images from bytes, streams, or file paths.</summary>
public static class IffShamReader {

  public static IffShamFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("SHAM file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static IffShamFile FromStream(Stream stream) {
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

  public static IffShamFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  public static IffShamFile FromSpan(ReadOnlySpan<byte> data) {
    var paletteSlices = _ValidateContainerAndFindSham(data);
    var ilbm = IlbmReader.FromSpan(data);

    if (ilbm.NumPlanes != IffShamFile.NumPlanes)
      throw new NotSupportedException($"SHAM requires six HAM bitplanes; this file declares {ilbm.NumPlanes}.");
    if (ilbm.ScanlinePalettes is not { } palettes)
      throw new InvalidDataException("SHAM chunk did not yield a sliced palette.");
    if (palettes.Length != paletteSlices * IffShamFile.PaletteBytesPerScanline)
      throw new InvalidDataException("SHAM palette payload is inconsistent with its declared slice count.");

    return new() {
      Width = ilbm.Width,
      Height = ilbm.Height,
      RawData = data.ToArray(),
      PixelData = ilbm.PixelData,
      ScanlinePalettes = palettes,
    };
  }

  /// <summary>Validates the IFF envelope and returns the number of sixteen-colour SHAM slices.</summary>
  private static int _ValidateContainerAndFindSham(ReadOnlySpan<byte> data) {
    if (data.Length < IffShamFile.MinFileSize)
      throw new InvalidDataException($"Invalid SHAM data: expected at least {IffShamFile.MinFileSize} bytes, got {data.Length}.");
    if (!data[..4].SequenceEqual("FORM"u8))
      throw new InvalidDataException("Invalid SHAM data: expected an IFF FORM container.");
    if (!data.Slice(8, 4).SequenceEqual("ILBM"u8))
      throw new InvalidDataException("Invalid SHAM data: expected an ILBM form.");

    var formSize = BinaryPrimitives.ReadUInt32BigEndian(data[4..]);
    var end = (long)formSize + 8;
    if (formSize < 4 || end > data.Length)
      throw new InvalidDataException("Invalid SHAM data: FORM size extends past the available bytes.");

    for (var offset = 12; offset + 8 <= end;) {
      var chunkSize = BinaryPrimitives.ReadUInt32BigEndian(data[(offset + 4)..]);
      var chunkData = offset + 8;
      var next = (long)chunkData + chunkSize + (chunkSize & 1);
      if (next > end)
        throw new InvalidDataException("Invalid SHAM data: an IFF chunk extends past the FORM boundary.");

      if (data.Slice(offset, 4).SequenceEqual("SHAM"u8)) {
        const int BYTES_PER_SLICE = IffShamFile.PaletteEntries * 2;
        if (chunkSize < 2 + BYTES_PER_SLICE || (chunkSize - 2) % BYTES_PER_SLICE != 0)
          throw new InvalidDataException("Invalid SHAM chunk: expected a version word followed by complete sixteen-colour slices.");

        var version = BinaryPrimitives.ReadUInt16BigEndian(data[chunkData..]);
        if (version != 0)
          throw new NotSupportedException($"Unsupported SHAM version {version}; only version 0 is defined by known files.");

        return checked((int)((chunkSize - 2) / BYTES_PER_SLICE));
      }

      offset = checked((int)next);
    }

    throw new InvalidDataException("IFF ILBM file does not contain a SHAM chunk.");
  }
}
