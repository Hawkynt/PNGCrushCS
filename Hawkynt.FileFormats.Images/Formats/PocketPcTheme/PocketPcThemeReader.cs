using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Core;
using FileFormat.EmbeddedPicture;

namespace FileFormat.PocketPcTheme;

/// <summary>Scans a Pocket PC theme cabinet for a picture stored in it whole.</summary>
public static class PocketPcThemeReader {

  private const int _CFHEADER_SIZE = 36;
  private const int _CFFOLDER_SIZE = 8;
  private const int _CFDATA_HEADER_SIZE = 8;

  public static PocketPcThemeFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Pocket PC theme not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static PocketPcThemeFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.CanSeek) {
      var data = new byte[stream.Length - stream.Position];
      stream.ReadExactly(data);
      return FromSpan(data);
    }

    using var memory = new MemoryStream();
    stream.CopyTo(memory);
    return FromSpan(memory.ToArray());
  }

  public static PocketPcThemeFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  public static PocketPcThemeFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < PocketPcThemeFile.Signature.Length)
      throw new InvalidDataException("Data too small to be a Pocket PC theme.");

    if (!data[..PocketPcThemeFile.Signature.Length].SequenceEqual(PocketPcThemeFile.Signature))
      throw new InvalidDataException("Not a Pocket PC theme: it is not a Microsoft cabinet.");

    // A stored CAB file larger than one CFDATA block has an eight-byte block header in the physical
    // cabinet every 32 KiB. Rebuild those type-0 folders first so the writer can round-trip pictures
    // of any size. XnView's looser raw-byte scan remains the fallback for cabinets this small parser
    // intentionally does not understand (reserves, compression, or merely CAB-looking fixtures).
    if (_TryFindPictureInStoredFolder(data, out var storedPicture))
      return _Decode(storedPicture);

    var found = _FindFirstPicture(data);
    if (found < 0)
      throw new InvalidDataException("A Pocket PC theme stores no GIF, PNG or JFIF this can reach without unpacking the cabinet.");

    return _Decode(data[found..]);
  }

  private static PocketPcThemeFile _Decode(ReadOnlySpan<byte> picture) {
    var decoded = PixelConverter.Convert(EmbeddedPictureReader.Decode(picture), PixelFormat.Rgb24);
    return new() { Width = decoded.Width, Height = decoded.Height, PixelData = decoded.PixelData };
  }

  /// <summary>
  /// Finds a picture in a CAB folder using compression type NONE, rebuilding its logical byte stream
  /// from successive CFDATA records. Only the no-flags header form is needed by the paired writer;
  /// everything else deliberately falls back to the XnView-compatible raw scan.
  /// </summary>
  private static bool _TryFindPictureInStoredFolder(ReadOnlySpan<byte> data, out byte[] picture) {
    picture = [];
    if (data.Length < _CFHEADER_SIZE)
      return false;

    var flags = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(30, 2));
    if (flags != 0)
      return false;

    var folderCount = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(26, 2));
    var folderTableLength = (long)_CFHEADER_SIZE + (long)folderCount * _CFFOLDER_SIZE;
    if (folderCount == 0 || folderTableLength > data.Length)
      return false;

    for (var folderIndex = 0; folderIndex < folderCount; ++folderIndex) {
      var folderAt = _CFHEADER_SIZE + folderIndex * _CFFOLDER_SIZE;
      var firstDataAt = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(folderAt, 4));
      var dataBlockCount = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(folderAt + 4, 2));
      var compressionType = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(folderAt + 6, 2));
      if (compressionType != 0 || dataBlockCount == 0 || firstDataAt > int.MaxValue)
        continue;

      using var folder = new MemoryStream();
      var at = (int)firstDataAt;
      var valid = true;
      for (var blockIndex = 0; blockIndex < dataBlockCount; ++blockIndex) {
        if (at < 0 || at > data.Length - _CFDATA_HEADER_SIZE) {
          valid = false;
          break;
        }

        var compressedLength = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(at + 4, 2));
        var uncompressedLength = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(at + 6, 2));
        if (compressedLength != uncompressedLength || compressedLength > data.Length - at - _CFDATA_HEADER_SIZE) {
          valid = false;
          break;
        }

        folder.Write(data.Slice(at + _CFDATA_HEADER_SIZE, compressedLength));
        at += _CFDATA_HEADER_SIZE + compressedLength;
      }

      if (!valid)
        continue;

      var folderBytes = folder.ToArray();
      var found = _FindFirstPicture(folderBytes);
      if (found < 0)
        continue;

      picture = folderBytes[found..];
      return true;
    }

    return false;
  }

  /// <summary>Where the first picture stored whole begins, or -1 when there is none.</summary>
  private static int _FindFirstPicture(ReadOnlySpan<byte> data) {
    for (var at = PocketPcThemeFile.ScanStart; at + 4 <= data.Length; ++at) {
      var window = data.Slice(at, 4);
      if (window.SequenceEqual(PocketPcThemeFile.GifSignature)
          || window.SequenceEqual(PocketPcThemeFile.PngSignature)
          || window.SequenceEqual(PocketPcThemeFile.JfifSignature))
        return at;
    }

    return -1;
  }
}
