export const environment = {
  production: false,

  /** Trailing slash included, so services concatenate `apiUrl + 'product'`. */
  apiUrl: 'http://localhost:5199/api/',

  /** Sent as `X-Anonymous-Id` so a signed-out visitor keeps one basket. */
  anonymousIdKey: 'wh_anon',

  defaultLanguage: 'en'
};
