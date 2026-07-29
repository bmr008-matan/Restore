#nullable enable
using API.Reporting.Data;
using API.Reporting.Model;
using API.Reporting.Model.Elements;

namespace API.Reporting
{
    /// <summary>
    /// Demo report definitions, used to seed the template catalogue and to exercise the renderer in
    /// tests. They are built in code rather than stored as JSON so that a change to the model breaks the
    /// build here instead of silently invalidating a seeded template at run time.
    /// </summary>
    public static class SampleReports
    {
        public const string StockByBrandCode = "STOCK_BY_BRAND";
        public const string StockByBrandHebrewCode = "STOCK_BY_BRAND_HE";

        /// <summary>
        /// Stock valuation grouped by brand and then by type, with subtotals at both levels, a grand
        /// total, a repeating page header and footer with page numbers, and a conditional rule that
        /// highlights high-value lines. Exercises every feature the designer exposes.
        /// </summary>
        public static ReportDefinition StockByBrand(TextDirection direction = TextDirection.Ltr)
        {
            var hebrew = direction == TextDirection.Rtl;

            var definition = new ReportDefinition
            {
                Name = hebrew ? "מלאי לפי מותג" : "Stock by Brand",
                Title = hebrew ? "דוח מלאי לפי מותג" : "Stock Valuation by Brand",
                Description = hebrew
                    ? "מלאי מקובץ לפי מותג וסוג, עם סכומי ביניים"
                    : "Stock grouped by brand and type, with subtotals at each level.",
                Page = new PageSetupDef
                {
                    PaperSize = PaperSize.A4,
                    Orientation = PageOrientation.Portrait,
                    Direction = direction,
                    Culture = hebrew ? "he-IL" : "en-GB",
                    // The top and bottom margins have to leave room for the page header and footer,
                    // because Chromium draws those inside the margin.
                    Margins = new MarginsDef { TopMm = 24, RightMm = 12, BottomMm = 18, LeftMm = 12 },
                    HeaderHeightMm = 12,
                    FooterHeightMm = 9
                },
                DefaultStyle = new StyleDef { FontSizePt = 9 },
                Parameters =
                {
                    new ReportParameter
                    {
                        Name = "brand",
                        Label = hebrew ? "מותג" : "Brand",
                        Type = ParameterType.Select,
                        LookupTableId = SampleDataProvider.BrandsTableId,
                        ValueField = "value",
                        DisplayField = "label",
                        Order = 1
                    },
                    new ReportParameter
                    {
                        Name = "minPrice",
                        Label = hebrew ? "מחיר מינימלי" : "Minimum price",
                        Type = ParameterType.Decimal,
                        DefaultValue = "0",
                        Order = 2
                    },
                    new ReportParameter
                    {
                        Name = "inStockOnly",
                        Label = hebrew ? "פריטים במלאי בלבד" : "In stock only",
                        Type = ParameterType.Bool,
                        DefaultValue = "false",
                        Order = 3
                    }
                },
                DataSets =
                {
                    new DataSetDef
                    {
                        Key = "detail",
                        Name = hebrew ? "פירוט מלאי" : "Stock detail",
                        SourceKind = DataSourceKind.Sample,
                        TableId = SampleDataProvider.ProductsTableId,
                        Fields = SampleDataProvider.ProductFields.ToList(),
                        Inputs =
                        {
                            new DataSetInput { Name = "brand", Kind = InputKind.Parameter, Value = "brand" },
                            new DataSetInput { Name = "minPrice", Kind = InputKind.Parameter, Value = "minPrice" },
                            new DataSetInput { Name = "inStockOnly", Kind = InputKind.Parameter, Value = "inStockOnly" }
                        },
                        Sort = { new SortDef { Field = "name" } }
                    }
                }
            };

            definition.Bands.Add(BuildReportHeader(hebrew));
            definition.Bands.Add(BuildPageHeader(hebrew));
            definition.Bands.Add(BuildDetail(hebrew));
            definition.Bands.Add(BuildPageFooter(hebrew));
            definition.Bands.Add(BuildReportFooter(hebrew));

            return definition;
        }

        private static BandDef BuildReportHeader(bool hebrew) => new()
        {
            Kind = BandKind.ReportHeader,
            HeightMm = 24,
            Elements =
            {
                new TextElement
                {
                    Name = "title",
                    Text = "{ReportTitle}",
                    XMm = 0, YMm = 0, WidthMm = 120, HeightMm = 9,
                    Style = new StyleDef { FontSizePt = 16, Bold = true }
                },
                new TextElement
                {
                    Name = "subtitle",
                    Text = hebrew
                        ? "הופק בתאריך {DateTime:dd/MM/yyyy HH:mm}"
                        : "Generated {DateTime:dd/MM/yyyy HH:mm}",
                    XMm = 0, YMm = 10, WidthMm = 120, HeightMm = 5,
                    Style = new StyleDef { FontSizePt = 8, Color = "#666666" }
                },
                new TextElement
                {
                    Name = "filters",
                    // Shows the parameters the run actually used, which is the difference between a
                    // report you can trust and one you cannot.
                    Text = hebrew
                        ? "מותג: {@brand}   מחיר מינימלי: {@minPrice:#,##0.00}"
                        : "Brand: {@brand}   Minimum price: {@minPrice:#,##0.00}",
                    XMm = 0, YMm = 16, WidthMm = 180, HeightMm = 5,
                    Style = new StyleDef { FontSizePt = 8, Color = "#666666" }
                },
                new LineElement { XMm = 0, YMm = 22, WidthMm = 186, HeightMm = 1, ThicknessPt = 1, Color = "#333333" }
            }
        };

        private static BandDef BuildPageHeader(bool hebrew) => new()
        {
            Kind = BandKind.PageHeader,
            HeightMm = 12,
            Elements =
            {
                new TextElement
                {
                    Name = "runningTitle",
                    Text = "{ReportTitle}",
                    XMm = 0, YMm = 3, WidthMm = 110, HeightMm = 5,
                    Style = new StyleDef { FontSizePt = 8, Color = "#666666" }
                },
                new PageNumberElement
                {
                    Name = "pageNumber",
                    Template = hebrew ? "עמוד {PageNumber} מתוך {TotalPages}" : "Page {PageNumber} of {TotalPages}",
                    XMm = 120, YMm = 3, WidthMm = 66, HeightMm = 5,
                    Style = new StyleDef { FontSizePt = 8, Color = "#666666", Align = HorizontalAlign.End }
                }
            }
        };

        private static BandDef BuildDetail(bool hebrew) => new()
        {
            Kind = BandKind.Detail,
            HeightMm = 40,
            Elements =
            {
                new TableElement
                {
                    Name = "stock",
                    DataSetKey = "detail",
                    XMm = 0, YMm = 0, WidthMm = 186, HeightMm = 40,
                    RepeatHeaderOnEachPage = true,
                    HeaderStyle = new StyleDef
                    {
                        BackColor = "#2C3E50",
                        Color = "#FFFFFF",
                        FontSizePt = 9
                    },
                    AlternateRowStyle = new StyleDef { BackColor = "#F7F7F7" },
                    CellBorder = new BorderDef
                    {
                        Bottom = new BorderSideDef { WidthPt = 0.25, Color = "#CCCCCC" }
                    },
                    Columns =
                    {
                        new TableColumnDef
                        {
                            Field = "name",
                            Caption = hebrew ? "פריט" : "Product",
                            WidthPercent = 42
                        },
                        new TableColumnDef
                        {
                            Field = "type",
                            Caption = hebrew ? "סוג" : "Type",
                            WidthPercent = 16
                        },
                        new TableColumnDef
                        {
                            Field = "quantityInStock",
                            Caption = hebrew ? "כמות" : "Qty",
                            WidthPercent = 10
                        },
                        new TableColumnDef
                        {
                            Field = "price",
                            Caption = hebrew ? "מחיר" : "Price",
                            WidthPercent = 14,
                            Format = "#,##0.00"
                        },
                        new TableColumnDef
                        {
                            Field = "stockValue",
                            Caption = hebrew ? "שווי מלאי" : "Stock value",
                            WidthPercent = 18,
                            Format = "#,##0.00",
                            Rules =
                            {
                                new ConditionalRuleDef
                                {
                                    Name = "High value",
                                    Expression = "Fields.stockValue > 5000",
                                    Actions = new RuleActions { Bold = true, Color = "#B00020" }
                                }
                            }
                        }
                    },
                    Groups =
                    {
                        new GroupDef
                        {
                            Field = "brand",
                            ShowHeader = true,
                            ShowFooter = true,
                            ShowItemCount = true,
                            KeepTogether = false,
                            HeaderText = hebrew ? "מותג: {brand}" : "Brand: {brand}",
                            FooterText = hebrew ? "סה\"כ {brand}" : "Total {brand}",
                            HeaderStyle = new StyleDef { BackColor = "#DDE4EA", FontSizePt = 10 },
                            Aggregates =
                            {
                                new AggregateDef
                                {
                                    Function = AggregateFunction.Sum, Field = "quantityInStock",
                                    TargetColumn = "quantityInStock"
                                },
                                new AggregateDef
                                {
                                    Function = AggregateFunction.Sum, Field = "stockValue",
                                    TargetColumn = "stockValue", Format = "#,##0.00"
                                }
                            }
                        },
                        new GroupDef
                        {
                            Field = "type",
                            ShowHeader = true,
                            ShowFooter = true,
                            HeaderText = hebrew ? "סוג: {type}" : "{type}",
                            FooterText = hebrew ? "סה\"כ {type}" : "Subtotal {type}",
                            HeaderStyle = new StyleDef { BackColor = "#F0F3F5", Italic = true },
                            Aggregates =
                            {
                                new AggregateDef
                                {
                                    Function = AggregateFunction.Sum, Field = "stockValue",
                                    TargetColumn = "stockValue", Format = "#,##0.00"
                                }
                            }
                        }
                    },
                    GrandTotalLabel = hebrew ? "סה\"כ הכל" : "Grand total",
                    GrandTotals =
                    {
                        new AggregateDef
                        {
                            Function = AggregateFunction.Sum, Field = "quantityInStock",
                            TargetColumn = "quantityInStock"
                        },
                        new AggregateDef
                        {
                            Function = AggregateFunction.Sum, Field = "stockValue",
                            TargetColumn = "stockValue", Format = "#,##0.00"
                        }
                    },
                    EmptyText = hebrew ? "אין נתונים" : "No stock matches these filters"
                }
            }
        };

        private static BandDef BuildPageFooter(bool hebrew) => new()
        {
            Kind = BandKind.PageFooter,
            HeightMm = 9,
            Elements =
            {
                new LineElement { XMm = 0, YMm = 0, WidthMm = 186, HeightMm = 1, ThicknessPt = 0.5, Color = "#CCCCCC" },
                new TextElement
                {
                    Name = "printedBy",
                    Text = hebrew ? "הופק על ידי {CurrentUser}" : "Printed by {CurrentUser}",
                    XMm = 0, YMm = 2, WidthMm = 110, HeightMm = 5,
                    Style = new StyleDef { FontSizePt = 7, Color = "#888888" }
                },
                new PageNumberElement
                {
                    Name = "footerPage",
                    Template = "{PageNumber} / {TotalPages}",
                    XMm = 150, YMm = 2, WidthMm = 36, HeightMm = 5,
                    Style = new StyleDef { FontSizePt = 7, Color = "#888888", Align = HorizontalAlign.End }
                }
            }
        };

        private static BandDef BuildReportFooter(bool hebrew) => new()
        {
            Kind = BandKind.ReportFooter,
            HeightMm = 14,
            Elements =
            {
                new TextElement
                {
                    Name = "summary",
                    Text = hebrew
                        ? "סוף הדוח — {RowCount} שורות"
                        : "End of report — {RowCount} rows",
                    XMm = 0, YMm = 4, WidthMm = 186, HeightMm = 6,
                    Style = new StyleDef { FontSizePt = 8, Italic = true, Color = "#666666" }
                }
            }
        };

        /// <summary>Both demo reports, for seeding.</summary>
        public static IEnumerable<(string Code, ReportDefinition Definition)> All()
        {
            yield return (StockByBrandCode, StockByBrand());
            yield return (StockByBrandHebrewCode, StockByBrand(TextDirection.Rtl));
        }
    }
}
