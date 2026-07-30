import { createTheme, CssBaseline, ThemeProvider } from '@mui/material';
import { Box } from '@mui/material';
import { Outlet } from 'react-router-dom';

/**
 * Root shell for the reporting app.
 *
 * Deliberately not the storefront's layout: no site header, no width-limited Container, no dark-mode
 * toggle. A report designer is a tool, and it wants the whole window — the storefront chrome cost about
 * 700px of horizontal space and 64px of vertical on a 1920px screen, and its navigation is irrelevant
 * once you are laying out a page.
 *
 * This is also the seam along which the reporting feature detaches from the host app: everything under
 * /reports renders inside this component and nothing else, so it can be lifted into a standalone project
 * without untangling it from the storefront's routes, theme or layout.
 */
const theme = createTheme({
    palette: {
        mode: 'light',
        background: { default: '#f4f5f7' }
    },
    // Denser than the storefront's default, since the designer packs a lot of controls per row.
    components: {
        MuiTooltip: { defaultProps: { arrow: true } }
    }
});

export default function ReportsApp() {
    return (
        <ThemeProvider theme={theme}>
            <CssBaseline />
            {/* Fills the viewport exactly: the designer sizes its panes from this height, and nothing
                outside it is allowed to scroll. */}
            <Box sx={{ height: '100vh', display: 'flex', flexDirection: 'column', overflow: 'hidden' }}>
                <Outlet />
            </Box>
        </ThemeProvider>
    );
}
