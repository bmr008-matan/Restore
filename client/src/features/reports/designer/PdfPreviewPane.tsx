import { useCallback, useEffect, useRef, useState } from 'react';
import { Alert, Box, Button, CircularProgress, Stack, Typography } from '@mui/material';
import RefreshIcon from '@mui/icons-material/Refresh';
import { ReportDefinition } from '../types/reportDefinition';
import reportsApi from '../api/reportsApi';
import { ApiError } from '../../../app/api/agent';

interface Props {
    definition: ReportDefinition;
    /** Bumped by the parent whenever the definition changes, to trigger a debounced re-render. */
    revision: number;
    autoRefresh: boolean;
}

/** How long editing has to pause before a preview is requested. */
const DEBOUNCE_MS = 900;

/**
 * Live preview.
 *
 * This is a real server render of the current definition, shown in an iframe — not a client-side
 * approximation. That is the whole reason the preview and the final PDF can never disagree: they are the
 * same code path, with the same expression evaluation and the same Chromium.
 *
 * It never persists anything; the endpoint takes the definition in the request body.
 */
export default function PdfPreviewPane({ definition, revision, autoRefresh }: Props) {
    const [url, setUrl] = useState<string | null>(null);
    const [loading, setLoading] = useState(false);
    const [error, setError] = useState<string | null>(null);
    const [pageCount, setPageCount] = useState(0);

    // Held so the previous object URL can be revoked; leaking them accumulates blobs for the session.
    const urlRef = useRef<string | null>(null);

    const render = useCallback(async () => {
        setLoading(true);
        setError(null);

        try {
            const result = await reportsApi.reports.preview({
                definition,
                options: { inline: true }
            });

            if (urlRef.current) URL.revokeObjectURL(urlRef.current);
            const next = URL.createObjectURL(result.blob);
            urlRef.current = next;

            setUrl(next);
            setPageCount(result.pageCount);
        } catch (e) {
            const apiError = e as ApiError;
            setError(apiError.summary ?? 'Preview failed.');
        } finally {
            setLoading(false);
        }
    }, [definition]);

    useEffect(() => {
        if (!autoRefresh) return;

        // Debounced, because every keystroke in the properties panel changes the definition and a
        // Chromium render per keystroke would be both slow and pointless.
        const timer = setTimeout(() => { void render(); }, DEBOUNCE_MS);
        return () => clearTimeout(timer);
        // eslint-disable-next-line react-hooks/exhaustive-deps
    }, [revision, autoRefresh]);

    // Revoke on unmount only; the render path handles replacing the previous URL.
    useEffect(() => () => {
        if (urlRef.current) URL.revokeObjectURL(urlRef.current);
    }, []);

    return (
        <Box sx={{ height: '100%', display: 'flex', flexDirection: 'column' }}>
            <Stack direction="row" alignItems="center" spacing={1} sx={{ px: 1, py: 0.5 }}>
                <Typography variant="caption" sx={{ flexGrow: 1, color: 'text.secondary' }}>
                    {loading ? 'Rendering…' : pageCount > 0 ? `${pageCount} page${pageCount === 1 ? '' : 's'}` : 'Preview'}
                </Typography>
                {loading && <CircularProgress size={14} />}
                <Button size="small" startIcon={<RefreshIcon />} onClick={() => void render()} disabled={loading}>
                    Refresh
                </Button>
            </Stack>

            {error && (
                <Alert severity="error" sx={{ mx: 1, mb: 1, whiteSpace: 'pre-line' }}>
                    {error}
                </Alert>
            )}

            <Box sx={{ flexGrow: 1, minHeight: 0, backgroundColor: 'action.hover' }}>
                {url ? (
                    <iframe
                        title="Report preview"
                        src={url}
                        style={{ width: '100%', height: '100%', border: 'none' }}
                    />
                ) : (
                    <Stack alignItems="center" justifyContent="center" sx={{ height: '100%' }}>
                        <Typography variant="caption" color="text.secondary">
                            {loading ? 'Rendering the first preview…' : 'No preview yet.'}
                        </Typography>
                    </Stack>
                )}
            </Box>
        </Box>
    );
}
