import { createBrowserRouter } from "react-router-dom";
import App from "../layout/App";
import Catalog from "../../features/catalog/Catalog";
import ProductDetails from "../../features/catalog/ProductDetails";
import ContactPage from "../../features/contacts/ContactPage";
import AboutPage from "../../features/about/AboutPage";
import HomePage from "../../features/home/HomePage";
import ReportsApp from "../../features/reports/ReportsApp";
import ReportListPage from "../../features/reports/ReportListPage";
import ReportDesignerPage from "../../features/reports/designer/ReportDesignerPage";

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
    },
    // Reporting is a top-level route rather than a child of App, so it gets its own shell instead of
    // the storefront's header and width-limited Container. See ReportsApp.
    {
        path: '/reports',
        element: <ReportsApp />,
        children: [
            {index: true, element:<ReportListPage />},
            {path: 'design/:id' ,element:<ReportDesignerPage />},
        ]
    }
]);