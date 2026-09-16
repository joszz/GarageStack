/**
 * Where the setup project stores the signed-in session for the other tests. Kept in its own
 * module because the Playwright config imports it, and the config may not import a file that
 * declares tests.
 */
export const AUTH_STATE_FILE = 'playwright/.auth/demo.json'
