import { useCallback, useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import {
    Alert, Box, Button, Chip, CircularProgress, Dialog, DialogActions, DialogContent, DialogTitle,
    IconButton, Paper, Stack, Table, TableBody, TableCell, TableHead, TableRow, TextField,
    Tooltip, Typography
} from '@mui/material';
import AddIcon from '@mui/icons-material/Add';
import EditIcon from '@mui/icons-material/Edit';
import PlayArrowIcon from '@mui/icons-material/PlayArrow';
import ContentCopyIcon from '@mui/icons-material/ContentCopy';
import DeleteOutlineIcon from '@mui/icons-material/DeleteOutline';
import reportsApi, { ReportTemplateSummary } from './api/reportsApi';
import { ApiError } from '../../app/api/agent';
import RunReportDialog from './run/RunReportDialog';

/** Lists report templates and is the entry point for designing or running one. */
export default function ReportListPage() {
    const navigate = useNavigate();

    const [templates, setTemplates] = useState<ReportTemplateSummary[]>([]);
    const [loading, setLoading] = useState(true);
    const [error, setError] = useState<string | null>(null);
    const [running, setRunning] = useState<ReportTemplateSummary | null>(null);
    const [createOpen, setCreateOpen] = useState(false);

    const load = useCallback(async () => {
        setLoading(true);
        setError(null);
        try {
            const page = await reportsApi.templates.list();
            setTemplates(page.items);
        } catch (e) {
            setError((e as ApiError).summary ?? 'Could not load reports.');
        } finally {
            setLoading(false);
        }
    }, []);

    useEffect(() => { void load(); }, [load]);

    const duplicate = async (template: ReportTemplateSummary) => {
        try {
            const copy = await reportsApi.templates.duplicate(template.id, {
                code: `${template.code}_COPY`,
                name: `${template.name} (copy)`
            });
            navigate(`/reports/design/${copy.id}`);
        } catch (e) {
            setError((e as ApiError).summary);
        }
    };

    const remove = async (template: ReportTemplateSummary) => {
        try {
            await reportsApi.templates.remove(template.id);
            await load();
        } catch (e) {
            setError((e as ApiError).summary);
        }
    };

    return (
        <Box sx={{ mt: 3 }}>
            <Stack direction="row" alignItems="center" sx={{ mb: 2 }}>
                <Typography variant="h5" sx={{ flexGrow: 1 }}>Reports</Typography>
                <Button variant="contained" startIcon={<AddIcon />} onClick={() => setCreateOpen(true)}>
                    New report
                </Button>
            </Stack>

            {error && <Alert severity="error" sx={{ mb: 2, whiteSpace: 'pre-line' }}>{error}</Alert>}

            {loading ? (
                <Stack alignItems="center" sx={{ py: 6 }}><CircularProgress /></Stack>
            ) : templates.length === 0 ? (
                <Paper sx={{ p: 4, textAlign: 'center' }}>
                    <Typography variant="body1" color="text.secondary">
                        No reports yet. Create one to start designing.
                    </Typography>
                </Paper>
            ) : (
                <Paper>
                    <Table size="small">
                        <TableHead>
                            <TableRow>
                                <TableCell>Name</TableCell>
                                <TableCell>Code</TableCell>
                                <TableCell align="right">Version</TableCell>
                                <TableCell>Last saved</TableCell>
                                <TableCell align="right">Actions</TableCell>
                            </TableRow>
                        </TableHead>
                        <TableBody>
                            {templates.map(template => (
                                <TableRow key={template.id} hover>
                                    <TableCell>
                                        <Typography variant="body2">{template.name}</Typography>
                                        {template.description && (
                                            <Typography variant="caption" color="text.secondary">
                                                {template.description}
                                            </Typography>
                                        )}
                                    </TableCell>
                                    <TableCell>
                                        <Chip label={template.code} size="small" variant="outlined" />
                                    </TableCell>
                                    <TableCell align="right">{template.version}</TableCell>
                                    <TableCell>
                                        <Typography variant="caption">
                                            {new Date(template.updatedAt).toLocaleString()}
                                            {template.updatedBy && ` · ${template.updatedBy}`}
                                        </Typography>
                                    </TableCell>
                                    <TableCell align="right">
                                        <Tooltip title="Run">
                                            <IconButton size="small" onClick={() => setRunning(template)}>
                                                <PlayArrowIcon fontSize="small" />
                                            </IconButton>
                                        </Tooltip>
                                        <Tooltip title="Design">
                                            <IconButton
                                                size="small"
                                                onClick={() => navigate(`/reports/design/${template.id}`)}
                                            >
                                                <EditIcon fontSize="small" />
                                            </IconButton>
                                        </Tooltip>
                                        <Tooltip title="Duplicate">
                                            <IconButton size="small" onClick={() => void duplicate(template)}>
                                                <ContentCopyIcon fontSize="small" />
                                            </IconButton>
                                        </Tooltip>
                                        <Tooltip title="Delete">
                                            <IconButton size="small" onClick={() => void remove(template)}>
                                                <DeleteOutlineIcon fontSize="small" />
                                            </IconButton>
                                        </Tooltip>
                                    </TableCell>
                                </TableRow>
                            ))}
                        </TableBody>
                    </Table>
                </Paper>
            )}

            <RunReportDialog template={running} onClose={() => setRunning(null)} />

            <CreateReportDialog
                open={createOpen}
                onClose={() => setCreateOpen(false)}
                onCreated={id => navigate(`/reports/design/${id}`)}
            />
        </Box>
    );
}

/** Creates a template and hands back its id, which is what the designer navigates to. */
function CreateReportDialog({ open, onClose, onCreated }: {
    open: boolean; onClose: () => void; onCreated: (id: number) => void;
}) {
    const [name, setName] = useState('');
    const [code, setCode] = useState('');
    const [busy, setBusy] = useState(false);
    const [error, setError] = useState<string | null>(null);

    const create = async () => {
        setBusy(true);
        setError(null);
        try {
            // No definition is sent: the server returns a blank design with sane page setup and the five
            // bands, which is more useful than an empty row.
            const created = await reportsApi.templates.create({
                name: name.trim(),
                code: code.trim() || undefined
            });
            onCreated(created.id);
        } catch (e) {
            setError((e as ApiError).summary);
        } finally {
            setBusy(false);
        }
    };

    return (
        <Dialog open={open} onClose={onClose} maxWidth="sm" fullWidth>
            <DialogTitle>New report</DialogTitle>
            <DialogContent>
                {error && <Alert severity="error" sx={{ mb: 2, whiteSpace: 'pre-line' }}>{error}</Alert>}
                <Stack spacing={2} sx={{ mt: 1 }}>
                    <TextField
                        label="Name"
                        value={name}
                        onChange={e => setName(e.target.value)}
                        autoFocus
                        fullWidth
                    />
                    <TextField
                        label="Code (optional)"
                        value={code}
                        onChange={e => setCode(e.target.value)}
                        helperText="A stable identifier other systems can use instead of the id. Derived from the name when left blank."
                        fullWidth
                    />
                </Stack>
            </DialogContent>
            <DialogActions>
                <Button onClick={onClose}>Cancel</Button>
                <Button
                    variant="contained"
                    onClick={() => void create()}
                    disabled={busy || name.trim().length === 0}
                >
                    {busy ? 'Creating…' : 'Create and design'}
                </Button>
            </DialogActions>
        </Dialog>
    );
}
