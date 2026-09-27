// Copyright (c) 2026 Nkosenhle. Licensed under AGPL-3.0.
// Source: CompliSA — Compliance & Tender Readiness Platform

import { inject } from '@angular/core';
import { HttpErrorResponse, HttpInterceptorFn, HttpStatusCode } from '@angular/common/http';
import { MatSnackBar } from '@angular/material/snack-bar';
import { catchError, throwError } from 'rxjs';
import { SessionExpiredError, SKIP_ERROR_TOAST } from './http-errors';

const TOAST_DURATION_MS = 6000;

/**
 * Global HTTP error handling — the outermost interceptor, so it sees each response after the
 * auth interceptor's refresh-and-retry. Responsibilities are split deliberately:
 *
 * - 401: owned by authInterceptor (refresh the token and retry; if that fails, AuthService logs
 *   out and redirects to /login). This only adds the "session expired" toast when that happens.
 * - 403 / 5xx / 0 (network): a consistent toast here.
 * - 400 / 404 / 409 / 429: not touched — components show the API's specific message.
 *
 * The error is always re-thrown so components can still react (stop spinners, etc.).
 */
export const errorInterceptor: HttpInterceptorFn = (req, next) => {
  const snackBar = inject(MatSnackBar);

  return next(req).pipe(
    catchError((error: unknown) => {
      if (!req.context.get(SKIP_ERROR_TOAST)) {
        const toast = toastFor(error);
        if (toast) {
          // Deferred one tick so it opens after the component's own error handler: MatSnackBar
          // shows one toast at a time, and the specific global message (e.g. "access denied")
          // should win over a component's generic "Action failed.".
          setTimeout(() =>
            snackBar.open(toast.message, 'Dismiss', { duration: TOAST_DURATION_MS, panelClass: toast.panelClass }),
          );
        }
      }
      return throwError(() => error);
    }),
  );
};

function toastFor(error: unknown): { message: string; panelClass: string } | null {
  if (error instanceof SessionExpiredError) {
    return { message: 'Your session has expired. Please sign in again.', panelClass: 'toast-info' };
  }

  if (!(error instanceof HttpErrorResponse)) {
    return null;
  }

  if (error.status === 0) {
    return {
      message: "Can't reach the server. Check your connection and try again.",
      panelClass: 'toast-error',
    };
  }

  if (error.status === HttpStatusCode.Forbidden) {
    return { message: "Access denied. You don't have permission to do that.", panelClass: 'toast-warning' };
  }

  if (error.status >= HttpStatusCode.InternalServerError) {
    return { message: 'Something went wrong on our side. Please try again.', panelClass: 'toast-error' };
  }

  return null;
}
