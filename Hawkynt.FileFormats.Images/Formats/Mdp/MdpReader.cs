using System;
using System.IO;
using FileFormat.Mda;

namespace FileFormat.Mdp;

/// <summary>Reads MicroDesign 3 Page (.MDP) files.</summary>
public static class MdpReader {

  private const int _MinimumSize = MdaFile.StampSize + 4;

  public static MdpFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("MicroDesign Page file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static MdpFile FromStream(Stream stream) {
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

  public static MdpFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  public static MdpFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < _MinimumSize)
      throw new InvalidDataException("Truncated MicroDesign Page header.");
    if (!data[..4].SequenceEqual(".MDP"u8))
      throw new InvalidDataException("Invalid MicroDesign Page file type.");
    if (!data.Slice(18, 5).SequenceEqual("v1.30"u8))
      throw new InvalidDataException("MicroDesign Page files must use the MicroDesign 3 v1.30 format.");

    var resolution = (MdpResolution)data[34];
    if (!Enum.IsDefined(resolution))
      throw new InvalidDataException($"Unsupported MicroDesign Page resolution code {data[34]}.");

    var pageFormat = (MdpPageFormat)data[35];
    if (!Enum.IsDefined(pageFormat))
      throw new InvalidDataException($"Unsupported MicroDesign Page format code {data[35]}.");

    var pageRamBlocks = data[36];

    // MDP is normatively AREA3 with only the type marker and stamp bytes 34-36 changed. Normalize
    // those four bytes and let the single AREA3 implementation perform all structural validation.
    var normalized = data.ToArray();
    ".MDA"u8.CopyTo(normalized);
    normalized[34] = 0;
    normalized[35] = 0;
    normalized[36] = 0;
    var area = MdaReader.FromBytes(normalized);
    if (area.Version != MdaVersion.Area3)
      throw new InvalidDataException("MicroDesign Page raster is not AREA3 encoded.");

    return new MdpFile {
      Width = area.Width,
      Height = area.Height,
      SerialNumber = area.SerialNumber,
      Resolution = resolution,
      PageFormat = pageFormat,
      PageRamBlocks = pageRamBlocks,
      RasterData = area.RasterData,
    };
  }
}
