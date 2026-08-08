using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace FileFormat.SecondNatureSlideShow;

/// <summary>Writes a Second Nature collection: the header, the directory, then the slides.</summary>
/// <remarks>
/// The directory is what has to close. Its first word is where the slides begin, so how many there
/// are is the space between the directory and them divided by eight, and every entry has to be the
/// one before it plus that one's length — with the last of them ending on the last byte of the file.
/// All three statements are written from the slides themselves, which is what the reader accounts for
/// the file by.
/// <para/>
/// A slide's record states its size twice, at two places, and the reader refuses a slide where the
/// two disagree or where either differs from the JPEG's own. Both are written from the picture.
/// </remarks>
public static class SecondNatureSlideShowWriter {

  /// <summary>Where the collection's title sits in the header.</summary>
  private const int _TitleOffset = 0x50, _TitleLength = 0x80;

  public static byte[] ToBytes(SecondNatureSlideShowFile file) {
    ArgumentNullException.ThrowIfNull(file);
    if (file.Slides.Count == 0)
      throw new ArgumentException("A Second Nature collection holds slides and this one has none.", nameof(file));

    if (file.Slides.Count > SecondNatureSlideShowFile.MaxSlides)
      throw new ArgumentException($"A Second Nature collection of {file.Slides.Count} slides is more than the {SecondNatureSlideShowFile.MaxSlides} one holds.", nameof(file));

    var directoryBytes = file.Slides.Count * SecondNatureSlideShowFile.DirectoryEntrySize;
    var first = SecondNatureSlideShowFile.DirectoryOffset + directoryBytes;

    var header = new byte[SecondNatureSlideShowFile.DirectoryOffset];
    Encoding.ASCII.GetBytes(SecondNatureSlideShowFile.Signature).CopyTo(header, 0);
    var title = Encoding.ASCII.GetBytes(file.Title ?? string.Empty);
    Array.Copy(title, 0, header, _TitleOffset, Math.Min(title.Length, _TitleLength - 1));

    using var output = new MemoryStream();
    output.Write(header);

    var at = first;
    foreach (var slide in file.Slides) {
      var jpeg = slide.Jpeg ?? throw new ArgumentException("A Second Nature slide carries no JPEG.", nameof(file));
      if (jpeg.Length < 4 || jpeg[0] != 0xFF || jpeg[1] != 0xD8 || jpeg[^2] != 0xFF || jpeg[^1] != 0xD9)
        throw new ArgumentException("A Second Nature slide holds a whole JPEG and this one does not begin and end as one.", nameof(file));

      if (slide.Width is < 1 or > ushort.MaxValue || slide.Height is < 1 or > ushort.MaxValue)
        throw new ArgumentException(
          $"A Second Nature slide states its size in unsigned words and {slide.Width} by {slide.Height} does not fit in them.", nameof(file));

      var length = SecondNatureSlideShowFile.SlideHeaderSize + jpeg.Length;
      _UInt32(output, at);
      _UInt32(output, length);
      at += length;
    }

    foreach (var slide in file.Slides) {
      var record = new byte[SecondNatureSlideShowFile.SlideHeaderSize];
      BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(SecondNatureSlideShowFile.SlideSizeOffset), (ushort)slide.Width);
      BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(SecondNatureSlideShowFile.SlideSizeOffset + 2), (ushort)slide.Height);
      BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(SecondNatureSlideShowFile.SlideSizeRepeatOffset), (ushort)slide.Width);
      BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(SecondNatureSlideShowFile.SlideSizeRepeatOffset + 2), (ushort)slide.Height);
      output.Write(record);
      output.Write(slide.Jpeg);
    }

    return output.ToArray();
  }

  private static void _UInt32(Stream output, int value) {
    Span<byte> word = stackalloc byte[4];
    BinaryPrimitives.WriteInt32LittleEndian(word, value);
    output.Write(word);
  }
}
