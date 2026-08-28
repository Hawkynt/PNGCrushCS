namespace SharpAstro.Jxr;

/// <summary>
/// FIELD_TAG values for the JXR tag-based file format (T.832 Annex A Table A.4).
/// </summary>
/// <remarks>
/// The JXR container reuses the IFD entry structure of TIFF 6.0 / EXIF 2.2 / ISO 12639,
/// so the lower-range tags (0x010D–0x013C, 0x8298) carry the same semantics as their TIFF
/// counterparts. The 0xBC00–0xBCFF block is JXR-specific and the 0xEA1C padding tag is
/// reserved by T.832. EXIF/ICC/GPS sub-IFD pointers (0x8769 / 0x8773 / 0x8825) are
/// explicitly permitted by T.832 A.7 Note 3 even though they are not in Table A.4.
/// </remarks>
public static class JxrTag
{
    public const ushort DocumentName          = 0x010D;
    public const ushort ImageDescription      = 0x010E;
    public const ushort EquipmentMake         = 0x010F;
    public const ushort EquipmentModel        = 0x0110;
    public const ushort PageName              = 0x011D;
    public const ushort PageNumber            = 0x0129;
    public const ushort SoftwareNameVersion   = 0x0131;
    public const ushort DateTime              = 0x0132;
    public const ushort ArtistName            = 0x013B;
    public const ushort HostComputer          = 0x013C;
    public const ushort XmpMetadata           = 0x02BC; // ISO 16684-1 XMP packet
    public const ushort CopyrightNotice       = 0x8298;
    public const ushort ExifIfd               = 0x8769; // EXIF 2.2 §4.6.3 sub-IFD pointer
    public const ushort IccProfile            = 0x8773; // ISO 15076-1 ICC profile blob
    public const ushort GpsInfoIfd            = 0x8825;
    public const ushort ColorSpace            = 0xA001;
    public const ushort PixelFormat           = 0xBC01; // REQUIRED — 16-byte GUID
    public const ushort SpatialXfrmPrimary    = 0xBC02; // Rotation / flip hint, range 0..7
    public const ushort ImageType             = 0xBC04;
    public const ushort PtmColorInfo          = 0xBC05;
    public const ushort ProfileLevelContainer = 0xBC06;
    public const ushort ImageWidth            = 0xBC80; // REQUIRED
    public const ushort ImageHeight           = 0xBC81; // REQUIRED
    public const ushort WidthResolution       = 0xBC82; // FLOAT, DPI
    public const ushort HeightResolution      = 0xBC83; // FLOAT, DPI
    public const ushort ImageOffset           = 0xBCC0; // REQUIRED — file offset of primary codestream
    public const ushort ImageByteCount        = 0xBCC1; // REQUIRED
    public const ushort AlphaOffset           = 0xBCC2; // Separate alpha codestream offset
    public const ushort AlphaByteCount        = 0xBCC3;
    public const ushort ImageBandPresence     = 0xBCC4;
    public const ushort AlphaBandPresence     = 0xBCC5;
    public const ushort PaddingData           = 0xEA1C;
}
