import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { environment } from '../../environments/environment';

export interface PingResponse {
  status: string;
  environment: string;
  utcNow: string;
  dhakaNow: string;
  version: string;
}

/**
 * Wraps the API's diagnostics endpoints.
 *
 * Exists mainly as the reference example of how a service in this codebase is
 * shaped: inject `HttpClient`, build off `environment.apiUrl`, return the
 * observable, and let the component subscribe.
 */
@Injectable({ providedIn: 'root' })
export class DiagnosticsService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = environment.apiUrl;

  ping() {
    return this.http.get<PingResponse>(`${this.baseUrl}diagnostics/ping`);
  }
}
