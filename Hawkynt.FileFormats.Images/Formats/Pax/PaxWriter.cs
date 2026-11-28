using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace FileFormat.Pax;

/// <summary>Writes Pick Ax (PAX) pictures, encrypted under <see cref="PaxFile.Password"/>.</summary>
/// <remarks>See <see cref="PaxReader"/> for the layout. Each row is compressed as a zlib stream of its own.</remarks>
public static class PaxWriter {

  /// <summary>Bytes in each of the two name fields.</summary>
  private const int _NAME_SIZE = 32;

  public static byte[] ToBytes(PaxFile file) {
    if (file.Width is < 1 or > ushort.MaxValue || file.Height is < 1 or > ushort.MaxValue)
      throw new ArgumentException($"A PAX picture is 1 to {ushort.MaxValue} pixels each way, not {file.Width}x{file.Height}.", nameof(file));
    if (file.ColorMode is not (PaxColorMode.Indexed8 or PaxColorMode.Rgb555 or PaxColorMode.Rgb24))
      throw new ArgumentException($"Not a PAX colour mode: {file.ColorMode}.", nameof(file));
    if (file.Encryption is < PaxEncryption.Blowfish or > PaxEncryption.BlowfishCbcThenSapphire)
      throw new ArgumentException($"Not a PAX encryption type: {file.Encryption}.", nameof(file));

    var stride = file.Width * (int)file.ColorMode;
    if (file.PixelData == null || file.PixelData.Length < stride * file.Height)
      throw new ArgumentException("Pixel data is smaller than the picture it describes.", nameof(file));

    var cipher = new PaxCipher(file.Password ?? string.Empty);
    using var output = new MemoryStream();
    output.Write("PAX"u8);

    var fileId = new byte[PaxFile.FileIdSize];
    file.FileId?.AsSpan(0, Math.Min(file.FileId.Length, fileId.Length)).CopyTo(fileId);
    output.Write(fileId);

    // The palette is stored as whole quads in an even number, since it is encrypted in eight-byte
    // blocks with nothing to say how much of the last one is padding.
    var paletteCount = 0;
    byte[] palette = [];
    if (file.ColorMode == PaxColorMode.Indexed8) {
      var stated = file.Palette == null ? 0 : Math.Min(file.PaletteCount, file.Palette.Length / 3);
      if (stated is < 1 or > 256)
        throw new ArgumentException("A paletted PAX needs 1 to 256 palette entries.", nameof(file));

      paletteCount = Math.Min(256, (stated + 1) & ~1);
      palette = new byte[PaxReader.PaletteBytes(paletteCount)];
      for (var i = 0; i < stated; ++i) {
        palette[4 * i] = file.Palette![3 * i];
        palette[4 * i + 1] = file.Palette[3 * i + 1];
        palette[4 * i + 2] = file.Palette[3 * i + 2];
      }
    }

    // A tag states its padded size: PAXIO and ImgSource both skip exactly the stated size, and both
    // lose their place in a file whose tag states fewer bytes than it occupies. ImgSource writes
    // the padded size itself, so a tag's data comes back with its zero padding.
    using var tags = new MemoryStream();
    foreach (var tag in file.Tags ?? []) {
      var data = tag.Data ?? [];
      var padded = (data.Length + 7) & ~7;
      var record = new byte[8 + padded];
      var name = Encoding.Latin1.GetBytes(tag.Name ?? string.Empty);
      name.AsSpan(0, Math.Min(name.Length, PaxTag.NameSize)).CopyTo(record);
      BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(4), (uint)padded);
      data.CopyTo(record, 8);
      cipher.EncryptHeader(record);
      tags.Write(record);
    }

    var headerLength = PaxReader.HeaderSize + palette.Length + (int)tags.Length;
    if (headerLength > ushort.MaxValue)
      throw new ArgumentException("Palette and tags together exceed what the PAX header length can state.", nameof(file));

    var header = new byte[PaxReader.HeaderSize];
    BinaryPrimitives.WriteUInt16LittleEndian(header, (ushort)headerLength);
    BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(2), (ushort)file.Width);
    BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(4), (ushort)file.Height);
    BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(6), (ushort)file.ColorMode);
    BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(8), (ushort)paletteCount);
    BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(10), (ushort)file.Encryption);
    if (file.Date is { } date) {
      header[12] = (byte)date.Day;
      header[13] = (byte)date.Month;
      BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(14), (ushort)date.Year);
    }

    _WriteName(header.AsSpan(16, _NAME_SIZE), file.ApplicationName);
    _WriteName(header.AsSpan(48, _NAME_SIZE), file.AuthorName);
    cipher.EncryptHeader(header);
    output.Write(header);

    cipher.EncryptHeader(palette);
    output.Write(palette);
    tags.Position = 0;
    tags.CopyTo(output);

    Span<byte> rowHeader = stackalloc byte[8];
    for (var y = 0; y < file.Height; ++y) {
      var compressed = _Deflate(file.PixelData.AsSpan(y * stride, stride));
      var row = new byte[(compressed.Length + 7) & ~7];
      compressed.CopyTo(row, 0);
      cipher.Encrypt(file.Encryption, row);

      BinaryPrimitives.WriteUInt32LittleEndian(rowHeader, (uint)compressed.Length);
      BinaryPrimitives.WriteUInt32LittleEndian(rowHeader[4..], (uint)row.Length);
      cipher.Encrypt(file.Encryption, rowHeader);
      output.Write(rowHeader);
      output.Write(row);
    }

    return output.ToArray();
  }

  private static void _WriteName(Span<byte> field, string? name) {
    if (string.IsNullOrEmpty(name))
      return;

    var bytes = Encoding.Latin1.GetBytes(name);
    bytes.AsSpan(0, Math.Min(bytes.Length, field.Length)).CopyTo(field);
  }

  private static byte[] _Deflate(ReadOnlySpan<byte> row) {
    using var buffer = new MemoryStream();
    using (var zlib = new ZLibStream(buffer, CompressionLevel.SmallestSize, leaveOpen: true))
      zlib.Write(row);

    return buffer.ToArray();
  }
}
