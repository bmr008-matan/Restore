/**
 * TypeScript mirror of the C# definition model in API/Reporting/Model.
 *
 * The designer edits this object and PUTs it back unchanged apart from the edits, so the shapes have to
 * agree with the server exactly. Enums are string unions because the API serialises enums by name.
 */

export type PaperSize = 'A3' | 'A4' | 'A5' | 'Letter' | 'Legal' | 'Custom';
export type PageOrientation = 'Portrait' | 'Landscape';
export type TextDirection = 'Ltr' | 'Rtl';
export type BandKind = 'ReportHeader' | 'PageHeader' | 'Detail' | 'PageFooter' | 'ReportFooter';
export type HorizontalAlign = 'Left' | 'Center' | 'Right' | 'Justify' | 'Start' | 'End';
export type VerticalAlign = 'Top' | 'Middle' | 'Bottom';
export type ParameterType =
    | 'Text' | 'Int' | 'Decimal' | 'Date' | 'DateRange' | 'Bool' | 'Select' | 'MultiSelect';
export type DataSourceKind = 'OracleTableId' | 'PushedData' | 'Sample';
export type InputKind = 'Literal' | 'Parameter' | 'Expression';
export type SortDirection = 'Asc' | 'Desc';
export type FieldDataType = 'String' | 'Int' | 'Decimal' | 'Date' | 'Bool';
export type AggregateFunction =
    | 'Sum' | 'Avg' | 'Min' | 'Max' | 'Count' | 'CountDistinct' | 'First' | 'Last';
export type LineOrientation = 'Horizontal' | 'Vertical';
export type ImageFit = 'Contain' | 'Cover' | 'Fill' | 'None';
export type BorderStyleName = 'None' | 'Solid' | 'Dashed' | 'Dotted' | 'Double';

/** Discriminator values match the JsonDerivedType names on ReportElement. */
export type ElementType = 'text' | 'field' | 'image' | 'line' | 'box' | 'table' | 'pageNumber';

export interface BorderSideDef {
    style?: BorderStyleName;
    widthPt?: number;
    color?: string;
}

export interface BorderDef {
    all?: BorderSideDef;
    top?: BorderSideDef;
    right?: BorderSideDef;
    bottom?: BorderSideDef;
    left?: BorderSideDef;
}

export interface PaddingDef {
    topMm?: number;
    rightMm?: number;
    bottomMm?: number;
    leftMm?: number;
}

/** Every property optional: null means "inherit from the level above". */
export interface StyleDef {
    fontFamily?: string;
    fontSizePt?: number;
    bold?: boolean;
    italic?: boolean;
    underline?: boolean;
    color?: string;
    backColor?: string;
    align?: HorizontalAlign;
    vAlign?: VerticalAlign;
    border?: BorderDef;
    padding?: PaddingDef;
    format?: string;
    lineHeight?: number;
}

export interface MarginsDef {
    topMm: number;
    rightMm: number;
    bottomMm: number;
    leftMm: number;
}

export interface PageSetupDef {
    paperSize: PaperSize;
    customWidthMm?: number;
    customHeightMm?: number;
    orientation: PageOrientation;
    margins: MarginsDef;
    headerHeightMm: number;
    footerHeightMm: number;
    direction: TextDirection;
    culture: string;
}

export interface ReportParameter {
    name: string;
    label?: string;
    type: ParameterType;
    required: boolean;
    defaultValue?: string;
    lookupTableId?: number;
    valueField?: string;
    displayField?: string;
    order: number;
    hidden: boolean;
}

export interface FieldMeta {
    name: string;
    caption?: string;
    dataType: FieldDataType;
    format?: string;
}

export interface DataSetInput {
    name: string;
    kind: InputKind;
    value?: string;
}

export interface SortDef {
    field: string;
    direction: SortDirection;
}

export interface DataSetDef {
    key: string;
    name?: string;
    sourceKind: DataSourceKind;
    tableId?: number;
    inputs: DataSetInput[];
    fields: FieldMeta[];
    sort: SortDef[];
    maxRows?: number;
}

export interface RuleActions {
    hide?: boolean;
    bold?: boolean;
    italic?: boolean;
    underline?: boolean;
    color?: string;
    backColor?: string;
    fontSizePt?: number;
    align?: HorizontalAlign;
    textOverride?: string;
    format?: string;
}

export interface ConditionalRuleDef {
    name?: string;
    expression: string;
    actions: RuleActions;
    enabled: boolean;
}

export interface AggregateDef {
    function: AggregateFunction;
    field?: string;
    /** Field name of the column this total prints under, so subtotals line up. */
    targetColumn?: string;
    format?: string;
    label?: string;
    style?: StyleDef;
}

export interface GroupDef {
    field?: string;
    expression?: string;
    sortDirection: SortDirection;
    showHeader: boolean;
    showFooter: boolean;
    headerText?: string;
    footerText?: string;
    aggregates: AggregateDef[];
    headerAggregates: AggregateDef[];
    pageBreakBefore: boolean;
    pageBreakAfter: boolean;
    keepTogether: boolean;
    repeatHeaderOnNewPage: boolean;
    showItemCount: boolean;
    headerStyle?: StyleDef;
    footerStyle?: StyleDef;
    rules: ConditionalRuleDef[];
}

export interface TableColumnDef {
    field?: string;
    expression?: string;
    caption?: string;
    widthMm?: number;
    widthPercent?: number;
    align?: HorizontalAlign;
    format?: string;
    style?: StyleDef;
    headerStyle?: StyleDef;
    visible: boolean;
    rules: ConditionalRuleDef[];
}

interface ElementBase {
    id: string;
    name?: string;
    xMm: number;
    yMm: number;
    widthMm: number;
    heightMm: number;
    visible: boolean;
    style?: StyleDef;
    rules: ConditionalRuleDef[];
    direction?: TextDirection;
}

export interface TextElement extends ElementBase {
    type: 'text';
    text: string;
    dataSetKey?: string;
}

export interface FieldElement extends ElementBase {
    type: 'field';
    field?: string;
    expression?: string;
    dataSetKey?: string;
    format?: string;
    nullText?: string;
}

export interface ImageElement extends ElementBase {
    type: 'image';
    source?: string;
    field?: string;
    dataSetKey?: string;
    fit: ImageFit;
}

export interface LineElement extends ElementBase {
    type: 'line';
    orientation: LineOrientation;
    thicknessPt: number;
    color: string;
}

export interface BoxElement extends ElementBase {
    type: 'box';
    cornerRadiusMm: number;
}

export interface PageNumberElement extends ElementBase {
    type: 'pageNumber';
    template: string;
}

export interface TableElement extends ElementBase {
    type: 'table';
    dataSetKey: string;
    columns: TableColumnDef[];
    /** Ordered: index 0 is the outermost level. Reordering reorders the nesting. */
    groups: GroupDef[];
    grandTotals: AggregateDef[];
    grandTotalLabel?: string;
    showColumnHeaders: boolean;
    repeatHeaderOnEachPage: boolean;
    headerStyle?: StyleDef;
    rowStyle?: StyleDef;
    alternateRowStyle?: StyleDef;
    grandTotalStyle?: StyleDef;
    cellBorder?: BorderDef;
    rowRules: ConditionalRuleDef[];
    emptyText?: string;
}

export type ReportElement =
    | TextElement | FieldElement | ImageElement | LineElement
    | BoxElement | PageNumberElement | TableElement;

export interface BandDef {
    kind: BandKind;
    heightMm: number;
    visible: boolean;
    style?: StyleDef;
    elements: ReportElement[];
    rules: ConditionalRuleDef[];
}

export interface ReportDefinition {
    schemaVersion: string;
    name: string;
    title?: string;
    description?: string;
    page: PageSetupDef;
    parameters: ReportParameter[];
    dataSets: DataSetDef[];
    bands: BandDef[];
    defaultStyle?: StyleDef;
}

/** Bands render in this order, and the designer lists them the same way. */
export const BAND_ORDER: BandKind[] =
    ['ReportHeader', 'PageHeader', 'Detail', 'PageFooter', 'ReportFooter'];

export const BAND_LABELS: Record<BandKind, string> = {
    ReportHeader: 'Report Header',
    PageHeader: 'Page Header',
    Detail: 'Detail',
    PageFooter: 'Page Footer',
    ReportFooter: 'Report Footer'
};

/**
 * Explains what each band is for. Worth surfacing in the UI, because the once-per-report versus
 * once-per-page distinction is the thing people get wrong when they first meet a banded designer.
 */
export const BAND_HINTS: Record<BandKind, string> = {
    ReportHeader: 'Printed once, at the very start of the report.',
    PageHeader: 'Repeated at the top of every page. Page numbers only work here or in the page footer.',
    Detail: 'The body of the report. Tables live here and grow across pages.',
    PageFooter: 'Repeated at the bottom of every page.',
    ReportFooter: 'Printed once, at the very end of the report.'
};

export const PAPER_SIZES_MM: Record<Exclude<PaperSize, 'Custom'>, [number, number]> = {
    A3: [297, 420],
    A4: [210, 297],
    A5: [148, 210],
    Letter: [215.9, 279.4],
    Legal: [215.9, 355.6]
};

/** Page dimensions with orientation applied, mirroring PageSetupDef.WidthMm/HeightMm on the server. */
export function pageSizeMm(page: PageSetupDef): { widthMm: number; heightMm: number } {
    const [w, h] = page.paperSize === 'Custom'
        ? [page.customWidthMm ?? 210, page.customHeightMm ?? 297]
        : PAPER_SIZES_MM[page.paperSize];

    return page.orientation === 'Portrait'
        ? { widthMm: w, heightMm: h }
        : { widthMm: h, heightMm: w };
}

export function contentWidthMm(page: PageSetupDef): number {
    return pageSizeMm(page).widthMm - page.margins.leftMm - page.margins.rightMm;
}

export const AGGREGATE_FUNCTIONS: AggregateFunction[] =
    ['Sum', 'Avg', 'Min', 'Max', 'Count', 'CountDistinct', 'First', 'Last'];

export const PARAMETER_TYPES: ParameterType[] =
    ['Text', 'Int', 'Decimal', 'Date', 'DateRange', 'Bool', 'Select', 'MultiSelect'];
