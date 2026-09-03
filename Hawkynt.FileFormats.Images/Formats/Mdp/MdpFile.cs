using System;
using FileFormat.Core;
using FileFormat.Mda;

namespace FileFormat.Mdp;

/// <summary>Resolution code stored in a MicroDesign Page stamp.</summary>
public enum MdpResolution : byte {
  /// <summary>240 dots per inch.</summary>
  Dpi240 = 0,

  /// <summary>360 dots per inch.</summary>
  Dpi360 = 1,

  /// <summary>300 dots per inch.</summary>
  Dpi300 = 2,
}

/// <summary>Page-layout code stored in a MicroDesign Page stamp.</summary>
public enum MdpPageFormat : byte {
  /// <summary>A5 portrait.</summary>
  A5Portrait = 0,

  /// <summary>A5 landscape.</summary>
  A5Landscape = 1,

  /// <summary>A4 portrait.</summary>
  A4Portrait = 2,

  /// <summary>A4 landscape.</summary>
  A4Landscape = 3,

  /// <summary>A5 portrait, high-resolution mode.</summary>
  A5PortraitHighResolution = 4,

  /// <summary>A5 landscape, high-resolution mode.</summary>
  A5LandscapeHighResolution = 5,
}

/// <summary>In-memory representation of a MicroDesign 3 Page (.MDP) bitmap.</summary>
/// <remarks>
/// MDP uses the MicroDesign 3 AREA3 raster codec and adds page-layout metadata to stamp bytes 34-36.
/// </remarks>
[FormatDetectionPriority(100)]
[FormatMagicBytes([0x2E, 0x4D, 0x44, 0x50])]
public readonly record struct MdpFile : IImageFormatReader<MdpFile>, IImageToRawImage<MdpFile>, IImageFromRawImage<MdpFile>, IImageFormatWriter<MdpFile> {

  /// <summary>Default resolution used when generic pixel conversion has no page metadata.</summary>
  public const MdpResolution DefaultResolution = MdpResolution.Dpi240;

  /// <summary>Default page format used when generic pixel conversion has no page metadata.</summary>
  public const MdpPageFormat DefaultPageFormat = MdpPageFormat.A5Portrait;

  /// <summary>Number of bytes represented by one page-RAM block in the MDP stamp.</summary>
  public const int PageRamBlockSize = 16 * 1024;

  static string IImageFormatMetadata<MdpFile>.PrimaryExtension => ".mdp";
  static string[] IImageFormatMetadata<MdpFile>.FileExtensions => [".mdp"];
  static MdpFile IImageFormatReader<MdpFile>.FromSpan(ReadOnlySpan<byte> data) => MdpReader.FromSpan(data);
  static byte[] IImageFormatWriter<MdpFile>.ToBytes(MdpFile file) => MdpWriter.ToBytes(file);

  /// <summary>Page bitmap width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Page bitmap height in lines.</summary>
  public int Height { get; init; }

  /// <summary>Seven-character ASCII user serial number from the file stamp.</summary>
  public string SerialNumber { get; init; }

  /// <summary>Declared page resolution.</summary>
  public MdpResolution Resolution { get; init; }

  /// <summary>Declared paper size/orientation.</summary>
  public MdpPageFormat PageFormat { get; init; }

  /// <summary>Page RAM requirement stored by MicroDesign, measured in 16 KiB blocks.</summary>
  public byte PageRamBlocks { get; init; }

  /// <summary>
  /// Uncompressed AREA3 packed monochrome raster. Bit 7 is the leftmost pixel; one is white and
  /// zero is black.
  /// </summary>
  public byte[] RasterData { get; init; }

  /// <summary>Converts the page bitmap to an indexed black-and-white image.</summary>
  public static RawImage ToRawImage(MdpFile file) {
    Validate(file, nameof(file));
    return MdaFile.ToRawImage(_AsMda(file));
  }

  /// <summary>
  /// Creates an MDP page from pixels using deterministic defaults for metadata that pixels cannot
  /// describe.
  /// </summary>
  /// <remarks>
  /// The MDP specification stores resolution, page format, page-RAM requirement, and user serial
  /// independently from the AREA3 raster. Generic conversion therefore uses the lowest format codes
  /// (<see cref="DefaultResolution"/> and <see cref="DefaultPageFormat"/>), the MDA default serial,
  /// and derives the RAM requirement from the uncompressed raster size. Use the metadata-aware
  /// overload when those values are known.
  /// </remarks>
  public static MdpFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var area = MdaFile.FromRawImage(image);
    var pageRamBlocks = (area.RasterData.Length + PageRamBlockSize - 1) / PageRamBlockSize;
    if (pageRamBlocks > byte.MaxValue)
      throw new ArgumentOutOfRangeException(nameof(image), $"MDP raster requires {pageRamBlocks} page-RAM blocks, but the stamp can store at most {byte.MaxValue}.");

    return _FromArea(
      area,
      DefaultResolution,
      DefaultPageFormat,
      checked((byte)pageRamBlocks),
      MdaFile.DefaultSerialNumber
    );
  }

  /// <summary>
  /// Creates an MDP file from pixels plus the page metadata that cannot be inferred from the pixels
  /// themselves.
  /// </summary>
  public static MdpFile FromRawImage(
    RawImage image,
    MdpResolution resolution,
    MdpPageFormat pageFormat,
    byte pageRamBlocks,
    string serialNumber
  ) {
    ArgumentNullException.ThrowIfNull(image);
    if (!Enum.IsDefined(resolution))
      throw new ArgumentOutOfRangeException(nameof(resolution), $"Unsupported MDP resolution code {(byte)resolution}.");
    if (!Enum.IsDefined(pageFormat))
      throw new ArgumentOutOfRangeException(nameof(pageFormat), $"Unsupported MDP page-format code {(byte)pageFormat}.");
    MdaFile.ValidateSerialNumber(serialNumber, nameof(serialNumber));

    var area = MdaFile.FromRawImage(image);
    return _FromArea(area, resolution, pageFormat, pageRamBlocks, serialNumber);
  }

  internal static void Validate(MdpFile file, string parameterName) {
    if (!Enum.IsDefined(file.Resolution))
      throw new ArgumentOutOfRangeException(parameterName, $"Unsupported MDP resolution code {(byte)file.Resolution}.");
    if (!Enum.IsDefined(file.PageFormat))
      throw new ArgumentOutOfRangeException(parameterName, $"Unsupported MDP page-format code {(byte)file.PageFormat}.");

    MdaFile.Validate(_AsMda(file), parameterName);
  }

  internal static MdaFile AsMda(MdpFile file) => _AsMda(file);

  private static MdpFile _FromArea(
    MdaFile area,
    MdpResolution resolution,
    MdpPageFormat pageFormat,
    byte pageRamBlocks,
    string serialNumber
  ) => new() {
    Width = area.Width,
    Height = area.Height,
    SerialNumber = serialNumber,
    Resolution = resolution,
    PageFormat = pageFormat,
    PageRamBlocks = pageRamBlocks,
    RasterData = area.RasterData,
  };

  private static MdaFile _AsMda(MdpFile file) => new() {
    Width = file.Width,
    Height = file.Height,
    Version = MdaVersion.Area3,
    SerialNumber = file.SerialNumber,
    RasterData = file.RasterData,
  };
}
