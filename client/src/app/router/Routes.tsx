import { createBrowserRouter } from "react-router-dom";
import App from "../layout/App";
import Catalog from "../../features/catalog/Catalog";
import ProductDetails from "../../features/catalog/ProductDetails";
import ContactPage from "../../features/contacts/ContactPage";
import AboutPage from "../../features/about/AboutPage";
import HomePage from "../../features/home/HomePage";

export const router =  createBrowserRouter([
    {
        path: '/',
        element: <App />,
        children :[
            {path: '' ,element:<HomePage />},
            {path: 'catalog' ,element:<Catalog />},
            {path: 'catalog/:id' ,element:<ProductDetails />},
            {path: 'about' ,element:<AboutPage />},
            {path: 'contract' ,element:<ContactPage />},
        ]
    }
]);