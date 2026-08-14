namespace FileFormat.Avi;

/// <summary>The video stream of an AVI: its header, its stream format, and how its frames are stored.</summary>
public sealed class AviVideoStream {

  /// <summary>The <c>strh</c> chunk of this stream.</summary>
  public required AviStreamHeader Header { get; init; }

  /// <summary>
  /// The <c>strf</c> chunk verbatim — a <c>BITMAPINFOHEADER</c> and, for a depth of eight or less,
  /// the palette behind it.
  /// </summary>
  /// <remarks>
  /// Kept as bytes rather than parsed into fields because that is exactly the second half of a
  /// Windows bitmap file: put a fourteen-byte file header in front of it and a frame behind it and
  /// the result is a <c>.bmp</c> the existing reader takes, palette and all. Re-describing those
  /// bytes here would mean a second place for a correction to have to be applied to.
  /// </remarks>
  public required byte[] Format { get; init; }

  /// <summary>The <c>biCompression</c> of <see cref="Format"/>, which is what says how frames are stored.</summary>
  public required uint Compression { get; init; }

  /// <summary>Which of the two storages this reader recognised the stream as.</summary>
  public required AviVideoCoding Coding { get; init; }

  /// <summary>Picture width in pixels, from <c>biWidth</c>.</summary>
  public required int Width { get; init; }

  /// <summary>Picture height in pixels, always positive — see <see cref="IsTopDown"/> for the sign.</summary>
  public required int Height { get; init; }

  /// <summary>Bits per pixel, from <c>biBitCount</c>.</summary>
  public required int BitsPerPixel { get; init; }

  /// <summary>Whether <c>biHeight</c> was negative, i.e. the rows of an uncompressed frame run top-down.</summary>
  public required bool IsTopDown { get; init; }

  /// <summary>The zero-based stream number, which is the two digits a frame chunk's name begins with.</summary>
  public required int StreamNumber { get; init; }
}
