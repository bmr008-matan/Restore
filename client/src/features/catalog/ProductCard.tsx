
import { Avatar, Button, Card, CardActionArea, CardActions, CardContent, CardHeader, CardMedia, Typography } from "@mui/material";
import { Product } from "../../app/models/product"
import { Link } from "react-router-dom";

interface Props {
    product: Product
} 
export default function ProductCard ({product}:Props){
    return(
        <Card>
            <CardHeader
                avatar= {
                    <Avatar sx={{bgcolor:'secondary.main'}}  > {product.name.charAt(0).toUpperCase()}</Avatar>
                }
                title = {product.name}
                titleTypographyProps={{sx:{fontWeight:'bold',color:'primary.main'} }}
                />
        <CardActionArea>
          <CardMedia
            sx={{height:140,backgroundSize:'contain',bgcolor:"primary.light"}}
            image={product.pictureUrl}
            
          />
          <CardContent>
            <Typography gutterBottom color="secondary" variant="h5" >
                ${(product.price/100).toFixed(2)}
            </Typography>
            <Typography variant="body2" color="text.secondary">
                {product.brand} / {product.type}
            </Typography>
          </CardContent>
        </CardActionArea>
        <CardActions>
          <Button size="small" > Add to cart </Button>
          <Button size="small" component={Link} to={`/catalog/${product.id}`}> View </Button>
        </CardActions>
      </Card>
    )
    } 