import axios, { AxiosError, AxiosResponse } from 'axios';

/**
 * Shared axios instance. The project had no central API layer, so this is the first one: base URL and
 * error shaping live here rather than being repeated at every call site.
 *
 * The default is relative on purpose. In production the API serves the built client from its own
 * wwwroot, so "/api" resolves to the same origin and no CORS is involved; in development the CRA dev
 * server proxies "/api" to the API (see "proxy" in package.json), so the same relative path works there
 * too. An absolute default would be baked into the bundle at build time — CRA inlines env vars during
 * `npm run build` — and a build made on a developer's machine would then ask every user's browser to
 * call localhost.
 *
 * REACT_APP_API_URL still overrides it, for hosting the client separately from the API.
 */
const agent = axios.create({
    baseURL: process.env.REACT_APP_API_URL ?? '/api'
});

/** Validation errors the API returns per field, from ValidationProblemDto. */
export interface ApiValidationMessage {
    path: string;
    message: string;
}

/**
 * A failed request, already turned into something a component can render. Raw AxiosErrors are awkward to
 * display, and the reporting endpoints return per-field messages that are worth keeping structured.
 */
export class ApiError extends Error {
    constructor(
        message: string,
        public status: number,
        public details: ApiValidationMessage[] = []
    ) {
        super(message);
        this.name = 'ApiError';
    }

    /** One string suitable for a snackbar, when there is no room to list each field. */
    get summary(): string {
        if (this.details.length === 0) return this.message;
        return this.details.map(d => (d.path ? `${d.path}: ${d.message}` : d.message)).join('\n');
    }
}

/** The error shape the API returns: ProblemDetails, or ValidationProblemDto for per-field messages. */
interface ApiErrorBody {
    message?: string;
    errors?: ApiValidationMessage[];
    title?: string;
}

/**
 * The PDF endpoints ask for `responseType: 'blob'`, and axios honours that for error responses too — so
 * a failed render arrives as a Blob wrapping JSON rather than as parsed JSON. Reading it back is what
 * keeps the server's actual explanation ("no Chromium found", a validation message) instead of
 * collapsing every failure into the generic fallback below.
 */
async function readErrorBody(raw: unknown): Promise<ApiErrorBody | undefined> {
    if (!(raw instanceof Blob)) return raw as ApiErrorBody | undefined;

    try {
        const text = await raw.text();
        return text ? (JSON.parse(text) as ApiErrorBody) : undefined;
    } catch {
        // A non-JSON body — an HTML error page, say — leaves the generic message in place.
        return undefined;
    }
}

agent.interceptors.response.use(
    (response: AxiosResponse) => response,
    async (error: AxiosError) => {
        const status = error.response?.status ?? 0;

        if (status === 0) {
            return Promise.reject(new ApiError(
                'Could not reach the API. Is the server running?', 0));
        }

        const data = await readErrorBody(error.response?.data);

        const message = data?.message
            ?? data?.title
            ?? (status === 404 ? 'Not found.' : `The request failed (HTTP ${status}).`);

        return Promise.reject(new ApiError(message, status, data?.errors ?? []));
    }
);

export default agent;
