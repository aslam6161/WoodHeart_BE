export const environment = {
  production: true,

  /**
   * Relative, not absolute. In production nginx serves the app and proxies
   * `/api` to the API on the same origin, which keeps cookies first-party and
   * removes CORS from the picture entirely.
   */
  apiUrl: '/api/',

  anonymousIdKey: 'wh_anon',

  defaultLanguage: 'en'
};
