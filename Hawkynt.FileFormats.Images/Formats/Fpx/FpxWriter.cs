using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace FileFormat.Fpx;

/// <summary>Writes a lossless non-hierarchical FlashPix image view with one NIF RGB source image.</summary>
/// <remarks>
/// The FlashPix 1.0 specification explicitly permits a non-hierarchical image containing only its
/// full-resolution subimage. The resolution still keeps the number it would have in a complete
/// pyramid. Pixels are stored as uncompressed 8-bit NIF RGB tiles, avoiding a lossy JPEG round trip
/// while retaining the format's mandatory 64 by 64 tiling and edge extrusion.
/// </remarks>
public static class FpxWriter {

  private const int _TileSide = 64;
  private const int _Channels = 3;
  private const int _BytesPerTile = _TileSide * _TileSide * _Channels;
  private const int _FlashPixStreamHeaderSize = 28;
  private const int _SubimageHeaderDataSize = 36;
  private const int _TileHeaderSize = 16;

  private const uint _VtI2 = 2;
  private const uint _VtI4 = 3;
  private const uint _VtUi1 = 17;
  private const uint _VtUi4 = 19;
  private const uint _VtBlob = 65;
  private const uint _VtClsid = 72;
  private const uint _VtVector = 0x1000;

  private const uint _PidCodePage = 1;
  private const uint _PidNumberOfResolutions = 0x01000000;
  private const uint _PidHighestWidth = 0x01000002;
  private const uint _PidHighestHeight = 0x01000003;
  private const uint _PidDataObjectId = 0x00010000;
  private const uint _PidDataObjectStatus = 0x00010100;
  private const uint _PidCreatingTransform = 0x00010101;
  private const uint _PidUsingTransforms = 0x00010102;
  private const uint _PidCachedImageHeight = 0x10000000;
  private const uint _PidCachedImageWidth = 0x10000001;
  private const uint _PidVisibleOutputs = 0x00010100;
  private const uint _PidMaximumImageIndex = 0x00010101;
  private const uint _PidMaximumTransformIndex = 0x00010102;
  private const uint _PidMaximumOperationIndex = 0x00010103;

  private static readonly Guid _ImageViewClass = new("56616700-C154-11CE-8553-00AA00A1F95B");
  private static readonly Guid _ImageObjectClass = new("56616000-C154-11CE-8553-00AA00A1F95B");
  private static readonly Guid _ResolutionClass = new("56616100-C154-11CE-8553-00AA00A1F95B");
  private static readonly Guid _ImageContentsClass = new("56616400-C154-11CE-8553-00AA00A1F95B");
  private static readonly Guid _DataObjectDescriptionClass = new("56616080-C154-11CE-8553-00AA00A1F95B");
  private static readonly Guid _GlobalInfoClass = new("56616F00-C154-11CE-8553-00AA00A1F95B");
  private static readonly Guid _SummaryInformationClass = new("F29F85E0-4FF9-1068-AB91-08002B27B3D9");
  private static readonly Guid _SubimageHeaderClass = new("00010000-C154-11CE-8553-00AA00A1F95B");
  private static readonly Guid _SubimageDataClass = new("00010100-C154-11CE-8553-00AA00A1F95B");

  /// <summary>Serializes a FlashPix picture to a CFB-backed image view.</summary>
  public static byte[] ToBytes(FpxFile file) {
    _Validate(file);

    var resolution = _GetResolutionNumber(file.Width, file.Height);
    var (header, data) = _BuildSubimage(file);
    var objectId = _CreateDeterministicObjectId(file);

    var compound = new CompoundFileWriter(_ImageViewClass);
    compound.AddStream(0, "\u0001CompObj", _BuildCompObj(_ImageViewClass, "FlashPix Image View"));
    compound.AddStream(0, "\u0005Data Object 000001", _BuildSourceDescription(objectId, file.Width, file.Height));
    compound.AddStream(0, "\u0005Global Info", _BuildGlobalInfo());
    compound.AddStream(0, "\u0005SummaryInformation", _BuildSummaryInformation());

    var image = compound.AddStorage(0, "Data Object Store 000001", _ImageObjectClass);
    compound.AddStream(image, "\u0001CompObj", _BuildCompObj(_ImageObjectClass, "FlashPix Image Object"));
    compound.AddStream(image, "\u0005Image Contents", _BuildImageContents(file.Width, file.Height, resolution));
    compound.AddStream(image, "\u0005SummaryInformation", _BuildSummaryInformation());

    var resolutionStorage = compound.AddStorage(image, $"Resolution {resolution:0000}", _ResolutionClass);
    compound.AddStream(resolutionStorage, "Subimage 0000 Data", data);
    compound.AddStream(resolutionStorage, "Subimage 0000 Header", header);

    return compound.Build();
  }

  private static void _Validate(FpxFile file) {
    if (file.Width <= 0)
      throw new ArgumentOutOfRangeException(nameof(file), file.Width, "Image width must be positive.");
    if (file.Height <= 0)
      throw new ArgumentOutOfRangeException(nameof(file), file.Height, "Image height must be positive.");
    if (file.PixelData is null)
      throw new ArgumentException("PixelData must not be null.", nameof(file));

    var expected = checked((long)file.Width * file.Height * _Channels);
    if (expected > int.MaxValue || file.PixelData.Length != (int)expected)
      throw new ArgumentException(
        $"PixelData must contain exactly {expected} RGB bytes for a {file.Width}x{file.Height} image.",
        nameof(file));

    var across = ((long)file.Width + _TileSide - 1) / _TileSide;
    var down = ((long)file.Height + _TileSide - 1) / _TileSide;
    var dataLength = checked(across * down * _BytesPerTile + _FlashPixStreamHeaderSize);
    if (dataLength > int.MaxValue)
      throw new ArgumentException("The tiled FlashPix image exceeds the maximum managed buffer size.", nameof(file));
  }

  private static int _GetResolutionNumber(int width, int height) {
    var size = Math.Max(width, height);
    var resolution = 0;
    while (size > _TileSide) {
      size = (size >> 1) + (size & 1);
      ++resolution;
    }

    return resolution;
  }

  private static (byte[] Header, byte[] Data) _BuildSubimage(FpxFile file) {
    var across = checked((file.Width + _TileSide - 1) / _TileSide);
    var down = checked((file.Height + _TileSide - 1) / _TileSide);
    var tileCount = checked(across * down);

    var headerPayload = new byte[checked(_SubimageHeaderDataSize + tileCount * _TileHeaderSize)];
    _WriteUInt32(headerPayload, 0, _SubimageHeaderDataSize);
    _WriteUInt32(headerPayload, 4, file.Width);
    _WriteUInt32(headerPayload, 8, file.Height);
    _WriteUInt32(headerPayload, 12, tileCount);
    _WriteUInt32(headerPayload, 16, _TileSide);
    _WriteUInt32(headerPayload, 20, _TileSide);
    _WriteUInt32(headerPayload, 24, _Channels);
    _WriteUInt32(headerPayload, 28, _SubimageHeaderDataSize);
    _WriteUInt32(headerPayload, 32, _TileHeaderSize);

    var dataPayload = new byte[checked(tileCount * _BytesPerTile)];
    for (var tile = 0; tile < tileCount; ++tile) {
      var entry = _SubimageHeaderDataSize + tile * _TileHeaderSize;
      _WriteUInt32(headerPayload, entry, checked(tile * _BytesPerTile));
      _WriteUInt32(headerPayload, entry + 4, _BytesPerTile);
      _WriteUInt32(headerPayload, entry + 8, 0); // uncompressed
      _WriteUInt32(headerPayload, entry + 12, 0);

      var left = tile % across * _TileSide;
      var top = tile / across * _TileSide;
      _WritePaddedTile(file, left, top, dataPayload.AsSpan(tile * _BytesPerTile, _BytesPerTile));
    }

    return (
      _BuildFlashPixStream(_SubimageHeaderClass, headerPayload),
      _BuildFlashPixStream(_SubimageDataClass, dataPayload));
  }

  private static void _WritePaddedTile(FpxFile file, int left, int top, Span<byte> destination) {
    for (var y = 0; y < _TileSide; ++y) {
      var sourceY = Math.Min(top + y, file.Height - 1);
      for (var x = 0; x < _TileSide; ++x) {
        var sourceX = Math.Min(left + x, file.Width - 1);
        var source = checked((sourceY * file.Width + sourceX) * _Channels);
        var target = (y * _TileSide + x) * _Channels;
        file.PixelData.AsSpan(source, _Channels).CopyTo(destination[target..]);
      }
    }
  }

  private static byte[] _BuildImageContents(int width, int height, int resolution) {
    var resolutionBase = 0x02000000u | checked((uint)resolution << 16);
    var color = new byte[20];
    _WriteUInt32(color, 0, 1); // one subimage
    _WriteUInt32(color, 4, _Channels);
    _WriteUInt32(color, 8, 0x00030000); // NIF RGB: red
    _WriteUInt32(color, 12, 0x00030001); // green
    _WriteUInt32(color, 16, 0x00030002); // blue

    return _BuildPropertySet(_ImageContentsClass, [
      new(_PidCodePage, _I2(1200)),
      new(_PidNumberOfResolutions, _Ui4(1)),
      new(_PidHighestWidth, _Ui4((uint)width)),
      new(_PidHighestHeight, _Ui4((uint)height)),
      new(resolutionBase, _Ui4((uint)width)),
      new(resolutionBase | 1, _Ui4((uint)height)),
      new(resolutionBase | 2, _Blob(color)),
      new(resolutionBase | 3, _Ui4Vector([_VtUi1, _VtUi1, _VtUi1])),
      new(resolutionBase | 4, _I4(0)),
    ]);
  }

  private static byte[] _BuildSourceDescription(Guid objectId, int width, int height)
    => _BuildPropertySet(_DataObjectDescriptionClass, [
      new(_PidCodePage, _I2(1200)),
      new(_PidDataObjectId, _Clsid(objectId)),
      new(_PidDataObjectStatus, _Ui4(0x00010001)), // exists, not purgeable
      new(_PidCreatingTransform, _Ui4(0)),
      new(_PidUsingTransforms, _Ui4Vector([])),
      new(_PidCachedImageHeight, _Ui4((uint)height)),
      new(_PidCachedImageWidth, _Ui4((uint)width)),
    ]);

  private static byte[] _BuildGlobalInfo()
    => _BuildPropertySet(_GlobalInfoClass, [
      new(_PidCodePage, _I2(1200)),
      new(_PidVisibleOutputs, _Ui4Vector([1])),
      new(_PidMaximumImageIndex, _Ui4(1)),
      new(_PidMaximumTransformIndex, _Ui4(0)),
      new(_PidMaximumOperationIndex, _Ui4(0)),
    ]);

  private static byte[] _BuildSummaryInformation()
    => _BuildPropertySet(_SummaryInformationClass, [
      new(_PidCodePage, _I2(1252)),
    ]);

  private static byte[] _BuildPropertySet(Guid classId, IReadOnlyList<Property> properties) {
    var valuesOffset = checked(8 + properties.Count * 8);
    var sectionSize = valuesOffset;
    foreach (var property in properties)
      sectionSize = checked(sectionSize + property.TypedValue.Length);

    var result = new byte[checked(48 + sectionSize)];
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(0, 2), 0xFFFE);
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(2, 2), 0);
    BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4, 4), 0);
    classId.TryWriteBytes(result.AsSpan(8, 16));
    BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(24, 4), 1);
    classId.TryWriteBytes(result.AsSpan(28, 16));
    BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(44, 4), 48);

    BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(48, 4), (uint)sectionSize);
    BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(52, 4), (uint)properties.Count);

    var valueAt = valuesOffset;
    for (var i = 0; i < properties.Count; ++i) {
      var pairAt = 56 + i * 8;
      BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(pairAt, 4), properties[i].Id);
      BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(pairAt + 4, 4), (uint)valueAt);
      properties[i].TypedValue.CopyTo(result, 48 + valueAt);
      valueAt += properties[i].TypedValue.Length;
    }

    return result;
  }

  private static byte[] _I2(short value) {
    var result = new byte[8];
    _WriteUInt32(result, 0, _VtI2);
    BinaryPrimitives.WriteInt16LittleEndian(result.AsSpan(4, 2), value);
    return result;
  }

  private static byte[] _I4(int value) {
    var result = new byte[8];
    _WriteUInt32(result, 0, _VtI4);
    BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(4, 4), value);
    return result;
  }

  private static byte[] _Ui4(uint value) {
    var result = new byte[8];
    _WriteUInt32(result, 0, _VtUi4);
    _WriteUInt32(result, 4, value);
    return result;
  }

  private static byte[] _Ui4Vector(ReadOnlySpan<uint> values) {
    var result = new byte[checked(8 + values.Length * sizeof(uint))];
    _WriteUInt32(result, 0, _VtUi4 | _VtVector);
    _WriteUInt32(result, 4, values.Length);
    for (var i = 0; i < values.Length; ++i)
      _WriteUInt32(result, 8 + i * sizeof(uint), values[i]);
    return result;
  }

  private static byte[] _Blob(ReadOnlySpan<byte> value) {
    var paddedLength = checked((value.Length + 3) & ~3);
    var result = new byte[checked(8 + paddedLength)];
    _WriteUInt32(result, 0, _VtBlob);
    _WriteUInt32(result, 4, value.Length);
    value.CopyTo(result.AsSpan(8));
    return result;
  }

  private static byte[] _Clsid(Guid value) {
    var result = new byte[20];
    _WriteUInt32(result, 0, _VtClsid);
    value.TryWriteBytes(result.AsSpan(4, 16));
    return result;
  }

  private static byte[] _BuildFlashPixStream(Guid classId, ReadOnlySpan<byte> payload) {
    var result = new byte[checked(_FlashPixStreamHeaderSize + payload.Length)];
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(0, 2), 0xFFFE);
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(2, 2), 0);
    BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4, 4), 0);
    classId.TryWriteBytes(result.AsSpan(8, 16));
    BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(24, 4), 1);
    payload.CopyTo(result.AsSpan(_FlashPixStreamHeaderSize));
    return result;
  }

  private static byte[] _BuildCompObj(Guid classId, string userType) {
    var clipboard = classId.ToString("B").ToUpperInvariant();
    const string programId = "Hawkynt.FlashPix";

    var ansiUser = Encoding.Latin1.GetBytes(userType + '\0');
    var ansiClipboard = Encoding.ASCII.GetBytes(clipboard + '\0');
    var ansiProgram = Encoding.ASCII.GetBytes(programId + '\0');
    var unicodeUser = Encoding.Unicode.GetBytes(userType + '\0');
    var unicodeClipboard = Encoding.Unicode.GetBytes(clipboard + '\0');
    var unicodeProgram = Encoding.Unicode.GetBytes(programId + '\0');

    var length = checked(
      28
      + 4 + ansiUser.Length
      + 4 + ansiClipboard.Length
      + 4 + ansiProgram.Length
      + 4
      + 4 + unicodeUser.Length
      + 4 + unicodeClipboard.Length
      + 4 + unicodeProgram.Length);

    var result = new byte[length];
    var at = 0;
    _WriteUInt32(result, at, 0xFFFE0001); at += 4;
    _WriteUInt32(result, at, 0x00000A03); at += 4;
    _WriteUInt32(result, at, uint.MaxValue); at += 4;
    classId.TryWriteBytes(result.AsSpan(at, 16)); at += 16;
    _WriteCounted(result, ref at, ansiUser);
    _WriteCounted(result, ref at, ansiClipboard);
    _WriteCounted(result, ref at, ansiProgram);
    _WriteUInt32(result, at, 0x71B239F4); at += 4;
    _WriteCounted(result, ref at, unicodeUser);
    _WriteCounted(result, ref at, unicodeClipboard);
    _WriteCounted(result, ref at, unicodeProgram);
    return result;
  }

  private static void _WriteCounted(byte[] destination, ref int at, byte[] value) {
    _WriteUInt32(destination, at, value.Length);
    at += 4;
    value.CopyTo(destination, at);
    at += value.Length;
  }

  private static Guid _CreateDeterministicObjectId(FpxFile file) {
    using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    Span<byte> dimensions = stackalloc byte[8];
    BinaryPrimitives.WriteInt32LittleEndian(dimensions, file.Width);
    BinaryPrimitives.WriteInt32LittleEndian(dimensions[4..], file.Height);
    hash.AppendData(dimensions);
    hash.AppendData(file.PixelData);
    var digest = hash.GetHashAndReset();
    return new Guid(digest.AsSpan(0, 16));
  }

  private static void _WriteUInt32(byte[] data, int offset, int value)
    => BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset, sizeof(uint)), checked((uint)value));

  private static void _WriteUInt32(byte[] data, int offset, uint value)
    => BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset, sizeof(uint)), value);

  private readonly record struct Property(uint Id, byte[] TypedValue);
}
