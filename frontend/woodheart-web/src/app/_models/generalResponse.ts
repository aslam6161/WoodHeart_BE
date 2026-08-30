/**
 * The response envelope every WoodHeart API endpoint returns.
 *
 * Mirrors `WoodHeart.Repository.GeneralResponse` on the backend. Keep the two
 * in step — if a field is added there, add it here.
 */
export interface GeneralResponse {
  id: number;
  isSuccess: boolean;
  message: string;

  /**
   * Stable machine-readable code, e.g. `ordering.insufficient_stock`.
   * Null on success.
   *
   * Branch on THIS, never on `message`. The message is prose: it gets
   * reworded, and it is translated to Bangla.
   */
  errorCode?: string | null;

  /** Per-field messages for form display. Keys are camelCase, matching the form controls. */
  errors?: Record<string, string[]> | null;
}

/** A `GeneralResponse` carrying a typed payload. */
export interface GeneralResponseOf<T> extends GeneralResponse {
  data?: T | null;
}

/**
 * Error codes the client reacts to specifically. Everything else falls through
 * to showing `message`.
 */
export const ErrorCodes = {
  validationFailed: 'common.validation_failed',
  invalidPhone: 'common.invalid_phone',
  rateLimited: 'common.rate_limited'
} as const;
