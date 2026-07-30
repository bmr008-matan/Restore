import { Box, Typography } from '@mui/material';
import { Rnd } from 'react-rnd';
import { ReportElement, StyleDef, TextDirection } from '../types/reportDefinition';

interface Props {
    element: ReportElement;
    /** Pixels per millimetre, so the canvas can be zoomed without changing stored coordinates. */
    pxPerMm: number;
    /** Printable width, needed to mirror coordinates in an RTL report. */
    contentMm: number;
    selected: boolean;
    /** Report direction, so an RTL design mirrors in the designer as it will in the PDF. */
    direction: TextDirection;
    onSelect: () => void;
    onMove: (xMm: number, yMm: number) => void;
    onResize: (xMm: number, yMm: number, widthMm: number, heightMm: number) => void;
}

/**
 * One draggable, resizable element on a band.
 *
 * Positions are stored in millimetres and converted to pixels only for display. Doing it the other way
 * round would bake the current zoom level into the saved definition.
 *
 * In an RTL report `xMm` is measured from the reading edge — the right — matching the server's use of
 * `inset-inline-start`. Mirroring is done by converting that coordinate into a left-based pixel offset
 * for display and converting back on drop. A CSS transform would be the obvious shortcut and is wrong:
 * `scaleX(-1)` mirrors the rendered pixels, so every glyph comes out backwards.
 */
export default function ElementBox({
    element, pxPerMm, contentMm, selected, direction, onSelect, onMove, onResize
}: Props) {
    const rtl = direction === 'Rtl';

    /** Stored x (from the reading edge) to the left offset react-rnd positions with. */
    const toDisplayX = (xMm: number, widthMm: number) =>
        (rtl ? contentMm - xMm - widthMm : xMm) * pxPerMm;

    /** The inverse, applied when a drag or resize finishes. */
    const toStoredX = (leftPx: number, widthMm: number) =>
        rtl ? contentMm - leftPx / pxPerMm - widthMm : leftPx / pxPerMm;

    return (
        <Rnd
            size={{ width: element.widthMm * pxPerMm, height: element.heightMm * pxPerMm }}
            position={{ x: toDisplayX(element.xMm, element.widthMm), y: element.yMm * pxPerMm }}
            bounds="parent"
            // Tables grow with their data on the server, so their height here is only indicative and
            // resizing one vertically would imply a control the renderer does not honour.
            enableResizing={element.type !== 'table' ? undefined : { right: true, left: true }}
            onDragStart={onSelect}
            onDragStop={(_e, d) => onMove(toStoredX(d.x, element.widthMm), d.y / pxPerMm)}
            onResizeStop={(_e, _dir, ref, _delta, position) => {
                const widthMm = ref.offsetWidth / pxPerMm;
                onResize(
                    toStoredX(position.x, widthMm),
                    position.y / pxPerMm,
                    widthMm,
                    ref.offsetHeight / pxPerMm
                );
            }}
            style={{ zIndex: selected ? 2 : 1 }}
        >
            <Box
                onMouseDown={onSelect}
                // The band underneath selects itself on click. Without stopping propagation here, that
                // handler runs straight after this one and clears the element selection again, so an
                // element could never stay selected long enough to edit.
                onClick={event => {
                    event.stopPropagation();
                    onSelect();
                }}
                dir={element.direction === 'Ltr' ? 'ltr' : element.direction === 'Rtl' ? 'rtl' : undefined}
                sx={{
                    width: '100%',
                    height: '100%',
                    boxSizing: 'border-box',
                    overflow: 'hidden',
                    cursor: 'move',
                    border: selected ? '2px solid' : '1px dashed',
                    borderColor: selected ? 'primary.main' : 'rgba(0,0,0,0.35)',
                    backgroundColor: element.style?.backColor ?? 'transparent',
                    opacity: element.visible ? 1 : 0.4,
                    px: 0.5,
                    display: 'flex',
                    alignItems: 'center'
                }}
            >
                <Preview element={element} />
            </Box>
        </Rnd>
    );
}

/**
 * A rough impression of the element, not an attempt at fidelity. Real layout is the server's job — the
 * preview pane shows the actual render — so this only has to be recognisable enough to lay out.
 */
function Preview({ element }: { element: ReportElement }) {
    const style = element.style;

    switch (element.type) {
        case 'line':
            return (
                <Box sx={{
                    width: '100%',
                    borderTop: `${Math.max(1, element.thicknessPt)}px solid ${element.color}`
                }} />
            );

        case 'box':
            return null;

        case 'image':
            return (
                <Typography variant="caption" sx={{ color: 'text.secondary', fontStyle: 'italic' }}>
                    {element.source || element.field || 'image'}
                </Typography>
            );

        case 'table':
            return (
                <Box sx={{ width: '100%' }}>
                    <Typography variant="caption" sx={{ fontWeight: 600 }}>
                        Table · {element.dataSetKey || 'no data set'}
                    </Typography>
                    <Typography variant="caption" display="block" sx={{ color: 'text.secondary' }}>
                        {element.columns.length} column{element.columns.length === 1 ? '' : 's'}
                        {element.groups.length > 0 && `, ${element.groups.length} group level${element.groups.length === 1 ? '' : 's'}`}
                    </Typography>
                </Box>
            );

        case 'field':
            return <Label text={element.field ? `{${element.field}}` : (element.expression ?? 'field')} style={style} />;

        case 'pageNumber':
            return <Label text={element.template} style={style} />;

        case 'text':
        default:
            return <Label text={element.type === 'text' ? element.text : ''} style={style} />;
    }
}

function Label({ text, style }: { text: string; style?: StyleDef }) {
    return (
        <Typography
            component="span"
            sx={{
                fontSize: style?.fontSizePt ? `${style.fontSizePt}pt` : '9pt',
                fontWeight: style?.bold ? 700 : 400,
                fontStyle: style?.italic ? 'italic' : 'normal',
                textDecoration: style?.underline ? 'underline' : 'none',
                color: style?.color ?? 'inherit',
                whiteSpace: 'nowrap',
                overflow: 'hidden',
                textOverflow: 'ellipsis',
                width: '100%',
                textAlign: alignOf(style)
            }}
        >
            {text}
        </Typography>
    );
}

function alignOf(style?: StyleDef): 'left' | 'center' | 'right' | 'justify' {
    switch (style?.align) {
        case 'Center': return 'center';
        case 'Right': return 'right';
        case 'Justify': return 'justify';
        // Start and End are logical; the surrounding direction resolves them, so left is the safe default.
        default: return 'left';
    }
}
