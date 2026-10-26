using CrystalDecisions.CrystalReports.Engine;
using CrystalDecisions.ReportAppServer.ReportDefModel;
using CrystalDecisions.Shared;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

using Area = CrystalDecisions.ReportAppServer.ReportDefModel.Area;
using ReportDocument = CrystalDecisions.CrystalReports.Engine.ReportDocument;
using Section = CrystalDecisions.ReportAppServer.ReportDefModel.Section;
using SectionFormat = CrystalDecisions.ReportAppServer.ReportDefModel.SectionFormat;
using ReportObject = CrystalDecisions.ReportAppServer.ReportDefModel.ReportObject;
using ObjectFormat = CrystalDecisions.ReportAppServer.ReportDefModel.ObjectFormat;
using FieldObject = CrystalDecisions.ReportAppServer.ReportDefModel.FieldObject;
using TextObject = CrystalDecisions.ReportAppServer.ReportDefModel.TextObject;
using BlobFieldObject = CrystalDecisions.ReportAppServer.ReportDefModel.BlobFieldObject;
using SubreportObject = CrystalDecisions.ReportAppServer.ReportDefModel.SubreportObject;
using PictureObject = CrystalDecisions.ReportAppServer.ReportDefModel.PictureObject;
using LineObject = CrystalDecisions.ReportAppServer.ReportDefModel.LineObject;
using BoxObject = CrystalDecisions.ReportAppServer.ReportDefModel.BoxObject;
using Border = CrystalDecisions.ReportAppServer.ReportDefModel.Border;
using FieldFormat = CrystalDecisions.ReportAppServer.ReportDefModel.FieldFormat;
using Paragraph = CrystalDecisions.ReportAppServer.ReportDefModel.Paragraph;
using ReportDefinition = CrystalDecisions.ReportAppServer.ReportDefModel.ReportDefinition;

class Program
{
    private readonly static string pattern = @"
            (?<=[a-z])[A-Z0-9] |      // Lower to Upper/Number (e.g., name2, nameN)
            (?<=[0-9])[a-zA-Z] |      // Number to Letter (e.g., 2n, 2N)
            (?<=[A-Z])[0-9] |         // Upper to Number (e.g., ID2)
            (?<=[A-Z])[A-Z](?=[a-z])  // Acronym to Word (e.g., HTMLParser -> HTML Parser)
        ";

    private static string[] _searchTerms = {};

    private enum Commands
    {
        [IODescription("--dump")]
        Dump
    }

    static int Main(string[] args)
    {
        if (args.Length <= 1)
        {
            Console.Error.WriteLine(BuildUsageOuput());
            return 1;
        }

        string rptPath = args[0];
        if (!File.Exists(rptPath))
        {
            Console.Error.WriteLine($"File not found: {rptPath}");
            return 1;
        }

        Commands command;
        switch (args[1])
        {
            case "--dump":
            default:
                command = Commands.Dump;
                break;
        }

        string outputPath = Path.ChangeExtension(rptPath, ".dump.md");

        if (args.Length > 2)
        {
            int outputIdx = args.ToList().IndexOf("-o") + 1;
            int searchIdx = args.ToList().IndexOf("-s") + 1;

            if (outputIdx != 0)
            {
                if (outputIdx >= args.Length || outputIdx == searchIdx - 1)
                {
                    Console.Error.WriteLine($"Invalid usage of: '-o'");
                    Console.Error.WriteLine(BuildUsageOuput());
                    return 1;
                }

                outputPath = args[outputIdx];

                if (Directory.Exists(outputPath))
                {
                    outputPath = Path.Combine(outputPath, Path.ChangeExtension(Path.GetFileName(rptPath), ".dump.md"));
                }
            }

            if (searchIdx != 0)
            {
                if (searchIdx >= args.Length || searchIdx == outputIdx - 1)
                {
                    Console.Error.WriteLine($"Invalid usage of: '-s'");
                    Console.Error.WriteLine(BuildUsageOuput());
                    return 1;
                }

                _searchTerms = args[searchIdx].Split(',');
            }
        }

        try
        {
            switch (command)
            {
                case Commands.Dump:
                default:
                    Dump(rptPath, outputPath);
                    break;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }

        return 0;
    }

    private static void Dump(string reportPath, string outputPath)
    {
        var reportDoc = new ReportDocument();
        reportDoc.Load(reportPath, OpenReportMethod.OpenReportByDefault);

        var clientDoc = reportDoc.ReportClientDocument;

        var includedFormulaNames = BuildIncludedFormulaNames(reportDoc.DataDefinition.FormulaFields.Cast<FormulaFieldDefinition>());

        var output = new StringBuilder();
        output.Append(DumpReportSections(clientDoc.ReportDefinition, includedFormulaNames, baseLevel: 1));

        var formulaFieldsOutput = DumpFormulaFields(reportDoc.DataDefinition.FormulaFields.Cast<FormulaFieldDefinition>(), headingLevel: 2);
        if (formulaFieldsOutput.Length > 0)
        {
            output.AppendLine("# Formula Fields");
            output.Append(formulaFieldsOutput);
        }

        if (reportDoc.Subreports.Count > 0)
        {
            var subreportOutput = new StringBuilder();
            foreach (ReportDocument sub in reportDoc.Subreports)
            {
                var subIncludedFormulaNames = BuildIncludedFormulaNames(sub.DataDefinition.FormulaFields.Cast<FormulaFieldDefinition>());

                // The Engine's ReportDocument.Subreports collection can't drive RAS section
                // introspection directly (throws "Not supported within subreports"), so the
                // subreport has to be opened through the main doc's SubreportController instead.
                ReportDefinition subReportDefinition = clientDoc.SubreportController.GetSubreport(sub.Name).Document.ReportDefinition;

                // Subreport nests under "## Subreport: Name", so its own Areas/Sections
                // start three levels deep ("### Area" / "#### Section").
                var subSectionsOutput = DumpReportSections(subReportDefinition, subIncludedFormulaNames, baseLevel: 3);
                var subFormulaOutput = DumpFormulaFields(sub.DataDefinition.FormulaFields.Cast<FormulaFieldDefinition>(), headingLevel: 3);

                if (subSectionsOutput.Length == 0 && subFormulaOutput.Length == 0)
                {
                    continue;
                }

                subreportOutput.AppendLine($"## Subreport: {sub.Name}");
                subreportOutput.Append(subSectionsOutput);

                if (subFormulaOutput.Length > 0)
                {
                    subreportOutput.AppendLine("### Formula Fields");
                    subreportOutput.Append(subFormulaOutput);
                }
            }

            if (subreportOutput.Length > 0)
            {
                output.AppendLine("# Subreports");
                output.Append(subreportOutput);
            }
        }

        File.WriteAllText(outputPath, output.ToString());
        Console.WriteLine($"Written to: {outputPath}");

        reportDoc.Close();
    }

    /// <summary>
    /// Dumps every Area/Section/Object under a report's (or subreport's) ReportDefinition.
    /// baseLevel controls markdown heading depth so subreport output can nest under
    /// "## Subreport: Name" instead of colliding with the main report's own headings.
    /// </summary>
    private static StringBuilder DumpReportSections(ReportDefinition reportDefinition, HashSet<string> includedFormulaNames, int baseLevel)
    {
        string areaHeading = H(baseLevel);
        string sectionHeading = H(baseLevel + 1);
        string subHeading = H(baseLevel + 2);
        int objectHeadingLevel = baseLevel + 3;

        var output = new StringBuilder();
        foreach (Area area in reportDefinition.Areas)
        {
            var sectionOutput = new StringBuilder();
            int position = 0;
            foreach (Section section in area.Sections.Cast<Section>())
            {
                position++;
                bool sectionHasContent = false;
                var thisSectionOutput = new StringBuilder();

                SectionFormat format = section.Format;
                var formula = format.ConditionFormulas[0];
                if (!string.IsNullOrWhiteSpace(formula.Text) && IsSearchTermInFormula(formula.Text))
                {
                    sectionHasContent = true;
                    thisSectionOutput.AppendLine($"{subHeading} Suppression Formula");
                    thisSectionOutput.AppendLine("```");
                    thisSectionOutput.AppendLine(formula.Text);
                    thisSectionOutput.AppendLine("```");
                    thisSectionOutput.AppendLine();
                }

                var sectionConditionalFormulas = GetSectionConditionalFormulas(format)
                    .Where(cf => IsSearchTermInFormula(cf.Value))
                    .ToList();
                if (sectionConditionalFormulas.Count > 0)
                {
                    sectionHasContent = true;
                    foreach (var cf in sectionConditionalFormulas)
                    {
                        thisSectionOutput.AppendLine($"{subHeading} Conditional {cf.Key} Formula");
                        thisSectionOutput.AppendLine("```");
                        thisSectionOutput.AppendLine(cf.Value);
                        thisSectionOutput.AppendLine("```");
                        thisSectionOutput.AppendLine();
                    }
                }

                if (_searchTerms.Length == 0)
                {
                    sectionHasContent = true;
                    thisSectionOutput.AppendLine($"- Section Height: {section.Height}");
                    thisSectionOutput.AppendLine($"- Section Width: {section.Width}");
                    thisSectionOutput.AppendLine($"- Statically Suppressed: {format.EnableSuppress}");
                    thisSectionOutput.AppendLine($"- Underlay Following Sections: {format.EnableUnderlaySection}");

                    var options = new List<string>();
                    if (format.EnableKeepTogether) options.Add("KeepTogether");
                    if (format.EnableNewPageBefore) options.Add("NewPageBefore");
                    if (format.EnableNewPageAfter) options.Add("NewPageAfter");
                    if (format.EnablePrintAtBottomOfPage) options.Add("PrintAtBottomOfPage");
                    if (format.EnableResetPageNumberAfter) options.Add("ResetPageNumberAfter");
                    if (format.EnableSuppressIfBlank) options.Add("SuppressIfBlank");
                    if (!string.IsNullOrEmpty(format.CssClass)) options.Add($"CssClass={format.CssClass}");
                    thisSectionOutput.AppendLine($"- Options: {(options.Count > 0 ? string.Join(", ", options) : "None")}");
                    thisSectionOutput.AppendLine($"- Background Color: {FormatColor(format.BackgroundColor)}");
                    thisSectionOutput.AppendLine();
                }

                var objectsOutput = DumpObjects(section, includedFormulaNames, objectHeadingLevel);
                if (objectsOutput.Length > 0)
                {
                    sectionHasContent = true;
                    thisSectionOutput.AppendLine($"{subHeading} Objects");
                    thisSectionOutput.Append(objectsOutput);
                    thisSectionOutput.AppendLine();
                }

                if (sectionHasContent)
                {
                    string prefix = Regex.Replace(
                        Regex.Replace(section.Name, @"\d+$", ""),
                        pattern, " $0", RegexOptions.IgnorePatternWhitespace);
                    string sectionTitle = $"{prefix} '{IndexToLetters(position)}'";
                    sectionOutput.AppendLine($"{sectionHeading} {sectionTitle}");
                    sectionOutput.Append(thisSectionOutput);
                }
            }

            if (sectionOutput.Length > 0)
            {
                string areaName = string.Empty;
                switch (area.Kind)
                {
                    case CrAreaSectionKindEnum.crAreaSectionKindPageHeader:
                        areaName = "Page Header";
                        break;
                    case CrAreaSectionKindEnum.crAreaSectionKindGroupHeader:
                        areaName = "Group Header";
                        break;
                    case CrAreaSectionKindEnum.crAreaSectionKindDetail:
                        areaName = "Detail";
                        break;
                    case CrAreaSectionKindEnum.crAreaSectionKindGroupFooter:
                        areaName = "Group Footer";
                        break;
                    case CrAreaSectionKindEnum.crAreaSectionKindPageFooter:
                        areaName = "Page Footer";
                        break;
                    case CrAreaSectionKindEnum.crAreaSectionKindReportFooter:
                        areaName = "Report Footer";
                        break;
                }

                output.AppendLine($"{areaHeading} {areaName}");
                output.Append(sectionOutput);
            }
        }

        return output;
    }

    private static HashSet<string> BuildIncludedFormulaNames(IEnumerable<FormulaFieldDefinition> formulas)
    {
        var includedFormulaNames = new HashSet<string>();
        foreach (FormulaFieldDefinition ff in formulas)
        {
            if (!string.IsNullOrWhiteSpace(ff.Text) && IsSearchTermInFormula(ff.Text))
            {
                includedFormulaNames.Add(ff.FormulaName);
            }
        }

        return includedFormulaNames;
    }

    private static StringBuilder DumpFormulaFields(IEnumerable<FormulaFieldDefinition> formulas, int headingLevel)
    {
        string heading = H(headingLevel);

        var formulaFieldsOutput = new StringBuilder();
        foreach (FormulaFieldDefinition ff in formulas)
        {
            if (!string.IsNullOrWhiteSpace(ff.Text) && IsSearchTermInFormula(ff.Text))
            {
                formulaFieldsOutput.AppendLine($"{heading} {ff.FormulaName}");
                formulaFieldsOutput.AppendLine("```");
                formulaFieldsOutput.AppendLine(ff.Text);
                formulaFieldsOutput.AppendLine("```");
                formulaFieldsOutput.AppendLine();
            }
        }

        return formulaFieldsOutput;
    }

    private static StringBuilder DumpObjects(Section section, HashSet<string> includedFormulaNames, int headingLevel)
    {
        var objectsOutput = new StringBuilder();
        string heading = H(headingLevel);

        foreach (ReportObject obj in section.ReportObjects.Cast<ReportObject>())
        {
            string text = null;
            string dataSource = null;
            string subreportName = null;
            string pictureInfo = null;

            switch (obj.Kind)
            {
                case CrReportObjectKindEnum.crReportObjectKindText:
                    text = ((TextObject)obj).Text;
                    break;
                case CrReportObjectKindEnum.crReportObjectKindField:
                    dataSource = ((FieldObject)obj).DataSource;
                    break;
                case CrReportObjectKindEnum.crReportObjectKindBlobField:
                    dataSource = ((BlobFieldObject)obj).DataSourceName;
                    break;
                case CrReportObjectKindEnum.crReportObjectKindSubreport:
                    subreportName = ((SubreportObject)obj).SubreportName;
                    break;
                case CrReportObjectKindEnum.crReportObjectKindPicture:
                    var picture = (PictureObject)obj;
                    if (!string.IsNullOrWhiteSpace(picture.GraphicLocation))
                    {
                        pictureInfo = $"`{picture.GraphicLocation}` (linked)";
                    }
                    else
                    {
                        int width = picture.OriginalWidth > 0 ? picture.OriginalWidth : picture.Width;
                        int height = picture.OriginalHeight > 0 ? picture.OriginalHeight : picture.Height;
                        pictureInfo = $"{width}x{height}";
                    }
                    break;
            }

            ObjectFormat objFormat = obj.Format;
            var suppressionFormula = objFormat.ConditionFormulas[0];
            string suppressionText = !string.IsNullOrWhiteSpace(suppressionFormula.Text) ? suppressionFormula.Text : null;
            var conditionalFormulas = GetObjectConditionalFormulas(obj);

            bool matchesSearch = _searchTerms.Length == 0
                || new[] { text, dataSource, subreportName, suppressionText }
                    .Concat(conditionalFormulas.Select(cf => cf.Value))
                    .Any(candidate => !string.IsNullOrWhiteSpace(candidate) && IsSearchTermInFormula(candidate));

            if (!matchesSearch)
            {
                continue;
            }

            objectsOutput.AppendLine($"{heading} {obj.Name} ({KindToDisplayName(obj.Kind)})");

            if (dataSource != null)
            {
                objectsOutput.AppendLine($"- DataSource: {FormatDataSourceReference(dataSource, includedFormulaNames)}");
            }

            if (text != null)
            {
                objectsOutput.AppendLine($"- Text: `{text}`");
            }

            if (subreportName != null)
            {
                objectsOutput.AppendLine($"- Subreport: {subreportName}");
            }

            if (pictureInfo != null)
            {
                objectsOutput.AppendLine($"- Image: {pictureInfo}");
            }

            objectsOutput.AppendLine($"- Position: Top={obj.Top}, Left={obj.Left}, Width={obj.Width}, Height={obj.Height}");
            AppendObjectLayout(objectsOutput, obj);

            if (obj.Format.EnableSuppress)
            {
                objectsOutput.AppendLine("- Suppressed (Hidden): True");
            }

            if (suppressionText != null)
            {
                objectsOutput.AppendLine("- Suppression Formula:");
                objectsOutput.AppendLine("```");
                objectsOutput.AppendLine($"  {suppressionText}");
                objectsOutput.AppendLine("```");
            }

            AppendConditionalFormulas(objectsOutput, conditionalFormulas);
        }

        return objectsOutput;
    }

    private static void AppendObjectLayout(StringBuilder output, ReportObject obj)
    {
        switch (obj.Kind)
        {
            case CrReportObjectKindEnum.crReportObjectKindField:
                var field = (FieldObject)obj;
                output.AppendLine($"- Font: {FormatFont(field.FontColor)}");
                AppendFieldFormat(output, field.FieldFormat, field.FieldValueType.ToString());
                break;
            case CrReportObjectKindEnum.crReportObjectKindText:
                var textObj = (TextObject)obj;
                string objectFont = FormatFont(textObj.FontColor);
                output.AppendLine($"- Font: {objectFont}");

                // Text objects can mix fonts per paragraph run, which the object-level font doesn't show.
                var runFonts = GetParagraphFonts(textObj).Where(f => f != objectFont).Distinct().ToList();
                if (runFonts.Count > 0)
                {
                    output.AppendLine($"- Run Fonts: {string.Join("; ", runFonts)}");
                }

                var paragraphAlignments = textObj.Paragraphs.Cast<Paragraph>().Select(p => EnumName(p.Alignment)).ToList();
                if (paragraphAlignments.Any(a => a != "Default"))
                {
                    output.AppendLine($"- Paragraph Alignment: {string.Join(", ", paragraphAlignments)}");
                }

                if (textObj.MaxNumberOfLines > 0)
                {
                    output.AppendLine($"- Max Lines: {textObj.MaxNumberOfLines}");
                }
                break;
            case CrReportObjectKindEnum.crReportObjectKindLine:
                var line = (LineObject)obj;
                output.AppendLine($"- Line: Style={EnumName(line.LineStyle)}, Thickness={line.LineThickness}, Color={FormatColor(line.LineColor)}, Right={line.Right}, Bottom={line.Bottom}, EndSection={line.EndSectionName}, ExtendToBottomOfSection={line.EnableExtendToBottomOfSection}");
                break;
            case CrReportObjectKindEnum.crReportObjectKindBox:
                var box = (BoxObject)obj;
                output.AppendLine($"- Box: Style={EnumName(box.LineStyle)}, Thickness={box.LineThickness}, Color={FormatColor(box.LineColor)}, Fill={FormatColor(box.FillColor)}, Right={box.Right}, Bottom={box.Bottom}, EndSection={box.EndSectionName}, ExtendToBottomOfSection={box.EnableExtendToBottomOfSection}, Corners={box.CornerEllipseWidth}x{box.CornerEllipseHeight}");
                break;
        }

        ObjectFormat format = obj.Format;
        var formatParts = new List<string>
        {
            $"HAlign={EnumName(format.HorizontalAlignment)}",
            $"VAlign={EnumName(format.VerticalAlignment)}",
        };
        if (format.EnableCanGrow) formatParts.Add("CanGrow");
        if (format.EnableKeepTogether) formatParts.Add("KeepTogether");
        if (format.EnableCloseAtPageBreak) formatParts.Add("CloseAtPageBreak");
        if (format.TextRotationAngle != CrTextRotationAngleEnum.crRotationAngleRotate0) formatParts.Add($"Rotation={EnumName(format.TextRotationAngle)}");
        if (!string.IsNullOrEmpty(format.CssClass)) formatParts.Add($"CssClass={format.CssClass}");
        if (!string.IsNullOrEmpty(format.ToolTipText)) formatParts.Add($"ToolTip=`{format.ToolTipText}`");
        if (!string.IsNullOrEmpty(format.HyperlinkText)) formatParts.Add($"Hyperlink=`{format.HyperlinkText}`");
        output.AppendLine($"- Format: {string.Join(", ", formatParts)}");

        Border border = obj.Border;
        if (border != null)
        {
            var borderParts = new List<string>();
            var sides = new[]
            {
                ("Left", border.LeftLineStyle),
                ("Right", border.RightLineStyle),
                ("Top", border.TopLineStyle),
                ("Bottom", border.BottomLineStyle),
            };
            foreach (var (side, style) in sides.Where(s => s.Item2 != CrLineStyleEnum.crLineStyleNoLine))
            {
                borderParts.Add($"{side}={EnumName(style)}");
            }

            if (borderParts.Count > 0) borderParts.Add($"Color={FormatColor(border.BorderColor)}");
            if (!IsNoColor(border.BackgroundColor)) borderParts.Add($"Background={FormatColor(border.BackgroundColor)}");
            if (border.HasDropShadow) borderParts.Add("DropShadow");
            if (border.EnableTightHorizontal) borderParts.Add("TightHorizontal");

            if (borderParts.Count > 0)
            {
                output.AppendLine($"- Border: {string.Join(", ", borderParts)}");
            }
        }
    }

    private static void AppendFieldFormat(StringBuilder output, FieldFormat fieldFormat, string valueType)
    {
        if (fieldFormat == null)
        {
            return;
        }

        var common = fieldFormat.CommonFormat;
        if (common != null && (common.EnableSuppressIfDuplicated || !common.EnableSystemDefault))
        {
            output.AppendLine($"- Common Format: SystemDefault={common.EnableSystemDefault}, SuppressIfDuplicated={common.EnableSuppressIfDuplicated}");
        }

        if ((valueType.Contains("Number") || valueType.Contains("Currency")) && fieldFormat.NumericFormat != null)
        {
            var n = fieldFormat.NumericFormat;
            output.AppendLine($"- Number Format: Decimals={n.NDecimalPlaces}, Rounding={EnumName(n.RoundingFormat)}, Negative={EnumName(n.NegativeFormat)}, " +
                $"Thousands={n.ThousandsSeparator}, Currency={EnumName(n.CurrencySymbolFormat)} `{n.CurrencySymbol}` {EnumName(n.CurrencyPosition)}, " +
                $"SuppressIfZero={n.EnableSuppressIfZero}, ZeroValue=`{n.ZeroValueString}`, ReverseSign={n.DisplayReverseSign}");
        }
        else if (valueType.Contains("Date") && fieldFormat.DateFormat != null)
        {
            var d = fieldFormat.DateFormat;
            output.AppendLine($"- Date Format: SystemDefault={EnumName(d.SystemDefaultType)}, Order={EnumName(d.DateOrder)}, Year={EnumName(d.YearFormat)}, " +
                $"Month={EnumName(d.MonthFormat)}, Day={EnumName(d.DayFormat)}, Separators=`{d.DatePrefixSeparator}|{d.DateFirstSeparator}|{d.DateSecondSeparator}|{d.DateSuffixSeparator}`");
        }
    }

    private static IEnumerable<string> GetParagraphFonts(TextObject textObj)
    {
        foreach (Paragraph paragraph in textObj.Paragraphs)
        {
            foreach (var element in paragraph.ParagraphElements.Cast<object>())
            {
                if (element is ParagraphTextElement textElement)
                {
                    yield return FormatFont(textElement.FontColor);
                }
                else if (element is ParagraphFieldElement fieldElement)
                {
                    yield return FormatFont(fieldElement.FontColor);
                }
            }
        }
    }

    private static List<KeyValuePair<string, string>> GetObjectConditionalFormulas(ReportObject obj)
    {
        var formulas = new List<KeyValuePair<string, string>>();

        var formatFormulas = obj.Format.ConditionFormulas;
        foreach (CrObjectFormatConditionFormulaTypeEnum type in Enum.GetValues(typeof(CrObjectFormatConditionFormulaTypeEnum)))
        {
            // Suppression already has its own line.
            if (type == CrObjectFormatConditionFormulaTypeEnum.crObjectFormatConditionFormulaTypeEnableSuppress) continue;
            AddConditionalFormula(formulas, EnumName(type), () => formatFormulas[type]?.Text);
        }

        if (obj.Border != null)
        {
            var borderFormulas = obj.Border.ConditionFormulas;
            foreach (CrBorderConditionFormulaTypeEnum type in Enum.GetValues(typeof(CrBorderConditionFormulaTypeEnum)))
            {
                AddConditionalFormula(formulas, $"Border {EnumName(type)}", () => borderFormulas[type]?.Text);
            }
        }

        FontColor fontColor = null;
        if (obj.Kind == CrReportObjectKindEnum.crReportObjectKindField) fontColor = ((FieldObject)obj).FontColor;
        else if (obj.Kind == CrReportObjectKindEnum.crReportObjectKindText) fontColor = ((TextObject)obj).FontColor;

        if (fontColor != null)
        {
            var fontFormulas = fontColor.ConditionFormulas;
            foreach (CrFontColorConditionFormulaTypeEnum type in Enum.GetValues(typeof(CrFontColorConditionFormulaTypeEnum)))
            {
                AddConditionalFormula(formulas, $"Font {EnumName(type)}", () => fontFormulas[type]?.Text);
            }
        }

        return formulas;
    }

    private static List<KeyValuePair<string, string>> GetSectionConditionalFormulas(SectionFormat format)
    {
        var formulas = new List<KeyValuePair<string, string>>();
        var sectionFormulas = format.ConditionFormulas;
        foreach (CrSectionAreaFormatConditionFormulaTypeEnum type in Enum.GetValues(typeof(CrSectionAreaFormatConditionFormulaTypeEnum)))
        {
            // Suppression already has its own heading.
            if (type == CrSectionAreaFormatConditionFormulaTypeEnum.crSectionAreaConditionFormulaTypeEnableSuppress) continue;
            AddConditionalFormula(formulas, EnumName(type), () => sectionFormulas[type]?.Text);
        }

        return formulas;
    }

    private static void AddConditionalFormula(List<KeyValuePair<string, string>> formulas, string name, Func<string> getText)
    {
        string text;
        try
        {
            text = getText();
        }
        catch (Exception)
        {
            // Not every formula type applies to every object kind, and the COM layer throws for those.
            return;
        }

        if (!string.IsNullOrWhiteSpace(text))
        {
            formulas.Add(new KeyValuePair<string, string>(name, text));
        }
    }

    private static void AppendConditionalFormulas(StringBuilder output, List<KeyValuePair<string, string>> formulas)
    {
        foreach (var formula in formulas)
        {
            output.AppendLine($"- Conditional {formula.Key} Formula:");
            output.AppendLine("```");
            output.AppendLine($"  {formula.Value}");
            output.AppendLine("```");
        }
    }

    private static string FormatFont(FontColor fontColor)
    {
        var font = fontColor?.Font;
        if (font == null)
        {
            return "(none)";
        }

        var styles = new List<string>();
        if (font.Bold) styles.Add("Bold");
        if (font.Italic) styles.Add("Italic");
        if (font.Underline) styles.Add("Underline");
        if (font.Strikethrough) styles.Add("Strikethrough");

        string style = styles.Count > 0 ? " " + string.Join(" ", styles) : string.Empty;
        return $"{font.Name} {font.Size}pt{style}, Color={FormatColor(fontColor.Color)}";
    }

    // Crystal stores colors as Win32 COLORREF (0x00BBGGRR); anything above 24 bits is the "no color" sentinel.
    private static bool IsNoColor(uint color) => color > 0xFFFFFF;

    private static string FormatColor(uint color)
    {
        if (IsNoColor(color))
        {
            return "None";
        }

        uint r = color & 0xFF;
        uint g = (color >> 8) & 0xFF;
        uint b = (color >> 16) & 0xFF;
        return $"#{r:X2}{g:X2}{b:X2}";
    }

    private static string EnumName(Enum value)
    {
        string name = value.ToString();
        string typeName = value.GetType().Name;

        // Values are named like crLineStyleSingle for CrLineStyleEnum, so strip the type-derived prefix.
        if (typeName.StartsWith("Cr") && typeName.EndsWith("Enum"))
        {
            string prefix = "cr" + typeName.Substring(2, typeName.Length - 6);
            if (name.StartsWith(prefix) && name.Length > prefix.Length)
            {
                return name.Substring(prefix.Length);
            }
        }

        // A few enums don't follow the type name exactly (crSectionAreaConditionFormulaType..., crRotationAngle...).
        return Regex.Replace(name, @"^cr\w*?(ConditionFormulaType|RotationAngle)", "");
    }

    private static string FormatDataSourceReference(string dataSource, HashSet<string> includedFormulaNames)
    {
        if (includedFormulaNames.Contains(dataSource))
        {
            return $"[`{dataSource}`](#{ToAnchor(dataSource)})";
        }

        return $"`{dataSource}`";
    }

    private static string ToAnchor(string headingText)
    {
        string slug = headingText.ToLowerInvariant();
        slug = Regex.Replace(slug, @"[^a-z0-9\s-]", "");
        slug = Regex.Replace(slug, @"\s+", "-");
        return slug;
    }

    private static string KindToDisplayName(CrReportObjectKindEnum kind)
    {
        switch (kind)
        {
            case CrReportObjectKindEnum.crReportObjectKindField:
                return "Field";
            case CrReportObjectKindEnum.crReportObjectKindText:
                return "Text";
            case CrReportObjectKindEnum.crReportObjectKindLine:
                return "Line";
            case CrReportObjectKindEnum.crReportObjectKindBox:
                return "Box";
            case CrReportObjectKindEnum.crReportObjectKindSubreport:
                return "Subreport";
            case CrReportObjectKindEnum.crReportObjectKindPicture:
                return "Picture";
            case CrReportObjectKindEnum.crReportObjectKindChart:
                return "Chart";
            case CrReportObjectKindEnum.crReportObjectKindCrosstab:
                return "Crosstab";
            case CrReportObjectKindEnum.crReportObjectKindBlobField:
                return "Blob Field";
            case CrReportObjectKindEnum.crReportObjectKindMap:
                return "Map";
            case CrReportObjectKindEnum.crReportObjectKindOlapGrid:
                return "OLAP Grid";
            case CrReportObjectKindEnum.crReportObjectKindFieldHeading:
                return "Field Heading";
            case CrReportObjectKindEnum.crReportObjectKindFlash:
                return "Flash";
            default:
                return kind.ToString();
        }
    }

    private static bool IsSearchTermInFormula(string formulaText)
        => _searchTerms.Length == 0 || _searchTerms.Any(st => formulaText.Contains(st));

    private static string H(int level) => new string('#', level);

    private static string BuildUsageOuput()
    {
        var output = new StringBuilder();
        output.AppendLine("Usage: CrystalDump path\\to\\report.rpt <command> <options>");
        output.AppendLine();
        output.AppendLine("COMMANDS:");
        output.AppendLine("--dump \t\t\t- dumps report export to provided or default output path");
        output.AppendLine();
        output.AppendLine("OPTIONS:");
        output.AppendLine("-o \"path\\to\\output.md\" \t- sets an output file for the dump");
        output.AppendLine("-s \"terms,to,search\" \t- comma-separated list of terms to search");
        output.AppendLine("-d \t\t\t- dry run for '--replace' command, prints what it would update");
        output.AppendLine("\nIf output.md is omitted, writes alongside the .rpt with a .dump.md extension.");

        return output.ToString();
    }

    private static string IndexToLetters(int num)
    {
        if (num <= 0) throw new ArgumentOutOfRangeException(nameof(num), "Error Converting Section Position");

        string letters = string.Empty;
        while (num > 0)
        {
            int rem = (num - 1) % 26;
            letters = Convert.ToChar('a' + rem) + letters;
            num = (num - 1) / 26;
        }

        return letters;
    }
}
