using System;
using System.IO;
using System.Text;
using FileFormat.Wrappers;

namespace FileFormat.CorelGallery;

/// <summary>Writes a Corel GALLERY file: the sixty-nine bytes of text, then the preview bitmap.</summary>
/// <remarks>
/// The header is the one all seven samples carry, byte for byte: the signature, the company name and
/// a run of spaces, each line ended with a line feed and a carriage return, and then six bytes. The
/// reader takes the bitmap at sixty-nine rather than searching for it, so a header of any other
/// length is a file it would not open.
/// <para/>
/// What goes out is the preview and nothing else. The drawing in a real one of these is Corel's own
/// vector record stream, which is not read here and cannot be written; the remarks on the file say
/// so.
/// </remarks>
public static class CorelGalleryWriter {

  /// <summary>What stands between the signature and the preview in every sample.</summary>
  private const string _Company = "Corel Corporation";

  /// <summary>The six bytes that follow the last line, which are the same in all seven.</summary>
  private static ReadOnlySpan<byte> _Trailer => [0x2B, 0x00, 0x40, 0x28, 0x00, 0x00];

  public static byte[] ToBytes(CorelGalleryFile file) {
    var preview = file.Preview ?? throw new ArgumentException("No preview to write.", nameof(file));
    if (preview.Width < 1 || preview.Height < 1 || preview.Width > CorelGalleryFile.MaxDimension || preview.Height > CorelGalleryFile.MaxDimension)
      throw new ArgumentException($"A Corel GALLERY preview of {preview.Width} by {preview.Height} is outside the {CorelGalleryFile.MaxDimension} this holds.", nameof(file));

    using var output = new MemoryStream();
    output.Write(CorelGalleryFile.Magic);
    output.WriteByte((byte)'\n');
    output.WriteByte((byte)'\r');
    output.Write(Encoding.ASCII.GetBytes(_Company));
    output.WriteByte((byte)'\n');
    output.WriteByte((byte)'\r');

    // The third line is spaces, and its length is what brings the header to the sixty-nine bytes the
    // reader takes the bitmap at.
    var spaces = CorelGalleryFile.PreviewOffset - (int)output.Length - 2 - _Trailer.Length;
    for (var i = 0; i < spaces; ++i)
      output.WriteByte((byte)' ');

    output.WriteByte((byte)'\n');
    output.WriteByte((byte)'\r');
    output.Write(_Trailer);

    output.Write(WrappedDib.Encode(preview));
    return output.ToArray();
  }
}
