using CrystalDecisions.CrystalReports.Engine;
using CrystalDecisions.ReportAppServer.ReportDefModel;
using CrystalDecisions.Shared;
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

using Area = CrystalDecisions.ReportAppServer.ReportDefModel.Area;
using ReportDocument = CrystalDecisions.CrystalReports.Engine.ReportDocument;
using Section = CrystalDecisions.ReportAppServer.ReportDefModel.Section;
using SectionFormat = CrystalDecisions.ReportAppServer.ReportDefModel.SectionFormat;

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

        var output = new StringBuilder();
        foreach (Area area in clientDoc.ReportDefinition.Areas)
        {
            var sectionOutput = new StringBuilder();
            int position = 0;
            foreach (Section section in area.Sections.Cast<Section>())
            {
                position++;
                SectionFormat format = section.Format;
                var formula = format.ConditionFormulas[0];
                if (!string.IsNullOrWhiteSpace(formula.Text) && IsSearchTermInFormula(formula.Text))
                {
                    string prefix = Regex.Replace(
                        Regex.Replace(section.Name, @"\d+$", ""),
                        pattern, " $0", RegexOptions.IgnorePatternWhitespace);
                    string sectionTitle = $"{prefix} '{IndexToLetters(position)}'";
                    sectionOutput.AppendLine($"## {sectionTitle}");
                    sectionOutput.AppendLine($"### Suppression Formula");
                    sectionOutput.AppendLine("```");
                    sectionOutput.AppendLine(formula.Text);
                    sectionOutput.AppendLine("```");
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

                output.AppendLine($"# {areaName}");
                output.Append(sectionOutput);
            }
        }

        var formulaFieldsOutput = new StringBuilder();
        foreach (FormulaFieldDefinition ff in reportDoc.DataDefinition.FormulaFields)
        {
            if (!string.IsNullOrWhiteSpace(ff.Text) && IsSearchTermInFormula(ff.Text)) {
                formulaFieldsOutput.AppendLine($"## {ff.FormulaName}");
                formulaFieldsOutput.AppendLine("```");
                formulaFieldsOutput.AppendLine(ff.Text);
                formulaFieldsOutput.AppendLine("```");
                formulaFieldsOutput.AppendLine();
            }
        }

        if (formulaFieldsOutput.Length > 0)
        {
            output.AppendLine();
            output.AppendLine("# Formula Fields");
            output.Append(formulaFieldsOutput);
        }

        if (reportDoc.Subreports.Count > 0)
        {
            var subreportOutput = new StringBuilder();
            foreach (ReportDocument sub in reportDoc.Subreports)
            {
                var subreportFormulaOutput = new StringBuilder();
                foreach (FormulaFieldDefinition ff in sub.DataDefinition.FormulaFields)
                {
                    if (!string.IsNullOrWhiteSpace(ff.Text) && IsSearchTermInFormula(ff.Text))
                    {
                        subreportFormulaOutput.AppendLine($"### {ff.FormulaName}");
                        subreportFormulaOutput.AppendLine("```");
                        subreportFormulaOutput.AppendLine(ff.Text);
                        subreportFormulaOutput.AppendLine("```");
                        subreportFormulaOutput.AppendLine();
                    }
                }

                if (subreportFormulaOutput.Length > 0)
                {
                    subreportOutput.AppendLine($"## Subreport: {sub.Name}");
                    subreportOutput.Append(subreportFormulaOutput);
                }
            }

            if (subreportOutput.Length > 0)
            {
                output.AppendLine("# Subreport Formula Fields");
                output.Append(subreportOutput);
            }
        }

        File.WriteAllText(outputPath, output.ToString());
        Console.WriteLine($"Written to: {outputPath}");

        reportDoc.Close();
    }

    private static bool IsSearchTermInFormula(string formulaText) 
        => _searchTerms.Length == 0 || _searchTerms.Any(st => formulaText.Contains(st));

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
