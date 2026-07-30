import { useCallback, useEffect, useRef, useState } from 'react';
import {
    Alert, Box, Button, Checkbox, CircularProgress, Dialog, DialogActions, DialogContent,
    DialogTitle, FormControlLabel, MenuItem, Stack, TextField, ToggleButton, ToggleButtonGroup,
    Typography
} from '@mui/material';
import DownloadIcon from '@mui/icons-material/Download';
import reportsApi, { ReportParameterInfo, ReportTemplateSummary } from '../api/reportsApi';
import { ApiError } from '../../../app/api/agent';
import { TextDirection } from '../types/reportDefinition';

interface Props {
    template: ReportTemplateSummary | null;
    onClose: () => void;
}

/**
 * Runs a report.
 *
 * The entire form is generated from the parameter metadata the API returns, so adding a parameter to a
 * report needs no change here — which is the point of declaring parameters on the template rather than
 * hard-coding a form per report.
 */
export default function RunReportDialog({ template, onClose }: Props) {
    const [parameters, setParameters] = useState<ReportParameterInfo[]>([]);
    const [values, setValues] = useState<Record<string, unknown>>({});
    const [direction, setDirection] = useState<TextDirection | ''>('');
    const [loading, setLoading] = useState(false);
    const [generating, setGenerating] = useState(false);
    const [error, setError] = useState<string | null>(null);
    const [pdfUrl, setPdfUrl] = useState<string | null>(null);
    const [pageCount, setPageCount] = useState(0);
    const [truncated, setTruncated] = useState(false);

    const urlRef = useRef<string | null>(null);
    const blobRef = useRef<Blob | null>(null);

    useEffect(() => {
        if (!template) return;

        let cancelled = false;
        setLoading(true);
        setError(null);
        setPdfUrl(null);

        (async () => {
            try {
                const info = await reportsApi.reports.parameters(template.id);
                if (cancelled) return;

                const visible = info.parameters.filter(p => !p.hidden);
                setParameters(visible);

                // Seed from the declared defaults. Relative-date tokens like @Today are resolved by the
                // server, so they are left as-is rather than guessed at here.
                const seeded: Record<string, unknown> = {};
                for (const p of visible) {
                    if (p.defaultValue && !p.defaultValue.startsWith('@')) {
                        seeded[p.name] = p.type === 'Bool'
                            ? p.defaultValue === 'true'
                            : p.defaultValue;
                    }
                }
                setValues(seeded);
            } catch (e) {
                if (!cancelled) setError((e as ApiError).summary ?? 'Could not load parameters.');
            } finally {
                if (!cancelled) setLoading(false);
            }
        })();

        return () => { cancelled = true; };
    }, [template]);

    useEffect(() => () => {
        if (urlRef.current) URL.revokeObjectURL(urlRef.current);
    }, []);

    const generate = useCallback(async () => {
        if (!template) return;

        setGenerating(true);
        setError(null);

        try {
            // Omit blanks entirely so the server applies each parameter's declared default rather than
            // receiving an empty string it would have to interpret.
            const payload = Object.fromEntries(
                Object.entries(values).filter(([, v]) => v !== '' && v !== undefined && v !== null));

            const result = await reportsApi.reports.generate(template.id, {
                parameters: payload,
                options: {
                    inline: true,
                    direction: direction || undefined,
                    fileName: `${template.code}.pdf`
                }
            });

            if (urlRef.current) URL.revokeObjectURL(urlRef.current);
            const url = URL.createObjectURL(result.blob);
            urlRef.current = url;
            blobRef.current = result.blob;

            setPdfUrl(url);
            setPageCount(result.pageCount);
            setTruncated(result.truncated);
        } catch (e) {
            setError((e as ApiError).summary ?? 'The report could not be generated.');
        } finally {
            setGenerating(false);
        }
    }, [template, values, direction]);

    const download = () => {
        if (!blobRef.current || !template) return;

        const url = URL.createObjectURL(blobRef.current);
        const link = document.createElement('a');
        link.href = url;
        link.download = `${template.code}.pdf`;
        link.click();
        URL.revokeObjectURL(url);
    };

    const set = (name: string, value: unknown) => setValues(v => ({ ...v, [name]: value }));

    const missingRequired = parameters
        .filter(p => p.required && !p.defaultValue)
        .filter(p => {
            const value = values[p.name];
            return value === undefined || value === null || value === '';
        });

    return (
        <Dialog open={!!template} onClose={onClose} maxWidth="lg" fullWidth>
            <DialogTitle>Run — {template?.name}</DialogTitle>
            <DialogContent>
                {loading && <Stack alignItems="center" sx={{ py: 3 }}><CircularProgress /></Stack>}

                {error && (
                    <Alert severity="error" sx={{ mb: 2, whiteSpace: 'pre-line' }}>{error}</Alert>
                )}

                {!loading && (
                    <Stack spacing={2}>
                        {parameters.length === 0 ? (
                            <Typography variant="body2" color="text.secondary">
                                This report has no parameters.
                            </Typography>
                        ) : (
                            <Box sx={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 2 }}>
                                {parameters.map(parameter => (
                                    <ParameterField
                                        key={parameter.name}
                                        parameter={parameter}
                                        value={values[parameter.name]}
                                        onChange={value => set(parameter.name, value)}
                                    />
                                ))}
                            </Box>
                        )}

                        <Stack direction="row" spacing={2} alignItems="center">
                            <Typography variant="caption" color="text.secondary">Direction</Typography>
                            <ToggleButtonGroup
                                exclusive
                                size="small"
                                value={direction}
                                onChange={(_e, value) => setDirection(value ?? '')}
                            >
                                <ToggleButton value="">As designed</ToggleButton>
                                <ToggleButton value="Ltr">LTR</ToggleButton>
                                <ToggleButton value="Rtl">RTL</ToggleButton>
                            </ToggleButtonGroup>
                            <Typography variant="caption" color="text.secondary">
                                One template can serve both languages.
                            </Typography>
                        </Stack>

                        {truncated && (
                            <Alert severity="warning">
                                The data was capped at the configured row limit, so this report is incomplete.
                                Narrow the parameters.
                            </Alert>
                        )}

                        <Box sx={{ height: 520, backgroundColor: 'action.hover' }}>
                            {pdfUrl ? (
                                <iframe
                                    title="Report"
                                    src={pdfUrl}
                                    style={{ width: '100%', height: '100%', border: 'none' }}
                                />
                            ) : (
                                <Stack alignItems="center" justifyContent="center" sx={{ height: '100%' }}>
                                    <Typography variant="body2" color="text.secondary">
                                        {generating ? 'Generating…' : 'Choose your parameters, then generate.'}
                                    </Typography>
                                </Stack>
                            )}
                        </Box>

                        {pageCount > 0 && (
                            <Typography variant="caption" color="text.secondary">
                                {pageCount} page{pageCount === 1 ? '' : 's'}
                            </Typography>
                        )}
                    </Stack>
                )}
            </DialogContent>
            <DialogActions>
                <Button onClick={onClose}>Close</Button>
                <Button startIcon={<DownloadIcon />} onClick={download} disabled={!pdfUrl}>
                    Download
                </Button>
                <Button
                    variant="contained"
                    onClick={() => void generate()}
                    disabled={generating || missingRequired.length > 0}
                >
                    {generating ? 'Generating…' : 'Generate'}
                </Button>
            </DialogActions>
        </Dialog>
    );
}

/** One input, chosen by the parameter's declared type. */
function ParameterField({ parameter, value, onChange }: {
    parameter: ReportParameterInfo;
    value: unknown;
    onChange: (value: unknown) => void;
}) {
    const label = parameter.label + (parameter.required ? ' *' : '');

    switch (parameter.type) {
        case 'Bool':
            return (
                <FormControlLabel
                    control={
                        <Checkbox
                            size="small"
                            checked={value === true || value === 'true'}
                            onChange={e => onChange(e.target.checked)}
                        />
                    }
                    label={label}
                />
            );

        case 'Date':
            return (
                <TextField
                    label={label}
                    type="date"
                    size="small"
                    value={(value as string) ?? ''}
                    onChange={e => onChange(e.target.value)}
                    InputLabelProps={{ shrink: true }}
                    helperText={parameter.defaultValue?.startsWith('@')
                        ? `Defaults to ${parameter.defaultValue}`
                        : undefined}
                    fullWidth
                />
            );

        case 'Int':
        case 'Decimal':
            return (
                <TextField
                    label={label}
                    type="number"
                    size="small"
                    value={(value as string) ?? ''}
                    onChange={e => onChange(e.target.value === '' ? '' : Number(e.target.value))}
                    fullWidth
                />
            );

        case 'Select':
            return (
                <TextField
                    select
                    label={label}
                    size="small"
                    value={(value as string) ?? ''}
                    onChange={e => onChange(e.target.value)}
                    fullWidth
                >
                    <MenuItem value="">Any</MenuItem>
                    {(parameter.options ?? []).map(option => (
                        <MenuItem key={option.value} value={option.value}>{option.label}</MenuItem>
                    ))}
                </TextField>
            );

        case 'MultiSelect':
            return (
                <TextField
                    select
                    label={label}
                    size="small"
                    value={Array.isArray(value) ? value : []}
                    onChange={e => onChange(
                        typeof e.target.value === 'string' ? [e.target.value] : e.target.value)}
                    SelectProps={{ multiple: true }}
                    fullWidth
                >
                    {(parameter.options ?? []).map(option => (
                        <MenuItem key={option.value} value={option.value}>{option.label}</MenuItem>
                    ))}
                </TextField>
            );

        default:
            return (
                <TextField
                    label={label}
                    size="small"
                    value={(value as string) ?? ''}
                    onChange={e => onChange(e.target.value)}
                    fullWidth
                />
            );
    }
}
