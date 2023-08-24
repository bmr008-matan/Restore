import { ShoppingCart } from "@mui/icons-material";
import {Badge, AppBar,IconButton,List,ListItem,Switch,Toolbar, Typography, Box } from "@mui/material";
import { NavLink } from "react-router-dom";

const midLinks = [
  {title:'Catalog', path:'/catalog'},
  {title:'About', path:'/about'},
  {title:'Contract', path:'/contract'},
]

const rightLinks = [
  {title:'Login', path:'/login'},
  {title:'Register', path:'/register'}
]

const navStyle = {color:"inherit", 
typography:"h6",
textDecoration:"none",
'&:hover':{color: "grey.500" },
 '&.active':{color: "text.secondary"}                        
};

export interface Props {
  darkMode: boolean
  handleThemeChange: () => void
};

export default function Headers ({darkMode,handleThemeChange} : Props) {
   {
    return (
      <AppBar position="static" sx={{mb:4}}>
        <Toolbar sx={{display:'flex', justifyContent:"space-between",alianItem:"center" }}  >
           <Box display="flex" alignItems="center" >
            <Typography variant="h6" component={NavLink} 
                    to="/" sx={navStyle }>
               RE-STORE
           </Typography>
           
           <Switch checked={darkMode} onChange ={handleThemeChange}/>
           </Box>
           <List sx={{display:'flex'}}>
                {midLinks.map(({title,path}) => (
                <ListItem 
                    component={NavLink} 
                    to={path} 
                    key={path} 
                    sx={navStyle}
                >
                 {title.toUpperCase()}  
                </ListItem>
                ))}
           </List>
           <Box display="flex" alignItems="center" >
           <IconButton size="large" edge="start" color="inherit" sx={{mr:2}} >
             <Badge badgeContent={4} color="secondary">
                    <ShoppingCart/>
             </Badge>
           </IconButton>
           <List sx={{display:"flex"}}>
                {rightLinks.map(({title,path}) => (
                <ListItem 
                    component={NavLink} 
                    to={path} 
                    key={path} 
                    sx={navStyle}
                >
                 {title.toUpperCase()}
                </ListItem>
                ))}
           </List>
           </Box>
        </Toolbar> 
      </AppBar>
    );
  }
}