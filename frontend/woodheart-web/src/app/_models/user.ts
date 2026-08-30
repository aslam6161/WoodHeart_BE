/** The signed-in user, as returned by login/register/refresh. */
export interface User {
  id: number;
  fullName?: string;

  /** E.164 — this is the login handle, not the email. */
  phoneNumber: string;

  email?: string | null;
  roles: string[];
  accessToken: string;

  /** `en` or `bn`. Drives the UI language and the language of SMS the customer receives. */
  preferredLanguage: string;
}

export interface LoginDto {
  /** Any format the customer types; the API normalises it. */
  phoneNumber: string;
  password: string;
}

export interface RegisterDto {
  fullName: string;
  phoneNumber: string;
  email?: string;
  password: string;
}
