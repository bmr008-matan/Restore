import { Box, List, ListItemButton, ListItemIcon, ListItemText, Typography } from '@mui/material';
import TextFieldsIcon from '@mui/icons-material/TextFields';
import DataObjectIcon from '@mui/icons-material/DataObject';
import TableChartIcon from '@mui/icons-material/TableChart';
import CropSquareIcon from '@mui/icons-material/CropSquare';
import HorizontalRuleIcon from '@mui/icons-material/HorizontalRule';
import ImageIcon from '@mui/icons-material/Image';
import TagIcon from '@mui/icons-material/Tag';
import { ElementType } from '../types/reportDefinition';

interface Props {
    /** Adds to the currently selected band, for people who would rather click than drag. */
    onAdd: (type: ElementType) => void;
}

const TOOLS: { type: ElementType; label: string; icon: JSX.Element; hint: string }[] = [
    { type: 'text', label: 'Text', icon: <TextFieldsIcon fontSize="small" />, hint: 'Static text, or tokens like {ReportTitle}' },
    { type: 'field', label: 'Field', icon: <DataObjectIcon fontSize="small" />, hint: 'One bound value from a data set' },
    { type: 'table', label: 'Table', icon: <TableChartIcon fontSize="small" />, hint: 'Data table with grouping and totals' },
    { type: 'box', label: 'Box', icon: <CropSquareIcon fontSize="small" />, hint: 'Filled or outlined panel' },
    { type: 'line', label: 'Line', icon: <HorizontalRuleIcon fontSize="small" />, hint: 'Horizontal or vertical rule' },
    { type: 'image', label: 'Image', icon: <ImageIcon fontSize="small" />, hint: 'Logo or a bound image path' },
    { type: 'pageNumber', label: 'Page number', icon: <TagIcon fontSize="small" />, hint: 'Only works on a page header or footer' }
];

/**
 * The element palette. Each entry is draggable onto a band and also clickable, since dragging is awkward
 * on a trackpad and the band strips accept a click-to-add just as well.
 */
export default function Toolbox({ onAdd }: Props) {
    return (
        <Box>
            <Typography variant="overline" sx={{ px: 1.5, color: 'text.secondary' }}>Toolbox</Typography>
            <List dense disablePadding>
                {TOOLS.map(tool => (
                    <ListItemButton
                        key={tool.type}
                        draggable
                        onDragStart={e => {
                            e.dataTransfer.setData('application/x-report-element', tool.type);
                            e.dataTransfer.effectAllowed = 'copy';
                        }}
                        onClick={() => onAdd(tool.type)}
                        title={tool.hint}
                        sx={{ py: 0.25 }}
                    >
                        <ListItemIcon sx={{ minWidth: 30 }}>{tool.icon}</ListItemIcon>
                        <ListItemText
                            primary={tool.label}
                            primaryTypographyProps={{ variant: 'body2' }}
                        />
                    </ListItemButton>
                ))}
            </List>
        </Box>
    );
}
