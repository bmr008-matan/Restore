import { Container, CssBaseline, createTheme } from "@mui/material";
import Header from "./Header";
import { ThemeProvider } from "@emotion/react";
import { useState } from "react";
import { Outlet, useLocation } from "react-router-dom";

function App() {
const [darkMode, setDarkMode] = useState(false);
const paletteType = darkMode ? 'dark':'light'

// The report designer is a full-window workspace — three panes and a preview, all sized from the
// viewport — so it opts out of the shell's width limit. Container's default maxWidth of 'lg' would
// otherwise box it into 1200px and leave the rest of a wide screen as empty gutters.
const fullBleed = useLocation().pathname.startsWith('/reports/design');

const theme = createTheme({
palette:{
  mode:paletteType,
  background: {default: paletteType=== 'light' ? "#eaeaea" : '#121212' }
}
})

function handleThemeChange (){ 
  setDarkMode(!darkMode);
}

return (
      <ThemeProvider theme={theme}>
      <CssBaseline/>
      <Header darkMode={darkMode} handleThemeChange={handleThemeChange}/>
      <Container maxWidth={fullBleed ? false : 'lg'} disableGutters={fullBleed}>
        <Outlet/>
      </Container>
      </ThemeProvider>
  );
}

export default App;
