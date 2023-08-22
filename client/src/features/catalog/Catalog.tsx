import { Product } from "../../app/models/product"
import ProductList from "./ProductList";
import { useEffect, useState } from "react";

interface Props {
    products: Product [];
    addProduct: () => void;
}
export default function Catalog (){
  
 const [products ,setProducts] = useState<Product[]>([]);
 useEffect(()=>{
 fetch('http://localhost:5000/api/products').then
 (Response => Response.json()).then
 (data => setProducts(data))
 },[]);
 
    return(
      <>
        <ProductList products={products}/>
      </>
    )
} 