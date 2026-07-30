import {
    BandDef, BandKind, ConditionalRuleDef, ElementType, ReportDefinition, ReportElement,
    ReportParameter, StyleDef, TableElement, PageSetupDef, DataSetDef, BAND_ORDER
} from '../../types/reportDefinition';

/**
 * Designer state. The `ReportDefinition` is the single source of truth — exactly the object that gets
 * PUT to the server and exactly what the preview renders — so there is no second layout model anywhere
 * in the client that could drift from it.
 *
 * Undo/redo is implemented by keeping whole definition snapshots rather than inverse operations. A
 * definition is a few tens of kilobytes at most, so the memory cost is irrelevant next to the
 * correctness win: every edit is undoable without anyone having to write and maintain its inverse.
 */
export interface DesignerState {
    definition: ReportDefinition;

    /** Id of the selected element, or null when a band or nothing is selected. */
    selectedElementId: string | null;

    /** Selected band. An element selection implies its band. */
    selectedBand: BandKind | null;

    past: ReportDefinition[];
    future: ReportDefinition[];

    /** True when there are unsaved edits. Reset by the caller after a successful save. */
    dirty: boolean;

    /** Canvas zoom, 1 = 100%. */
    zoom: number;

    /** Snap grid in mm. Zero disables snapping. */
    gridMm: number;
}

const HISTORY_LIMIT = 60;

export type DesignerAction =
    | { type: 'load'; definition: ReportDefinition }
    | { type: 'saved' }
    | { type: 'undo' }
    | { type: 'redo' }
    | { type: 'selectElement'; id: string | null; band?: BandKind }
    | { type: 'selectBand'; band: BandKind | null }
    | { type: 'setZoom'; zoom: number }
    | { type: 'setGrid'; gridMm: number }
    | { type: 'patchReport'; patch: Partial<ReportDefinition> }
    | { type: 'patchPage'; patch: Partial<PageSetupDef> }
    | { type: 'patchBand'; band: BandKind; patch: Partial<BandDef> }
    | { type: 'addElement'; band: BandKind; element: ReportElement }
    | { type: 'patchElement'; id: string; patch: Partial<ReportElement> }
    | { type: 'patchElementStyle'; id: string; patch: Partial<StyleDef> }
    | { type: 'moveElement'; id: string; xMm: number; yMm: number }
    | { type: 'resizeElement'; id: string; xMm: number; yMm: number; widthMm: number; heightMm: number }
    | { type: 'removeElement'; id: string }
    | { type: 'duplicateElement'; id: string }
    | { type: 'moveElementToBand'; id: string; band: BandKind }
    | { type: 'setElementRules'; id: string; rules: ConditionalRuleDef[] }
    | { type: 'patchTable'; id: string; patch: Partial<TableElement> }
    | { type: 'setParameters'; parameters: ReportParameter[] }
    | { type: 'setDataSets'; dataSets: DataSetDef[] };

/** Actions that only change what is selected or how the canvas looks never touch history. */
const NON_HISTORY: DesignerAction['type'][] =
    ['load', 'saved', 'undo', 'redo', 'selectElement', 'selectBand', 'setZoom', 'setGrid'];

export function createInitialState(definition: ReportDefinition): DesignerState {
    return {
        definition,
        selectedElementId: null,
        selectedBand: null,
        past: [],
        future: [],
        dirty: false,
        zoom: 1,
        gridMm: 1
    };
}

export function designerReducer(state: DesignerState, action: DesignerAction): DesignerState {
    switch (action.type) {
        case 'load':
            return createInitialState(action.definition);

        case 'saved':
            return { ...state, dirty: false };

        case 'undo': {
            if (state.past.length === 0) return state;
            const previous = state.past[state.past.length - 1];
            return {
                ...state,
                definition: previous,
                past: state.past.slice(0, -1),
                future: [state.definition, ...state.future],
                dirty: true,
                // A selected element may not exist in the restored definition.
                selectedElementId: findElement(previous, state.selectedElementId) ? state.selectedElementId : null
            };
        }

        case 'redo': {
            if (state.future.length === 0) return state;
            const next = state.future[0];
            return {
                ...state,
                definition: next,
                past: [...state.past, state.definition],
                future: state.future.slice(1),
                dirty: true,
                selectedElementId: findElement(next, state.selectedElementId) ? state.selectedElementId : null
            };
        }

        case 'selectElement':
            return {
                ...state,
                selectedElementId: action.id,
                selectedBand: action.band ?? (action.id ? bandOf(state.definition, action.id) : state.selectedBand)
            };

        case 'selectBand':
            return { ...state, selectedBand: action.band, selectedElementId: null };

        case 'setZoom':
            return { ...state, zoom: clamp(action.zoom, 0.25, 3) };

        case 'setGrid':
            return { ...state, gridMm: Math.max(0, action.gridMm) };

        default:
            return applyEdit(state, action);
    }
}

/** Wraps every mutating action so history and the dirty flag are handled in exactly one place. */
function applyEdit(state: DesignerState, action: DesignerAction): DesignerState {
    const definition = edit(state.definition, action, state);
    if (definition === state.definition) return state;

    const recordHistory = !NON_HISTORY.includes(action.type);

    return {
        ...state,
        definition,
        past: recordHistory
            ? [...state.past, state.definition].slice(-HISTORY_LIMIT)
            : state.past,
        // Any new edit invalidates the redo stack, as in every editor.
        future: recordHistory ? [] : state.future,
        dirty: true,
        selectedElementId: nextSelection(state, action)
    };
}

function nextSelection(state: DesignerState, action: DesignerAction): string | null {
    if (action.type === 'addElement') return action.element.id;
    if (action.type === 'removeElement' && state.selectedElementId === action.id) return null;
    return state.selectedElementId;
}

/**
 * Produces the next definition. Returns the same object when nothing changed, so `applyEdit` can skip
 * pushing a no-op onto the undo stack — dragging an element one pixel and back should not cost two undos.
 */
function edit(
    definition: ReportDefinition, action: DesignerAction, state: DesignerState): ReportDefinition {
    switch (action.type) {
        case 'patchReport':
            return { ...definition, ...action.patch };

        case 'patchPage':
            return { ...definition, page: { ...definition.page, ...action.patch } };

        case 'patchBand':
            return mapBand(definition, action.band, band => ({ ...band, ...action.patch }));

        case 'addElement':
            return mapBand(definition, action.band, band => ({
                ...band,
                elements: [...band.elements, action.element]
            }));

        case 'patchElement':
            return mapElement(definition, action.id, element => ({ ...element, ...action.patch } as ReportElement));

        case 'patchElementStyle':
            return mapElement(definition, action.id, element => ({
                ...element,
                style: pruneStyle({ ...element.style, ...action.patch })
            }));

        case 'moveElement': {
            const existing = findElement(definition, action.id);
            if (existing && existing.xMm === action.xMm && existing.yMm === action.yMm) return definition;

            return mapElement(definition, action.id, element => ({
                ...element,
                xMm: snap(action.xMm, state.gridMm),
                yMm: snap(action.yMm, state.gridMm)
            }));
        }

        case 'resizeElement':
            return mapElement(definition, action.id, element => ({
                ...element,
                xMm: snap(action.xMm, state.gridMm),
                yMm: snap(action.yMm, state.gridMm),
                // A zero or negative size would fail server validation, so it is clamped here.
                widthMm: Math.max(2, snap(action.widthMm, state.gridMm)),
                heightMm: Math.max(2, snap(action.heightMm, state.gridMm))
            }));

        case 'removeElement':
            return {
                ...definition,
                bands: definition.bands.map(band => ({
                    ...band,
                    elements: band.elements.filter(e => e.id !== action.id)
                }))
            };

        case 'duplicateElement': {
            const source = findElement(definition, action.id);
            const band = bandOf(definition, action.id);
            if (!source || !band) return definition;

            const copy: ReportElement = {
                ...source,
                id: newElementId(),
                name: source.name ? `${source.name} copy` : undefined,
                // Offset so the copy is visibly distinct rather than exactly on top of the original.
                xMm: source.xMm + 4,
                yMm: source.yMm + 4
            };

            return mapBand(definition, band, b => ({ ...b, elements: [...b.elements, copy] }));
        }

        case 'moveElementToBand': {
            const source = findElement(definition, action.id);
            const current = bandOf(definition, action.id);
            if (!source || !current || current === action.band) return definition;

            return {
                ...definition,
                bands: definition.bands.map(band => {
                    if (band.kind === current) {
                        return { ...band, elements: band.elements.filter(e => e.id !== action.id) };
                    }
                    if (band.kind === action.band) {
                        return { ...band, elements: [...band.elements, source] };
                    }
                    return band;
                })
            };
        }

        case 'setElementRules':
            return mapElement(definition, action.id, element => ({ ...element, rules: action.rules }));

        case 'patchTable':
            return mapElement(definition, action.id, element =>
                element.type === 'table' ? { ...element, ...action.patch } as TableElement : element);

        case 'setParameters':
            return { ...definition, parameters: action.parameters };

        case 'setDataSets':
            return { ...definition, dataSets: action.dataSets };

        default:
            return definition;
    }
}

function mapBand(
    definition: ReportDefinition, kind: BandKind, project: (band: BandDef) => BandDef): ReportDefinition {
    return {
        ...definition,
        bands: definition.bands.map(band => (band.kind === kind ? project(band) : band))
    };
}

function mapElement(
    definition: ReportDefinition,
    id: string,
    project: (element: ReportElement) => ReportElement): ReportDefinition {
    return {
        ...definition,
        bands: definition.bands.map(band => ({
            ...band,
            elements: band.elements.map(element => (element.id === id ? project(element) : element))
        }))
    };
}

export function findElement(definition: ReportDefinition, id: string | null): ReportElement | undefined {
    if (!id) return undefined;
    for (const band of definition.bands) {
        const found = band.elements.find(e => e.id === id);
        if (found) return found;
    }
    return undefined;
}

export function bandOf(definition: ReportDefinition, id: string): BandKind | null {
    for (const band of definition.bands) {
        if (band.elements.some(e => e.id === id)) return band.kind;
    }
    return null;
}

/** Bands in render order, creating any the definition is missing so the canvas always shows all five. */
export function orderedBands(definition: ReportDefinition): BandDef[] {
    return BAND_ORDER.map(kind =>
        definition.bands.find(b => b.kind === kind) ?? {
            kind,
            heightMm: kind === 'Detail' ? 40 : 12,
            visible: true,
            elements: [],
            rules: []
        });
}

function snap(value: number, gridMm: number): number {
    if (gridMm <= 0) return round(value);
    return round(Math.round(value / gridMm) * gridMm);
}

/** Keeps stored coordinates to a sane precision instead of accumulating float noise from dragging. */
function round(value: number): number {
    return Math.round(value * 100) / 100;
}

function clamp(value: number, min: number, max: number): number {
    return Math.min(max, Math.max(min, value));
}

/**
 * Drops keys whose value is undefined or empty. Without this, clearing a colour in the properties panel
 * would store `""`, which the server treats as a value and rejects as an invalid colour rather than
 * inheriting.
 */
function pruneStyle(style: StyleDef): StyleDef | undefined {
    const entries = Object.entries(style).filter(([, value]) =>
        value !== undefined && value !== null && value !== '');

    return entries.length === 0 ? undefined : Object.fromEntries(entries) as StyleDef;
}

/** Short, collision-resistant enough for element ids within one definition. */
export function newElementId(): string {
    return Math.random().toString(36).slice(2, 10);
}

/** A new element of the requested type, with defaults that are immediately valid on the server. */
export function createElement(type: ElementType, xMm = 4, yMm = 4): ReportElement {
    const base = {
        id: newElementId(),
        xMm,
        yMm,
        visible: true,
        rules: [] as ConditionalRuleDef[]
    };

    switch (type) {
        case 'text':
            return { ...base, type: 'text', text: 'Text', widthMm: 50, heightMm: 6 };
        case 'field':
            return { ...base, type: 'field', widthMm: 40, heightMm: 6 };
        case 'image':
            return { ...base, type: 'image', fit: 'Contain', widthMm: 30, heightMm: 20 };
        case 'line':
            return { ...base, type: 'line', orientation: 'Horizontal', thicknessPt: 0.5, color: '#000000', widthMm: 80, heightMm: 2 };
        case 'box':
            return { ...base, type: 'box', cornerRadiusMm: 0, widthMm: 50, heightMm: 20 };
        case 'pageNumber':
            return { ...base, type: 'pageNumber', template: 'Page {PageNumber} of {TotalPages}', widthMm: 50, heightMm: 6 };
        case 'table':
            return {
                ...base,
                type: 'table',
                dataSetKey: '',
                widthMm: 180,
                heightMm: 40,
                columns: [],
                groups: [],
                grandTotals: [],
                grandTotalLabel: 'Total',
                showColumnHeaders: true,
                repeatHeaderOnEachPage: true,
                rowRules: [],
                emptyText: 'No data'
            };
    }
}
