import {
    Box, IconButton, Paper, Stack, TextField, Tooltip, Typography
} from '@mui/material';
import VisibilityIcon from '@mui/icons-material/Visibility';
import VisibilityOffIcon from '@mui/icons-material/VisibilityOff';
import InfoOutlinedIcon from '@mui/icons-material/InfoOutlined';
import {
    BAND_HINTS, BAND_LABELS, BandDef, BandKind, ElementType, ReportDefinition, pageSizeMm
} from '../types/reportDefinition';
import { DesignerAction, createElement, orderedBands } from './state/designerReducer';
import ElementBox from './ElementBox';

interface Props {
    definition: ReportDefinition;
    selectedElementId: string | null;
    selectedBand: BandKind | null;
    zoom: number;
    dispatch: React.Dispatch<DesignerAction>;
    /** The scrolling viewport, so the toolbar's "fit width" can measure the space actually available. */
    viewportRef?: React.Ref<HTMLDivElement>;
}

/** Base scale at 100% zoom. A4's 210mm then comes to a comfortable on-screen width. */
export const BASE_PX_PER_MM = 3.4;

/** Padding around the page inside the viewport. Exported so "fit width" can subtract it. */
export const CANVAS_PADDING_PX = 16;

/**
 * The page canvas: bands stacked in render order, each a positioning context for its elements.
 *
 * The canvas shows content width only — the page margins are excluded rather than drawn — because that
 * is the space elements are actually positioned within, and it matches the coordinate system the server
 * uses. Showing the full paper would mean every element's stored x had to be offset by the left margin.
 */
export default function BandCanvas({
    definition, selectedElementId, selectedBand, zoom, dispatch, viewportRef
}: Props) {
    const pxPerMm = BASE_PX_PER_MM * zoom;
    const { widthMm } = pageSizeMm(definition.page);
    const contentMm = widthMm - definition.page.margins.leftMm - definition.page.margins.rightMm;

    return (
        <Box
            ref={viewportRef}
            sx={{
                p: `${CANVAS_PADDING_PX}px`, overflow: 'auto', height: '100%',
                backgroundColor: 'action.hover'
            }}
        >
            <Paper
                elevation={3}
                sx={{ width: contentMm * pxPerMm, mx: 'auto', backgroundColor: 'background.paper' }}
            >
                {orderedBands(definition).map(band => (
                    <BandStrip
                        key={band.kind}
                        band={band}
                        definition={definition}
                        pxPerMm={pxPerMm}
                        contentMm={contentMm}
                        selected={selectedBand === band.kind && !selectedElementId}
                        selectedElementId={selectedElementId}
                        dispatch={dispatch}
                    />
                ))}
            </Paper>

            <Typography variant="caption" display="block" textAlign="center" sx={{ mt: 1, color: 'text.secondary' }}>
                {contentMm.toFixed(0)}mm printable width · {definition.page.paperSize}{' '}
                {definition.page.orientation}{definition.page.direction === 'Rtl' ? ' · RTL' : ''}
            </Typography>
        </Box>
    );
}

interface BandStripProps {
    band: BandDef;
    definition: ReportDefinition;
    pxPerMm: number;
    contentMm: number;
    selected: boolean;
    selectedElementId: string | null;
    dispatch: React.Dispatch<DesignerAction>;
}

function BandStrip({
    band, definition, pxPerMm, contentMm, selected, selectedElementId, dispatch
}: BandStripProps) {
    // The detail band grows with its table on the server, so its designed height is a minimum here.
    const isDetail = band.kind === 'Detail';

    const rtl = definition.page.direction === 'Rtl';

    /**
     * Accepts a drop from the toolbox or the data panel. In an RTL report the stored x is measured from
     * the right edge, so the drop point has to be converted the same way ElementBox converts it back.
     */
    const handleDrop = (event: React.DragEvent) => {
        event.preventDefault();

        const rect = event.currentTarget.getBoundingClientRect();
        const leftMm = (event.clientX - rect.left) / pxPerMm;
        const yMm = (event.clientY - rect.top) / pxPerMm;

        const place = (element: ReturnType<typeof createElement>) => {
            const xMm = rtl
                ? Math.max(0, contentMm - leftMm - element.widthMm)
                : leftMm;

            dispatch({
                type: 'addElement',
                band: band.kind,
                element: { ...element, xMm, yMm }
            });
        };

        const elementType = event.dataTransfer.getData('application/x-report-element') as ElementType;
        if (elementType) {
            place(createElement(elementType, leftMm, yMm));
            return;
        }

        // Dropping a field creates a bound field element, which is what dragging a field name implies.
        const fieldPayload = event.dataTransfer.getData('application/x-report-field');
        if (fieldPayload) {
            const { dataSetKey, field } = JSON.parse(fieldPayload) as { dataSetKey: string; field: string };
            const element = createElement('field', leftMm, yMm);
            place({ ...element, type: 'field', field, dataSetKey, name: field });
        }
    };

    return (
        <Box sx={{ borderBottom: '2px solid', borderColor: 'divider' }}>
            <Stack
                direction="row"
                alignItems="center"
                spacing={1}
                onClick={() => dispatch({ type: 'selectBand', band: band.kind })}
                sx={{
                    px: 1,
                    py: 0.25,
                    cursor: 'pointer',
                    backgroundColor: selected ? 'primary.main' : 'action.selected',
                    color: selected ? 'primary.contrastText' : 'text.primary'
                }}
            >
                {/* minWidth 0 with noWrap lets the label truncate instead of pushing the controls on
                    the right past the edge of the page. */}
                <Typography variant="caption" noWrap sx={{ fontWeight: 700, flexGrow: 1, minWidth: 0 }}>
                    {BAND_LABELS[band.kind].toUpperCase()}
                </Typography>

                <Tooltip title={BAND_HINTS[band.kind]}>
                    <InfoOutlinedIcon sx={{ fontSize: 14, flexShrink: 0 }} />
                </Tooltip>

                <TextField
                    value={band.heightMm}
                    onChange={e => dispatch({
                        type: 'patchBand',
                        band: band.kind,
                        patch: { heightMm: Math.max(0, Number(e.target.value) || 0) }
                    })}
                    onClick={e => e.stopPropagation()}
                    type="number"
                    size="small"
                    variant="standard"
                    inputProps={{ style: { fontSize: 11, width: 42, textAlign: 'right' }, step: 1, min: 0 }}
                    sx={{ flexShrink: 0, '& input': { color: 'inherit' } }}
                />
                <Typography variant="caption" sx={{ flexShrink: 0 }}>mm</Typography>

                <Tooltip title={band.visible ? 'Hide this band' : 'Show this band'}>
                    <IconButton
                        size="small"
                        onClick={e => {
                            e.stopPropagation();
                            dispatch({ type: 'patchBand', band: band.kind, patch: { visible: !band.visible } });
                        }}
                        sx={{ color: 'inherit' }}
                    >
                        {band.visible
                            ? <VisibilityIcon sx={{ fontSize: 16 }} />
                            : <VisibilityOffIcon sx={{ fontSize: 16 }} />}
                    </IconButton>
                </Tooltip>
            </Stack>

            <Box
                onDragOver={e => e.preventDefault()}
                onDrop={handleDrop}
                onClick={() => dispatch({ type: 'selectBand', band: band.kind })}
                // Matches the report's direction so text inside elements flows and aligns the way it
                // will in the PDF, without any pixel mirroring.
                dir={rtl ? 'rtl' : 'ltr'}
                sx={{
                    position: 'relative',
                    width: contentMm * pxPerMm,
                    [isDetail ? 'minHeight' : 'height']: band.heightMm * pxPerMm,
                    opacity: band.visible ? 1 : 0.45,
                    // A faint grid, purely a visual aid for alignment.
                    backgroundImage:
                        'linear-gradient(to right, rgba(0,0,0,0.05) 1px, transparent 1px),' +
                        'linear-gradient(to bottom, rgba(0,0,0,0.05) 1px, transparent 1px)',
                    backgroundSize: `${10 * pxPerMm}px ${10 * pxPerMm}px`
                }}
            >
                {band.elements.map(element => (
                    <ElementBox
                        key={element.id}
                        element={element}
                        pxPerMm={pxPerMm}
                        contentMm={contentMm}
                        direction={definition.page.direction}
                        selected={element.id === selectedElementId}
                        onSelect={() => dispatch({ type: 'selectElement', id: element.id, band: band.kind })}
                        onMove={(xMm, yMm) => dispatch({ type: 'moveElement', id: element.id, xMm, yMm })}
                        onResize={(xMm, yMm, widthMm, heightMm) =>
                            dispatch({ type: 'resizeElement', id: element.id, xMm, yMm, widthMm, heightMm })}
                    />
                ))}

                {band.elements.length === 0 && (
                    <Typography
                        variant="caption"
                        sx={{
                            position: 'absolute', top: 4, left: 8, color: 'text.disabled',
                            pointerEvents: 'none'
                        }}
                    >
                        Drag an element or a field here
                    </Typography>
                )}
            </Box>
        </Box>
    );
}
