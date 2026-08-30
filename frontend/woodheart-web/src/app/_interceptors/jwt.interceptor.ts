import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { AccountService } from '../_services/account.service';

/**
 * Attaches the access token, and the anonymous id for signed-out visitors.
 *
 * IMSAngular subscribes to `currentUser$` inside the interceptor with
 * `take(1)` and reads the value out of the callback. That works because the
 * subject is synchronous, but it reads as async code that happens to behave
 * synchronously. Here the token is a signal, so it is simply read.
 */
export const jwtInterceptor: HttpInterceptorFn = (request, next) => {
  const account = inject(AccountService);

  const token = account.accessToken();
  const anonymousId = account.anonymousId();

  const headers: Record<string, string> = {};

  if (token) {
    headers['Authorization'] = `Bearer ${token}`;
  }

  // Sent even when signed in: a customer may have built a basket before
  // logging in, and the API needs both ids to merge it.
  if (anonymousId) {
    headers['X-Anonymous-Id'] = anonymousId;
  }

  return next(Object.keys(headers).length > 0 ? request.clone({ setHeaders: headers }) : request);
};
