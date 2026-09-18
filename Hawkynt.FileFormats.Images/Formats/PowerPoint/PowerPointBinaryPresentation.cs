using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using FileFormat.Fpx;

namespace FileFormat.PowerPoint;

/// <summary>Builds a complete one-slide PowerPoint 97-2003 presentation around one raster picture.</summary>
/// <remarks>
/// The persisted object graph and Current User stream were produced once from a controlled one-slide,
/// one-picture presentation by LibreOffice 25.2 and retained as compressed format data. Comparing
/// independent 7x5 and 31x17 source pictures reduces the variable fields to exactly the OfficeArt
/// BLIP UID/size and the picture shape anchor; those fields are rebuilt below from the caller's
/// image. No LibreOffice implementation code is copied and LibreOffice is not a runtime dependency.
/// <para/>
/// The resulting CFB contains the streams PowerPoint requires for an editable presentation:
/// <c>PowerPoint Document</c> with its UserEdit/PersistDirectory/object graph, <c>Current User</c>,
/// and <c>Pictures</c>. LibreOffice emits byte-identical binary content for .ppt, .pps and .pot from
/// the same source, so the same native presentation model serves all three legacy extensions.
/// </remarks>
internal static class PowerPointBinaryPresentation {

  private const int _UidOffset = 962;
  private const int _PictureSizeOffset = 980;
  private const int _AnchorLeftOffset = 48886;
  private const int _AnchorTopOffset = 48888;
  private const int _AnchorRightOffset = 48890;
  private const int _AnchorBottomOffset = 48892;
  private const short _AnchorLeft = 576;
  private const short _AnchorTop = 576;
  private const int _AnchorBox = 1152;

  private static readonly Guid _PowerPoint8ClassId = new("64818D10-4F9B-11CF-86EA-00AA00B929E8");

  private const string _CurrentUserZlib =
    "eNpjYPjGr8LAwCACxPEHJj7mOMzAwMPwhZkZyHcuLSpKzStRCC1OLeIA8gHd7ApM";

  private const string _PowerPointDocumentZlib =
    "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";

  internal static byte[] Write(int width, int height, ReadOnlySpan<byte> png) {
    if (width <= 0 || height <= 0)
      throw new ArgumentOutOfRangeException(nameof(width), $"PowerPoint slide picture needs positive dimensions, not {width}x{height}.");

    var pictures = _BuildPngBlip(png);
    var uid = pictures.AsSpan(PowerPointFile.RecordHeaderSize, 16).ToArray();
    var document = _Inflate(_PowerPointDocumentZlib);
    if (document.Length <= _AnchorBottomOffset + sizeof(short))
      throw new InvalidDataException("Embedded PowerPoint presentation skeleton is truncated.");

    uid.CopyTo(document, _UidOffset);
    BinaryPrimitives.WriteUInt32LittleEndian(document.AsSpan(_PictureSizeOffset), checked((uint)pictures.Length));

    var scale = Math.Min((double)_AnchorBox / width, (double)_AnchorBox / height);
    var displayWidth = Math.Max(1, (int)Math.Round(width * scale));
    var displayHeight = Math.Max(1, (int)Math.Round(height * scale));
    BinaryPrimitives.WriteInt16LittleEndian(document.AsSpan(_AnchorLeftOffset), _AnchorLeft);
    BinaryPrimitives.WriteInt16LittleEndian(document.AsSpan(_AnchorTopOffset), _AnchorTop);
    BinaryPrimitives.WriteInt16LittleEndian(document.AsSpan(_AnchorRightOffset), checked((short)(_AnchorLeft + displayWidth)));
    BinaryPrimitives.WriteInt16LittleEndian(document.AsSpan(_AnchorBottomOffset), checked((short)(_AnchorTop + displayHeight)));

    var compound = new CompoundFileWriter(_PowerPoint8ClassId);
    compound.AddStream(0, "PowerPoint Document", document);
    compound.AddStream(0, "Current User", _Inflate(_CurrentUserZlib));
    compound.AddStream(0, "Pictures", pictures);
    return compound.Build();
  }

  private static byte[] _BuildPngBlip(ReadOnlySpan<byte> png) {
    var payloadLength = checked(png.Length + PowerPointFile.BlipPrefixSize);
    var result = new byte[checked(PowerPointFile.RecordHeaderSize + payloadLength)];
    BinaryPrimitives.WriteUInt16LittleEndian(result, PowerPointFile.PngBlipVersionAndInstance);
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(2), PowerPointFile.PngBlipType);
    BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4), checked((uint)payloadLength));
    PowerPointWriter.ComputeBlipUid(png).CopyTo(result, PowerPointFile.RecordHeaderSize);
    result[PowerPointFile.RecordHeaderSize + 16] = 0xFF;
    png.CopyTo(result.AsSpan(PowerPointFile.RecordHeaderSize + PowerPointFile.BlipPrefixSize));
    return result;
  }

  private static byte[] _Inflate(string base64) {
    using var source = new MemoryStream(Convert.FromBase64String(base64), false);
    using var zlib = new ZLibStream(source, CompressionMode.Decompress, false);
    using var target = new MemoryStream();
    zlib.CopyTo(target);
    return target.ToArray();
  }
}
