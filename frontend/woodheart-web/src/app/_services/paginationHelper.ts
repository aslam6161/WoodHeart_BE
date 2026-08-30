import { HttpClient, HttpParams } from '@angular/common/http';
import { map } from 'rxjs/operators';
import { Observable } from 'rxjs';
import { PaginatedResult } from '../_models/pagination';

/**
 * Reads a paged response: the body is the array, the counts come from the
 * `X-Pagination` header.
 *
 * The header only reaches this code because the API adds it to
 * `Access-Control-Expose-Headers`. Without that the browser hides it on any
 * cross-origin request and the pager silently shows a single page — which
 * looks like a backend bug and is not one.
 */
export function getPaginatedResult<T>(
  url: string,
  params: HttpParams,
  http: HttpClient
): Observable<PaginatedResult<T>> {
  const paginatedResult = new PaginatedResult<T>();

  return http.get<T>(url, { observe: 'response', params }).pipe(
    map(response => {
      paginatedResult.result = response.body ?? undefined;

      const pagination = response.headers.get('X-Pagination');

      if (pagination) {
        paginatedResult.pagination = JSON.parse(pagination);
      }

      return paginatedResult;
    })
  );
}

export function getPaginationHeaders(pageNumber: number, pageSize: number): HttpParams {
  let params = new HttpParams();

  params = params.append('pageNumber', pageNumber.toString());
  params = params.append('pageSize', pageSize.toString());

  return params;
}

/**
 * Appends a value only when it is set.
 *
 * Sending `?categoryId=` for an unselected filter is not the same as omitting
 * it — model binding sees an empty string and the query filters on nothing,
 * so the customer gets zero results from a filter they never applied.
 */
export function appendIfPresent(
  params: HttpParams,
  key: string,
  value: string | number | boolean | null | undefined
): HttpParams {
  if (value === null || value === undefined || value === '') {
    return params;
  }

  return params.append(key, value.toString());
}
