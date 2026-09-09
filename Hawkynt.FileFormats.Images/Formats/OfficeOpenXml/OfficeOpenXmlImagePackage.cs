using System;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using FileFormat.Core;
using FileFormat.EmbeddedPicture;
using FileFormat.Png;

namespace FileFormat.OfficeOpenXml;

/// <summary>Builds the minimal native Office Open XML package needed to display one PNG picture.</summary>
/// <remarks>
/// This is deliberately an OPC/OOXML implementation, not an Open XML SDK dependency. Word,
/// SpreadsheetML and PresentationML all use the same ZIP package and relationship machinery, so the
/// package plumbing lives here while each public file type only selects its main-part content type.
/// The emitted XML follows ISO/IEC 29500 / ECMA-376 shapes and Microsoft's published minimum-package
/// examples. Macro-enabled variants merely use their macro-capable main-part content type; no VBA
/// project is fabricated when the caller supplied only pixels.
/// </remarks>
internal static class OfficeOpenXmlImagePackage {

  internal const string WordDocumentContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml";
  internal const string WordTemplateContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.template.main+xml";
  internal const string WordMacroDocumentContentType = "application/vnd.ms-word.document.macroEnabled.main+xml";
  internal const string WordMacroTemplateContentType = "application/vnd.ms-word.template.macroEnabledTemplate.main+xml";

  internal const string ExcelWorkbookContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml";
  internal const string ExcelTemplateContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.template.main+xml";
  internal const string ExcelMacroWorkbookContentType = "application/vnd.ms-excel.sheet.macroEnabled.main+xml";
  internal const string ExcelMacroTemplateContentType = "application/vnd.ms-excel.template.macroEnabled.main+xml";

  internal const string PowerPointPresentationContentType = "application/vnd.openxmlformats-officedocument.presentationml.presentation.main+xml";
  internal const string PowerPointSlideShowContentType = "application/vnd.openxmlformats-officedocument.presentationml.slideshow.main+xml";
  internal const string PowerPointTemplateContentType = "application/vnd.openxmlformats-officedocument.presentationml.template.main+xml";
  internal const string PowerPointMacroPresentationContentType = "application/vnd.ms-powerpoint.presentation.macroEnabled.main+xml";
  internal const string PowerPointMacroSlideShowContentType = "application/vnd.ms-powerpoint.slideshow.macroEnabled.main+xml";
  internal const string PowerPointMacroTemplateContentType = "application/vnd.ms-powerpoint.template.macroEnabled.main+xml";

  private const string _CONTENT_TYPES_NS = "http://schemas.openxmlformats.org/package/2006/content-types";
  private const string _PACKAGE_RELATIONSHIPS_NS = "http://schemas.openxmlformats.org/package/2006/relationships";
  private const string _OFFICE_RELATIONSHIPS_NS = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
  private const long _EMU_PER_PIXEL_96_DPI = 9_525;

  /// <summary>Writes a minimal WordprocessingML document/template with one inline picture.</summary>
  internal static byte[] WriteWord(int width, int height, ReadOnlySpan<byte> rgb24, string mainContentType) {
    var png = _EncodePng(width, height, rgb24);
    var (cx, cy) = _Fit(width, height, 5_943_600, 8_229_600); // 6.5 x 9 in printable area.

    using var memory = new MemoryStream();
    using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, true, Encoding.UTF8)) {
      _WriteText(archive, "[Content_Types].xml", $$"""
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Types xmlns="{{_CONTENT_TYPES_NS}}">
          <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
          <Default Extension="xml" ContentType="application/xml"/>
          <Default Extension="png" ContentType="image/png"/>
          <Override PartName="/word/document.xml" ContentType="{{mainContentType}}"/>
        </Types>
        """);

      _WriteText(archive, "_rels/.rels", _Relationships(
        ("rId1", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument", "word/document.xml")
      ));

      _WriteText(archive, "word/_rels/document.xml.rels", _Relationships(
        ("rId1", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/image", "media/image1.png")
      ));

      _WriteText(archive, "word/document.xml", $$"""
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"
                    xmlns:r="{{_OFFICE_RELATIONSHIPS_NS}}"
                    xmlns:wp="http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing"
                    xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main"
                    xmlns:pic="http://schemas.openxmlformats.org/drawingml/2006/picture">
          <w:body>
            <w:p>
              <w:r>
                <w:drawing>
                  <wp:inline distT="0" distB="0" distL="0" distR="0">
                    <wp:extent cx="{{cx}}" cy="{{cy}}"/>
                    <wp:effectExtent l="0" t="0" r="0" b="0"/>
                    <wp:docPr id="1" name="Picture 1"/>
                    <wp:cNvGraphicFramePr>
                      <a:graphicFrameLocks noChangeAspect="1"/>
                    </wp:cNvGraphicFramePr>
                    <a:graphic>
                      <a:graphicData uri="http://schemas.openxmlformats.org/drawingml/2006/picture">
                        <pic:pic>
                          <pic:nvPicPr>
                            <pic:cNvPr id="0" name="image1.png"/>
                            <pic:cNvPicPr/>
                          </pic:nvPicPr>
                          <pic:blipFill>
                            <a:blip r:embed="rId1"/>
                            <a:stretch><a:fillRect/></a:stretch>
                          </pic:blipFill>
                          <pic:spPr>
                            <a:xfrm>
                              <a:off x="0" y="0"/>
                              <a:ext cx="{{cx}}" cy="{{cy}}"/>
                            </a:xfrm>
                            <a:prstGeom prst="rect"><a:avLst/></a:prstGeom>
                          </pic:spPr>
                        </pic:pic>
                      </a:graphicData>
                    </a:graphic>
                  </wp:inline>
                </w:drawing>
              </w:r>
            </w:p>
            <w:sectPr>
              <w:pgSz w="12240" h="15840"/>
              <w:pgMar top="1440" right="1440" bottom="1440" left="1440" header="720" footer="720" gutter="0"/>
            </w:sectPr>
          </w:body>
        </w:document>
        """);

      _WriteBytes(archive, "word/media/image1.png", png);
    }

    return memory.ToArray();
  }

  /// <summary>Writes a minimal SpreadsheetML workbook/template with one worksheet drawing.</summary>
  internal static byte[] WriteExcel(int width, int height, ReadOnlySpan<byte> rgb24, string mainContentType) {
    var png = _EncodePng(width, height, rgb24);
    var cx = checked((long)width * _EMU_PER_PIXEL_96_DPI);
    var cy = checked((long)height * _EMU_PER_PIXEL_96_DPI);

    using var memory = new MemoryStream();
    using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, true, Encoding.UTF8)) {
      _WriteText(archive, "[Content_Types].xml", $$"""
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Types xmlns="{{_CONTENT_TYPES_NS}}">
          <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
          <Default Extension="xml" ContentType="application/xml"/>
          <Default Extension="png" ContentType="image/png"/>
          <Override PartName="/xl/workbook.xml" ContentType="{{mainContentType}}"/>
          <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
          <Override PartName="/xl/drawings/drawing1.xml" ContentType="application/vnd.openxmlformats-officedocument.drawing+xml"/>
        </Types>
        """);

      _WriteText(archive, "_rels/.rels", _Relationships(
        ("rId1", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument", "xl/workbook.xml")
      ));

      _WriteText(archive, "xl/workbook.xml", $$"""
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                  xmlns:r="{{_OFFICE_RELATIONSHIPS_NS}}">
          <sheets><sheet name="Sheet1" sheetId="1" r:id="rId1"/></sheets>
        </workbook>
        """);

      _WriteText(archive, "xl/_rels/workbook.xml.rels", _Relationships(
        ("rId1", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet", "worksheets/sheet1.xml")
      ));

      _WriteText(archive, "xl/worksheets/sheet1.xml", $$"""
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                   xmlns:r="{{_OFFICE_RELATIONSHIPS_NS}}">
          <sheetData/>
          <drawing r:id="rId1"/>
        </worksheet>
        """);

      _WriteText(archive, "xl/worksheets/_rels/sheet1.xml.rels", _Relationships(
        ("rId1", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/drawing", "../drawings/drawing1.xml")
      ));

      _WriteText(archive, "xl/drawings/drawing1.xml", $$"""
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <xdr:wsDr xmlns:xdr="http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing"
                  xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main"
                  xmlns:r="{{_OFFICE_RELATIONSHIPS_NS}}">
          <xdr:oneCellAnchor>
            <xdr:from>
              <xdr:col>0</xdr:col><xdr:colOff>0</xdr:colOff>
              <xdr:row>0</xdr:row><xdr:rowOff>0</xdr:rowOff>
            </xdr:from>
            <xdr:ext cx="{{cx}}" cy="{{cy}}"/>
            <xdr:pic>
              <xdr:nvPicPr>
                <xdr:cNvPr id="2" name="Picture 1" descr="image1.png"/>
                <xdr:cNvPicPr><a:picLocks noChangeAspect="1"/></xdr:cNvPicPr>
              </xdr:nvPicPr>
              <xdr:blipFill>
                <a:blip r:embed="rId1"/>
                <a:stretch><a:fillRect/></a:stretch>
              </xdr:blipFill>
              <xdr:spPr>
                <a:xfrm><a:off x="0" y="0"/><a:ext cx="{{cx}}" cy="{{cy}}"/></a:xfrm>
                <a:prstGeom prst="rect"><a:avLst/></a:prstGeom>
              </xdr:spPr>
            </xdr:pic>
            <xdr:clientData/>
          </xdr:oneCellAnchor>
        </xdr:wsDr>
        """);

      _WriteText(archive, "xl/drawings/_rels/drawing1.xml.rels", _Relationships(
        ("rId1", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/image", "../media/image1.png")
      ));

      _WriteBytes(archive, "xl/media/image1.png", png);
    }

    return memory.ToArray();
  }

  /// <summary>Writes a complete minimum PresentationML package with one picture-only slide.</summary>
  internal static byte[] WritePowerPoint(int width, int height, ReadOnlySpan<byte> rgb24, string mainContentType) {
    const long slideWidth = 9_144_000;
    const long slideHeight = 6_858_000;
    var png = _EncodePng(width, height, rgb24);
    var (cx, cy) = _Fit(width, height, slideWidth, slideHeight);
    var x = (slideWidth - cx) / 2;
    var y = (slideHeight - cy) / 2;

    using var memory = new MemoryStream();
    using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, true, Encoding.UTF8)) {
      _WriteText(archive, "[Content_Types].xml", $$"""
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Types xmlns="{{_CONTENT_TYPES_NS}}">
          <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
          <Default Extension="xml" ContentType="application/xml"/>
          <Default Extension="png" ContentType="image/png"/>
          <Override PartName="/ppt/presentation.xml" ContentType="{{mainContentType}}"/>
          <Override PartName="/ppt/presProps.xml" ContentType="application/vnd.openxmlformats-officedocument.presentationml.presProps+xml"/>
          <Override PartName="/ppt/slides/slide1.xml" ContentType="application/vnd.openxmlformats-officedocument.presentationml.slide+xml"/>
          <Override PartName="/ppt/slideLayouts/slideLayout1.xml" ContentType="application/vnd.openxmlformats-officedocument.presentationml.slideLayout+xml"/>
          <Override PartName="/ppt/slideMasters/slideMaster1.xml" ContentType="application/vnd.openxmlformats-officedocument.presentationml.slideMaster+xml"/>
          <Override PartName="/ppt/theme/theme1.xml" ContentType="application/vnd.openxmlformats-officedocument.theme+xml"/>
        </Types>
        """);

      _WriteText(archive, "_rels/.rels", _Relationships(
        ("rId1", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument", "ppt/presentation.xml")
      ));

      _WriteText(archive, "ppt/presentation.xml", $$"""
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <p:presentation xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main"
                        xmlns:r="{{_OFFICE_RELATIONSHIPS_NS}}">
          <p:sldMasterIdLst><p:sldMasterId id="2147483648" r:id="rId1"/></p:sldMasterIdLst>
          <p:sldIdLst><p:sldId id="256" r:id="rId2"/></p:sldIdLst>
          <p:sldSz cx="{{slideWidth}}" cy="{{slideHeight}}" type="screen4x3"/>
          <p:notesSz cx="6858000" cy="9144000"/>
          <p:defaultTextStyle/>
        </p:presentation>
        """);

      _WriteText(archive, "ppt/_rels/presentation.xml.rels", _Relationships(
        ("rId1", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/slideMaster", "slideMasters/slideMaster1.xml"),
        ("rId2", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/slide", "slides/slide1.xml"),
        ("rId3", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/presProps", "presProps.xml"),
        ("rId5", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/theme", "theme/theme1.xml")
      ));

      _WriteText(archive, "ppt/presProps.xml", """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <p:presentationPr xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main"
                          xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main"
                          xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"/>
        """);

      _WriteText(archive, "ppt/slides/slide1.xml", $$"""
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <p:sld xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main"
               xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main"
               xmlns:r="{{_OFFICE_RELATIONSHIPS_NS}}">
          <p:cSld>
            <p:spTree>
              {{_PresentationRootGroup()}}
              <p:pic>
                <p:nvPicPr>
                  <p:cNvPr id="2" name="Picture 1" descr="image1.png"/>
                  <p:cNvPicPr><a:picLocks noChangeAspect="1"/></p:cNvPicPr>
                  <p:nvPr/>
                </p:nvPicPr>
                <p:blipFill>
                  <a:blip r:embed="rId2"/>
                  <a:stretch><a:fillRect/></a:stretch>
                </p:blipFill>
                <p:spPr>
                  <a:xfrm><a:off x="{{x}}" y="{{y}}"/><a:ext cx="{{cx}}" cy="{{cy}}"/></a:xfrm>
                  <a:prstGeom prst="rect"><a:avLst/></a:prstGeom>
                </p:spPr>
              </p:pic>
            </p:spTree>
          </p:cSld>
          <p:clrMapOvr><a:masterClrMapping/></p:clrMapOvr>
        </p:sld>
        """);

      _WriteText(archive, "ppt/slides/_rels/slide1.xml.rels", _Relationships(
        ("rId1", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/slideLayout", "../slideLayouts/slideLayout1.xml"),
        ("rId2", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/image", "../media/image1.png")
      ));

      _WriteText(archive, "ppt/slideLayouts/slideLayout1.xml", $$"""
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <p:sldLayout xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main"
                     xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main"
                     xmlns:r="{{_OFFICE_RELATIONSHIPS_NS}}" type="blank" preserve="1">
          <p:cSld name="Blank"><p:spTree>{{_PresentationRootGroup()}}</p:spTree></p:cSld>
          <p:clrMapOvr><a:masterClrMapping/></p:clrMapOvr>
        </p:sldLayout>
        """);

      _WriteText(archive, "ppt/slideLayouts/_rels/slideLayout1.xml.rels", _Relationships(
        ("rId1", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/slideMaster", "../slideMasters/slideMaster1.xml")
      ));

      _WriteText(archive, "ppt/slideMasters/slideMaster1.xml", $$"""
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <p:sldMaster xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main"
                     xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main"
                     xmlns:r="{{_OFFICE_RELATIONSHIPS_NS}}">
          <p:cSld><p:spTree>{{_PresentationRootGroup()}}</p:spTree></p:cSld>
          <p:clrMap bg1="lt1" tx1="dk1" bg2="lt2" tx2="dk2" accent1="accent1" accent2="accent2"
                    accent3="accent3" accent4="accent4" accent5="accent5" accent6="accent6"
                    hlink="hlink" folHlink="folHlink"/>
          <p:sldLayoutIdLst><p:sldLayoutId id="2147483649" r:id="rId1"/></p:sldLayoutIdLst>
          <p:txStyles><p:titleStyle/><p:bodyStyle/><p:otherStyle/></p:txStyles>
        </p:sldMaster>
        """);

      _WriteText(archive, "ppt/slideMasters/_rels/slideMaster1.xml.rels", _Relationships(
        ("rId1", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/slideLayout", "../slideLayouts/slideLayout1.xml"),
        ("rId5", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/theme", "../theme/theme1.xml")
      ));

      _WriteText(archive, "ppt/theme/theme1.xml", _PresentationTheme());
      _WriteBytes(archive, "ppt/media/image1.png", png);
    }

    return memory.ToArray();
  }

  /// <summary>Reads the first decodable media image and the package main-part content type.</summary>
  internal static (RawImage Image, string MainContentType) ReadFirstImage(
    ReadOnlySpan<byte> data,
    string mediaPrefix,
    string mainPartName) {

    try {
      using var memory = new MemoryStream(data.ToArray(), false);
      using var archive = new ZipArchive(memory, ZipArchiveMode.Read, false, Encoding.UTF8);
      var contentType = _ReadMainContentType(archive, mainPartName);

      foreach (var entry in archive.Entries) {
        if (!entry.FullName.StartsWith(mediaPrefix, StringComparison.OrdinalIgnoreCase)
            || entry.FullName.EndsWith("/", StringComparison.Ordinal)
            || entry.Length == 0)
          continue;

        using var stream = entry.Open();
        using var picture = new MemoryStream();
        stream.CopyTo(picture);
        try {
          var decoded = EmbeddedPictureReader.Decode(picture.ToArray());
          return (PixelConverter.Convert(decoded, PixelFormat.Rgb24), contentType);
        } catch (InvalidDataException) {
          // Media folders may contain vectors, audio or other objects. Keep looking for a raster.
        }
      }

      throw new InvalidDataException($"The Office package contains no decodable picture below {mediaPrefix}.");
    } catch (InvalidDataException) {
      throw;
    } catch (Exception exception) when (exception is IOException or NotSupportedException or ArgumentException) {
      throw new InvalidDataException("The Office Open XML package is malformed.", exception);
    }
  }

  private static string _ReadMainContentType(ZipArchive archive, string mainPartName) {
    var entry = archive.GetEntry("[Content_Types].xml")
      ?? throw new InvalidDataException("The Office Open XML package has no [Content_Types].xml part.");
    using var stream = entry.Open();
    var document = XDocument.Load(stream, LoadOptions.None);
    var ns = XNamespace.Get(_CONTENT_TYPES_NS);
    var contentType = document.Root?
      .Elements(ns + "Override")
      .FirstOrDefault(element => string.Equals((string?)element.Attribute("PartName"), mainPartName, StringComparison.Ordinal))?
      .Attribute("ContentType")?.Value;

    return string.IsNullOrWhiteSpace(contentType)
      ? throw new InvalidDataException($"The Office package does not declare the main part {mainPartName}.")
      : contentType;
  }

  private static byte[] _EncodePng(int width, int height, ReadOnlySpan<byte> rgb24) {
    if (width <= 0 || height <= 0)
      throw new InvalidDataException($"An Office picture needs positive dimensions, not {width}x{height}.");
    var expected = checked(width * height * 3);
    if (rgb24.Length < expected)
      throw new InvalidDataException($"Office picture data is truncated: expected {expected} RGB bytes, got {rgb24.Length}.");

    return PngWriter.ToBytes(PngFile.FromRawImage(new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Rgb24,
      PixelData = rgb24[..expected].ToArray(),
    }));
  }

  private static (long Width, long Height) _Fit(int width, int height, long maxWidth, long maxHeight) {
    var naturalWidth = checked((long)width * _EMU_PER_PIXEL_96_DPI);
    var naturalHeight = checked((long)height * _EMU_PER_PIXEL_96_DPI);
    if (naturalWidth <= maxWidth && naturalHeight <= maxHeight)
      return (naturalWidth, naturalHeight);

    if (checked(naturalWidth * maxHeight) > checked(naturalHeight * maxWidth))
      return (maxWidth, Math.Max(1, checked(naturalHeight * maxWidth / naturalWidth)));

    return (Math.Max(1, checked(naturalWidth * maxHeight / naturalHeight)), maxHeight);
  }

  private static string _Relationships(params (string Id, string Type, string Target)[] relationships) {
    var builder = new StringBuilder();
    builder.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n")
      .Append("<Relationships xmlns=\"").Append(_PACKAGE_RELATIONSHIPS_NS).Append("\">\n");
    foreach (var (id, type, target) in relationships)
      builder.Append("  <Relationship Id=\"").Append(id).Append("\" Type=\"").Append(type)
        .Append("\" Target=\"").Append(target).Append("\"/>\n");
    return builder.Append("</Relationships>\n").ToString();
  }

  private static string _PresentationRootGroup() => """
    <p:nvGrpSpPr>
      <p:cNvPr id="1" name=""/>
      <p:cNvGrpSpPr/>
      <p:nvPr/>
    </p:nvGrpSpPr>
    <p:grpSpPr>
      <a:xfrm>
        <a:off x="0" y="0"/><a:ext cx="0" cy="0"/>
        <a:chOff x="0" y="0"/><a:chExt cx="0" cy="0"/>
      </a:xfrm>
    </p:grpSpPr>
    """;

  private static string _PresentationTheme() => """
    <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
    <a:theme xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" name="Office">
      <a:themeElements>
        <a:clrScheme name="Office">
          <a:dk1><a:sysClr val="windowText" lastClr="000000"/></a:dk1>
          <a:lt1><a:sysClr val="window" lastClr="FFFFFF"/></a:lt1>
          <a:dk2><a:srgbClr val="1F497D"/></a:dk2>
          <a:lt2><a:srgbClr val="EEECE1"/></a:lt2>
          <a:accent1><a:srgbClr val="4F81BD"/></a:accent1>
          <a:accent2><a:srgbClr val="C0504D"/></a:accent2>
          <a:accent3><a:srgbClr val="9BBB59"/></a:accent3>
          <a:accent4><a:srgbClr val="8064A2"/></a:accent4>
          <a:accent5><a:srgbClr val="4BACC6"/></a:accent5>
          <a:accent6><a:srgbClr val="F79646"/></a:accent6>
          <a:hlink><a:srgbClr val="0000FF"/></a:hlink>
          <a:folHlink><a:srgbClr val="800080"/></a:folHlink>
        </a:clrScheme>
        <a:fontScheme name="Office">
          <a:majorFont><a:latin typeface="Calibri"/><a:ea typeface=""/><a:cs typeface=""/></a:majorFont>
          <a:minorFont><a:latin typeface="Calibri"/><a:ea typeface=""/><a:cs typeface=""/></a:minorFont>
        </a:fontScheme>
        <a:fmtScheme name="Office">
          <a:fillStyleLst>
            <a:solidFill><a:schemeClr val="phClr"/></a:solidFill>
            <a:solidFill><a:schemeClr val="phClr"/></a:solidFill>
            <a:solidFill><a:schemeClr val="phClr"/></a:solidFill>
          </a:fillStyleLst>
          <a:lnStyleLst>
            <a:ln w="9525"><a:solidFill><a:schemeClr val="phClr"/></a:solidFill><a:prstDash val="solid"/></a:ln>
            <a:ln w="25400"><a:solidFill><a:schemeClr val="phClr"/></a:solidFill><a:prstDash val="solid"/></a:ln>
            <a:ln w="38100"><a:solidFill><a:schemeClr val="phClr"/></a:solidFill><a:prstDash val="solid"/></a:ln>
          </a:lnStyleLst>
          <a:effectStyleLst>
            <a:effectStyle><a:effectLst/></a:effectStyle>
            <a:effectStyle><a:effectLst/></a:effectStyle>
            <a:effectStyle><a:effectLst/></a:effectStyle>
          </a:effectStyleLst>
          <a:bgFillStyleLst>
            <a:solidFill><a:schemeClr val="phClr"/></a:solidFill>
            <a:solidFill><a:schemeClr val="phClr"/></a:solidFill>
            <a:solidFill><a:schemeClr val="phClr"/></a:solidFill>
          </a:bgFillStyleLst>
        </a:fmtScheme>
      </a:themeElements>
    </a:theme>
    """;

  private static void _WriteText(ZipArchive archive, string path, string text)
    => _WriteBytes(archive, path, Encoding.UTF8.GetBytes(text.TrimStart()));

  private static void _WriteBytes(ZipArchive archive, string path, ReadOnlySpan<byte> bytes) {
    var entry = archive.CreateEntry(path, CompressionLevel.SmallestSize);
    entry.LastWriteTime = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
    using var stream = entry.Open();
    stream.Write(bytes);
  }
}
