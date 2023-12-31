import { Button, Divider, Grid, Table, TableBody, TableCell, TableContainer, TableRow, Typography } from "@mui/material";
import axios from "axios";
import { Component, useEffect, useState } from "react";
import { Link, useParams } from "react-router-dom";
import { Product } from "../../app/models/product";
import KeyboardBackspaceIcon from '@mui/icons-material/KeyboardBackspace';

export interface IAppProps {

}

export default function ProductDetails (props: IAppProps) {

  const {id} = useParams<{id: string}>();
  const [product,setProduct] = useState<Product | null >(null) ;
  const [loading,setLoading] = useState(true) ;

  useEffect(() => {
  axios.get(`http://localhost:5000/api/products/${id}`).then
  (response => setProduct(response.data)).catch 
  (error => console.log(error)).finally
  (() => setLoading(false));
  },[id])
  
  if (loading ) return <h3> Loading ... pls hold on my cat is here </h3>
  
  if (!product) return <h3> Product not found {id} ... contact cat admin </h3>

  return (
    <Grid container spacing={6}>
        <Grid item xs={6}>
            <img src={product.pictureUrl} alt={product.name} style={{width: '100%'}} />
        </Grid>
        <Grid item xs={6}>
            <Typography variant="h3" > {product.name}</Typography> 
            <Divider sx={{mb:2}}/>
            <Typography variant="h4" color={"secondary"}> ${(product.price/100).toFixed(2)}</Typography> 
            <TableContainer>
               <Table> 
                <TableBody>
                  <TableRow>
                      <TableCell>Name</TableCell>
                      <TableCell>{product.name}</TableCell>
                  </TableRow>
                  <TableRow>
                      <TableCell>Description</TableCell>
                      <TableCell>{product.description}</TableCell>
                  </TableRow>
                  <TableRow>
                      <TableCell>Type</TableCell>
                      <TableCell>{product.type}</TableCell>
                  </TableRow>
                  <TableRow>
                      <TableCell>Brand</TableCell>
                      <TableCell>{product.brand}</TableCell>
                  </TableRow>
                  <TableRow>
                      <TableCell>Quantity In Stock</TableCell>
                      <TableCell>{product.quantityInStock}</TableCell>
                  </TableRow>
                  </TableBody>
               </Table>
            </TableContainer>
            <Button sx={{mt:4}} size="small" variant="outlined" endIcon={<KeyboardBackspaceIcon/>} component={Link} to={`/catalog`}  > Back </Button>
          </Grid>
    </Grid>
  );
}
