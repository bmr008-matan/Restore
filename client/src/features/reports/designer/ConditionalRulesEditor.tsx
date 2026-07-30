import { useState } from 'react';
import {
    Box, Button, Checkbox, Chip, FormControlLabel, IconButton, MenuItem, Paper, Stack,
    TextField, Typography
} from '@mui/material';
import DeleteOutlineIcon from '@mui/icons-material/DeleteOutline';
import AddIcon from '@mui/icons-material/Add';
import { ConditionalRuleDef, DataSetDef, HorizontalAlign, ReportParameter } from '../types/reportDefinition';

interface Props {
    rules: ConditionalRuleDef[];
    /** Fields available to the expression, when the rule sits somewhere that has a current row. */
    dataSet?: DataSetDef;
    parameters: ReportParameter[];
    /**
     * False for band rules. A band is evaluated once per report and has no current row, so a rule reading
     * Fields there would always see nulls — the server rejects it, and the UI should not offer it.
     */
    allowFields: boolean;
    onChange: (rules: ConditionalRuleDef[]) => void;
}

const ALIGNMENTS: HorizontalAlign[] = ['Left', 'Center', 'Right', 'Justify', 'Start', 'End'];

/**
 * Conditional formatting rules.
 *
 * Expressions are evaluated on the server at render time, never here, which is why a rule's effect in the
 * preview is always exactly its effect in the final PDF. The insert chips exist because guessing the
 * right identifier is the main thing that goes wrong when writing one of these.
 */
export default function ConditionalRulesEditor({
    rules, dataSet, parameters, allowFields, onChange
}: Props) {
    const [focused, setFocused] = useState<number | null>(null);

    const add = () => onChange([...rules, {
        expression: '',
        actions: {},
        enabled: true
    }]);

    const patch = (index: number, change: Partial<ConditionalRuleDef>) =>
        onChange(rules.map((r, i) => (i === index ? { ...r, ...change } : r)));

    const patchActions = (index: number, change: Partial<ConditionalRuleDef['actions']>) =>
        onChange(rules.map((r, i) => (i === index ? { ...r, actions: { ...r.actions, ...change } } : r)));

    const insert = (index: number, token: string) => {
        const current = rules[index].expression;
        patch(index, { expression: current ? `${current} ${token}` : token });
    };

    return (
        <Box>
            <Stack direction="row" alignItems="center" sx={{ mb: 1 }}>
                <Typography variant="subtitle2" sx={{ flexGrow: 1 }}>
                    Conditional rules ({rules.length})
                </Typography>
                <Button size="small" startIcon={<AddIcon />} onClick={add}>Add</Button>
            </Stack>

            {rules.length === 0 && (
                <Typography variant="caption" color="text.secondary">
                    No rules. Add one to change formatting based on the data, a parameter, or the viewer's role.
                </Typography>
            )}

            <Stack spacing={1.5}>
                {rules.map((rule, index) => (
                    <Paper key={index} variant="outlined" sx={{ p: 1.5 }}>
                        <Stack direction="row" alignItems="center" spacing={1} sx={{ mb: 1 }}>
                            <TextField
                                label="Rule name"
                                size="small"
                                value={rule.name ?? ''}
                                onChange={e => patch(index, { name: e.target.value || undefined })}
                                sx={{ flexGrow: 1 }}
                            />
                            <FormControlLabel
                                control={
                                    <Checkbox
                                        size="small"
                                        checked={rule.enabled}
                                        onChange={e => patch(index, { enabled: e.target.checked })}
                                    />
                                }
                                label={<Typography variant="caption">Enabled</Typography>}
                            />
                            <IconButton size="small" onClick={() => onChange(rules.filter((_, i) => i !== index))}>
                                <DeleteOutlineIcon fontSize="small" />
                            </IconButton>
                        </Stack>

                        <TextField
                            label="When"
                            size="small"
                            value={rule.expression}
                            onChange={e => patch(index, { expression: e.target.value })}
                            onFocus={() => setFocused(index)}
                            placeholder={allowFields ? 'Fields.total > 10000' : 'Params.showDetail == true'}
                            error={rule.expression.trim().length === 0}
                            helperText={rule.expression.trim().length === 0
                                ? 'A rule needs an expression.'
                                : 'Checked on the server when the report renders.'}
                            fullWidth
                            multiline
                            maxRows={3}
                        />

                        {focused === index && (
                            <Box sx={{ mt: 0.5 }}>
                                <Typography variant="caption" color="text.secondary">Insert:</Typography>
                                <Box sx={{ mt: 0.5 }}>
                                    {allowFields && (dataSet?.fields ?? []).map(f => (
                                        <Chip
                                            key={f.name}
                                            label={`Fields.${f.name}`}
                                            size="small"
                                            onClick={() => insert(index, `Fields.${f.name}`)}
                                            sx={{ mr: 0.5, mb: 0.5 }}
                                        />
                                    ))}
                                    {parameters.map(p => (
                                        <Chip
                                            key={p.name}
                                            label={`Params.${p.name}`}
                                            size="small"
                                            variant="outlined"
                                            onClick={() => insert(index, `Params.${p.name}`)}
                                            sx={{ mr: 0.5, mb: 0.5 }}
                                        />
                                    ))}
                                    <Chip
                                        label='Report.User.IsInRole("Manager")'
                                        size="small"
                                        variant="outlined"
                                        color="secondary"
                                        onClick={() => insert(index, 'Report.User.IsInRole("Manager")')}
                                        sx={{ mr: 0.5, mb: 0.5 }}
                                    />
                                    <Chip
                                        label="IsNull(...)"
                                        size="small"
                                        variant="outlined"
                                        onClick={() => insert(index, 'IsNull()')}
                                        sx={{ mr: 0.5, mb: 0.5 }}
                                    />
                                </Box>
                            </Box>
                        )}

                        <Typography variant="caption" sx={{ display: 'block', mt: 1.5, fontWeight: 700 }}>
                            Then
                        </Typography>

                        <Box sx={{ display: 'grid', gridTemplateColumns: '1fr 1fr', columnGap: 1 }}>
                            <ActionToggle
                                label="Bold" value={rule.actions.bold}
                                onChange={v => patchActions(index, { bold: v })}
                            />
                            <ActionToggle
                                label="Italic" value={rule.actions.italic}
                                onChange={v => patchActions(index, { italic: v })}
                            />
                            <ActionToggle
                                label="Underline" value={rule.actions.underline}
                                onChange={v => patchActions(index, { underline: v })}
                            />
                            <ActionToggle
                                label="Hide" value={rule.actions.hide}
                                onChange={v => patchActions(index, { hide: v })}
                            />
                        </Box>

                        <Stack direction="row" spacing={1} sx={{ mt: 1 }}>
                            <TextField
                                label="Text colour"
                                size="small"
                                value={rule.actions.color ?? ''}
                                onChange={e => patchActions(index, { color: e.target.value || undefined })}
                                placeholder="#B00020"
                                sx={{ flexGrow: 1 }}
                            />
                            <TextField
                                label="Fill"
                                size="small"
                                value={rule.actions.backColor ?? ''}
                                onChange={e => patchActions(index, { backColor: e.target.value || undefined })}
                                placeholder="#FFF3F3"
                                sx={{ flexGrow: 1 }}
                            />
                        </Stack>

                        <Stack direction="row" spacing={1} sx={{ mt: 1 }}>
                            <TextField
                                label="Size (pt)"
                                size="small"
                                type="number"
                                value={rule.actions.fontSizePt ?? ''}
                                onChange={e => patchActions(index, {
                                    fontSizePt: e.target.value ? Number(e.target.value) : undefined
                                })}
                                sx={{ width: 100 }}
                            />
                            <TextField
                                select
                                label="Align"
                                size="small"
                                value={rule.actions.align ?? ''}
                                onChange={e => patchActions(index, {
                                    align: (e.target.value || undefined) as HorizontalAlign | undefined
                                })}
                                sx={{ width: 120 }}
                            >
                                <MenuItem value="">inherit</MenuItem>
                                {ALIGNMENTS.map(a => <MenuItem key={a} value={a}>{a}</MenuItem>)}
                            </TextField>
                            <TextField
                                label="Format"
                                size="small"
                                value={rule.actions.format ?? ''}
                                onChange={e => patchActions(index, { format: e.target.value || undefined })}
                                placeholder="#,##0.00"
                                sx={{ flexGrow: 1 }}
                            />
                        </Stack>

                        <TextField
                            label="Replace text with"
                            size="small"
                            value={rule.actions.textOverride ?? ''}
                            onChange={e => patchActions(index, { textOverride: e.target.value || undefined })}
                            fullWidth
                            sx={{ mt: 1 }}
                        />
                    </Paper>
                ))}
            </Stack>
        </Box>
    );
}

/**
 * Three-state: unset means "leave whatever the style chain produced", which is different from explicitly
 * setting it off. Cycling through unset is the only way to clear an action once applied.
 */
function ActionToggle({ label, value, onChange }: {
    label: string; value?: boolean; onChange: (value: boolean | undefined) => void;
}) {
    return (
        <FormControlLabel
            control={
                <Checkbox
                    size="small"
                    checked={value === true}
                    indeterminate={value === undefined}
                    onChange={() => onChange(value === true ? undefined : true)}
                />
            }
            label={<Typography variant="caption">{label}</Typography>}
        />
    );
}
