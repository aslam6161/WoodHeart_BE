import { Routes } from '@angular/router';
import { DefaultLayout } from './_layout/default-layout/default-layout';
import { Home } from './home/home';
import { NotFound } from './errors/not-found/not-found';
import { ServerError } from './errors/server-error/server-error';
import { staffGuard } from './_guards/auth.guard';

/**
 * Two top-level shells: the public storefront and the admin panel.
 *
 * Admin is lazily loaded behind `staffGuard`, so a customer browsing products
 * never downloads it. That is worth more here than it looks — a large share of
 * this audience is on a mid-range phone over 4G, and the admin bundle would
 * otherwise be dead weight in the initial download.
 */
export const routes: Routes = [
  {
    path: '',
    component: DefaultLayout,
    children: [
      { path: '', component: Home, title: 'WoodHeart — Interiors, made in Bangladesh' },

      // Phase 1 onward:
      // { path: 'products', loadComponent: ... },
      // { path: 'products/:slug', loadComponent: ... },
      // { path: 'cart', loadComponent: ... },
      // { path: 'checkout', loadComponent: ... },
      // { path: 'account', canActivate: [authGuard], loadChildren: ... },

      { path: 'not-found', component: NotFound, title: 'Page not found' },
      { path: 'server-error', component: ServerError, title: 'Something went wrong' }
    ]
  },

  {
    path: 'admin',
    canActivate: [staffGuard],
    loadComponent: () => import('./_layout/admin-layout/admin-layout').then(m => m.AdminLayout),
    children: [
      // Phase 1 onward: dashboard, products, categories, orders, inventory,
      // discounts, consultations, customers, settings.
    ]
  },

  // Catch-all last. Redirects rather than rendering in place, so the address
  // bar matches what the customer is looking at and the page is linkable.
  { path: '**', redirectTo: 'not-found' }
];
