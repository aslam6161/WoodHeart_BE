import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, throwError } from 'rxjs';
import { ToastService } from '../_services/toast.service';
import { ErrorCodes, GeneralResponse } from '../_models/generalResponse';

/**
 * Turns an HTTP failure into something the customer can act on.
 *
 * Every failure from this API arrives in the same `GeneralResponse` shape —
 * binding failures and business failures alike — so there is exactly one
 * envelope to unpack here rather than two.
 */
export const errorInterceptor: HttpInterceptorFn = (request, next) => {
  const router = inject(Router);
  const toast = inject(ToastService);

  return next(request).pipe(
    catchError((error: HttpErrorResponse) => {
      const body = error.error as GeneralResponse | undefined;

      switch (error.status) {
        case 400:
          // Validation failures are handed back to the component so it can mark
          // the individual controls. A toast saying "please correct the
          // highlighted fields" without highlighting anything is worse than
          // silence.
          if (body?.errorCode !== ErrorCodes.validationFailed) {
            toast.error(body?.message ?? 'That request could not be completed.');
          }
          break;

        case 401:
          // The jwt interceptor will have refreshed if it could. Reaching here
          // means the session is genuinely gone.
          toast.error('Please sign in to continue.');
          break;

        case 403:
          toast.error('You do not have permission to do that.');
          break;

        case 404:
          router.navigateByUrl('/not-found');
          break;

        case 409:
          toast.error(
            body?.message ?? 'This changed while you were working on it. Reload and try again.'
          );
          break;

        case 429:
          toast.warning(body?.message ?? 'Too many requests. Please wait a moment.');
          break;

        case 0:
          // No response at all — the customer is offline, or on a 4G connection
          // that dropped. Not a server fault, and not worth a scary message.
          toast.error('No connection. Check your internet and try again.');
          break;

        default:
          if (error.status >= 500) {
            // The correlation id is what lets support find this exact request
            // in the logs, so it goes in front of the customer.
            const correlationId = error.headers?.get('X-Correlation-Id');

            router.navigateByUrl('/server-error', {
              state: { error: body, correlationId }
            });
          }
          break;
      }

      return throwError(() => error);
    })
  );
};
