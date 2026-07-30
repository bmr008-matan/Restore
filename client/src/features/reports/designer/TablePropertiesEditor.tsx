import { useState } from 'react';
import {
    Badge, Box, Button, Checkbox, Dialog, DialogActions, DialogContent, DialogTitle,
    FormControlLabel, IconButton, MenuItem, Stack, TextField, Tooltip, Typography
} from '@mui/material';
import RuleIcon from '@mui/icons-material/Rule';
import ArrowUpwardIcon from '@mui/icons-material/ArrowUpward';
import ArrowDownwardIcon from '@mui/icons-material/ArrowDownward';
import DeleteOutlineIcon from '@mui/icons-material/DeleteOutline';
import AddIcon from '@mui/icons-material/Add';
import VisibilityIcon from '@mui/icons-material/Visibility';
import VisibilityOffIcon from '@mui/icons-material/VisibilityOff';
import {
    DataSetDef, HorizontalAlign, ReportDefinition, TableColumnDef, TableElement
} from '../types/reportDefinition';
import ConditionalRulesEditor from './ConditionalRulesEditor';

interface Props {
    table: TableElement;
    definition: ReportDefinition;
    onChange: (patch: Partial<TableElement>) => void;
}

const ALIGNMENTS: HorizontalAlign[] = ['Left', 'Center', 'Right', 'Start', 'End'];

/** Column list and table-wide options. Grouping lives in its own editor. */
export default function TablePropertiesEditor({ table, definition, onChange }: Props) {
    /** Index of the column whose rules are open, or null. */
    const [rulesFor, setRulesFor] = useState<number | null>(null);

    const dataSet: DataSetDef | undefined =
        definition.dataSets.find(d => d.key.toLowerCase() === table.dataSetKey.toLowerCase());

    const setColumns = (columns: TableColumnDef[]) => onChange({ columns });

    const addColumn = () => {
        // Offer the first field not already shown, since adding a duplicate is rarely what is wanted.
        const used = new Set(table.columns.map(c => c.field));
        const field = dataSet?.fields.find(f => !used.has(f.name)) ?? dataSet?.fields[0];

        setColumns([...table.columns, {
            field: field?.name,
            caption: field?.caption ?? field?.name,
            format: field?.format,
            visible: true,
            rules: []
        }]);
    };

    const patchColumn = (index: number, patch: Partial<TableColumnDef>) =>
        setColumns(table.columns.map((c, i) => (i === index ? { ...c, ...patch } : c)));

    const move = (index: number, delta: number) => {
        const target = index + delta;
        if (target < 0 || target >= table.columns.length) return;

        const next = [...table.columns];
        [next[index], next[target]] = [next[target], next[index]];
        setColumns(next);
    };

    const totalPercent = table.columns
        .filter(c => c.visible)
        .reduce((sum, c) => sum + (c.widthPercent ?? 0), 0);

    return (
        <Box>
            <TextField
                select
                label="Data set"
                size="small"
                value={table.dataSetKey}
                onChange={e => onChange({ dataSetKey: e.target.value })}
                error={!table.dataSetKey}
                helperText={table.dataSetKey ? undefined : 'A table needs a data set.'}
                fullWidth
                sx={{ mb: 1.5 }}
            >
                {definition.dataSets.map(d => (
                    <MenuItem key={d.key} value={d.key}>{d.key}</MenuItem>
                ))}
            </TextField>

            <Stack direction="row" alignItems="center" sx={{ mb: 0.5 }}>
                <Typography variant="subtitle2" sx={{ flexGrow: 1 }}>
                    Columns ({table.columns.length})
                </Typography>
                <Button
                    size="small"
                    startIcon={<AddIcon />}
                    onClick={addColumn}
                    disabled={!dataSet || dataSet.fields.length === 0}
                >
                    Add
                </Button>
            </Stack>

            {table.columns.length === 0 && (
                <Typography variant="caption" color="error">
                    A table needs at least one column to save.
                </Typography>
            )}

            {totalPercent > 100.5 && (
                <Typography variant="caption" sx={{ display: 'block', color: 'warning.main' }}>
                    Widths add up to {totalPercent.toFixed(0)}%, so the table will overflow.
                </Typography>
            )}

            {/* Each column gets two rows. Fitting field, caption, width, align, format and five actions
                onto one line in a 380px panel collapsed every input to an unusable sliver. */}
            <Stack spacing={1} sx={{ mt: 1 }}>
                {table.columns.map((column, index) => (
                    <Box
                        key={index}
                        sx={{ p: 0.75, borderRadius: 1, backgroundColor: 'action.hover' }}
                    >
                        <Stack direction="row" spacing={0.5} sx={{ mb: 0.75 }}>
                            <TextField
                                select
                                size="small"
                                label="Field"
                                value={column.field ?? ''}
                                onChange={e => patchColumn(index, { field: e.target.value || undefined })}
                                sx={{ width: 120 }}
                            >
                                <MenuItem value="">(expression)</MenuItem>
                                {(dataSet?.fields ?? []).map(f => (
                                    <MenuItem key={f.name} value={f.name}>{f.name}</MenuItem>
                                ))}
                            </TextField>

                            <TextField
                                size="small"
                                label="Caption"
                                value={column.caption ?? ''}
                                onChange={e => patchColumn(index, { caption: e.target.value || undefined })}
                                sx={{ flexGrow: 1, minWidth: 80 }}
                            />
                        </Stack>

                        <Stack direction="row" spacing={0.5} alignItems="center">
                            <TextField
                                size="small"
                                label="Width %"
                                type="number"
                                value={column.widthPercent ?? ''}
                                onChange={e => patchColumn(index, {
                                    widthPercent: e.target.value ? Number(e.target.value) : undefined
                                })}
                                sx={{ width: 78 }}
                            />

                            <TextField
                                select
                                size="small"
                                label="Align"
                                value={column.align ?? ''}
                                onChange={e => patchColumn(index, {
                                    align: (e.target.value || undefined) as HorizontalAlign | undefined
                                })}
                                sx={{ width: 84 }}
                            >
                                <MenuItem value="">auto</MenuItem>
                                {ALIGNMENTS.map(a => <MenuItem key={a} value={a}>{a}</MenuItem>)}
                            </TextField>

                            <TextField
                                size="small"
                                label="Format"
                                value={column.format ?? ''}
                                onChange={e => patchColumn(index, { format: e.target.value || undefined })}
                                sx={{ flexGrow: 1, minWidth: 70 }}
                            />
                        </Stack>

                        <Stack direction="row" spacing={0} alignItems="center" justifyContent="flex-end">
                            {/* Per-column conditional rules, evaluated per row: this is where
                                "make the amount red when it exceeds X" lives. */}
                            <Tooltip title="Conditional rules for this column">
                                <IconButton size="small" onClick={() => setRulesFor(index)}>
                                    <Badge
                                        badgeContent={column.rules.length}
                                        color="primary"
                                        overlap="circular"
                                        sx={{ '& .MuiBadge-badge': { fontSize: 9, height: 14, minWidth: 14 } }}
                                    >
                                        <RuleIcon fontSize="small" />
                                    </Badge>
                                </IconButton>
                            </Tooltip>

                            <Tooltip title={column.visible ? 'Hide column' : 'Show column'}>
                                <IconButton size="small" onClick={() => patchColumn(index, { visible: !column.visible })}>
                                    {column.visible
                                        ? <VisibilityIcon fontSize="small" />
                                        : <VisibilityOffIcon fontSize="small" />}
                                </IconButton>
                            </Tooltip>

                            {/* Disabled buttons fire no events, so each tooltip needs a wrapper element. */}
                            <Tooltip title="Move left">
                                <span>
                                    <IconButton size="small" onClick={() => move(index, -1)} disabled={index === 0}>
                                        <ArrowUpwardIcon fontSize="small" sx={{ transform: 'rotate(-90deg)' }} />
                                    </IconButton>
                                </span>
                            </Tooltip>
                            <Tooltip title="Move right">
                                <span>
                                    <IconButton
                                        size="small"
                                        onClick={() => move(index, 1)}
                                        disabled={index === table.columns.length - 1}
                                    >
                                        <ArrowDownwardIcon fontSize="small" sx={{ transform: 'rotate(-90deg)' }} />
                                    </IconButton>
                                </span>
                            </Tooltip>
                            <Tooltip title="Remove column">
                                <IconButton
                                    size="small"
                                    onClick={() => setColumns(table.columns.filter((_, i) => i !== index))}
                                >
                                    <DeleteOutlineIcon fontSize="small" />
                                </IconButton>
                            </Tooltip>
                        </Stack>
                    </Box>
                ))}
            </Stack>

            <Box sx={{ mt: 2 }}>
                <Typography variant="subtitle2">Table options</Typography>

                <FormControlLabel
                    control={
                        <Checkbox
                            size="small"
                            checked={table.showColumnHeaders}
                            onChange={e => onChange({ showColumnHeaders: e.target.checked })}
                        />
                    }
                    label={<Typography variant="caption">Show column headers</Typography>}
                />
                <FormControlLabel
                    control={
                        <Checkbox
                            size="small"
                            checked={table.repeatHeaderOnEachPage}
                            onChange={e => onChange({ repeatHeaderOnEachPage: e.target.checked })}
                        />
                    }
                    label={
                        <Typography variant="caption">
                            Repeat headers on every page
                        </Typography>
                    }
                />

                <TextField
                    label="Text when there is no data"
                    size="small"
                    value={table.emptyText ?? ''}
                    onChange={e => onChange({ emptyText: e.target.value || undefined })}
                    fullWidth
                    sx={{ mt: 1 }}
                />

                <Box sx={{ mt: 2 }}>
                    <Typography variant="subtitle2">Row rules</Typography>
                    <Typography variant="caption" color="text.secondary">
                        Applied to a whole detail row, for banding or highlighting.
                    </Typography>
                    <ConditionalRulesEditor
                        rules={table.rowRules}
                        dataSet={dataSet}
                        parameters={definition.parameters}
                        allowFields
                        onChange={rowRules => onChange({ rowRules })}
                    />
                </Box>
            </Box>

            <Dialog
                open={rulesFor !== null}
                onClose={() => setRulesFor(null)}
                maxWidth="sm"
                fullWidth
            >
                <DialogTitle>
                    Rules for column{' '}
                    {rulesFor !== null && (table.columns[rulesFor]?.caption ?? table.columns[rulesFor]?.field)}
                </DialogTitle>
                <DialogContent>
                    {rulesFor !== null && (
                        <ConditionalRulesEditor
                            rules={table.columns[rulesFor].rules}
                            dataSet={dataSet}
                            parameters={definition.parameters}
                            allowFields
                            onChange={rules => patchColumn(rulesFor, { rules })}
                        />
                    )}
                </DialogContent>
                <DialogActions>
                    <Button onClick={() => setRulesFor(null)} variant="contained">Done</Button>
                </DialogActions>
            </Dialog>
        </Box>
    );
}
