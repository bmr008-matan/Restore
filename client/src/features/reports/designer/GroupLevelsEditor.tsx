import {
    Box, Button, Checkbox, Chip, Divider, FormControlLabel, IconButton, MenuItem, Paper,
    Stack, TextField, Tooltip, Typography
} from '@mui/material';
import ArrowUpwardIcon from '@mui/icons-material/ArrowUpward';
import ArrowDownwardIcon from '@mui/icons-material/ArrowDownward';
import DeleteOutlineIcon from '@mui/icons-material/DeleteOutline';
import AddIcon from '@mui/icons-material/Add';
import {
    AGGREGATE_FUNCTIONS, AggregateDef, AggregateFunction, DataSetDef, GroupDef, TableElement
} from '../types/reportDefinition';

interface Props {
    table: TableElement;
    dataSet?: DataSetDef;
    onChange: (patch: Partial<TableElement>) => void;
}

/**
 * Multi-level grouping.
 *
 * Nesting is the list order — level 1 is outermost — so moving a level up or down is the whole
 * reordering operation. Nothing else in the definition refers to a level by index, which is why this can
 * be a plain reorder rather than a remapping exercise.
 */
export default function GroupLevelsEditor({ table, dataSet, onChange }: Props) {
    const fields = dataSet?.fields ?? [];

    const setGroups = (groups: GroupDef[]) => onChange({ groups });

    const addLevel = () => setGroups([...table.groups, {
        field: fields[0]?.name,
        sortDirection: 'Asc',
        showHeader: true,
        showFooter: true,
        aggregates: [],
        headerAggregates: [],
        pageBreakBefore: false,
        pageBreakAfter: false,
        keepTogether: false,
        repeatHeaderOnNewPage: false,
        showItemCount: false,
        rules: []
    }]);

    const patchLevel = (index: number, patch: Partial<GroupDef>) =>
        setGroups(table.groups.map((g, i) => (i === index ? { ...g, ...patch } : g)));

    const removeLevel = (index: number) => setGroups(table.groups.filter((_, i) => i !== index));

    const move = (index: number, delta: number) => {
        const target = index + delta;
        if (target < 0 || target >= table.groups.length) return;

        const next = [...table.groups];
        [next[index], next[target]] = [next[target], next[index]];
        setGroups(next);
    };

    // Only the outermost level requesting keep-together can be honoured, because the renderer implements
    // it with a table row group and those cannot nest. Worth saying here rather than letting the server
    // warning be the first anyone hears of it.
    const keepTogetherLevels = table.groups
        .map((g, i) => (g.keepTogether ? i : -1))
        .filter(i => i >= 0);

    return (
        <Box>
            <Stack direction="row" alignItems="center" sx={{ mb: 1 }}>
                <Typography variant="subtitle2" sx={{ flexGrow: 1 }}>
                    Group levels ({table.groups.length})
                </Typography>
                <Button size="small" startIcon={<AddIcon />} onClick={addLevel} disabled={fields.length === 0}>
                    Add level
                </Button>
            </Stack>

            {fields.length === 0 && (
                <Typography variant="caption" color="text.secondary">
                    Bind this table to a data set first, so there are fields to group by.
                </Typography>
            )}

            {table.groups.length === 0 && fields.length > 0 && (
                <Typography variant="caption" color="text.secondary">
                    No grouping — this table prints as a flat list.
                </Typography>
            )}

            {keepTogetherLevels.length > 1 && (
                <Typography variant="caption" sx={{ display: 'block', color: 'warning.main', mb: 1 }}>
                    Keep-together is set on more than one level. Only level {keepTogetherLevels[0] + 1} can be
                    honoured — the renderer uses a table row group and those cannot nest.
                </Typography>
            )}

            <Stack spacing={1.5}>
                {table.groups.map((group, index) => (
                    <Paper key={index} variant="outlined" sx={{ p: 1.5 }}>
                        <Stack direction="row" alignItems="center" spacing={1} sx={{ mb: 1 }}>
                            <Chip
                                label={index === 0 ? `Level ${index + 1} (outermost)` : `Level ${index + 1}`}
                                size="small"
                                color={index === 0 ? 'primary' : 'default'}
                            />
                            <Box sx={{ flexGrow: 1 }} />
                            {/* Wrapped in a span because a disabled button fires no events, so MUI
                                cannot attach a tooltip to one directly. */}
                            <Tooltip title="Move out one level">
                                <span>
                                    <IconButton size="small" onClick={() => move(index, -1)} disabled={index === 0}>
                                        <ArrowUpwardIcon fontSize="small" />
                                    </IconButton>
                                </span>
                            </Tooltip>
                            <Tooltip title="Move in one level">
                                <span>
                                    <IconButton
                                        size="small"
                                        onClick={() => move(index, 1)}
                                        disabled={index === table.groups.length - 1}
                                    >
                                        <ArrowDownwardIcon fontSize="small" />
                                    </IconButton>
                                </span>
                            </Tooltip>
                            <Tooltip title="Remove this level">
                                <IconButton size="small" onClick={() => removeLevel(index)}>
                                    <DeleteOutlineIcon fontSize="small" />
                                </IconButton>
                            </Tooltip>
                        </Stack>

                        <Stack direction="row" spacing={1} sx={{ mb: 1 }}>
                            <TextField
                                select
                                label="Group by"
                                size="small"
                                value={group.field ?? ''}
                                onChange={e => patchLevel(index, { field: e.target.value, expression: undefined })}
                                sx={{ flexGrow: 1 }}
                            >
                                {fields.map(f => (
                                    <MenuItem key={f.name} value={f.name}>{f.name}</MenuItem>
                                ))}
                            </TextField>

                            <TextField
                                select
                                label="Sort"
                                size="small"
                                value={group.sortDirection}
                                onChange={e => patchLevel(index, { sortDirection: e.target.value as 'Asc' | 'Desc' })}
                                sx={{ width: 110 }}
                            >
                                <MenuItem value="Asc">Asc</MenuItem>
                                <MenuItem value="Desc">Desc</MenuItem>
                            </TextField>
                        </Stack>

                        <Stack direction="row" spacing={1} sx={{ mb: 1 }}>
                            <TextField
                                label="Header text"
                                size="small"
                                value={group.headerText ?? ''}
                                onChange={e => patchLevel(index, { headerText: e.target.value || undefined })}
                                placeholder={group.field ? `{${group.field}}` : ''}
                                helperText="Tokens: {field}, {Count}, {SumOfX}"
                                fullWidth
                            />
                            <TextField
                                label="Footer text"
                                size="small"
                                value={group.footerText ?? ''}
                                onChange={e => patchLevel(index, { footerText: e.target.value || undefined })}
                                placeholder={group.field ? `Total {${group.field}}` : ''}
                                fullWidth
                            />
                        </Stack>

                        <Box sx={{ display: 'grid', gridTemplateColumns: '1fr 1fr', columnGap: 1 }}>
                            <Toggle
                                label="Show group header"
                                checked={group.showHeader}
                                onChange={v => patchLevel(index, { showHeader: v })}
                            />
                            <Toggle
                                label="Show group footer"
                                checked={group.showFooter}
                                onChange={v => patchLevel(index, { showFooter: v })}
                            />
                            <Toggle
                                label="Repeat header on new page"
                                checked={group.repeatHeaderOnNewPage}
                                onChange={v => patchLevel(index, { repeatHeaderOnNewPage: v })}
                            />
                            <Toggle
                                label="Show item count"
                                checked={group.showItemCount}
                                onChange={v => patchLevel(index, { showItemCount: v })}
                            />
                            <Toggle
                                label="Page break before"
                                checked={group.pageBreakBefore}
                                onChange={v => patchLevel(index, { pageBreakBefore: v })}
                            />
                            <Toggle
                                label="Page break after"
                                checked={group.pageBreakAfter}
                                onChange={v => patchLevel(index, { pageBreakAfter: v })}
                            />
                            <Toggle
                                label="Keep group together"
                                checked={group.keepTogether}
                                onChange={v => patchLevel(index, { keepTogether: v })}
                            />
                        </Box>

                        <Divider sx={{ my: 1 }} />

                        <AggregateList
                            label="Footer totals"
                            aggregates={group.aggregates}
                            table={table}
                            dataSet={dataSet}
                            onChange={aggregates => patchLevel(index, { aggregates })}
                        />
                    </Paper>
                ))}
            </Stack>

            <Divider sx={{ my: 2 }} />

            <Stack direction="row" spacing={1} sx={{ mb: 1 }}>
                <TextField
                    label="Grand total label"
                    size="small"
                    value={table.grandTotalLabel ?? ''}
                    onChange={e => onChange({ grandTotalLabel: e.target.value || undefined })}
                    fullWidth
                />
            </Stack>

            <AggregateList
                label="Grand totals"
                aggregates={table.grandTotals}
                table={table}
                dataSet={dataSet}
                onChange={grandTotals => onChange({ grandTotals })}
            />
        </Box>
    );
}

function Toggle({ label, checked, onChange }: {
    label: string; checked: boolean; onChange: (value: boolean) => void;
}) {
    return (
        <FormControlLabel
            control={<Checkbox size="small" checked={checked} onChange={e => onChange(e.target.checked)} />}
            label={<Typography variant="caption">{label}</Typography>}
        />
    );
}

interface AggregateListProps {
    label: string;
    aggregates: AggregateDef[];
    table: TableElement;
    dataSet?: DataSetDef;
    onChange: (aggregates: AggregateDef[]) => void;
}

/**
 * Totals for one level. `targetColumn` is what makes a subtotal print underneath the column it adds up;
 * without it the total lands in the group's label cell instead.
 */
function AggregateList({ label, aggregates, table, dataSet, onChange }: AggregateListProps) {
    const numericFields = (dataSet?.fields ?? []).filter(f => f.dataType === 'Int' || f.dataType === 'Decimal');
    const columns = table.columns.filter(c => c.visible);

    const add = () => onChange([...aggregates, {
        function: 'Sum',
        field: numericFields[0]?.name,
        targetColumn: numericFields[0]?.name,
        format: '#,##0.00'
    }]);

    const patch = (index: number, change: Partial<AggregateDef>) =>
        onChange(aggregates.map((a, i) => (i === index ? { ...a, ...change } : a)));

    return (
        <Box>
            <Stack direction="row" alignItems="center">
                <Typography variant="caption" sx={{ fontWeight: 700, flexGrow: 1 }}>{label}</Typography>
                <Button size="small" startIcon={<AddIcon />} onClick={add}>Add</Button>
            </Stack>

            {aggregates.length === 0 && (
                <Typography variant="caption" color="text.disabled">None</Typography>
            )}

            {/* Two rows per total rather than one. The properties panel is narrow, and cramming four
                controls onto a single line squeezed the function dropdown down to an unreadable sliver. */}
            <Stack spacing={1} sx={{ mt: 0.5 }}>
                {aggregates.map((aggregate, index) => (
                    <Box
                        key={index}
                        sx={{
                            display: 'grid',
                            gridTemplateColumns: '1fr 1fr auto',
                            gap: 0.5,
                            alignItems: 'center',
                            p: 0.75,
                            borderRadius: 1,
                            backgroundColor: 'action.hover'
                        }}
                    >
                        <TextField
                            select
                            size="small"
                            label="Function"
                            value={aggregate.function}
                            onChange={e => patch(index, { function: e.target.value as AggregateFunction })}
                        >
                            {AGGREGATE_FUNCTIONS.map(fn => (
                                <MenuItem key={fn} value={fn}>{fn}</MenuItem>
                            ))}
                        </TextField>

                        <TextField
                            select
                            size="small"
                            label="Of field"
                            value={aggregate.field ?? ''}
                            onChange={e => patch(index, { field: e.target.value || undefined })}
                            // Count needs no field, so the picker is disabled rather than misleading.
                            disabled={aggregate.function === 'Count'}
                        >
                            <MenuItem value="">—</MenuItem>
                            {(dataSet?.fields ?? []).map(f => (
                                <MenuItem key={f.name} value={f.name}>{f.name}</MenuItem>
                            ))}
                        </TextField>

                        <IconButton
                            size="small"
                            onClick={() => onChange(aggregates.filter((_, i) => i !== index))}
                            sx={{ gridRow: '1 / span 2' }}
                        >
                            <DeleteOutlineIcon fontSize="small" />
                        </IconButton>

                        <TextField
                            select
                            size="small"
                            label="Print under column"
                            value={aggregate.targetColumn ?? ''}
                            onChange={e => patch(index, { targetColumn: e.target.value || undefined })}
                            sx={{ gridColumn: '1 / span 2' }}
                        >
                            <MenuItem value="">the group's label cell</MenuItem>
                            {columns.map(c => (
                                <MenuItem key={c.field ?? c.caption} value={c.field ?? c.caption ?? ''}>
                                    {c.caption ?? c.field}
                                </MenuItem>
                            ))}
                        </TextField>
                    </Box>
                ))}
            </Stack>
        </Box>
    );
}
