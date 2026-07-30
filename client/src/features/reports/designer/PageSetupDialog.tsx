import {
    Alert, Button, Dialog, DialogActions, DialogContent, DialogTitle, MenuItem, Stack, TextField,
    ToggleButton, ToggleButtonGroup, Typography
} from '@mui/material';
import { PageSetupDef, PaperSize, pageSizeMm } from '../types/reportDefinition';

interface Props {
    open: boolean;
    page: PageSetupDef;
    onClose: () => void;
    onChange: (patch: Partial<PageSetupDef>) => void;
}

const PAPER_SIZES: PaperSize[] = ['A4', 'A3', 'A5', 'Letter', 'Legal', 'Custom'];

/** Common cultures, with Hebrew first since RTL is a primary use case here. */
const CULTURES = [
    { value: 'he-IL', label: 'Hebrew (Israel)' },
    { value: 'en-GB', label: 'English (UK)' },
    { value: 'en-US', label: 'English (US)' },
    { value: 'ar-SA', label: 'Arabic (Saudi Arabia)' },
    { value: 'de-DE', label: 'German (Germany)' },
    { value: 'fr-FR', label: 'French (France)' }
];

export default function PageSetupDialog({ open, page, onClose, onChange }: Props) {
    const { widthMm, heightMm } = pageSizeMm(page);

    // Chromium draws the page header and footer inside the page margin, so a band taller than its margin
    // is clipped. The server refuses to save it; warning here saves a round trip.
    const headerTooTall = page.headerHeightMm > page.margins.topMm;
    const footerTooTall = page.footerHeightMm > page.margins.bottomMm;

    return (
        <Dialog open={open} onClose={onClose} maxWidth="sm" fullWidth>
            <DialogTitle>Page setup</DialogTitle>
            <DialogContent>
                <Stack spacing={2} sx={{ mt: 1 }}>
                    <Stack direction="row" spacing={1}>
                        <TextField
                            select
                            label="Paper size"
                            size="small"
                            value={page.paperSize}
                            onChange={e => onChange({ paperSize: e.target.value as PaperSize })}
                            sx={{ flexGrow: 1 }}
                        >
                            {PAPER_SIZES.map(s => <MenuItem key={s} value={s}>{s}</MenuItem>)}
                        </TextField>

                        <ToggleButtonGroup
                            exclusive
                            size="small"
                            value={page.orientation}
                            onChange={(_e, value) => value && onChange({ orientation: value })}
                        >
                            <ToggleButton value="Portrait">Portrait</ToggleButton>
                            <ToggleButton value="Landscape">Landscape</ToggleButton>
                        </ToggleButtonGroup>
                    </Stack>

                    {page.paperSize === 'Custom' && (
                        <Stack direction="row" spacing={1}>
                            <TextField
                                label="Width (mm)"
                                size="small"
                                type="number"
                                value={page.customWidthMm ?? ''}
                                onChange={e => onChange({ customWidthMm: Number(e.target.value) || undefined })}
                                error={!page.customWidthMm}
                                fullWidth
                            />
                            <TextField
                                label="Height (mm)"
                                size="small"
                                type="number"
                                value={page.customHeightMm ?? ''}
                                onChange={e => onChange({ customHeightMm: Number(e.target.value) || undefined })}
                                error={!page.customHeightMm}
                                fullWidth
                            />
                        </Stack>
                    )}

                    <Typography variant="caption" color="text.secondary">
                        Page is {widthMm.toFixed(0)} × {heightMm.toFixed(0)} mm,
                        printable width {(widthMm - page.margins.leftMm - page.margins.rightMm).toFixed(0)} mm.
                    </Typography>

                    <Typography variant="subtitle2">Margins (mm)</Typography>
                    <Stack direction="row" spacing={1}>
                        <Margin label="Top" value={page.margins.topMm}
                            onChange={v => onChange({ margins: { ...page.margins, topMm: v } })} />
                        <Margin label="Right" value={page.margins.rightMm}
                            onChange={v => onChange({ margins: { ...page.margins, rightMm: v } })} />
                        <Margin label="Bottom" value={page.margins.bottomMm}
                            onChange={v => onChange({ margins: { ...page.margins, bottomMm: v } })} />
                        <Margin label="Left" value={page.margins.leftMm}
                            onChange={v => onChange({ margins: { ...page.margins, leftMm: v } })} />
                    </Stack>

                    <Typography variant="subtitle2">Repeating header and footer</Typography>
                    <Stack direction="row" spacing={1}>
                        <TextField
                            label="Header height (mm)"
                            size="small"
                            type="number"
                            value={page.headerHeightMm}
                            onChange={e => onChange({ headerHeightMm: Number(e.target.value) || 0 })}
                            error={headerTooTall}
                            fullWidth
                        />
                        <TextField
                            label="Footer height (mm)"
                            size="small"
                            type="number"
                            value={page.footerHeightMm}
                            onChange={e => onChange({ footerHeightMm: Number(e.target.value) || 0 })}
                            error={footerTooTall}
                            fullWidth
                        />
                    </Stack>

                    {(headerTooTall || footerTooTall) && (
                        <Alert severity="error">
                            {headerTooTall && `The page header (${page.headerHeightMm}mm) is taller than the top margin (${page.margins.topMm}mm). `}
                            {footerTooTall && `The page footer (${page.footerHeightMm}mm) is taller than the bottom margin (${page.margins.bottomMm}mm). `}
                            Repeating headers and footers are drawn inside the margin, so they would be cut off.
                            Increase the margin.
                        </Alert>
                    )}

                    <Typography variant="subtitle2">Language</Typography>
                    <Stack direction="row" spacing={1}>
                        <ToggleButtonGroup
                            exclusive
                            size="small"
                            value={page.direction}
                            onChange={(_e, value) => value && onChange({ direction: value })}
                        >
                            <ToggleButton value="Ltr">Left to right</ToggleButton>
                            <ToggleButton value="Rtl">Right to left</ToggleButton>
                        </ToggleButtonGroup>

                        <TextField
                            select
                            label="Culture"
                            size="small"
                            value={page.culture}
                            onChange={e => onChange({ culture: e.target.value })}
                            helperText="Number and date formatting."
                            sx={{ flexGrow: 1 }}
                        >
                            {CULTURES.map(c => <MenuItem key={c.value} value={c.value}>{c.label}</MenuItem>)}
                        </TextField>
                    </Stack>
                </Stack>
            </DialogContent>
            <DialogActions>
                <Button onClick={onClose} variant="contained">Done</Button>
            </DialogActions>
        </Dialog>
    );
}

function Margin({ label, value, onChange }: {
    label: string; value: number; onChange: (value: number) => void;
}) {
    return (
        <TextField
            label={label}
            size="small"
            type="number"
            value={value}
            onChange={e => onChange(Math.max(0, Number(e.target.value) || 0))}
            fullWidth
        />
    );
}
