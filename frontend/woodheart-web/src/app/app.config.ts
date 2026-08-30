import {
  ApplicationConfig,
  provideBrowserGlobalErrorListeners,
  provideZonelessChangeDetection
} from '@angular/core';
import { provideRouter, withComponentInputBinding, withInMemoryScrolling } from '@angular/router';
import { provideHttpClient, withFetch, withInterceptors } from '@angular/common/http';
import { provideClientHydration, withEventReplay } from '@angular/platform-browser';

import { routes } from './app.routes';
import { jwtInterceptor } from './_interceptors/jwt.interceptor';
import { errorInterceptor } from './_interceptors/error.interceptor';
import { loadingInterceptor } from './_interceptors/loading.interceptor';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideZonelessChangeDetection(),

    provideRouter(
      routes,
      // Route params bind straight to component inputs, so a product detail
      // page takes `slug` as an input rather than reading ActivatedRoute.
      withComponentInputBinding(),
      // Without this, navigating from halfway down a category listing into a
      // product lands you halfway down the product page.
      withInMemoryScrolling({ scrollPositionRestoration: 'enabled', anchorScrolling: 'enabled' })
    ),

    provideHttpClient(
      // fetch rather than XHR: required for SSR to stream properly.
      withFetch(),
      // Order matters. Outermost first: loading wraps everything so the spinner
      // covers the whole exchange, then jwt attaches credentials, then error
      // sits closest to the response so it sees failures first.
      withInterceptors([loadingInterceptor, jwtInterceptor, errorInterceptor])
    ),

    provideClientHydration(withEventReplay())
  ]
};
