using System;
using System.IO;
using System.Text;
using FileFormat.Png;

namespace FileFormat.PocketPcTheme;

/// <summary>Writes a Pocket PC theme as an uncompressed Microsoft cabinet.</summary>
/// <remarks>
/// Cabinet compression type zero is intentional. <see cref="PocketPcThemeReader"/> mirrors XnView's
/// reader and discovers a theme picture by scanning the cabinet bytes for an image signature rather
/// than decompressing folders, so a writer using MSZIP or LZX would produce files its paired reader
/// could not see. PNG keeps the image lossless while still making the usual Pocket PC theme-sized
/// pictures small enough that cabinet storage adds very little.
/// </remarks>
public static class PocketPcThemeWriter {

  private const int _CFHEADER_SIZE = 36;
  private const int _CFFOLDER_SIZE = 8;
  private const int _CFFILE_FIXED_SIZE = 16;
  private const int _CFDATA_HEADER_SIZE = 8;
  private const int _MAX_DATA_BLOCK_SIZE = 32768;
  private const ushort _FILE_ATTRIBUTE_ARCHIVE = 0x20;

  private const string _TODAY_FILE_NAME = "tdywater.png";
  private const string _START_FILE_NAME = "stwater.png";
  private const string _SETUP_FILE_NAME = "_setup.xml";

  private const string _SETUP_XML = """
    <?xml version="1.0" encoding="utf-8"?>
    <wap-provisioningdoc>
      <characteristic type="Install">
        <parm name="InstallPhase" value="install" />
        <parm name="OSVersionMin" value="3.0" />
        <parm name="OSVersionMax" value="6.99" />
        <parm name="BuildNumberMin" value="0" />
        <parm name="BuildNumberMax" value="-536870912" />
        <parm name="AppName" value="PNGCrushCS Theme" />
        <parm name="InstallDir" value="%CE2%" translation="install" />
        <parm name="NumDirs" value="1" />
        <parm name="NumFiles" value="2" />
        <parm name="NumRegKeys" value="0" />
        <parm name="NumRegVals" value="0" />
        <parm name="NumShortcuts" value="0" />
      </characteristic>
      <characteristic type="FileOperation">
        <characteristic type="%CE2%" translation="install">
          <characteristic type="MakeDir" />
          <characteristic type="tdywater.png" translation="install">
            <characteristic type="Extract">
              <parm name="Source" value="tdywater.png" />
            </characteristic>
          </characteristic>
          <characteristic type="stwater.png" translation="install">
            <characteristic type="Extract">
              <parm name="Source" value="stwater.png" />
            </characteristic>
          </characteristic>
        </characteristic>
      </characteristic>
    </wap-provisioningdoc>
    """;

  /// <summary>Encodes the image and wraps it in a Windows CE theme cabinet.</summary>
  public static byte[] ToBytes(PocketPcThemeFile file) {
    if (file.Width <= 0)
      throw new ArgumentOutOfRangeException(nameof(file), "Theme width must be positive.");
    if (file.Height <= 0)
      throw new ArgumentOutOfRangeException(nameof(file), "Theme height must be positive.");
    if (file.PixelData is null)
      throw new ArgumentException("Theme pixel data must not be null.", nameof(file));

    var expectedPixels = checked(file.Width * file.Height * 3);
    if (file.PixelData.Length != expectedPixels)
      throw new ArgumentException(
        $"Theme pixel data has {file.PixelData.Length} bytes; {expectedPixels} are required for {file.Width}x{file.Height} RGB24.",
        nameof(file));

    var png = PngWriter.ToBytes(PngFile.FromRawImage(PocketPcThemeFile.ToRawImage(file)));
    var setup = Encoding.UTF8.GetBytes(_SETUP_XML);
    return _WriteCabinet(png, setup);
  }

  private static byte[] _WriteCabinet(byte[] png, byte[] setup) {
    var payloadLength64 = (long)png.Length * 2 + setup.Length;
    if (payloadLength64 > int.MaxValue)
      throw new InvalidOperationException("Pocket PC theme is too large to serialize into one byte array.");

    var payloadLength = (int)payloadLength64;
    var blockCount = (payloadLength + _MAX_DATA_BLOCK_SIZE - 1) / _MAX_DATA_BLOCK_SIZE;
    if ((uint)blockCount > ushort.MaxValue)
      throw new InvalidOperationException("Pocket PC theme needs more CAB data blocks than one folder can address.");

    var coffFiles = _CFHEADER_SIZE + _CFFOLDER_SIZE;
    var dataOffset = coffFiles
      + _CFFILE_FIXED_SIZE + Encoding.ASCII.GetByteCount(_TODAY_FILE_NAME) + 1
      + _CFFILE_FIXED_SIZE + Encoding.ASCII.GetByteCount(_START_FILE_NAME) + 1
      + _CFFILE_FIXED_SIZE + Encoding.ASCII.GetByteCount(_SETUP_FILE_NAME) + 1;
    var cabinetLength64 = (long)dataOffset + payloadLength + (long)blockCount * _CFDATA_HEADER_SIZE;
    if (cabinetLength64 > int.MaxValue)
      throw new InvalidOperationException("Pocket PC theme is too large to serialize into one byte array.");

    var payload = new byte[payloadLength];
    png.CopyTo(payload, 0);
    png.CopyTo(payload, png.Length);
    setup.CopyTo(payload, png.Length * 2);

    using var memory = new MemoryStream((int)cabinetLength64);
    using var writer = new BinaryWriter(memory, Encoding.ASCII, leaveOpen: true);

    _WriteHeader(writer, (uint)cabinetLength64, (uint)coffFiles, (ushort)blockCount, (uint)dataOffset);
    _WriteFile(writer, _TODAY_FILE_NAME, (uint)png.Length, 0);
    _WriteFile(writer, _START_FILE_NAME, (uint)png.Length, (uint)png.Length);
    _WriteFile(writer, _SETUP_FILE_NAME, (uint)setup.Length, (uint)(png.Length * 2L));

    for (var sourceOffset = 0; sourceOffset < payload.Length;) {
      var blockLength = Math.Min(_MAX_DATA_BLOCK_SIZE, payload.Length - sourceOffset);
      writer.Write(0u); // CFDATA.csum: [MS-CAB] explicitly permits zero when no checksum is supplied.
      writer.Write((ushort)blockLength);
      writer.Write((ushort)blockLength);
      writer.Write(payload, sourceOffset, blockLength);
      sourceOffset += blockLength;
    }

    writer.Flush();
    return memory.ToArray();
  }

  private static void _WriteHeader(
    BinaryWriter writer,
    uint cabinetLength,
    uint filesOffset,
    ushort dataBlockCount,
    uint dataOffset) {
    writer.Write(PocketPcThemeFile.Signature);
    writer.Write(0u);
    writer.Write(cabinetLength);
    writer.Write(0u);
    writer.Write(filesOffset);
    writer.Write(0u);
    writer.Write((byte)3); // versionMinor
    writer.Write((byte)1); // versionMajor
    writer.Write((ushort)1); // cFolders
    writer.Write((ushort)3); // cFiles
    writer.Write((ushort)0); // flags: no reserves and no cabinet chaining
    writer.Write((ushort)0); // setID
    writer.Write((ushort)0); // iCabinet

    writer.Write(dataOffset); // CFFOLDER.coffCabStart
    writer.Write(dataBlockCount);
    writer.Write((ushort)0); // CFFOLDER.typeCompress = NONE
  }

  private static void _WriteFile(BinaryWriter writer, string name, uint size, uint folderOffset) {
    writer.Write(size);
    writer.Write(folderOffset);
    writer.Write((ushort)0); // iFolder
    writer.Write((ushort)0); // DOS date: deterministic/unspecified
    writer.Write((ushort)0); // DOS time: deterministic/unspecified
    writer.Write(_FILE_ATTRIBUTE_ARCHIVE);
    writer.Write(Encoding.ASCII.GetBytes(name));
    writer.Write((byte)0);
  }
}
