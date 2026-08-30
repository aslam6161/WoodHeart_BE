import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal, PLATFORM_ID } from '@angular/core';
import { isPlatformBrowser } from '@angular/common';
import { tap } from 'rxjs/operators';
import { environment } from '../../environments/environment';
import { LoginDto, RegisterDto, User } from '../_models/user';
import { GeneralResponseOf } from '../_models/generalResponse';

const USER_KEY = 'wh_user';

/**
 * Holds who is signed in.
 *
 * IMSAngular exposes a `currentUser$` BehaviorSubject; this is the signal
 * equivalent, which removes the `take(1)`-and-read-from-the-callback pattern
 * the interceptors there need.
 */
@Injectable({ providedIn: 'root' })
export class AccountService {
  private readonly http = inject(HttpClient);
  private readonly platformId = inject(PLATFORM_ID);

  private readonly baseUrl = environment.apiUrl;

  private readonly currentUser = signal<User | null>(null);

  readonly user = this.currentUser.asReadonly();
  readonly isAuthenticated = computed(() => this.currentUser() !== null);
  readonly accessToken = computed(() => this.currentUser()?.accessToken ?? null);
  readonly roles = computed(() => this.currentUser()?.roles ?? []);
  readonly isAdmin = computed(() => this.hasAnyRole('Admin'));
  readonly isStaff = computed(() => this.hasAnyRole('Admin', 'Manager', 'Staff'));

  private readonly anonymous = signal<string | null>(null);
  readonly anonymousId = this.anonymous.asReadonly();

  constructor() {
    // Guarded: this service is constructed during server-side rendering too,
    // where localStorage does not exist. Touching it there throws and takes
    // the whole SSR render down.
    if (isPlatformBrowser(this.platformId)) {
      this.restoreFromStorage();
      this.ensureAnonymousId();
    }
  }

  hasAnyRole(...roles: string[]): boolean {
    const current = this.currentUser()?.roles ?? [];
    return roles.some(role => current.includes(role));
  }

  login(dto: LoginDto) {
    return this.http
      .post<GeneralResponseOf<User>>(`${this.baseUrl}account/login`, dto)
      .pipe(tap(response => this.setUser(response.data ?? null)));
  }

  register(dto: RegisterDto) {
    return this.http
      .post<GeneralResponseOf<User>>(`${this.baseUrl}account/register`, dto)
      .pipe(tap(response => this.setUser(response.data ?? null)));
  }

  logout(): void {
    this.currentUser.set(null);

    if (isPlatformBrowser(this.platformId)) {
      localStorage.removeItem(USER_KEY);
    }

    // The anonymous id deliberately survives sign-out, so a customer who logs
    // out mid-shop still has their basket.
  }

  setUser(user: User | null): void {
    this.currentUser.set(user);

    if (!isPlatformBrowser(this.platformId)) {
      return;
    }

    if (user) {
      localStorage.setItem(USER_KEY, JSON.stringify(user));
    } else {
      localStorage.removeItem(USER_KEY);
    }
  }

  private restoreFromStorage(): void {
    const stored = localStorage.getItem(USER_KEY);

    if (!stored) {
      return;
    }

    try {
      this.currentUser.set(JSON.parse(stored) as User);
    } catch {
      // Corrupt or from an older shape. Drop it rather than letting a parse
      // error break every page load from now on.
      localStorage.removeItem(USER_KEY);
    }
  }

  private ensureAnonymousId(): void {
    let id = localStorage.getItem(environment.anonymousIdKey);

    if (!id) {
      id = crypto.randomUUID();
      localStorage.setItem(environment.anonymousIdKey, id);
    }

    this.anonymous.set(id);
  }
}
