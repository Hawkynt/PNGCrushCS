using System;
using System.IO;

namespace FileFormat.Dicom;

/// <summary>Reads DICOM files from bytes, streams, or file paths.</summary>
public static class DicomReader {

  private const int _PREAMBLE_SIZE = 128;
  private const int _MAGIC_SIZE = 4;
  private const int _MIN_FILE_SIZE = _PREAMBLE_SIZE + _MAGIC_SIZE + 8; // preamble + DICM + at least one tag header

  private static readonly byte[] _DicmMagic = [(byte)'D', (byte)'I', (byte)'C', (byte)'M'];

  public static DicomFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("DICOM file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static DicomFile FromStream(Stream stream) {
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

  public static DicomFile FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static DicomFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < _MIN_FILE_SIZE)
      throw new InvalidDataException("Data too small for a valid DICOM file.");

    // Validate DICM magic at offset 128
    if (data[128] != _DicmMagic[0] || data[129] != _DicmMagic[1] ||
        data[130] != _DicmMagic[2] || data[131] != _DicmMagic[3])
      throw new InvalidDataException("Missing DICM magic at offset 128.");

    var width = 0;
    var height = 0;
    var bitsAllocated = 0;
    var bitsStored = 0;
    var samplesPerPixel = 1;
    var photometric = DicomPhotometricInterpretation.Monochrome2;
    var windowCenter = 0.0;
    var windowWidth = 0.0;
    byte[]? pixelData = null;

    var offset = _PREAMBLE_SIZE + _MAGIC_SIZE;

    while (offset < data.Length) {
      var (group, element, vr, value, nextOffset) = DicomTagReader.ReadTag(data, offset);
      if (nextOffset <= offset)
        break;

      offset = nextOffset;

      switch (group, element) {
        case (0x0028, 0x0010): // Rows
          height = DicomTagReader.ReadUS(value);
          break;
        case (0x0028, 0x0011): // Columns
          width = DicomTagReader.ReadUS(value);
          break;
        case (0x0028, 0x0100): // BitsAllocated
          bitsAllocated = DicomTagReader.ReadUS(value);
          break;
        case (0x0028, 0x0101): // BitsStored
          bitsStored = DicomTagReader.ReadUS(value);
          break;
        case (0x0028, 0x0002): // SamplesPerPixel
          samplesPerPixel = DicomTagReader.ReadUS(value);
          break;
        case (0x0028, 0x0004): // PhotometricInterpretation
          photometric = _ParsePhotometric(DicomTagReader.ReadCS(value));
          break;
        case (0x0028, 0x1050): // WindowCenter
          windowCenter = DicomTagReader.ReadDS(value);
          break;
        case (0x0028, 0x1051): // WindowWidth
          windowWidth = DicomTagReader.ReadDS(value);
          break;
        case (0x7FE0, 0x0010): // PixelData
          pixelData = value;
          break;
      }
    }

    return new DicomFile {
      Width = width,
      Height = height,
      BitsAllocated = bitsAllocated,
      BitsStored = bitsStored,
      SamplesPerPixel = samplesPerPixel,
      PhotometricInterpretation = photometric,
      PixelData = pixelData ?? [],
      WindowCenter = windowCenter,
      WindowWidth = windowWidth
    };
  }

  private static DicomPhotometricInterpretation _ParsePhotometric(string value) => value switch {
    "MONOCHROME1" => DicomPhotometricInterpretation.Monochrome1,
    "MONOCHROME2" => DicomPhotometricInterpretation.Monochrome2,
    "RGB" => DicomPhotometricInterpretation.Rgb,
    "PALETTE COLOR" => DicomPhotometricInterpretation.PaletteColor,
    _ => DicomPhotometricInterpretation.Monochrome2
  };
}
