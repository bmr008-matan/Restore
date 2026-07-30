import { useState } from 'react';
import {
    Box, Button, Chip, Collapse, Dialog, DialogActions, DialogContent, DialogTitle, IconButton,
    List, ListItemButton, ListItemText, MenuItem, Stack, TextField, Tooltip, Typography
} from '@mui/material';
import ExpandMoreIcon from '@mui/icons-material/ExpandMore';
import ExpandLessIcon from '@mui/icons-material/ExpandLess';
import DeleteOutlineIcon from '@mui/icons-material/DeleteOutline';
import AddIcon from '@mui/icons-material/Add';
import PreviewIcon from '@mui/icons-material/Preview';
import { DataSetDef, ReportDefinition, FieldMeta } from '../types/reportDefinition';
import { TableSource, DataPreview } from '../api/reportsApi';
import reportsApi from '../api/reportsApi';
import { DesignerAction } from './state/designerReducer';

interface Props {
    definition: ReportDefinition;
    sources: TableSource[];
    dispatch: React.Dispatch<DesignerAction>;
}

/**
 * Data sets and their fields.
 *
 * A data set is chosen from the predefined table-id catalogue — the designer never sees or writes a
 * query, which is what keeps SQL out of the browser entirely. Field names are draggable onto a band,
 * where they become bound field elements.
 */
export default function DataPanel({ definition, sources, dispatch }: Props) {
    const [expanded, setExpanded] = useState<string | null>(definition.dataSets[0]?.key ?? null);
    const [addOpen, setAddOpen] = useState(false);
    const [preview, setPreview] = useState<{ key: string; data: DataPreview } | null>(null);

    const addDataSet = (source: TableSource, key: string) => {
        const dataSet: DataSetDef = {
            key,
            name: source.name,
            sourceKind: source.sourceKind,
            tableId: source.tableId,
            // Inputs the source declares are pre-created as literals, so what the source expects is
            // visible rather than something the author has to know to add.
            inputs: source.inputs.map(i => ({ name: i.name, kind: 'Literal' as const, value: '' })),
            fields: source.fields,
            sort: []
        };

        dispatch({ type: 'setDataSets', dataSets: [...definition.dataSets, dataSet] });
        setExpanded(key);
        setAddOpen(false);
    };

    const removeDataSet = (key: string) => {
        dispatch({
            type: 'setDataSets',
            dataSets: definition.dataSets.filter(d => d.key !== key)
        });
    };

    const showPreview = async (dataSet: DataSetDef) => {
        if (!dataSet.tableId) return;
        try {
            const inputs = Object.fromEntries(
                dataSet.inputs.filter(i => i.kind === 'Literal' && i.value).map(i => [i.name, i.value]));
            const data = await reportsApi.sources.sample(dataSet.tableId, inputs);
            setPreview({ key: dataSet.key, data });
        } catch {
            // A failed preview is a convenience feature failing, not a reason to interrupt designing.
        }
    };

    return (
        <Box>
            <Stack direction="row" alignItems="center" sx={{ px: 1.5, pt: 1 }}>
                <Typography variant="overline" sx={{ color: 'text.secondary', flexGrow: 1 }}>Data sets</Typography>
                <Tooltip title="Add a data set">
                    <IconButton size="small" onClick={() => setAddOpen(true)}><AddIcon fontSize="small" /></IconButton>
                </Tooltip>
            </Stack>

            {definition.dataSets.length === 0 && (
                <Typography variant="caption" sx={{ px: 1.5, color: 'text.disabled' }}>
                    No data sets yet. Add one to bind a table.
                </Typography>
            )}

            <List dense disablePadding>
                {definition.dataSets.map(dataSet => (
                    <Box key={dataSet.key}>
                        <ListItemButton
                            onClick={() => setExpanded(expanded === dataSet.key ? null : dataSet.key)}
                            sx={{ py: 0.25 }}
                        >
                            {expanded === dataSet.key
                                ? <ExpandLessIcon fontSize="small" />
                                : <ExpandMoreIcon fontSize="small" />}

                            <ListItemText
                                primary={dataSet.key}
                                secondary={`table ${dataSet.tableId ?? '—'} · ${dataSet.fields.length} fields`}
                                primaryTypographyProps={{ variant: 'body2', fontWeight: 600 }}
                                secondaryTypographyProps={{ variant: 'caption' }}
                            />

                            <Tooltip title="Preview rows">
                                <IconButton
                                    size="small"
                                    onClick={e => { e.stopPropagation(); void showPreview(dataSet); }}
                                >
                                    <PreviewIcon fontSize="small" />
                                </IconButton>
                            </Tooltip>
                            <Tooltip title="Remove data set">
                                <IconButton
                                    size="small"
                                    onClick={e => { e.stopPropagation(); removeDataSet(dataSet.key); }}
                                >
                                    <DeleteOutlineIcon fontSize="small" />
                                </IconButton>
                            </Tooltip>
                        </ListItemButton>

                        <Collapse in={expanded === dataSet.key} unmountOnExit>
                            <Box sx={{ pl: 3, pr: 1.5, pb: 1 }}>
                                {dataSet.fields.map(field => (
                                    <FieldChip key={field.name} field={field} dataSetKey={dataSet.key} />
                                ))}
                            </Box>
                        </Collapse>
                    </Box>
                ))}
            </List>

            <AddDataSetDialog
                open={addOpen}
                sources={sources}
                existingKeys={definition.dataSets.map(d => d.key)}
                onClose={() => setAddOpen(false)}
                onAdd={addDataSet}
            />

            <Dialog open={!!preview} onClose={() => setPreview(null)} maxWidth="lg" fullWidth>
                <DialogTitle>Sample rows · {preview?.key}</DialogTitle>
                <DialogContent>
                    {preview && <PreviewTable data={preview.data} />}
                </DialogContent>
                <DialogActions><Button onClick={() => setPreview(null)}>Close</Button></DialogActions>
            </Dialog>
        </Box>
    );
}

/** A draggable field name. Dropping it on a band creates a bound field element. */
function FieldChip({ field, dataSetKey }: { field: FieldMeta; dataSetKey: string }) {
    return (
        <Chip
            label={field.name}
            size="small"
            draggable
            onDragStart={e => {
                e.dataTransfer.setData('application/x-report-field',
                    JSON.stringify({ dataSetKey, field: field.name }));
                e.dataTransfer.effectAllowed = 'copy';
            }}
            title={`${field.dataType}${field.format ? ` · ${field.format}` : ''}`}
            sx={{ mr: 0.5, mb: 0.5, cursor: 'grab' }}
        />
    );
}

interface AddDialogProps {
    open: boolean;
    sources: TableSource[];
    existingKeys: string[];
    onClose: () => void;
    onAdd: (source: TableSource, key: string) => void;
}

function AddDataSetDialog({ open, sources, existingKeys, onClose, onAdd }: AddDialogProps) {
    const [tableId, setTableId] = useState<number | ''>('');
    const [key, setKey] = useState('');

    const source = sources.find(s => s.tableId === tableId);
    const duplicate = existingKeys.includes(key.trim());
    const canAdd = !!source && key.trim().length > 0 && !duplicate;

    return (
        <Dialog open={open} onClose={onClose} maxWidth="sm" fullWidth>
            <DialogTitle>Add a data set</DialogTitle>
            <DialogContent>
                <Stack spacing={2} sx={{ mt: 1 }}>
                    <TextField
                        select
                        label="Table source"
                        value={tableId}
                        onChange={e => {
                            const id = Number(e.target.value);
                            setTableId(id);
                            // Suggest a key from the source name, which is right most of the time.
                            const picked = sources.find(s => s.tableId === id);
                            if (picked && !key) {
                                setKey(picked.name.toLowerCase().replace(/[^a-z0-9]+/g, '_').replace(/^_|_$/g, ''));
                            }
                        }}
                        fullWidth
                    >
                        {sources.map(s => (
                            <MenuItem key={s.tableId} value={s.tableId}>
                                {s.tableId} — {s.name}
                            </MenuItem>
                        ))}
                    </TextField>

                    {source?.description && (
                        <Typography variant="caption" color="text.secondary">{source.description}</Typography>
                    )}

                    <TextField
                        label="Data set key"
                        value={key}
                        onChange={e => setKey(e.target.value)}
                        error={duplicate}
                        helperText={duplicate
                            ? 'A data set with this key already exists.'
                            : 'Tables and fields reference the data set by this key.'}
                        fullWidth
                    />

                    {source && source.inputs.length > 0 && (
                        <Box>
                            <Typography variant="caption" color="text.secondary">
                                This source accepts: {source.inputs.map(i => i.name).join(', ')}
                            </Typography>
                        </Box>
                    )}
                </Stack>
            </DialogContent>
            <DialogActions>
                <Button onClick={onClose}>Cancel</Button>
                <Button
                    variant="contained"
                    disabled={!canAdd}
                    onClick={() => source && onAdd(source, key.trim())}
                >
                    Add
                </Button>
            </DialogActions>
        </Dialog>
    );
}

function PreviewTable({ data }: { data: DataPreview }) {
    if (data.rows.length === 0) {
        return <Typography variant="body2" color="text.secondary">This source returned no rows.</Typography>;
    }

    return (
        <Box sx={{ overflowX: 'auto' }}>
            <Box component="table" sx={{
                borderCollapse: 'collapse',
                fontSize: 12,
                '& th, & td': { border: '1px solid', borderColor: 'divider', px: 1, py: 0.25, textAlign: 'left' },
                '& th': { backgroundColor: 'action.hover', fontWeight: 700 }
            }}>
                <thead>
                    <tr>{data.fields.map(f => <th key={f.name}>{f.name}</th>)}</tr>
                </thead>
                <tbody>
                    {data.rows.map((row, i) => (
                        <tr key={i}>
                            {data.fields.map(f => <td key={f.name}>{formatCell(row[f.name])}</td>)}
                        </tr>
                    ))}
                </tbody>
            </Box>
        </Box>
    );
}

function formatCell(value: unknown): string {
    if (value === null || value === undefined) return '';
    return String(value);
}
