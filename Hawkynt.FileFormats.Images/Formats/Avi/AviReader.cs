using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FileFormat.Bmp;
using FileFormat.Riff;

namespace FileFormat.Avi;

/// <summary>
/// Reads the video frames of an AVI whose frames are already a format this library decodes.
/// </summary>
/// <remarks>
/// An AVI is a RIFF file of form <c>AVI </c>, so the container comes apart with the RIFF reader this
/// repository already has: <c>LIST hdrl</c> holds the main header and one <c>LIST strl</c> per
/// stream, and <c>LIST movi</c> holds the frames, one chunk each, named for the stream they belong
/// to. None of that is codec-specific and all of it is shared with every other AVI.
/// <para/>
/// What the frames *are* is the codec's business, and this reader takes exactly two:
/// <list type="bullet">
///   <item>
///     <c>MJPG</c> — each frame chunk is a whole JPEG, which <see cref="FileFormat.Jpeg.JpegReader"/>
///     decodes.
///   </item>
///   <item>
///     <c>BI_RGB</c> — each frame chunk is the pixel array of a Windows DIB, which is the second
///     half of a <c>.bmp</c> and is handed to <see cref="BmpReader"/> with a file header in front.
///   </item>
/// </list>
/// Anything else is refused by name. There is no partial decode and no empty frame reported as a
/// success: a container this cannot read says which codec it holds and stops.
/// </remarks>
public static class AviReader {

  private const string _FORM_TYPE = "AVI ";
  private const string _HEADER_LIST = "hdrl";
  private const string _STREAM_LIST = "strl";
  private const string _MOVIE_LIST = "movi";
  private const string _RECORD_LIST = "rec ";
  private const string _MAIN_HEADER_ID = "avih";
  private const string _STREAM_HEADER_ID = "strh";
  private const string _STREAM_FORMAT_ID = "strf";

  /// <summary><c>BI_RGB</c>, the only <c>biCompression</c> that means "these are samples".</summary>
  private const uint _BI_RGB = 0;

  /// <summary><c>MJPG</c> as it sits in the little-endian <c>biCompression</c> field.</summary>
  private const uint _MJPG = 0x47504A4D;

  /// <summary>
  /// The same code in lower case, which ffprobe reads as the same codec — a container patched from
  /// <c>MJPG</c> to <c>mjpg</c> is still reported as mjpeg with the same frame count.
  /// </summary>
  private const uint _MJPG_LOWER = 0x67706A6D;

  public static AviFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("AVI file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static AviFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.CanSeek) {
      var data = new byte[stream.Length - stream.Position];
      stream.ReadExactly(data);
      return FromBytes(data);
    }

    using var buffer = new MemoryStream();
    stream.CopyTo(buffer);
    return FromBytes(buffer.ToArray());
  }

  public static AviFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  public static AviFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < RiffHeader.StructSize)
      throw new InvalidDataException("Data is too small to be a valid AVI file.");

    // RiffReader takes a byte array; allocate once here rather than at every use below.
    var riff = RiffReader.FromBytes(data.ToArray());
    if (riff.FormType.ToString() != _FORM_TYPE)
      throw new InvalidDataException($"Invalid AVI form type: expected '{_FORM_TYPE}', got '{riff.FormType}'.");

    var headerList = riff.Lists.FirstOrDefault(l => l.ListType.ToString() == _HEADER_LIST)
      ?? throw new InvalidDataException($"Missing '{_HEADER_LIST}' list.");

    var mainChunk = headerList.Chunks.FirstOrDefault(c => c.Id.ToString() == _MAIN_HEADER_ID)
      ?? throw new InvalidDataException($"Missing '{_MAIN_HEADER_ID}' chunk.");

    if (mainChunk.Data.Length < AviMainHeader.StructSize)
      throw new InvalidDataException($"Invalid '{_MAIN_HEADER_ID}' chunk size: expected at least {AviMainHeader.StructSize}, got {mainChunk.Data.Length}.");

    var header = AviMainHeader.ReadFrom(mainChunk.Data);
    var video = _FindVideoStream(headerList);
    var frames = _CollectFrames(riff, video.StreamNumber);

    return new AviFile {
      Header = header,
      Video = video,
      Frames = frames,
    };
  }

  /// <summary>Finds the first <c>LIST strl</c> describing pictures and works out how they are stored.</summary>
  private static AviVideoStream _FindVideoStream(RiffList headerList) {
    var streamNumber = -1;
    foreach (var streamList in headerList.SubLists.Where(l => l.ListType.ToString() == _STREAM_LIST)) {
      // Every strl counts towards the stream number, video or not: the two digits a frame chunk's
      // name starts with are the stream's position in this list, and skipping the audio ones would
      // make the video stream's frames go looking under the wrong name.
      ++streamNumber;

      var streamHeaderChunk = streamList.Chunks.FirstOrDefault(c => c.Id.ToString() == _STREAM_HEADER_ID);
      if (streamHeaderChunk == null || streamHeaderChunk.Data.Length < AviStreamHeader.StructSize)
        continue;

      var streamHeader = AviStreamHeader.ReadFrom(streamHeaderChunk.Data);
      if (!streamHeader.IsVideo)
        continue;

      var formatChunk = streamList.Chunks.FirstOrDefault(c => c.Id.ToString() == _STREAM_FORMAT_ID)
        ?? throw new InvalidDataException($"Video stream {streamNumber} has no '{_STREAM_FORMAT_ID}' chunk.");

      if (formatChunk.Data.Length < BitmapInfoHeader.StructSize)
        throw new InvalidDataException($"Invalid '{_STREAM_FORMAT_ID}' chunk size: expected at least {BitmapInfoHeader.StructSize}, got {formatChunk.Data.Length}.");

      var info = BitmapInfoHeader.ReadFrom(formatChunk.Data);
      var compression = (uint)info.Compression;
      var coding = compression switch {
        _BI_RGB => AviVideoCoding.Uncompressed,
        _MJPG or _MJPG_LOWER => AviVideoCoding.MotionJpeg,
        _ => throw new NotSupportedException(
          $"AVI video stream {streamNumber} is stored as '{_Describe(compression)}' (0x{compression:X8}, stream handler '{_Describe(streamHeader.Handler)}'), "
          + "which this reader does not decode. Only MJPG frames and uncompressed BI_RGB frames are read."),
      };

      if (info.Width <= 0 || info.Height == 0)
        throw new InvalidDataException($"Video stream {streamNumber} states an impossible size of {info.Width}x{info.Height}.");

      if (info.HeaderSize > formatChunk.Data.Length)
        throw new InvalidDataException($"Video stream {streamNumber} states a {info.HeaderSize}-byte stream format but the chunk holds {formatChunk.Data.Length}.");

      if (coding == AviVideoCoding.Uncompressed)
        _RefuseUnrenderableDepth(streamNumber, info.BitsPerPixel);

      return new AviVideoStream {
        Header = streamHeader,
        Format = formatChunk.Data,
        Compression = compression,
        Coding = coding,
        Width = info.Width,
        Height = Math.Abs(info.Height),
        BitsPerPixel = info.BitsPerPixel,
        IsTopDown = info.Height < 0,
        StreamNumber = streamNumber,
      };
    }

    throw new InvalidDataException("AVI file contains no video stream.");
  }

  /// <summary>Collects the frame chunks belonging to the given stream, in the order they were written.</summary>
  private static List<byte[]> _CollectFrames(RiffFile riff, int streamNumber) {
    var movieList = riff.Lists.FirstOrDefault(l => l.ListType.ToString() == _MOVIE_LIST)
      ?? throw new InvalidDataException($"Missing '{_MOVIE_LIST}' list.");

    var frames = new List<byte[]>();
    _AppendFrames(movieList.Chunks, streamNumber, frames);

    // An interleaved file wraps each group of chunks in a 'rec ' list instead of putting them
    // straight in movi. A file does one or the other, never a mixture, so appending after the direct
    // chunks keeps the frames in order in both cases.
    foreach (var record in movieList.SubLists.Where(l => l.ListType.ToString() == _RECORD_LIST))
      _AppendFrames(record.Chunks, streamNumber, frames);

    return frames;
  }

  private static void _AppendFrames(List<RiffChunk> chunks, int streamNumber, List<byte[]> frames) {
    var compressed = $"{streamNumber:00}dc";
    var uncompressed = $"{streamNumber:00}db";

    foreach (var chunk in chunks) {
      var id = chunk.Id.ToString();
      if (id != compressed && id != uncompressed)
        continue;

      // A zero-length frame chunk carries no picture, and ffmpeg does not invent one for it: an AVI
      // of four '00dc' chunks one of which is empty is reported by `ffprobe -count_frames` as three
      // frames. Counting it here would make our frame count disagree with the oracle's.
      if (chunk.Data.Length == 0)
        continue;

      frames.Add(chunk.Data);
    }
  }

  /// <summary>Refuses an uncompressed depth the bitmap path does not turn into the right colours.</summary>
  /// <remarks>
  /// 16 and 32 were refused here because <see cref="BmpReader"/> returned both of them wrong rather
  /// than refusing: a 32-bit <c>BI_RGB</c> bitmap came back as <c>Indexed1</c> with no palette and
  /// threw when asked for colours, and a 16-bit one was read as 5-6-5 where <c>BI_RGB</c> is 5-5-5,
  /// which put 395 of 2257 pixels of a gradient wrong against ffmpeg's own decode of it. Both were
  /// the bitmap reader's to fix, and both are now fixed: it reads the channel masks rather than
  /// guessing a layout, and a file of either depth decodes to ffmpeg's reading of it exactly. So the
  /// two depths are read here as well, and what is left is the depths a DIB has no meaning for.
  /// </remarks>
  private static void _RefuseUnrenderableDepth(int streamNumber, int bitsPerPixel) {
    if (bitsPerPixel is 1 or 4 or 8 or 16 or 24 or 32)
      return;

    throw new NotSupportedException(
      $"AVI video stream {streamNumber} holds uncompressed frames of {bitsPerPixel} bits per pixel, which is not a depth a device-independent bitmap is stored at. Uncompressed frames of 1, 4, 8, 16, 24 and 32 bits are read.");
  }

  /// <summary>Renders a four-character code the way a person would recognise it in an error message.</summary>
  /// <remarks>
  /// A refusal has to name the codec, and the number alone does not: nobody recognises 0x34363248,
  /// where everybody recognises H264. Codes that are not four printable letters — BI_RGB's zero
  /// among them — have no name to give, so those keep the number.
  /// </remarks>
  private static string _Describe(uint fourCC) {
    Span<char> letters = stackalloc char[4];
    for (var i = 0; i < 4; ++i) {
      var value = (byte)(fourCC >> (i * 8));
      if (value is < 0x20 or > 0x7E)
        return $"0x{fourCC:X8}";

      letters[i] = (char)value;
    }

    return new(letters);
  }

  private static string _Describe(FourCC fourCC)
    => _Describe(fourCC.A | ((uint)fourCC.B << 8) | ((uint)fourCC.C << 16) | ((uint)fourCC.D << 24));

  /// <summary>Turns one BI_RGB frame chunk into the Windows bitmap file it is the pixel array of.</summary>
  /// <remarks>
  /// The stream format chunk is a <c>BITMAPINFOHEADER</c> followed, at eight bits and under, by the
  /// palette — which is precisely what sits between a bitmap's file header and its pixels. So the
  /// whole of the frame's description is already here in the layout the bitmap reader expects, and
  /// the only thing an AVI leaves out is the fourteen-byte file header, which says nothing a frame
  /// depends on beyond where the pixels start.
  /// </remarks>
  internal static byte[] ToBitmapFile(AviVideoStream stream, byte[] frame) {
    var pixelOffset = BitmapFileHeader.StructSize + stream.Format.Length;
    var file = new byte[pixelOffset + frame.Length];

    file[0] = (byte)'B';
    file[1] = (byte)'M';
    BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(2), file.Length);
    BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(10), pixelOffset);
    stream.Format.CopyTo(file, BitmapFileHeader.StructSize);
    frame.CopyTo(file, pixelOffset);

    return file;
  }

  /// <summary>How many bytes one uncompressed frame of this stream must hold.</summary>
  /// <remarks>
  /// Checked before the bitmap reader sees the frame, because that reader fills a row it has no
  /// bytes for with zeroes and returns the picture anyway — which for a short frame chunk would be
  /// a black band presented as a decode.
  /// </remarks>
  internal static int UncompressedFrameSize(AviVideoStream stream) {
    var bytesPerRow = (stream.Width * stream.BitsPerPixel + 7) / 8;
    return ((bytesPerRow + 3) & ~3) * stream.Height;
  }
}
