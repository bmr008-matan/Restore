import { useState } from 'react';
import {
    Box, Checkbox, Divider, FormControlLabel, MenuItem, Stack, Tab, Tabs, TextField, Typography
} from '@mui/material';
import {
    BAND_HINTS, BAND_LABELS, BandKind, DataSetDef, HorizontalAlign, ReportDefinition,
    ReportElement, StyleDef, TableElement, TextDirection
} from '../types/reportDefinition';
import { DesignerAction, findElement } from './state/designerReducer';
import TablePropertiesEditor from './TablePropertiesEditor';
import GroupLevelsEditor from './GroupLevelsEditor';
import ConditionalRulesEditor from './ConditionalRulesEditor';

interface Props {
    definition: ReportDefinition;
    selectedElementId: string | null;
    selectedBand: BandKind | null;
    dispatch: React.Dispatch<DesignerAction>;
}

const ALIGNMENTS: HorizontalAlign[] = ['Left', 'Center', 'Right', 'Justify', 'Start', 'End'];

/** Properties for whatever is selected: an element, or the band itself when nothing is. */
export default function PropertiesPanel({
    definition, selectedElementId, selectedBand, dispatch
}: Props) {
    const element = findElement(definition, selectedElementId);

    if (element) {
        return <ElementProperties element={element} definition={definition} dispatch={dispatch} />;
    }

    if (selectedBand) {
        return <BandProperties band={selectedBand} definition={definition} dispatch={dispatch} />;
    }

    return (
        <Box sx={{ p: 2 }}>
            <Typography variant="body2" color="text.secondary">
                Select an element or a band to edit its properties.
            </Typography>
        </Box>
    );
}

function ElementProperties({ element, definition, dispatch }: {
    element: ReportElement; definition: ReportDefinition; dispatch: React.Dispatch<DesignerAction>;
}) {
    const [tab, setTab] = useState(0);
    const isTable = element.type === 'table';

    const dataSetKey = 'dataSetKey' in element ? element.dataSetKey : undefined;
    const dataSet: DataSetDef | undefined = definition.dataSets.find(
        d => d.key.toLowerCase() === (dataSetKey ?? '').toLowerCase());

    const patch = (change: Partial<ReportElement>) =>
        dispatch({ type: 'patchElement', id: element.id, patch: change });

    const patchStyle = (change: Partial<StyleDef>) =>
        dispatch({ type: 'patchElementStyle', id: element.id, patch: change });

    // Tables get an extra Groups tab; everything else has three.
    const tabs = isTable ? ['Layout', 'Data', 'Groups', 'Style', 'Rules'] : ['Layout', 'Data', 'Style', 'Rules'];
    const label = tabs[tab];

    return (
        <Box sx={{ height: '100%', overflow: 'auto' }}>
            <Box sx={{ px: 1.5, pt: 1.5 }}>
                <Typography variant="subtitle2">
                    {element.type} · {element.name ?? element.id}
                </Typography>
            </Box>

            <Tabs
                value={tab}
                onChange={(_e, v) => setTab(v)}
                variant="scrollable"
                scrollButtons="auto"
                sx={{ minHeight: 36, '& .MuiTab-root': { minHeight: 36, fontSize: 12 } }}
            >
                {tabs.map(t => <Tab key={t} label={t} />)}
            </Tabs>

            <Divider />

            <Box sx={{ p: 1.5 }}>
                {label === 'Layout' && (
                    <Stack spacing={1.5}>
                        <TextField
                            label="Name"
                            size="small"
                            value={element.name ?? ''}
                            onChange={e => patch({ name: e.target.value || undefined })}
                            helperText="For your reference only."
                            fullWidth
                        />

                        <Stack direction="row" spacing={1}>
                            <NumberField label="X (mm)" value={element.xMm} onChange={v => patch({ xMm: v })} />
                            <NumberField label="Y (mm)" value={element.yMm} onChange={v => patch({ yMm: v })} />
                        </Stack>
                        <Stack direction="row" spacing={1}>
                            <NumberField label="Width (mm)" value={element.widthMm} onChange={v => patch({ widthMm: v })} />
                            <NumberField label="Height (mm)" value={element.heightMm} onChange={v => patch({ heightMm: v })} />
                        </Stack>

                        <FormControlLabel
                            control={
                                <Checkbox
                                    size="small"
                                    checked={element.visible}
                                    onChange={e => patch({ visible: e.target.checked })}
                                />
                            }
                            label={<Typography variant="caption">Visible</Typography>}
                        />

                        <TextField
                            select
                            label="Direction"
                            size="small"
                            value={element.direction ?? ''}
                            onChange={e => patch({ direction: (e.target.value || undefined) as TextDirection | undefined })}
                            helperText="Override for an LTR code inside an RTL report."
                            fullWidth
                        >
                            <MenuItem value="">inherit from report</MenuItem>
                            <MenuItem value="Ltr">Left to right</MenuItem>
                            <MenuItem value="Rtl">Right to left</MenuItem>
                        </TextField>
                    </Stack>
                )}

                {label === 'Data' && <DataTab element={element} definition={definition} patch={patch} />}

                {label === 'Groups' && isTable && (
                    <GroupLevelsEditor
                        table={element as TableElement}
                        dataSet={dataSet}
                        onChange={p => dispatch({ type: 'patchTable', id: element.id, patch: p })}
                    />
                )}

                {label === 'Style' && <StyleTab style={element.style} onChange={patchStyle} />}

                {label === 'Rules' && (
                    <ConditionalRulesEditor
                        rules={element.rules}
                        dataSet={dataSet}
                        parameters={definition.parameters}
                        allowFields
                        onChange={rules => dispatch({ type: 'setElementRules', id: element.id, rules })}
                    />
                )}
            </Box>
        </Box>
    );
}

/** Type-specific bindings. Kept separate so the layout tab stays the same for every element. */
function DataTab({ element, definition, patch }: {
    element: ReportElement;
    definition: ReportDefinition;
    patch: (change: Partial<ReportElement>) => void;
}) {
    if (element.type === 'table') {
        return (
            <TablePropertiesEditor
                table={element}
                definition={definition}
                onChange={p => patch(p as Partial<ReportElement>)}
            />
        );
    }

    const dataSetPicker = (value: string | undefined, onPick: (key: string | undefined) => void) => (
        <TextField
            select
            label="Data set"
            size="small"
            value={value ?? ''}
            onChange={e => onPick(e.target.value || undefined)}
            helperText="Resolves against the first row of this data set."
            fullWidth
        >
            <MenuItem value="">none</MenuItem>
            {definition.dataSets.map(d => <MenuItem key={d.key} value={d.key}>{d.key}</MenuItem>)}
        </TextField>
    );

    const fieldsOf = (key?: string) =>
        definition.dataSets.find(d => d.key.toLowerCase() === (key ?? '').toLowerCase())?.fields ?? [];

    switch (element.type) {
        case 'text':
            return (
                <Stack spacing={1.5}>
                    <TextField
                        label="Text"
                        size="small"
                        value={element.text}
                        onChange={e => patch({ text: e.target.value } as Partial<ReportElement>)}
                        helperText="Tokens: {ReportTitle} {Date} {CurrentUser} {@param} {field}"
                        multiline
                        maxRows={4}
                        fullWidth
                    />
                    {dataSetPicker(element.dataSetKey,
                        key => patch({ dataSetKey: key } as Partial<ReportElement>))}
                </Stack>
            );

        case 'field':
            return (
                <Stack spacing={1.5}>
                    {dataSetPicker(element.dataSetKey,
                        key => patch({ dataSetKey: key } as Partial<ReportElement>))}
                    <TextField
                        select
                        label="Field"
                        size="small"
                        value={element.field ?? ''}
                        onChange={e => patch({ field: e.target.value || undefined } as Partial<ReportElement>)}
                        fullWidth
                    >
                        <MenuItem value="">(use expression)</MenuItem>
                        {fieldsOf(element.dataSetKey).map(f => (
                            <MenuItem key={f.name} value={f.name}>{f.name}</MenuItem>
                        ))}
                    </TextField>
                    <TextField
                        label="Expression"
                        size="small"
                        value={element.expression ?? ''}
                        onChange={e => patch({ expression: e.target.value || undefined } as Partial<ReportElement>)}
                        placeholder="Fields.qty * Fields.price"
                        fullWidth
                    />
                    <TextField
                        label="Format"
                        size="small"
                        value={element.format ?? ''}
                        onChange={e => patch({ format: e.target.value || undefined } as Partial<ReportElement>)}
                        placeholder="#,##0.00"
                        fullWidth
                    />
                    <TextField
                        label="Text when empty"
                        size="small"
                        value={element.nullText ?? ''}
                        onChange={e => patch({ nullText: e.target.value || undefined } as Partial<ReportElement>)}
                        fullWidth
                    />
                </Stack>
            );

        case 'pageNumber':
            return (
                <TextField
                    label="Template"
                    size="small"
                    value={element.template}
                    onChange={e => patch({ template: e.target.value } as Partial<ReportElement>)}
                    helperText="{PageNumber} and {TotalPages} are filled in by the renderer."
                    fullWidth
                />
            );

        case 'image':
            return (
                <Stack spacing={1.5}>
                    <TextField
                        label="Source"
                        size="small"
                        value={element.source ?? ''}
                        onChange={e => patch({ source: e.target.value || undefined } as Partial<ReportElement>)}
                        helperText="Path, or a data: URI. Page headers and footers require a data: URI."
                        fullWidth
                    />
                    {dataSetPicker(element.dataSetKey,
                        key => patch({ dataSetKey: key } as Partial<ReportElement>))}
                    <TextField
                        select
                        label="Fit"
                        size="small"
                        value={element.fit}
                        onChange={e => patch({ fit: e.target.value } as Partial<ReportElement>)}
                        fullWidth
                    >
                        {['Contain', 'Cover', 'Fill', 'None'].map(f => (
                            <MenuItem key={f} value={f}>{f}</MenuItem>
                        ))}
                    </TextField>
                </Stack>
            );

        case 'line':
            return (
                <Stack spacing={1.5}>
                    <TextField
                        select
                        label="Orientation"
                        size="small"
                        value={element.orientation}
                        onChange={e => patch({ orientation: e.target.value } as Partial<ReportElement>)}
                        fullWidth
                    >
                        <MenuItem value="Horizontal">Horizontal</MenuItem>
                        <MenuItem value="Vertical">Vertical</MenuItem>
                    </TextField>
                    <NumberField
                        label="Thickness (pt)"
                        value={element.thicknessPt}
                        onChange={v => patch({ thicknessPt: v } as Partial<ReportElement>)}
                    />
                    <TextField
                        label="Colour"
                        size="small"
                        value={element.color}
                        onChange={e => patch({ color: e.target.value } as Partial<ReportElement>)}
                        fullWidth
                    />
                </Stack>
            );

        default:
            return (
                <Typography variant="caption" color="text.secondary">
                    This element has no data bindings.
                </Typography>
            );
    }
}

function StyleTab({ style, onChange }: {
    style?: StyleDef; onChange: (patch: Partial<StyleDef>) => void;
}) {
    return (
        <Stack spacing={1.5}>
            <Stack direction="row" spacing={1}>
                <TextField
                    label="Font size (pt)"
                    size="small"
                    type="number"
                    value={style?.fontSizePt ?? ''}
                    onChange={e => onChange({ fontSizePt: e.target.value ? Number(e.target.value) : undefined })}
                    sx={{ width: 120 }}
                />
                <TextField
                    label="Font family"
                    size="small"
                    value={style?.fontFamily ?? ''}
                    onChange={e => onChange({ fontFamily: e.target.value || undefined })}
                    sx={{ flexGrow: 1 }}
                />
            </Stack>

            <Box>
                <FormControlLabel
                    control={<Checkbox size="small" checked={!!style?.bold}
                        onChange={e => onChange({ bold: e.target.checked || undefined })} />}
                    label={<Typography variant="caption">Bold</Typography>}
                />
                <FormControlLabel
                    control={<Checkbox size="small" checked={!!style?.italic}
                        onChange={e => onChange({ italic: e.target.checked || undefined })} />}
                    label={<Typography variant="caption">Italic</Typography>}
                />
                <FormControlLabel
                    control={<Checkbox size="small" checked={!!style?.underline}
                        onChange={e => onChange({ underline: e.target.checked || undefined })} />}
                    label={<Typography variant="caption">Underline</Typography>}
                />
            </Box>

            <Stack direction="row" spacing={1}>
                <TextField
                    label="Text colour"
                    size="small"
                    value={style?.color ?? ''}
                    onChange={e => onChange({ color: e.target.value || undefined })}
                    placeholder="#000000"
                    sx={{ flexGrow: 1 }}
                />
                <TextField
                    label="Fill"
                    size="small"
                    value={style?.backColor ?? ''}
                    onChange={e => onChange({ backColor: e.target.value || undefined })}
                    placeholder="#FFFFFF"
                    sx={{ flexGrow: 1 }}
                />
            </Stack>

            <Stack direction="row" spacing={1}>
                <TextField
                    select
                    label="Align"
                    size="small"
                    value={style?.align ?? ''}
                    onChange={e => onChange({ align: (e.target.value || undefined) as HorizontalAlign | undefined })}
                    sx={{ flexGrow: 1 }}
                >
                    <MenuItem value="">inherit</MenuItem>
                    {ALIGNMENTS.map(a => <MenuItem key={a} value={a}>{a}</MenuItem>)}
                </TextField>
                <TextField
                    label="Format"
                    size="small"
                    value={style?.format ?? ''}
                    onChange={e => onChange({ format: e.target.value || undefined })}
                    placeholder="#,##0.00"
                    sx={{ flexGrow: 1 }}
                />
            </Stack>
        </Stack>
    );
}

function BandProperties({ band, definition, dispatch }: {
    band: BandKind; definition: ReportDefinition; dispatch: React.Dispatch<DesignerAction>;
}) {
    const def = definition.bands.find(b => b.kind === band);
    if (!def) return null;

    return (
        <Box sx={{ p: 1.5, height: '100%', overflow: 'auto' }}>
            <Typography variant="subtitle2">{BAND_LABELS[band]}</Typography>
            <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mb: 1.5 }}>
                {BAND_HINTS[band]}
            </Typography>

            <Stack spacing={1.5}>
                <NumberField
                    label="Height (mm)"
                    value={def.heightMm}
                    onChange={v => dispatch({ type: 'patchBand', band, patch: { heightMm: v } })}
                />

                <FormControlLabel
                    control={
                        <Checkbox
                            size="small"
                            checked={def.visible}
                            onChange={e => dispatch({ type: 'patchBand', band, patch: { visible: e.target.checked } })}
                        />
                    }
                    label={<Typography variant="caption">Visible</Typography>}
                />

                <Divider />

                <ConditionalRulesEditor
                    rules={def.rules}
                    parameters={definition.parameters}
                    // A band has no current row, so Fields would always be empty here.
                    allowFields={false}
                    onChange={rules => dispatch({ type: 'patchBand', band, patch: { rules } })}
                />
            </Stack>
        </Box>
    );
}

function NumberField({ label, value, onChange }: {
    label: string; value: number; onChange: (value: number) => void;
}) {
    return (
        <TextField
            label={label}
            size="small"
            type="number"
            value={value}
            onChange={e => onChange(Number(e.target.value) || 0)}
            inputProps={{ step: 1 }}
            sx={{ flexGrow: 1 }}
        />
    );
}
