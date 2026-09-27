// Copyright (c) 2026 Nkosenhle. Licensed under AGPL-3.0.
// Source: CompliSA — Compliance & Tender Readiness Platform

import { inject } from '@angular/core';
import {
  HttpErrorResponse,
  HttpEvent,
  HttpHandlerFn,
  HttpInterceptorFn,
  HttpRequest,
  HttpStatusCode,
} from '@angular/common/http';
import { BehaviorSubject, Observable, throwError } from 'rxjs';
import { catchError, filter, switchMap, take } from 'rxjs/operators';
import { AuthService } from '../services/auth';
import { SessionExpiredError } from './http-errors';

// Sent without the bearer token and never retried on 401. The cookie-authenticated ones
// (csrf-token, refresh-token, logout) must stay anonymous: their CSRF token is issued and
// validated for an anonymous caller, and a bearer header would make ASP.NET bind it to a user.
const AUTH_ENDPOINTS = [
  '/api/v1/auth/login',
  '/api/v1/auth/register',
  '/api/v1/auth/csrf-token',
  '/api/v1/auth/refresh-token',
  '/api/v1/auth/logout',
];

// Module-scope, not instance state — a functional interceptor is just a function, so this
// is the only place shared state can live to ensure only one refresh is ever in flight at
// a time, with other 401s queuing behind it.
let isRefreshing = false;
const refreshedTokenSubject = new BehaviorSubject<string | null>(null);

export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const authService = inject(AuthService);
  const isAuthEndpoint = AUTH_ENDPOINTS.some((endpoint) => req.url.includes(endpoint));

  const authReq = isAuthEndpoint ? req : addAuthHeader(req, authService.getAccessToken());

  return next(authReq).pipe(
    catchError((error: unknown) => {
      if (error instanceof HttpErrorResponse && error.status === HttpStatusCode.Unauthorized && !isAuthEndpoint) {
        return handleUnauthorized(authReq, next, authService);
      }
      return throwError(() => error);
    }),
  );
};

function handleUnauthorized(
  req: HttpRequest<unknown>,
  next: HttpHandlerFn,
  authService: AuthService,
): Observable<HttpEvent<unknown>> {
  if (!isRefreshing) {
    isRefreshing = true;
    refreshedTokenSubject.next(null);

    return authService.refreshToken().pipe(
      switchMap((success) => {
        isRefreshing = false;

        if (!success) {
          return throwError(() => new SessionExpiredError());
        }

        const newToken = authService.getAccessToken();
        refreshedTokenSubject.next(newToken);
        return next(addAuthHeader(req, newToken));
      }),
      catchError((error: unknown) => {
        isRefreshing = false;
        return throwError(() => error);
      }),
    );
  }

  return refreshedTokenSubject.pipe(
    filter((token): token is string => token !== null),
    take(1),
    switchMap((token) => next(addAuthHeader(req, token))),
  );
}

function addAuthHeader(req: HttpRequest<unknown>, token: string | null): HttpRequest<unknown> {
  return token ? req.clone({ setHeaders: { Authorization: `Bearer ${token}` } }) : req;
}
