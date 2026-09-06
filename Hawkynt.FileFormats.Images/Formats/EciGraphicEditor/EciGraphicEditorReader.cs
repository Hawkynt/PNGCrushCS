using System;
using System.IO;

namespace FileFormat.EciGraphicEditor;

/// <summary>Reads ECI Graphic Editor (.eci/.ecp) files from bytes, streams, or file paths.</summary>
/// <remarks>
/// Two forms of the same picture. An .eci is 32770 bytes and carries the two banks outright; an
/// .ecp is the same 32768 bytes of payload behind a byte-oriented run-length coder, and the two are
/// told apart by length because the packed form states no signature of its own.
/// <para/>
/// That is the reference decoder's own rule for the .eci extension, and it has the same corner: a
/// packed file that happened to come out at exactly 32770 bytes would be read as an unpacked one.
/// Nothing distinguishes them, so nothing can.
/// </remarks>
public static class EciGraphicEditorReader {

  public static EciGraphicEditorFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("ECI Graphic Editor file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static EciGraphicEditorFile FromStream(Stream stream) {
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

  public static EciGraphicEditorFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < EciGraphicEditorFile.LoadAddressSize + 1)
      throw new InvalidDataException(
        $"Data too small for a valid ECI Graphic Editor file (got {data.Length} bytes).");

    var loadAddress = (ushort)(data[0] | (data[1] << 8));
    ReadOnlySpan<byte> payload = data.Length == EciGraphicEditorFile.FileSize
      ? data[EciGraphicEditorFile.LoadAddressSize..]
      : _Unpack(data).AsSpan();

    return new() {
      LoadAddress = loadAddress,
      FirstBitmap = _Section(payload, 0, EciGraphicEditorFile.BitmapSize),
      FirstScreens = _Section(payload, EciGraphicEditorFile.ScreensOffsetInBank, EciGraphicEditorFile.ScreensSize),
      SecondBitmap = _Section(payload, EciGraphicEditorFile.SecondBankOffset, EciGraphicEditorFile.BitmapSize),
      SecondScreens = _Section(
        payload,
        EciGraphicEditorFile.SecondBankOffset + EciGraphicEditorFile.ScreensOffsetInBank,
        EciGraphicEditorFile.ScreensSize),
    };
  }

  public static EciGraphicEditorFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  /// <summary>A copy of one section of the payload, short where a truncated file ends inside it.</summary>
  private static byte[] _Section(ReadOnlySpan<byte> payload, int offset, int length) {
    if (offset >= payload.Length)
      return [];

    return payload.Slice(offset, Math.Min(length, payload.Length - offset)).ToArray();
  }

  /// <summary>
  /// Unpacks the .ecp form: a stream of literal bytes in which one chosen value introduces a run.
  /// </summary>
  /// <remarks>
  /// The escape value is stated in the third byte of the file, immediately after the load address,
  /// and the stream itself starts at the fourth. Meeting it means a count and a value follow; every
  /// other byte stands for itself. A count of zero contributes nothing and the next command is read,
  /// which is how the escape value itself is written literally.
  /// </remarks>
  private static byte[] _Unpack(ReadOnlySpan<byte> data) {
    if (data.Length < EciGraphicEditorFile.LoadAddressSize + 2)
      throw new InvalidDataException(
        "A packed ECI Graphic Editor file needs an escape byte and at least one command.");

    var escape = data[2];
    var unpacked = new byte[EciGraphicEditorFile.PayloadSize];
    var at = 3;
    var written = 0;

    while (written < unpacked.Length) {
      if (at >= data.Length)
        throw new InvalidDataException(
          $"Packed ECI Graphic Editor data ended after {written} of {unpacked.Length} bytes.");

      int value = data[at++];
      var count = 1;
      if (value == escape) {
        if (at + 1 >= data.Length)
          throw new InvalidDataException("Packed ECI Graphic Editor data ended inside a run.");

        count = data[at++];
        value = data[at++];
      }

      while (count-- > 0 && written < unpacked.Length)
        unpacked[written++] = (byte)value;
    }

    return unpacked;
  }
}
