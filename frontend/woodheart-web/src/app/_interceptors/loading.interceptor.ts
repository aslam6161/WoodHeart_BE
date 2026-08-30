import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { finalize } from 'rxjs';
import { BusyService } from '../_services/busy.service';

/** Requests that must not raise the global spinner. */
const SILENT_ENDPOINTS = [
  'diagnostics/ping',
  // Typeahead fires on nearly every keystroke; a spinner flickering over the
  // whole page while someone types is worse than no feedback at all. Search
  // shows its own inline indicator instead.
  'product/search-suggest'
];

export const loadingInterceptor: HttpInterceptorFn = (request, next) => {
  const busy = inject(BusyService);

  const isSilent =
    request.headers.has('X-Silent') || SILENT_ENDPOINTS.some(path => request.url.includes(path));

  if (isSilent) {
    return next(request);
  }

  busy.busy();

  // finalize, not a tap on success: the counter must come down on error and on
  // cancellation too, or one failed request leaves the spinner up forever.
  return next(request).pipe(finalize(() => busy.idle()));
};
