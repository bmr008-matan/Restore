import { useCallback, useEffect, useReducer, useRef, useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import {
    Alert, AppBar, Box, Button, Chip, CircularProgress, Divider, IconButton, Paper, Snackbar,
    Stack, Toolbar, Tooltip, Typography
} from '@mui/material';
import ArrowBackIcon from '@mui/icons-material/ArrowBack';
import UndoIcon from '@mui/icons-material/Undo';
import RedoIcon from '@mui/icons-material/Redo';
import SaveIcon from '@mui/icons-material/Save';
import SettingsIcon from '@mui/icons-material/Settings';
import TuneIcon from '@mui/icons-material/Tune';
import VisibilityIcon from '@mui/icons-material/Visibility';
import ZoomInIcon from '@mui/icons-material/ZoomIn';
import ZoomOutIcon from '@mui/icons-material/ZoomOut';
import DeleteOutlineIcon from '@mui/icons-material/DeleteOutline';
import ContentCopyIcon from '@mui/icons-material/ContentCopy';
import FitScreenIcon from '@mui/icons-material/FitScreen';
import ViewSidebarIcon from '@mui/icons-material/ViewSidebar';
import VerticalSplitIcon from '@mui/icons-material/VerticalSplit';
import { contentWidthMm, ElementType, ReportDefinition } from '../types/reportDefinition';
import reportsApi, { TableSource, ValidationMessage } from '../api/reportsApi';
import { ApiError } from '../../../app/api/agent';
import {
    createElement, createInitialState, designerReducer, findElement
} from './state/designerReducer';
import BandCanvas, { BASE_PX_PER_MM, CANVAS_PADDING_PX } from './BandCanvas';
import Toolbox from './Toolbox';
import DataPanel from './DataPanel';
import PropertiesPanel from './PropertiesPanel';
import PageSetupDialog from './PageSetupDialog';
import ParametersEditor from './ParametersEditor';
import PdfPreviewPane from './PdfPreviewPane';

/** Placeholder until a template is loaded, so the reducer always has a definition. */
const EMPTY: ReportDefinition = {
    schemaVersion: '1.0',
    name: '',
    page: {
        paperSize: 'A4',
        orientation: 'Portrait',
        margins: { topMm: 15, rightMm: 12, bottomMm: 15, leftMm: 12 },
        headerHeightMm: 0,
        footerHeightMm: 0,
        direction: 'Ltr',
        culture: 'en-GB'
    },
    parameters: [],
    dataSets: [],
    bands: []
};

/**
 * The designer.
 *
 * The definition in reducer state is the single source of truth. Saving serialises it and PUTs it; the
 * preview posts the same object to the server. Nothing about layout is computed twice.
 */
export default function ReportDesignerPage() {
    const { id } = useParams<{ id: string }>();
    const navigate = useNavigate();

    const [state, dispatch] = useReducer(designerReducer, createInitialState(EMPTY));
    const [sources, setSources] = useState<TableSource[]>([]);
    const [loading, setLoading] = useState(true);
    const [saving, setSaving] = useState(false);
    const [version, setVersion] = useState<number | null>(null);
    const [code, setCode] = useState('');
    const [error, setError] = useState<string | null>(null);
    const [warnings, setWarnings] = useState<ValidationMessage[]>([]);
    const [toast, setToast] = useState<string | null>(null);

    const [pageSetupOpen, setPageSetupOpen] = useState(false);
    const [parametersOpen, setParametersOpen] = useState(false);
    const [previewOpen, setPreviewOpen] = useState(false);

    // Both side panels collapse, because on a laptop their fixed 220 + 380 is most of the window.
    const [leftOpen, setLeftOpen] = useState(true);
    const [rightOpen, setRightOpen] = useState(true);

    // The preview's height in px rather than a percentage, so dragging the splitter is what sets it.
    const [previewHeight, setPreviewHeight] = useState(280);

    const canvasColumn = useRef<HTMLDivElement>(null);
    const canvasViewport = useRef<HTMLDivElement>(null);
    const draggingPreview = useRef(false);

    // Counts edits so the preview knows when to re-render, without diffing the whole definition.
    const revision = useRef(0);
    revision.current += 1;

    useEffect(() => {
        let cancelled = false;

        (async () => {
            setLoading(true);
            try {
                const [template, catalog] = await Promise.all([
                    reportsApi.templates.get(Number(id)),
                    reportsApi.sources.list()
                ]);

                if (cancelled) return;

                dispatch({ type: 'load', definition: template.definition });
                setVersion(template.version);
                setCode(template.code);
                setWarnings(template.warnings);
                setSources(catalog);
            } catch (e) {
                if (!cancelled) setError((e as ApiError).summary ?? 'Could not load this report.');
            } finally {
                if (!cancelled) setLoading(false);
            }
        })();

        return () => { cancelled = true; };
    }, [id]);

    /** Warns before losing unsaved edits to a page reload or a closed tab. */
    useEffect(() => {
        if (!state.dirty) return;

        const handler = (event: BeforeUnloadEvent) => {
            event.preventDefault();
            event.returnValue = '';
        };

        window.addEventListener('beforeunload', handler);
        return () => window.removeEventListener('beforeunload', handler);
    }, [state.dirty]);

    const save = useCallback(async () => {
        setSaving(true);
        setError(null);

        try {
            const saved = await reportsApi.templates.update(Number(id), {
                definition: state.definition,
                // Sending the loaded version turns this into a concurrency check, so a second editor
                // gets told rather than silently overwriting.
                expectedVersion: version ?? undefined
            });

            setVersion(saved.version);
            setWarnings(saved.warnings);
            dispatch({ type: 'saved' });
            setToast(`Saved as version ${saved.version}.`);
        } catch (e) {
            const apiError = e as ApiError;
            setError(apiError.status === 409
                ? `${apiError.message} Reload the report before saving again.`
                : apiError.summary);
        } finally {
            setSaving(false);
        }
    }, [id, state.definition, version]);

    /** Ctrl/Cmd+S, Z and Y, since anyone designing something expects them to work. */
    useEffect(() => {
        const handler = (event: KeyboardEvent) => {
            if (!(event.ctrlKey || event.metaKey)) return;

            const key = event.key.toLowerCase();
            if (key === 's') {
                event.preventDefault();
                void save();
            } else if (key === 'z' && !event.shiftKey) {
                event.preventDefault();
                dispatch({ type: 'undo' });
            } else if (key === 'y' || (key === 'z' && event.shiftKey)) {
                event.preventDefault();
                dispatch({ type: 'redo' });
            }
        };

        window.addEventListener('keydown', handler);
        return () => window.removeEventListener('keydown', handler);
    }, [save]);

    /** Drag the splitter between the canvas and the preview. */
    const startPreviewDrag = (event: React.MouseEvent) => {
        event.preventDefault();
        draggingPreview.current = true;
        // Without this, dragging across the canvas selects band labels and element text as it goes.
        document.body.style.userSelect = 'none';
    };

    useEffect(() => {
        const move = (event: MouseEvent) => {
            const column = canvasColumn.current;
            if (!draggingPreview.current || !column) return;

            const rect = column.getBoundingClientRect();
            // Never let the preview swallow the canvas entirely — the splitter would become
            // unreachable, since it is the canvas side that is being dragged against.
            const maxHeight = Math.max(120, rect.height - 160);
            setPreviewHeight(Math.min(Math.max(rect.bottom - event.clientY, 120), maxHeight));
        };

        const stop = () => {
            if (!draggingPreview.current) return;
            draggingPreview.current = false;
            document.body.style.userSelect = '';
        };

        window.addEventListener('mousemove', move);
        window.addEventListener('mouseup', stop);
        return () => {
            window.removeEventListener('mousemove', move);
            window.removeEventListener('mouseup', stop);
            // Unmounting mid-drag would otherwise leave the whole document unselectable.
            stop();
        };
    }, []);

    /** Zooms so the page fills the canvas viewport — the fastest way to use whatever room there is. */
    const fitWidth = useCallback(() => {
        const viewport = canvasViewport.current;
        if (!viewport) return;

        // clientWidth excludes the viewport's own scrollbar, so fitting cannot induce a horizontal one.
        const availablePx = viewport.clientWidth - CANVAS_PADDING_PX * 2;
        const contentMm = contentWidthMm(state.definition.page);
        if (availablePx <= 0 || contentMm <= 0) return;

        dispatch({ type: 'setZoom', zoom: availablePx / (contentMm * BASE_PX_PER_MM) });
    }, [state.definition.page]);

    const addElement = (type: ElementType) => {
        // Falls back to the detail band so clicking a tool always does something visible.
        const band = state.selectedBand ?? 'Detail';
        dispatch({ type: 'addElement', band, element: createElement(type) });
    };

    const selected = findElement(state.definition, state.selectedElementId);

    if (loading) {
        return (
            <Stack alignItems="center" sx={{ mt: 8 }}>
                <CircularProgress />
            </Stack>
        );
    }

    return (
        // Fills the shell, which is already exactly the viewport height — no header to subtract.
        <Box sx={{ flexGrow: 1, minHeight: 0, display: 'flex', flexDirection: 'column' }}>
            <AppBar position="static" color="default" elevation={1}>
                <Toolbar variant="dense" sx={{ gap: 1 }}>
                    <Tooltip title="Back to reports">
                        <IconButton size="small" onClick={() => navigate('/reports')}>
                            <ArrowBackIcon fontSize="small" />
                        </IconButton>
                    </Tooltip>

                    <Typography variant="subtitle2" noWrap>
                        {state.definition.name || 'Untitled report'}
                    </Typography>
                    <Chip label={code} size="small" variant="outlined" />
                    {version !== null && <Chip label={`v${version}`} size="small" />}
                    {state.dirty && <Chip label="unsaved" size="small" color="warning" />}

                    <Box sx={{ flexGrow: 1 }} />

                    {/* A disabled button fires no events, so MUI cannot show a tooltip on one directly.
                        The span gives the tooltip something that still receives hover. */}
                    <Tooltip title="Undo (Ctrl+Z)">
                        <span>
                            <IconButton
                                size="small"
                                onClick={() => dispatch({ type: 'undo' })}
                                disabled={state.past.length === 0}
                            >
                                <UndoIcon fontSize="small" />
                            </IconButton>
                        </span>
                    </Tooltip>
                    <Tooltip title="Redo (Ctrl+Y)">
                        <span>
                            <IconButton
                                size="small"
                                onClick={() => dispatch({ type: 'redo' })}
                                disabled={state.future.length === 0}
                            >
                                <RedoIcon fontSize="small" />
                            </IconButton>
                        </span>
                    </Tooltip>

                    <Divider orientation="vertical" flexItem sx={{ mx: 0.5 }} />

                    <Tooltip title="Zoom out">
                        <IconButton size="small" onClick={() => dispatch({ type: 'setZoom', zoom: state.zoom - 0.1 })}>
                            <ZoomOutIcon fontSize="small" />
                        </IconButton>
                    </Tooltip>
                    <Typography variant="caption" sx={{ width: 34, textAlign: 'center' }}>
                        {Math.round(state.zoom * 100)}%
                    </Typography>
                    <Tooltip title="Zoom in">
                        <IconButton size="small" onClick={() => dispatch({ type: 'setZoom', zoom: state.zoom + 0.1 })}>
                            <ZoomInIcon fontSize="small" />
                        </IconButton>
                    </Tooltip>
                    <Tooltip title="Fit the page to the canvas width">
                        <IconButton size="small" onClick={fitWidth}>
                            <FitScreenIcon fontSize="small" />
                        </IconButton>
                    </Tooltip>

                    <Divider orientation="vertical" flexItem sx={{ mx: 0.5 }} />

                    <Tooltip title={leftOpen ? 'Hide toolbox and data' : 'Show toolbox and data'}>
                        <IconButton
                            size="small"
                            onClick={() => setLeftOpen(o => !o)}
                            color={leftOpen ? 'primary' : 'default'}
                        >
                            <ViewSidebarIcon fontSize="small" sx={{ transform: 'scaleX(-1)' }} />
                        </IconButton>
                    </Tooltip>
                    <Tooltip title={rightOpen ? 'Hide properties' : 'Show properties'}>
                        <IconButton
                            size="small"
                            onClick={() => setRightOpen(o => !o)}
                            color={rightOpen ? 'primary' : 'default'}
                        >
                            <VerticalSplitIcon fontSize="small" />
                        </IconButton>
                    </Tooltip>

                    <Divider orientation="vertical" flexItem sx={{ mx: 0.5 }} />

                    <Button size="small" startIcon={<SettingsIcon />} onClick={() => setPageSetupOpen(true)}>
                        Page setup
                    </Button>
                    <Button size="small" startIcon={<TuneIcon />} onClick={() => setParametersOpen(true)}>
                        Parameters ({state.definition.parameters.length})
                    </Button>
                    <Button
                        size="small"
                        startIcon={<VisibilityIcon />}
                        onClick={() => setPreviewOpen(o => !o)}
                        variant={previewOpen ? 'contained' : 'text'}
                    >
                        Preview
                    </Button>
                    <Button
                        size="small"
                        variant="contained"
                        startIcon={<SaveIcon />}
                        onClick={() => void save()}
                        disabled={saving || !state.dirty}
                    >
                        {saving ? 'Saving…' : 'Save'}
                    </Button>
                </Toolbar>
            </AppBar>

            {error && (
                <Alert severity="error" onClose={() => setError(null)} sx={{ whiteSpace: 'pre-line' }}>
                    {error}
                </Alert>
            )}

            {warnings.length > 0 && (
                <Alert severity="warning" onClose={() => setWarnings([])}>
                    {warnings.map((w, i) => (
                        <Typography key={i} variant="caption" display="block">
                            {w.path}: {w.message}
                        </Typography>
                    ))}
                </Alert>
            )}

            <Box sx={{ flexGrow: 1, minHeight: 0, display: 'flex' }}>
                {leftOpen && (
                    <Paper
                        square
                        elevation={0}
                        sx={{
                            width: 220, flexShrink: 0, borderRight: '1px solid', borderColor: 'divider',
                            overflow: 'auto'
                        }}
                    >
                        <Toolbox onAdd={addElement} />
                        <Divider sx={{ my: 1 }} />
                        <DataPanel definition={state.definition} sources={sources} dispatch={dispatch} />
                    </Paper>
                )}

                <Box
                    ref={canvasColumn}
                    sx={{ flexGrow: 1, minWidth: 0, display: 'flex', flexDirection: 'column' }}
                >
                    <Box sx={{ flexGrow: 1, minHeight: 0 }}>
                        <BandCanvas
                            definition={state.definition}
                            selectedElementId={state.selectedElementId}
                            selectedBand={state.selectedBand}
                            zoom={state.zoom}
                            dispatch={dispatch}
                            viewportRef={canvasViewport}
                        />
                    </Box>

                    {previewOpen && (
                        <>
                            <Box
                                onMouseDown={startPreviewDrag}
                                role="separator"
                                aria-orientation="horizontal"
                                aria-label="Resize the preview"
                                sx={{
                                    height: 7, flexShrink: 0, cursor: 'row-resize',
                                    backgroundColor: 'divider',
                                    '&:hover': { backgroundColor: 'primary.main' }
                                }}
                            />
                            <Paper
                                square
                                elevation={0}
                                sx={{ height: previewHeight, flexShrink: 0, overflow: 'hidden' }}
                            >
                                <PdfPreviewPane
                                    definition={state.definition}
                                    revision={revision.current}
                                    autoRefresh={previewOpen}
                                />
                            </Paper>
                        </>
                    )}
                </Box>

                {rightOpen && <Paper
                    square
                    elevation={0}
                    // Wide enough for the group and rule editors, which carry several controls per row.
                    sx={{ width: 380, flexShrink: 0, borderLeft: '1px solid', borderColor: 'divider', display: 'flex', flexDirection: 'column' }}
                >
                    {selected && (
                        <Stack direction="row" spacing={0.5} sx={{ p: 0.5, borderBottom: '1px solid', borderColor: 'divider' }}>
                            <Tooltip title="Duplicate element">
                                <IconButton
                                    size="small"
                                    onClick={() => dispatch({ type: 'duplicateElement', id: selected.id })}
                                >
                                    <ContentCopyIcon fontSize="small" />
                                </IconButton>
                            </Tooltip>
                            <Tooltip title="Delete element">
                                <IconButton
                                    size="small"
                                    onClick={() => dispatch({ type: 'removeElement', id: selected.id })}
                                >
                                    <DeleteOutlineIcon fontSize="small" />
                                </IconButton>
                            </Tooltip>
                        </Stack>
                    )}

                    <Box sx={{ flexGrow: 1, minHeight: 0 }}>
                        <PropertiesPanel
                            definition={state.definition}
                            selectedElementId={state.selectedElementId}
                            selectedBand={state.selectedBand}
                            dispatch={dispatch}
                        />
                    </Box>
                </Paper>}
            </Box>

            <PageSetupDialog
                open={pageSetupOpen}
                page={state.definition.page}
                onClose={() => setPageSetupOpen(false)}
                onChange={patch => dispatch({ type: 'patchPage', patch })}
            />

            <ParametersEditor
                open={parametersOpen}
                parameters={state.definition.parameters}
                sources={sources}
                onClose={() => setParametersOpen(false)}
                onChange={parameters => dispatch({ type: 'setParameters', parameters })}
            />

            <Snackbar
                open={!!toast}
                autoHideDuration={3000}
                onClose={() => setToast(null)}
                message={toast}
            />
        </Box>
    );
}
