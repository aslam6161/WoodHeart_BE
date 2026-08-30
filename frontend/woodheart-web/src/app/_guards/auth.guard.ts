import { inject } from '@angular/core';
import { CanActivateFn, CanDeactivateFn, Router } from '@angular/router';
import { AccountService } from '../_services/account.service';
import { ToastService } from '../_services/toast.service';

/** Requires any signed-in user. */
export const authGuard: CanActivateFn = (_route, state) => {
  const account = inject(AccountService);
  const router = inject(Router);

  if (account.isAuthenticated()) {
    return true;
  }

  // returnUrl, so signing in lands the customer back where they were rather
  // than on the home page. On a checkout flow that difference is an
  // abandoned order.
  return router.createUrlTree(['/login'], { queryParams: { returnUrl: state.url } });
};

/** Admin only — settings, payment configuration. */
export const adminGuard: CanActivateFn = () => {
  const account = inject(AccountService);
  const router = inject(Router);
  const toast = inject(ToastService);

  if (account.isAdmin()) {
    return true;
  }

  toast.error('You do not have permission to view that page.');

  return router.createUrlTree(['/']);
};

/** Any staff role — the admin panel at large. */
export const staffGuard: CanActivateFn = () => {
  const account = inject(AccountService);
  const router = inject(Router);
  const toast = inject(ToastService);

  if (account.isStaff()) {
    return true;
  }

  toast.error('You do not have permission to view that page.');

  return router.createUrlTree(['/']);
};

/** Implemented by any component that guards against navigating away mid-edit. */
export interface HasUnsavedChanges {
  hasUnsavedChanges(): boolean;
}

/**
 * Blocks navigation away from a dirty form.
 *
 * Deliberately a plain `confirm`: it is synchronous, so there is no window in
 * which the router proceeds while a styled modal is still resolving. Losing an
 * admin's half-finished product description is the exact thing this exists to
 * prevent, so the boring implementation is the correct one.
 */
export const preventUnsavedChangesGuard: CanDeactivateFn<HasUnsavedChanges> = component => {
  if (!component.hasUnsavedChanges()) {
    return true;
  }

  return confirm('You have unsaved changes. Are you sure you want to leave?');
};
