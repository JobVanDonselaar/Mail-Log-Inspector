using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using A = DocumentFormat.OpenXml.Drawing;
using C = DocumentFormat.OpenXml.Drawing.Charts;
using S = DocumentFormat.OpenXml.Spreadsheet;
using Xdr = DocumentFormat.OpenXml.Drawing.Spreadsheet;

namespace MailLogInspector.App;

/// <summary>
/// Gedeelde huisstijl en bouwstenen voor de Excel-rapporten. Zoekresultaten, domeinanalyse en
/// het analyserapport gebruiken dezelfde stijlnummers, kleuren, grafieken en paginaopmaak.
/// </summary>
internal static class ExcelReportKit
{
    public const uint StyleTitle = 1;
    public const uint StyleSubtitle = 2;
    public const uint StyleNote = 3;
    public const uint StyleSection = 4;
    public const uint StyleTableHeader = 5;
    public const uint StyleBody = 6;
    public const uint StyleBodyAlternate = 7;
    public const uint StyleNumber = 8;
    public const uint StyleNumberAlternate = 9;
    public const uint StylePercent = 10;
    public const uint StyleDateTime = 11;
    public const uint StyleKpiLabel = 12;
    public const uint StyleKpiBlue = 13;
    public const uint StyleKpiGreen = 14;
    public const uint StyleKpiPercent = 15;
    public const uint StyleKpiRed = 16;
    public const uint StyleKpiOrange = 17;
    public const uint StyleDuration = 18;
    public const uint StyleKpiText = 19;

    public const string ChartBlue = "2F75B5";
    public const string ChartGreen = "4C9A2A";
    public const string ChartRed = "C0504D";
    public const string ChartOrange = "E08214";

    public static void AddWorkbookStyles(WorkbookPart workbookPart)
    {
        WorkbookStylesPart stylesPart = workbookPart.AddNewPart<WorkbookStylesPart>();
        stylesPart.Stylesheet = CreateStylesheet();
        stylesPart.Stylesheet.Save();
    }

    private static S.Stylesheet CreateStylesheet()
    {
        var numberingFormats = new S.NumberingFormats(
            new S.NumberingFormat { NumberFormatId = 164, FormatCode = "#,##0" },
            new S.NumberingFormat { NumberFormatId = 165, FormatCode = "0.0%" },
            new S.NumberingFormat { NumberFormatId = 166, FormatCode = "0.0 \"sec\"" },
            new S.NumberingFormat { NumberFormatId = 167, FormatCode = "dd-mm-yyyy hh:mm" })
        { Count = 4 };

        var fonts = new S.Fonts(
            Font("FF1F2937", 11),
            Font("FFFFFFFF", 18, bold: true),
            Font("FFFFFFFF", 11, bold: true),
            Font("FF637386", 10),
            Font("FF1F4E78", 14, bold: true),
            Font("FF1F4E78", 16, bold: true),
            Font("FF2F855A", 16, bold: true),
            Font("FFC83B2B", 16, bold: true),
            Font("FFC77912", 16, bold: true))
        { Count = 9 };

        var fills = new S.Fills(
            new S.Fill(new S.PatternFill { PatternType = S.PatternValues.None }),
            new S.Fill(new S.PatternFill { PatternType = S.PatternValues.Gray125 }),
            SolidFill("FF1F4E78"),
            SolidFill("FFEAF2F8"),
            SolidFill("FF2F75B5"),
            SolidFill("FFF4F7FA"),
            SolidFill("FFE2F0D9"),
            SolidFill("FFFCE4D6"),
            SolidFill("FFFFF2CC"))
        { Count = 9 };

        var borders = new S.Borders(new S.Border(), ThinBorder("FFD8E0EA")) { Count = 2 };
        var formats = new S.CellFormats(
            new S.CellFormat(),
            Format(fontId: 1, fillId: 2, alignment: Align(S.HorizontalAlignmentValues.Left)),
            Format(fontId: 2, fillId: 2, alignment: Align(S.HorizontalAlignmentValues.Left)),
            Format(fontId: 3, fillId: 3, alignment: new S.Alignment { Vertical = S.VerticalAlignmentValues.Center, WrapText = true }),
            Format(fontId: 4, fillId: 3, alignment: Align(S.HorizontalAlignmentValues.Left)),
            Format(fontId: 2, fillId: 4, borderId: 1, alignment: Align(S.HorizontalAlignmentValues.Left)),
            Format(borderId: 1, alignment: Align(S.HorizontalAlignmentValues.Left)),
            Format(fillId: 5, borderId: 1, alignment: Align(S.HorizontalAlignmentValues.Left)),
            Format(borderId: 1, numberFormatId: 164, alignment: Align(S.HorizontalAlignmentValues.Right)),
            Format(fillId: 5, borderId: 1, numberFormatId: 164, alignment: Align(S.HorizontalAlignmentValues.Right)),
            Format(borderId: 1, numberFormatId: 165, alignment: Align(S.HorizontalAlignmentValues.Right)),
            Format(borderId: 1, numberFormatId: 167, alignment: Align(S.HorizontalAlignmentValues.Left)),
            Format(fontId: 3, fillId: 3, borderId: 1, alignment: Align(S.HorizontalAlignmentValues.Center)),
            Format(fontId: 5, fillId: 3, borderId: 1, numberFormatId: 164, alignment: Align(S.HorizontalAlignmentValues.Center)),
            Format(fontId: 6, fillId: 6, borderId: 1, numberFormatId: 164, alignment: Align(S.HorizontalAlignmentValues.Center)),
            Format(fontId: 6, fillId: 6, borderId: 1, numberFormatId: 165, alignment: Align(S.HorizontalAlignmentValues.Center)),
            Format(fontId: 7, fillId: 7, borderId: 1, numberFormatId: 164, alignment: Align(S.HorizontalAlignmentValues.Center)),
            Format(fontId: 8, fillId: 8, borderId: 1, numberFormatId: 164, alignment: Align(S.HorizontalAlignmentValues.Center)),
            Format(fontId: 5, fillId: 3, borderId: 1, numberFormatId: 166, alignment: Align(S.HorizontalAlignmentValues.Center)),
            Format(fontId: 5, fillId: 3, borderId: 1, alignment: Align(S.HorizontalAlignmentValues.Center)))
        { Count = 20 };

        return new S.Stylesheet(
            numberingFormats,
            fonts,
            fills,
            borders,
            new S.CellStyleFormats(new S.CellFormat()) { Count = 1 },
            formats,
            new S.CellStyles(new S.CellStyle { Name = "Normal", FormatId = 0, BuiltinId = 0 }) { Count = 1 },
            new S.DifferentialFormats { Count = 0 },
            new S.TableStyles { Count = 0, DefaultTableStyle = "TableStyleMedium2", DefaultPivotStyle = "PivotStyleLight16" });
    }

    private static S.Alignment Align(S.HorizontalAlignmentValues horizontal) =>
        new() { Horizontal = horizontal, Vertical = S.VerticalAlignmentValues.Center };

    private static S.Font Font(string color, double size, bool bold = false)
    {
        var font = new S.Font();
        if (bold)
        {
            font.Append(new S.Bold());
        }
        font.Append(
            new S.FontSize { Val = size },
            new S.Color { Rgb = color },
            new S.FontName { Val = "Aptos" },
            new S.FontFamilyNumbering { Val = 2 });
        return font;
    }

    private static S.Fill SolidFill(string color) =>
        new(new S.PatternFill(new S.ForegroundColor { Rgb = color }, new S.BackgroundColor { Indexed = 64 })
        {
            PatternType = S.PatternValues.Solid
        });

    private static S.Border ThinBorder(string color) =>
        new(
            new S.LeftBorder(new S.Color { Rgb = color }) { Style = S.BorderStyleValues.Thin },
            new S.RightBorder(new S.Color { Rgb = color }) { Style = S.BorderStyleValues.Thin },
            new S.TopBorder(new S.Color { Rgb = color }) { Style = S.BorderStyleValues.Thin },
            new S.BottomBorder(new S.Color { Rgb = color }) { Style = S.BorderStyleValues.Thin },
            new S.DiagonalBorder());

    private static S.CellFormat Format(
        uint fontId = 0,
        uint fillId = 0,
        uint borderId = 0,
        uint numberFormatId = 0,
        S.Alignment? alignment = null)
    {
        var format = new S.CellFormat
        {
            FontId = fontId,
            FillId = fillId,
            BorderId = borderId,
            NumberFormatId = numberFormatId,
            ApplyFont = fontId > 0,
            ApplyFill = fillId > 0,
            ApplyBorder = borderId > 0,
            ApplyNumberFormat = numberFormatId > 0,
            ApplyAlignment = alignment is not null
        };
        if (alignment is not null)
        {
            format.Append(alignment);
        }
        return format;
    }

    public static double RoundChartMaximum(double value)
    {
        if (value <= 0)
        {
            return 0;
        }

        double targetStep = value / 4.0;
        double magnitude = Math.Pow(10, Math.Floor(Math.Log10(targetStep)));
        double normalized = targetStep / magnitude;
        double factor = normalized <= 1
            ? 1
            : normalized <= 2
                ? 2
                : normalized <= 2.5
                    ? 2.5
                    : normalized <= 5
                        ? 5
                        : 10;
        return Math.Ceiling(value / (factor * magnitude)) * factor * magnitude;
    }

    public static C.ChartSpace CreateBarChart(
        C.BarDirectionValues direction,
        string title,
        string color,
        string categoryFormula,
        string valueFormula,
        IReadOnlyList<string> categories,
        IReadOnlyList<double> values,
        uint categoryAxisId,
        uint valueAxisId,
        string numberFormat = "#,##0",
        bool showValues = false,
        double? maximumValue = null)
    {
        var chartSpace = new C.ChartSpace();
        chartSpace.Append(new C.EditingLanguage { Val = "nl-NL" });
        var chart = new C.Chart();
        chart.Append(CreateChartTitle(title));
        var plotArea = new C.PlotArea(new C.Layout());
        var barChart = new C.BarChart(
            new C.BarDirection { Val = direction },
            new C.BarGrouping { Val = C.BarGroupingValues.Clustered },
            new C.VaryColors { Val = false });
        var series = new C.BarChartSeries(
            new C.Index { Val = 0 },
            new C.Order { Val = 0 },
            new C.ChartShapeProperties(
                new A.SolidFill(new A.RgbColorModelHex { Val = color }),
                new A.Outline(new A.NoFill())),
            new C.CategoryAxisData(CreateStringReference(categoryFormula, categories)),
            new C.Values(CreateNumberReference(valueFormula, values, numberFormat)));
        barChart.Append(series);
        if (showValues)
        {
            barChart.Append(new C.DataLabels(
                new C.NumberingFormat { FormatCode = numberFormat, SourceLinked = false },
                new C.ShowLegendKey { Val = false },
                new C.ShowValue { Val = true },
                new C.ShowCategoryName { Val = false },
                new C.ShowSeriesName { Val = false }));
        }
        barChart.Append(new C.GapWidth { Val = (ushort)(direction == C.BarDirectionValues.Column ? 55 : 65) });
        barChart.Append(new C.AxisId { Val = categoryAxisId }, new C.AxisId { Val = valueAxisId });
        plotArea.Append(barChart);
        plotArea.Append(CreateCategoryAxis(categoryAxisId, valueAxisId, direction));
        plotArea.Append(CreateValueAxis(valueAxisId, categoryAxisId, direction, numberFormat, maximumValue));
        chart.Append(plotArea, new C.PlotVisibleOnly { Val = true }, new C.DisplayBlanksAs { Val = C.DisplayBlanksAsValues.Zero });
        chartSpace.Append(chart);
        return chartSpace;
    }

    private static C.StringReference CreateStringReference(string formula, IReadOnlyList<string> values)
    {
        var cache = new C.StringCache();
        cache.Append(new C.PointCount { Val = checked((uint)values.Count) });
        for (uint index = 0; index < values.Count; index++)
        {
            cache.Append(new C.StringPoint { Index = index, NumericValue = new C.NumericValue(values[(int)index]) });
        }
        return new C.StringReference(new C.Formula(formula), cache);
    }

    private static C.NumberReference CreateNumberReference(string formula, IReadOnlyList<double> values, string numberFormat)
    {
        var cache = new C.NumberingCache(new C.FormatCode(numberFormat));
        cache.Append(new C.PointCount { Val = checked((uint)values.Count) });
        for (uint index = 0; index < values.Count; index++)
        {
            cache.Append(new C.NumericPoint
            {
                Index = index,
                NumericValue = new C.NumericValue(values[(int)index].ToString(CultureInfo.InvariantCulture))
            });
        }
        return new C.NumberReference(new C.Formula(formula), cache);
    }

    private static C.CategoryAxis CreateCategoryAxis(
        uint axisId,
        uint crossingAxisId,
        C.BarDirectionValues direction)
    {
        return new C.CategoryAxis(
            new C.AxisId { Val = axisId },
            new C.Scaling(new C.Orientation { Val = C.OrientationValues.MinMax }),
            new C.Delete { Val = false },
            new C.AxisPosition { Val = direction == C.BarDirectionValues.Bar ? C.AxisPositionValues.Left : C.AxisPositionValues.Bottom },
            new C.TickLabelPosition { Val = C.TickLabelPositionValues.NextTo },
            new C.CrossingAxis { Val = crossingAxisId },
            new C.Crosses { Val = C.CrossesValues.AutoZero },
            new C.AutoLabeled { Val = true },
            new C.LabelAlignment { Val = C.LabelAlignmentValues.Center },
            new C.LabelOffset { Val = 100 });
    }

    private static C.ValueAxis CreateValueAxis(
        uint axisId,
        uint crossingAxisId,
        C.BarDirectionValues direction,
        string numberFormat,
        double? maximumValue)
    {
        var scaling = new C.Scaling(new C.Orientation { Val = C.OrientationValues.MinMax });
        if (maximumValue.HasValue)
        {
            scaling.Append(new C.MaxAxisValue { Val = maximumValue.Value });
        }
        scaling.Append(new C.MinAxisValue { Val = 0 });

        return new C.ValueAxis(
            new C.AxisId { Val = axisId },
            scaling,
            new C.Delete { Val = false },
            new C.AxisPosition { Val = direction == C.BarDirectionValues.Bar ? C.AxisPositionValues.Bottom : C.AxisPositionValues.Left },
            new C.MajorGridlines(
                new C.ChartShapeProperties(
                    new A.Outline(
                        new A.SolidFill(new A.RgbColorModelHex { Val = "D9E2F3" }))
                    {
                        Width = 6350
                    })),
            new C.NumberingFormat { FormatCode = numberFormat, SourceLinked = false },
            new C.TickLabelPosition { Val = C.TickLabelPositionValues.NextTo },
            new C.CrossingAxis { Val = crossingAxisId },
            new C.Crosses { Val = C.CrossesValues.AutoZero },
            new C.CrossBetween { Val = C.CrossBetweenValues.Between });
    }

    private static C.Title CreateChartTitle(string title) =>
        new(
            new C.ChartText(new C.RichText(
                new A.BodyProperties(),
                new A.ListStyle(),
                new A.Paragraph(
                    new A.ParagraphProperties(
                        new A.DefaultRunProperties
                        {
                            FontSize = 1100,
                            Bold = true
                        }),
                    new A.Run(new A.Text(title))))),
            new C.Overlay { Val = false });

    public static Xdr.TwoCellAnchor CreateAnchor(
        string relationshipId,
        uint drawingId,
        string name,
        int fromColumn,
        int fromRow,
        int toColumn,
        int toRow)
    {
        var graphicFrame = new Xdr.GraphicFrame(
            new Xdr.NonVisualGraphicFrameProperties(
                new Xdr.NonVisualDrawingProperties { Id = drawingId, Name = name },
                new Xdr.NonVisualGraphicFrameDrawingProperties()),
            new Xdr.Transform(
                new A.Offset { X = 0, Y = 0 },
                new A.Extents { Cx = 0, Cy = 0 }),
            new A.Graphic(new A.GraphicData(
                new C.ChartReference { Id = relationshipId })
            { Uri = "http://schemas.openxmlformats.org/drawingml/2006/chart" }))
        { Macro = string.Empty };

        return new Xdr.TwoCellAnchor(
            Marker<Xdr.FromMarker>(fromColumn, fromRow),
            Marker<Xdr.ToMarker>(toColumn, toRow),
            graphicFrame,
            new Xdr.ClientData());
    }

    private static T Marker<T>(int column, int row) where T : OpenXmlCompositeElement, new()
    {
        var marker = new T();
        marker.Append(
            new Xdr.ColumnId(column.ToString(CultureInfo.InvariantCulture)),
            new Xdr.ColumnOffset("0"),
            new Xdr.RowId(row.ToString(CultureInfo.InvariantCulture)),
            new Xdr.RowOffset("0"));
        return marker;
    }

    public static S.SheetProperties FitToPageProperties() =>
        new(new S.PageSetupProperties { FitToPage = true, AutoPageBreaks = false });

    public static S.SheetViews DashboardView() =>
        new(new S.SheetView
        {
            WorkbookViewId = 0,
            ZoomScale = 80U,
            ZoomScaleNormal = 100U
        });

    public static S.SheetViews FrozenView(double rows, string topLeftCell) =>
        new(new S.SheetView(
            new S.Pane
            {
                VerticalSplit = rows,
                TopLeftCell = topLeftCell,
                ActivePane = S.PaneValues.BottomLeft,
                State = S.PaneStateValues.Frozen
            })
        { WorkbookViewId = 0 });

    public static S.Column Column(uint index, double width) =>
        new() { Min = index, Max = index, Width = width, CustomWidth = true };

    public static S.PageMargins ReportPageMargins() =>
        new() { Left = 0.3, Right = 0.3, Top = 0.5, Bottom = 0.5, Header = 0.2, Footer = 0.2 };

    public static S.PageSetup LandscapePageSetup() =>
        new()
        {
            PaperSize = 9,
            Orientation = S.OrientationValues.Landscape,
            FitToWidth = 1,
            FitToHeight = 0
        };

    public static S.MergeCells MergeRanges(params string[] ranges)
    {
        var merges = new S.MergeCells();
        foreach (string range in ranges)
        {
            merges.Append(new S.MergeCell { Reference = range });
        }
        return merges;
    }

    public static S.Row KpiRow(uint rowIndex, uint style, params (string Column, string Label)[] values)
    {
        return CreateSparseRow(
            rowIndex,
            values.Select(value => StringCell($"{value.Column}{rowIndex}", value.Label, style)).ToArray());
    }

    public static S.Row StyledSpanRow(
        uint rowIndex,
        int firstColumn,
        int lastColumn,
        string value,
        uint style,
        double height)
    {
        var cells = new List<S.Cell> { StringCell($"{ColumnName(firstColumn)}{rowIndex}", value, style) };
        for (int column = firstColumn + 1; column <= lastColumn; column++)
        {
            cells.Add(StyledBlank($"{ColumnName(column)}{rowIndex}", style));
        }
        return CreateSparseRow(rowIndex, height, cells.ToArray());
    }

    public static S.Row CreateStyledStringRow(uint rowIndex, uint style, params string[] values)
    {
        var cells = values.Select((value, index) =>
            StringCell($"{ColumnName(index + 1)}{rowIndex}", value, style)).ToArray();
        return CreateSparseRow(rowIndex, cells);
    }

    public static S.Row CreateSparseRow(uint rowIndex, params S.Cell[] cells) =>
        CreateSparseRow(rowIndex, null, cells);

    public static S.Row CreateSparseRow(uint rowIndex, double? height, params S.Cell[] cells)
    {
        var row = new S.Row { RowIndex = rowIndex };
        if (height.HasValue)
        {
            row.Height = height.Value;
            row.CustomHeight = true;
        }
        row.Append(cells);
        return row;
    }

    public static S.Cell StringCell(string reference, string? value, uint style = 0) => new()
    {
        CellReference = reference,
        DataType = S.CellValues.String,
        CellValue = new S.CellValue(value ?? string.Empty),
        StyleIndex = style
    };

    public static S.Cell NumberCell(string reference, double value, uint style = 0) => new()
    {
        CellReference = reference,
        DataType = S.CellValues.Number,
        CellValue = new S.CellValue(value.ToString(CultureInfo.InvariantCulture)),
        StyleIndex = style
    };

    public static S.Cell DateCell(string reference, DateTime? value, uint style) =>
        value.HasValue
            ? NumberCell(reference, value.Value.ToOADate(), style)
            : StyledBlank(reference, style);

    public static S.Cell StyledBlank(string reference, uint style) => new()
    {
        CellReference = reference,
        StyleIndex = style
    };

    public static string ColumnName(int column)
    {
        string result = string.Empty;
        while (column > 0)
        {
            column--;
            result = (char)('A' + column % 26) + result;
            column /= 26;
        }
        return result;
    }
}
