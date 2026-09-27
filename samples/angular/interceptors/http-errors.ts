// Copyright (c) 2026 Nkosenhle. Licensed under AGPL-3.0.
// Source: CompliSA — Compliance & Tender Readiness Platform

import { HttpContextToken } from '@angular/common/http';

/** Set on a request to suppress the global error toast (e.g. fire-and-forget calls). */
export const SKIP_ERROR_TOAST = new HttpContextToken<boolean>(() => false);

/**
 * Thrown by authInterceptor when a 401 couldn't be recovered by refreshing the session. By then
 * AuthService has already logged out and redirected to /login; errorInterceptor just tells the
 * user why.
 */
export class SessionExpiredError extends Error {
  constructor() {
    super('Session expired.');
    this.name = 'SessionExpiredError';
  }
}
