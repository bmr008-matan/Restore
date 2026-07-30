import {
    Button, Checkbox, Dialog, DialogActions, DialogContent, DialogTitle, FormControlLabel,
    IconButton, MenuItem, Paper, Stack, TextField, Typography
} from '@mui/material';
import DeleteOutlineIcon from '@mui/icons-material/DeleteOutline';
import AddIcon from '@mui/icons-material/Add';
import {
    PARAMETER_TYPES, ParameterType, ReportParameter
} from '../types/reportDefinition';
import { TableSource } from '../api/reportsApi';

interface Props {
    open: boolean;
    parameters: ReportParameter[];
    sources: TableSource[];
    onClose: () => void;
    onChange: (parameters: ReportParameter[]) => void;
}

/** Relative-date and user tokens the server resolves at run time. */
const DEFAULT_TOKENS = [
    '@Today', '@Yesterday', '@Tomorrow', '@StartOfMonth', '@EndOfMonth',
    '@StartOfYear', '@EndOfYear', '@CurrentUser'
];

const IDENTIFIER = /^[A-Za-z_][A-Za-z0-9_]*$/;

/**
 * Report parameters. These drive two things at once: the run dialog the end user sees, and the contract
 * an integrator discovers through GET /api/reports/{id}/parameters — so getting them right here is what
 * makes a report self-service.
 */
export default function ParametersEditor({ open, parameters, sources, onClose, onChange }: Props) {
    const add = () => onChange([...parameters, {
        name: `param${parameters.length + 1}`,
        type: 'Text',
        required: false,
        order: parameters.length + 1,
        hidden: false
    }]);

    const patch = (index: number, change: Partial<ReportParameter>) =>
        onChange(parameters.map((p, i) => (i === index ? { ...p, ...change } : p)));

    return (
        <Dialog open={open} onClose={onClose} maxWidth="md" fullWidth>
            <DialogTitle>Report parameters</DialogTitle>
            <DialogContent>
                <Typography variant="caption" color="text.secondary">
                    Parameters are prompted for when the report runs, and can be used in data set inputs,
                    expressions and header text.
                </Typography>

                <Stack spacing={1.5} sx={{ mt: 2 }}>
                    {parameters.length === 0 && (
                        <Typography variant="body2" color="text.secondary">
                            No parameters. The report will run without prompting.
                        </Typography>
                    )}

                    {parameters.map((parameter, index) => {
                        const nameInvalid = !IDENTIFIER.test(parameter.name);
                        const duplicate = parameters
                            .filter((_, i) => i !== index)
                            .some(p => p.name.toLowerCase() === parameter.name.toLowerCase());
                        const needsLookup = parameter.type === 'Select' || parameter.type === 'MultiSelect';

                        return (
                            <Paper key={index} variant="outlined" sx={{ p: 1.5 }}>
                                <Stack direction="row" spacing={1} sx={{ mb: 1 }}>
                                    <TextField
                                        label="Name"
                                        size="small"
                                        value={parameter.name}
                                        onChange={e => patch(index, { name: e.target.value })}
                                        error={nameInvalid || duplicate}
                                        helperText={
                                            duplicate ? 'Duplicate name.'
                                                : nameInvalid ? 'Letters, digits and underscores, starting with a letter.'
                                                    : 'Used in expressions as Params.' + parameter.name
                                        }
                                        sx={{ flexGrow: 1 }}
                                    />
                                    <TextField
                                        label="Label"
                                        size="small"
                                        value={parameter.label ?? ''}
                                        onChange={e => patch(index, { label: e.target.value || undefined })}
                                        helperText="Shown to the user."
                                        sx={{ flexGrow: 1 }}
                                    />
                                    <IconButton
                                        size="small"
                                        onClick={() => onChange(parameters.filter((_, i) => i !== index))}
                                    >
                                        <DeleteOutlineIcon fontSize="small" />
                                    </IconButton>
                                </Stack>

                                <Stack direction="row" spacing={1}>
                                    <TextField
                                        select
                                        label="Type"
                                        size="small"
                                        value={parameter.type}
                                        onChange={e => patch(index, { type: e.target.value as ParameterType })}
                                        sx={{ width: 140 }}
                                    >
                                        {PARAMETER_TYPES.map(t => <MenuItem key={t} value={t}>{t}</MenuItem>)}
                                    </TextField>

                                    <TextField
                                        label="Default"
                                        size="small"
                                        value={parameter.defaultValue ?? ''}
                                        onChange={e => patch(index, { defaultValue: e.target.value || undefined })}
                                        helperText={parameter.type === 'Date' ? 'A value, or a token below.' : ' '}
                                        sx={{ flexGrow: 1 }}
                                    />

                                    <TextField
                                        label="Order"
                                        size="small"
                                        type="number"
                                        value={parameter.order}
                                        onChange={e => patch(index, { order: Number(e.target.value) || 0 })}
                                        sx={{ width: 90 }}
                                    />
                                </Stack>

                                {parameter.type === 'Date' && (
                                    <Stack direction="row" spacing={0.5} sx={{ mt: 0.5, flexWrap: 'wrap' }}>
                                        {DEFAULT_TOKENS.filter(t => t !== '@CurrentUser').map(token => (
                                            <Button
                                                key={token}
                                                size="small"
                                                onClick={() => patch(index, { defaultValue: token })}
                                                sx={{ minWidth: 0, fontSize: 11 }}
                                            >
                                                {token}
                                            </Button>
                                        ))}
                                    </Stack>
                                )}

                                {needsLookup && (
                                    <Stack direction="row" spacing={1} sx={{ mt: 1 }}>
                                        <TextField
                                            select
                                            label="Lookup source"
                                            size="small"
                                            value={parameter.lookupTableId ?? ''}
                                            onChange={e => {
                                                const id = Number(e.target.value);
                                                const source = sources.find(s => s.tableId === id);
                                                patch(index, {
                                                    lookupTableId: id,
                                                    // Default to the conventional value/label pair when the
                                                    // source has them, which most lookups do.
                                                    valueField: parameter.valueField
                                                        ?? source?.fields.find(f => f.name === 'value')?.name
                                                        ?? source?.fields[0]?.name,
                                                    displayField: parameter.displayField
                                                        ?? source?.fields.find(f => f.name === 'label')?.name
                                                        ?? source?.fields[0]?.name
                                                });
                                            }}
                                            error={!parameter.lookupTableId}
                                            helperText={parameter.lookupTableId ? ' ' : 'Required for a Select.'}
                                            sx={{ flexGrow: 1 }}
                                        >
                                            {sources.map(s => (
                                                <MenuItem key={s.tableId} value={s.tableId}>
                                                    {s.tableId} — {s.name}
                                                </MenuItem>
                                            ))}
                                        </TextField>

                                        <TextField
                                            select
                                            label="Value field"
                                            size="small"
                                            value={parameter.valueField ?? ''}
                                            onChange={e => patch(index, { valueField: e.target.value || undefined })}
                                            error={!parameter.valueField}
                                            sx={{ width: 140 }}
                                        >
                                            {(sources.find(s => s.tableId === parameter.lookupTableId)?.fields ?? [])
                                                .map(f => <MenuItem key={f.name} value={f.name}>{f.name}</MenuItem>)}
                                        </TextField>

                                        <TextField
                                            select
                                            label="Display field"
                                            size="small"
                                            value={parameter.displayField ?? ''}
                                            onChange={e => patch(index, { displayField: e.target.value || undefined })}
                                            sx={{ width: 140 }}
                                        >
                                            {(sources.find(s => s.tableId === parameter.lookupTableId)?.fields ?? [])
                                                .map(f => <MenuItem key={f.name} value={f.name}>{f.name}</MenuItem>)}
                                        </TextField>
                                    </Stack>
                                )}

                                <Stack direction="row" spacing={2} sx={{ mt: 0.5 }}>
                                    <FormControlLabel
                                        control={
                                            <Checkbox
                                                size="small"
                                                checked={parameter.required}
                                                onChange={e => patch(index, { required: e.target.checked })}
                                            />
                                        }
                                        label={<Typography variant="caption">Required</Typography>}
                                    />
                                    <FormControlLabel
                                        control={
                                            <Checkbox
                                                size="small"
                                                checked={parameter.hidden}
                                                onChange={e => patch(index, { hidden: e.target.checked })}
                                            />
                                        }
                                        label={<Typography variant="caption">Hidden</Typography>}
                                    />
                                </Stack>

                                {parameter.required && parameter.hidden && !parameter.defaultValue && (
                                    <Typography variant="caption" color="error">
                                        Hidden and required with no default — the report could never run.
                                    </Typography>
                                )}
                            </Paper>
                        );
                    })}
                </Stack>
            </DialogContent>
            <DialogActions>
                <Button startIcon={<AddIcon />} onClick={add}>Add parameter</Button>
                <Button onClick={onClose} variant="contained">Done</Button>
            </DialogActions>
        </Dialog>
    );
}
