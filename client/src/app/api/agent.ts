import axios, { AxiosError, AxiosResponse } from 'axios';

/**
 * Shared axios instance. The project had no central API layer, so this is the first one: base URL and
 * error shaping live here rather than being repeated at every call site.
 */
const agent = axios.create({
    baseURL: process.env.REACT_APP_API_URL ?? 'http://localhost:5000/api'
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

agent.interceptors.response.use(
    (response: AxiosResponse) => response,
    (error: AxiosError) => {
        const status = error.response?.status ?? 0;
        const data = error.response?.data as
            { message?: string; errors?: ApiValidationMessage[]; title?: string } | undefined;

        if (status === 0) {
            return Promise.reject(new ApiError(
                'Could not reach the API. Is the server running?', 0));
        }

        const message = data?.message
            ?? data?.title
            ?? (status === 404 ? 'Not found.' : 'The request failed.');

        return Promise.reject(new ApiError(message, status, data?.errors ?? []));
    }
);

export default agent;
