using System;
using System.IO;
using System.Text;
using FileFormat.Png;

namespace FileFormat.PocketPcTheme;

/// <summary>Writes a Pocket PC theme as an uncompressed Windows CE installation cabinet.</summary>
/// <remarks>
/// Windows CE installation cabinets add a contract on top of CAB: a binary <c>.000</c> instruction
/// member and numbered 8.3 payload names. Pocket PC versions before Windows Mobile 5 use that binary
/// instruction member; newer versions may use <c>_setup.xml</c>. The writer emits both forms with the
/// same two file operations.
/// <para/>
/// CAB compression type NONE is deliberate. Older Pocket PC devices do not support compressed CAB
/// packages. PNG keeps the arbitrary source picture lossless; the same image is used for both the
/// Today and Start-menu backgrounds because this format model contains one image, not two backgrounds
/// and a colour scheme.
/// </remarks>
public static class PocketPcThemeWriter {

  private const int _CFHEADER_SIZE = 36;
  private const int _CFFOLDER_SIZE = 8;
  private const int _CFFILE_FIXED_SIZE = 16;
  private const int _CFDATA_HEADER_SIZE = 8;
  private const int _MAX_DATA_BLOCK_SIZE = 32768;
  private const int _CE_CONTROL_HEADER_SIZE = 100;
  private const ushort _FILE_ATTRIBUTE_ARCHIVE = 0x20;
  private const uint _CE_FILE_ALWAYS_OVERWRITE = 0x40000000;

  private const string _CONTROL_FILE_NAME = "PNGCRUSH.000";
  private const string _START_CAB_FILE_NAME = "0STWATER.002";
  private const string _TODAY_CAB_FILE_NAME = "TDYWATER.001";
  private const string _SETUP_FILE_NAME = "_setup.xml";
  private const string _TODAY_TARGET_FILE_NAME = "tdywater.png";
  private const string _START_TARGET_FILE_NAME = "stwater.png";

  private const string _SETUP_XML = """
    <?xml version="1.0" encoding="utf-8"?>
    <wap-provisioningdoc>
      <characteristic type="Install">
        <parm name="InstallPhase" value="install" />
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
              <parm name="Source" value="TDYWATER.001" />
            </characteristic>
          </characteristic>
          <characteristic type="stwater.png" translation="install">
            <characteristic type="Extract">
              <parm name="Source" value="0STWATER.002" />
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
    var control = _BuildCeControl();
    var setup = Encoding.UTF8.GetBytes(_SETUP_XML);
    return _WriteCabinet(control, png, setup);
  }

  private static byte[] _BuildCeControl() {
    using var memory = new MemoryStream(256);
    using var writer = new BinaryWriter(memory, Encoding.ASCII, leaveOpen: true);
    writer.Write(new byte[_CE_CONTROL_HEADER_SIZE]);

    var appNameOffset = checked((ushort)memory.Position);
    var appNameLength = _WriteAsciiZ(writer, "PNGCrushCS Theme");
    var providerOffset = checked((ushort)memory.Position);
    var providerLength = _WriteAsciiZ(writer, "Hawkynt");

    var stringsOffset = checked((uint)memory.Position);
    _WriteCeString(writer, 1, "%CE2%");

    var dirsOffset = checked((uint)memory.Position);
    writer.Write((ushort)1);
    writer.Write((ushort)4);
    writer.Write((ushort)1);
    writer.Write((ushort)0);

    var filesOffset = checked((uint)memory.Position);
    _WriteCeFile(writer, 1, _TODAY_TARGET_FILE_NAME);
    _WriteCeFile(writer, 2, _START_TARGET_FILE_NAME);

    var emptySectionsOffset = checked((uint)memory.Position);
    var totalLength = emptySectionsOffset;

    memory.Position = 0;
    writer.Write("MSCE"u8);
    writer.Write(0u);
    writer.Write(totalLength);
    writer.Write(0u);
    writer.Write(1u);
    writer.Write(0u);
    writer.Write(0u);
    writer.Write(0u);
    writer.Write(0u);
    writer.Write(0u);
    writer.Write(0u);
    writer.Write(0u);
    writer.Write((ushort)1);
    writer.Write((ushort)1);
    writer.Write((ushort)2);
    writer.Write((ushort)0);
    writer.Write((ushort)0);
    writer.Write((ushort)0);
    writer.Write(stringsOffset);
    writer.Write(dirsOffset);
    writer.Write(filesOffset);
    writer.Write(emptySectionsOffset);
    writer.Write(emptySectionsOffset);
    writer.Write(emptySectionsOffset);
    writer.Write(appNameOffset);
    writer.Write(appNameLength);
    writer.Write(providerOffset);
    writer.Write(providerLength);
    writer.Write((ushort)0);
    writer.Write((ushort)0);
    writer.Write((ushort)0);
    writer.Write((ushort)0);

    writer.Flush();
    return memory.ToArray();
  }

  private static ushort _WriteAsciiZ(BinaryWriter writer, string value) {
    var bytes = Encoding.ASCII.GetBytes(value);
    writer.Write(bytes);
    writer.Write((byte)0);
    return checked((ushort)(bytes.Length + 1));
  }

  private static void _WriteCeString(BinaryWriter writer, ushort id, string value) {
    writer.Write(id);
    writer.Write(checked((ushort)(Encoding.ASCII.GetByteCount(value) + 1)));
    _WriteAsciiZ(writer, value);
  }

  private static void _WriteCeFile(BinaryWriter writer, ushort id, string targetName) {
    writer.Write(id);
    writer.Write((ushort)1);
    writer.Write(id);
    writer.Write(_CE_FILE_ALWAYS_OVERWRITE);
    writer.Write(checked((ushort)(Encoding.ASCII.GetByteCount(targetName) + 1)));
    _WriteAsciiZ(writer, targetName);
  }

  private static byte[] _WriteCabinet(byte[] control, byte[] png, byte[] setup) {
    var payloadLength64 = (long)control.Length + (long)png.Length * 2 + setup.Length;
    if (payloadLength64 > int.MaxValue)
      throw new InvalidOperationException("Pocket PC theme is too large to serialize into one byte array.");

    var payloadLength = (int)payloadLength64;
    var blockCount = (payloadLength + _MAX_DATA_BLOCK_SIZE - 1) / _MAX_DATA_BLOCK_SIZE;
    if ((uint)blockCount > ushort.MaxValue)
      throw new InvalidOperationException("Pocket PC theme needs more CAB data blocks than one folder can address.");

    var coffFiles = _CFHEADER_SIZE + _CFFOLDER_SIZE;
    var dataOffset = coffFiles
      + _CabFileRecordSize(_CONTROL_FILE_NAME)
      + _CabFileRecordSize(_START_CAB_FILE_NAME)
      + _CabFileRecordSize(_TODAY_CAB_FILE_NAME)
      + _CabFileRecordSize(_SETUP_FILE_NAME);
    var cabinetLength64 = (long)dataOffset + payloadLength + (long)blockCount * _CFDATA_HEADER_SIZE;
    if (cabinetLength64 > int.MaxValue)
      throw new InvalidOperationException("Pocket PC theme is too large to serialize into one byte array.");

    var payload = new byte[payloadLength];
    var payloadAt = 0;
    control.CopyTo(payload, payloadAt);
    payloadAt += control.Length;
    png.CopyTo(payload, payloadAt);
    payloadAt += png.Length;
    png.CopyTo(payload, payloadAt);
    payloadAt += png.Length;
    setup.CopyTo(payload, payloadAt);

    using var memory = new MemoryStream((int)cabinetLength64);
    using var writer = new BinaryWriter(memory, Encoding.ASCII, leaveOpen: true);

    _WriteHeader(writer, (uint)cabinetLength64, (uint)coffFiles, (ushort)blockCount, (uint)dataOffset);
    _WriteFile(writer, _CONTROL_FILE_NAME, (uint)control.Length, 0);
    _WriteFile(writer, _START_CAB_FILE_NAME, (uint)png.Length, (uint)control.Length);
    _WriteFile(writer, _TODAY_CAB_FILE_NAME, (uint)png.Length, checked((uint)(control.Length + (long)png.Length)));
    _WriteFile(writer, _SETUP_FILE_NAME, (uint)setup.Length, checked((uint)(control.Length + (long)png.Length * 2)));

    for (var sourceOffset = 0; sourceOffset < payload.Length;) {
      var blockLength = Math.Min(_MAX_DATA_BLOCK_SIZE, payload.Length - sourceOffset);
      writer.Write(0u);
      writer.Write((ushort)blockLength);
      writer.Write((ushort)blockLength);
      writer.Write(payload, sourceOffset, blockLength);
      sourceOffset += blockLength;
    }

    writer.Flush();
    return memory.ToArray();
  }

  private static int _CabFileRecordSize(string name)
    => _CFFILE_FIXED_SIZE + Encoding.ASCII.GetByteCount(name) + 1;

  private static void _WriteHeader(BinaryWriter writer, uint cabinetLength, uint filesOffset, ushort dataBlockCount, uint dataOffset) {
    writer.Write(PocketPcThemeFile.Signature);
    writer.Write(0u);
    writer.Write(cabinetLength);
    writer.Write(0u);
    writer.Write(filesOffset);
    writer.Write(0u);
    writer.Write((byte)3);
    writer.Write((byte)1);
    writer.Write((ushort)1);
    writer.Write((ushort)4);
    writer.Write((ushort)0);
    writer.Write((ushort)0);
    writer.Write((ushort)0);
    writer.Write(dataOffset);
    writer.Write(dataBlockCount);
    writer.Write((ushort)0);
  }

  private static void _WriteFile(BinaryWriter writer, string name, uint size, uint folderOffset) {
    writer.Write(size);
    writer.Write(folderOffset);
    writer.Write((ushort)0);
    writer.Write((ushort)0);
    writer.Write((ushort)0);
    writer.Write(_FILE_ATTRIBUTE_ARCHIVE);
    writer.Write(Encoding.ASCII.GetBytes(name));
    writer.Write((byte)0);
  }
}
